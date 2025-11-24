using System.Threading.Tasks;

namespace MongoDB.Sync.Web.Services
{
    // Minimal generic authentication/authorization surface
    public interface IGenericAuthService
    {
        Task<string?> ValidateTokenAsync(HttpContext? httpContext, string? token);

        Task<bool> AuthorizeAppAsync(HttpContext? httpContext, string? userId, string appId);
    }
}