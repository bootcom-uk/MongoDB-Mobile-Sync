using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDBDemoSync.Services;
using System.ComponentModel;
using System.Security.Claims;

namespace MongoDBDemoSync.Controllers
{
    
    public class InitialSyncController : BaseController
    {

        internal InitialSyncService _initialSyncService;

        public InitialSyncController(InitialSyncService initialSyncService, ILogger<InitialSyncController> logger) : base(logger)
        {
            _initialSyncService = initialSyncService;
        }

        [HttpGet]
        [Description("Confirms whether the initial sync has now completed for this app")]
        public async Task<ActionResult<bool>> HasInitialSyncCompleted()
        {
            var audienceClaim = User.Claims.FirstOrDefault(record => record.Type == "aud");

            if (audienceClaim is null) { return NotFound(); }

            return Ok(await _initialSyncService.HasInitialSyncCompleted(audienceClaim.Value));
        }

        [Authorize(Policy = "IsAdministrator")]        
        [HttpPost]
        public async Task<IActionResult> PerformInitialSync()
        {
            var audienceClaim = User.Claims.First(record => record.Type == "aud");

            await _initialSyncService.PerformInitialSync(audienceClaim.Value);

            return NoContent();

        }
    }
}
