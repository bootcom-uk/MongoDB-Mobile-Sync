using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MongoDB.Driver;
using MongoDBDemoSync;
using MongoDBDemoSync.Configuration;
using MongoDBDemoSync.Hubs;
using MongoDBDemoSync.Interfaces;
using MongoDBDemoSync.Services;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

var assembly = Assembly.GetExecutingAssembly();
var assemblyName = assembly.GetName().Name;
var stream = null as Stream;

#if DEBUG
stream = assembly.GetManifestResourceStream($"{assemblyName}.appsettings.development.json");

#else
            stream = assembly.GetManifestResourceStream($"{assemblyName}.appsettings.json");
#endif

var configurationBuilder = builder.Configuration.AddJsonStream(stream);

var apiConfiguration = configurationBuilder.Build().Get<APIConfiguration>();

builder.WebHost.UseSentry(options =>
{
    // A DSN is required.  You can set it here, or in configuration, or in an environment variable.
    options.Dsn = apiConfiguration!.SentryConfigurationSection.Dsn;

    // Enable Sentry performance monitoring
    options.TracesSampleRate = 1.0;

#if DEBUG
    // Log debug information about the Sentry SDK
    options.Debug = true;
#endif
});

// Add services to the container.
builder.Services.AddSingleton<IMongoClient, MongoClient>(sp =>
{
    return new MongoClient(apiConfiguration!.MongoConfigurationSection.Connectionstring);
});

builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddHttpClient();

builder.Services.AddScoped<InitialSyncService>();
builder.Services.AddScoped<IAppSyncService, AppSyncService>();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // security definition
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        Description = "JWT Authorization header using the Bearer scheme. Example: 'Bearer 12345abcdef'"
    });

});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("IsAdministrator", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => c.Type == "Permissions" && c.Value.Split(',').Contains("ADMIN"))
        ));

    options.AddPolicy("CanRead", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => c.Type == "Permissions" && c.Value.Split(',').Contains("READ"))
        ));

    options.AddPolicy("CanWrite", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => c.Type == "Permissions" && c.Value.Split(',').Contains("WRITE"))
        ));
});
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidAudiences = apiConfiguration!.TokenConfigurationSection.Audience,
        ValidIssuer = apiConfiguration!.TokenConfigurationSection.Issuer,

        IssuerSigningKey = new RsaSecurityKey(PemUtils.ImportPublicKey(apiConfiguration.TokenConfigurationSection.PublicKey!)),

        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero

    };

    // Custom handling for invalid tokens
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {            
            if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
            {
                if (context.Response.Headers.Count(record => record.Key == "Token-Expired") == 0)
                {
                    context.Response.Headers.Append("Token-Expired", "true");
                }
            }
            return Task.CompletedTask;
        }
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<UpdateHub>("/hubs/update");

app.Run();
