using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Sync.Models;
using MongoDB.Sync.Models.Web;
using MongoDB.Sync.Web.Helpers;
using MongoDB.Sync.Web.Hubs;
using MongoDB.Sync.Web.Interfaces;
using MongoDB.Sync.Web.Models.SyncModels;
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
            var group = app.MapGroup("/api");

            // 🔹 DataSyncController routes
            group.MapPost("/live-update", async (
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

            group.MapPost("/Send/{AppName}", async (
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

            group.MapPost("/Collect", async (
                [FromForm(Name = "AppName")] string appName,
                [FromServices] IAppSyncService syncService) =>
            {
                var data = await syncService.GetAppInformation(appName);
                return data is null ? Results.NotFound() : Results.Ok(data);
            })
            .RequireAuthorization()
            .WithName("GetAppInformation");

            group.MapPost("/check-updates", async (
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

            group.MapPost("/sync", async (
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

            // 🔹 InitialSyncController routes
            group.MapGet("/initialsync", async (
                [FromServices] MongoDB.Sync.Web.Services.InitialSyncService service,
                ClaimsPrincipal user) =>
            {
                var audienceClaim = user.Claims.FirstOrDefault(c => c.Type == "aud");
                if (audienceClaim is null) return Results.NotFound();
                return Results.Ok(await service.HasInitialSyncCompleted(audienceClaim.Value));
            })
            .WithName("HasInitialSyncCompleted");

            group.MapPost("/initialsync", async (
                [FromServices] MongoDB.Sync.Web.Services.InitialSyncService service,
                ClaimsPrincipal user) =>
            {
                var audienceClaim = user.Claims.First(c => c.Type == "aud");
                await service.PerformInitialSync(audienceClaim.Value, null);
                return Results.NoContent();
            })
            .RequireAuthorization("IsAdministrator")
            .WithName("PerformInitialSync");
        }
    }
}
