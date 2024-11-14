
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDBDemoSync.Interfaces;
using MongoDBDemoSync.Models;
using System.Security.Claims;

namespace MongoDBDemoSync.Controllers
{
    
    public class DataSyncController : BaseController
    {

        private readonly IAppSyncService _syncService;

        public DataSyncController(IAppSyncService syncService, ILogger<DataSyncController> logger) : base(logger)
        {
            _syncService = syncService;
        }

        [Authorize]
        [HttpPost("Collect")]
        public async Task<ActionResult<IEnumerable<DatabaseAndCollection>>> GetAppInformation([FromForm(Name = "AppName")] string appName)
        {
            var data = await _syncService.GetAppInformation(appName);

            if(data == null) { return NotFound(); } 

            return Ok(data);
        }

        [Authorize]
        [HttpPost("sync")]
        public async Task<ActionResult<SyncResult>> SyncData(
    [FromForm(Name = "AppName")] string appName,
    [FromForm(Name = "LastSyncDate")] DateTime? lastSyncDate,
    [FromForm(Name = "LastSyncedId")] string? lastSyncedId, // ID of the last synced document, 
    [FromForm(Name = "DatabaseName")] string databaseName,
    [FromForm(Name = "CollectionName")] string collectionName,
    [FromForm(Name = "PageNumber")] int pageNumber = 1)   // Page number to continue from
        {
            var userId = User.Claims.FirstOrDefault(record => record.Type == ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User not found.");
            }

            // Validate that the user has permission for the app (JWT-based check)
            if (!_syncService.UserHasPermission(appName, userId))
            {
                return Forbid("User does not have permission to sync this app.");
            }

            // Call the sync service to fetch data in batches
            var syncResult = await _syncService.SyncAppDataAsync(appName, userId, lastSyncDate, databaseName, collectionName, pageNumber, lastSyncedId);

            if (!syncResult.Success)
            {
                _logger.LogError($"Sync failed for app {appName}, user {userId}: {syncResult.ErrorMessage}");
                return StatusCode(500, "Sync failed.");
            }

            // Set the current page and collection name in the result
            syncResult.PageNumber = pageNumber; // Keep track of the page for the client to know the next batch
            syncResult.AppName = appName; // Include the app's collection name for clarity
            syncResult.DatabaseName = databaseName;

            return Ok(syncResult);
        }


    }
}
