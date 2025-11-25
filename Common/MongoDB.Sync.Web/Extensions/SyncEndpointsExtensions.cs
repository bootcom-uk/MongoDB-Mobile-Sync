using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using MongoDB.Sync.Models;
using MongoDB.Sync.Models.Web;
using MongoDB.Sync.Web.Helpers;
using MongoDB.Sync.Web.Hubs;
using MongoDB.Sync.Web.Interfaces;
using MongoDB.Sync.Web.Models.SyncModels;
using MongoDB.Sync.Web.Services;
using System.Security.Claims;
using System.Text.Json;

namespace MongoDB.Sync.Web.Extensions
{
    public static class SyncApiEndpointExtensions
    {

        public static void UseSyncApi(this WebApplication app)
        {
            // 👇 Global middleware for permissions
            app.UseMiddleware<PermissionAuthorizationMiddleware>();

            // 👇 Auto-map all your endpoints
            app.MapSyncControllers();

            // (Optional) You could also register hubs, e.g.
            app.MapHub<UpdateHub>("/updateHub");

            // 👇 Optionally log a friendly startup message
            var logger = app.Logger;
            logger.LogInformation("MongoDB Sync API endpoints and middleware registered successfully.");
        }
    


        public static void MapSyncControllers(this IEndpointRouteBuilder app)
        {
            var dataSyncGroup = app.MapGroup("/api/DataSync");

            // 🔹 DataSyncController routes
            dataSyncGroup.MapPost("/live-update", async (
                [FromServices] IHubContext<UpdateHub> hub,
                [FromServices] ILoggerFactory loggerFactory,
                [FromBody] PayloadModel payload) =>
            {
                var logger = loggerFactory.CreateLogger("LiveUpdate");
                await hub.Clients.Groups(payload.AppId).SendAsync("ReceiveUpdate", JsonSerializer.Serialize(payload));
                logger.LogInformation($"Sent document {JsonSerializer.Serialize(payload)} via SignalR");
                return Results.NoContent();
            })
            .RequiresPermission("SERVICE_PERMISSION")
            .WithName("ReceiveLiveUpdate");

            dataSyncGroup.MapPost("/Send/{AppName}", async (
                [FromRoute] string appName,
                [FromBody] LocalCacheDataChange localChange,
                [FromServices] IAppSyncService syncService) =>
            {
                var webChange = new WebLocalCacheDataChange
                {
                    Id = localChange.Id,
                    CollectionName = localChange.CollectionName,
                    IsDeletion = localChange.IsDeletion,
                    SerializedDocument = localChange.SerializedDocument,
                    Timestamp = localChange.Timestamp
                };

                if (!string.IsNullOrWhiteSpace(webChange.SerializedDocument))
                    webChange.Document = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(webChange.SerializedDocument);

                var result = await syncService.WriteDataToMongo(appName, webChange);
                if (result == null) return Results.BadRequest("An unhandled exception occurred");
                if (result.ContainsKey("error")) return Results.BadRequest(result);

                return Results.Ok(result);
            })
            .RequireAuthorization()
            .WithName("SendDataToDatabase");

            dataSyncGroup.MapPost("/Collect", async (
                [FromForm(Name = "AppName")] string appName,
                [FromServices] IAppSyncService syncService) =>
            {
                var data = await syncService.GetAppInformation(appName);
                return data is null ? Results.NotFound() : Results.Ok(data);
            })
            .RequireAuthorization()
            .WithName("GetAppInformation");

            dataSyncGroup.MapPost("/check-updates", async (
                [FromBody] WebSyncCollectionCheckRequest request,
                [FromServices] IAppSyncService syncService,
                ClaimsPrincipal user) =>
            {
                var userId = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

                if (!syncService.UserHasPermission(request.AppName, userId))
                    return Results.Forbid();

                var updates = await syncService.CheckForCollectionUpdatesAsync(request.AppName, userId, request.Collections);
                return Results.Ok(new WebSyncCollectionCheckResponse { Updates = updates });
            })
            .RequireAuthorization()
            .WithName("CheckForCollectionUpdates");

            dataSyncGroup.MapPost("/sync", async (
                [FromForm(Name = "AppName")] string appName,
                [FromForm(Name = "LastSyncDate")] DateTime? lastSyncDate,
                [FromForm(Name = "LastSyncedId")] string? lastSyncedId,
                [FromForm(Name = "DatabaseName")] string dbName,
                [FromForm(Name = "CollectionName")] string collectionName,
                [FromForm(Name = "PageNumber")] int pageNumber,
                [FromForm(Name = "InitialSync")] bool initialSync,
                [FromServices] IAppSyncService syncService,
                [FromServices] ILoggerFactory loggerFactory,
                ClaimsPrincipal user) =>
            {
                var logger = loggerFactory.CreateLogger("SyncData");
                var userId = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

                if (!syncService.UserHasPermission(appName, userId))
                    return Results.Forbid();

                var syncResult = await syncService.SyncAppDataAsync(appName, userId, dbName, collectionName, initialSync, pageNumber, lastSyncedId, lastSyncDate);
                if (!syncResult.Success)
                {
                    logger.LogError($"Sync failed for app {appName}, user {userId}: {syncResult.ErrorMessage}");
                    return Results.StatusCode(500);
                }

                syncResult.PageNumber = pageNumber;
                syncResult.AppName = appName;
                syncResult.DatabaseName = dbName;

                return Results.Ok(syncResult);
            })
            .RequireAuthorization()
            .WithName("SyncData");

            var initialSyncGroup = app.MapGroup("/api");

            // 🔹 InitialSyncController routes
            initialSyncGroup.MapGet("/initialsync", async (
                [FromServices] MongoDB.Sync.Web.Services.InitialSyncService service,
                ClaimsPrincipal user) =>
            {
                var audienceClaim = user.Claims.FirstOrDefault(c => c.Type == "aud");
                if (audienceClaim is null) return Results.NotFound();
                return Results.Ok(await service.HasInitialSyncCompleted(audienceClaim.Value));
            })
            .WithName("HasInitialSyncCompleted");

            initialSyncGroup.MapPost("/initialsync", async (
                [FromServices] MongoDB.Sync.Web.Services.InitialSyncService service,
                ClaimsPrincipal user) =>
            {
                var audienceClaim = user.Claims.First(c => c.Type == "aud");
                await service.PerformInitialSync(audienceClaim.Value, null);
                return Results.NoContent();
            })
            .RequireAuthorization("IsAdministrator")
            .WithName("PerformInitialSync");

            // 🔹 AppController routes
            var appGroup = app.MapGroup("/api/App");

            appGroup.MapGet("/schema", async (
        [FromServices] BsonSchemaService schemaService) =>
            {
                return Results.Ok(await schemaService.GetFullDatabaseSchemaAsync());
            })
    .RequiresPermission("ADMIN")
    .WithName("GetSchema");

            appGroup.MapGet("/all", async (
                [FromServices] IAppSyncService appSyncService,
                [FromServices] ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("GetApps");
                try
                {
                    var apps = await appSyncService.GetAppSyncMappings();
                    return Results.Ok(apps);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error getting apps");
                    return Results.BadRequest(ex);
                }
            })
            .RequiresPermission("ADMIN")
            .WithName("GetApps");

            appGroup.MapPost("/", async (
                [FromBody] AppSyncMapping appSyncMapping,
                [FromServices] IAppSyncService appSyncService,
                [FromServices] IHubContext<UpdateHub> hub,
                [FromServices] InitialSyncService initialSyncService,
                [FromServices] ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("SaveApp");

                try
                {
                    var newAppSyncMapping = await appSyncService.SaveAppSyncMapping(appSyncMapping);

                    if (newAppSyncMapping != null)
                    {
                        await hub.Clients.Groups(appSyncMapping.AppId).SendAsync("AppSyncStarted");
                        await initialSyncService.PerformInitialSync(appSyncMapping.AppName, appSyncMapping);
                        await hub.Clients.Groups(appSyncMapping.AppId).SendAsync("AppSyncCompleted", newAppSyncMapping);
                    }

                    return Results.NoContent();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error saving the app sync mapping");
                    return Results.BadRequest(ex);
                }
            })
            .RequiresPermission("ADMIN")
            .WithName("SaveApp");

            appGroup.MapDelete("/{id}", async (
                [FromRoute] string id,
                [FromServices] IAppSyncService appSyncService,
                [FromServices] ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("DeleteApp");

                try
                {
                    await appSyncService.DeleteAppSyncMapping(id);
                    return Results.NoContent();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error deleting app");
                    return Results.BadRequest(ex);
                }
            })
            .RequiresPermission("ADMIN")
            .WithName("DeleteApp");

        }
        
    }
}
