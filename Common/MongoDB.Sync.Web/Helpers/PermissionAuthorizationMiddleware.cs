using MongoDB.Sync.Web.Extensions;

namespace MongoDB.Sync.Web.Helpers
{
    public class PermissionAuthorizationMiddleware
    {
        private readonly RequestDelegate _next;

        public PermissionAuthorizationMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            var endpoint = context.GetEndpoint();
            var metadata = endpoint?.Metadata.GetMetadata<RequiresPermissionMetadata>();
            if (metadata != null)
            {
                var user = context.User;
                if (!user.Identity?.IsAuthenticated ?? true)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                if (!user.HasClaim("permissions", metadata.Permission))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Missing required permission.");
                    return;
                }
            }

            await _next(context);
        }
    }

}
