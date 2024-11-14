using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace MongoDBDemoSync.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public abstract class BaseController : ControllerBase
    {

        internal readonly ILogger _logger;

        public BaseController(ILogger logger)
        {
            _logger = logger;
        }

    }
}
