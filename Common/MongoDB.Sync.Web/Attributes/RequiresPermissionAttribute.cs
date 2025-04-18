using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;

namespace MongoDB.Sync.Web.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class RequiresPermissionAttribute : AuthorizeAttribute, IAsyncAuthorizationFilter
    {
        private readonly string _requiredPermission;

        public RequiresPermissionAttribute(string requiredPermission)
        {
            _requiredPermission = requiredPermission;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            // Get the permissions claim from the user
            var user = context.HttpContext.User;
            var permissionsClaim = user.FindFirst("Permissions")?.Value;

            if (string.IsNullOrEmpty(permissionsClaim) || !permissionsClaim.Split(',').Contains(_requiredPermission))
            {
                // Deny access if the required permission is missing
                context.Result = new ForbidResult();
                return;
            }

            await Task.CompletedTask;
        }
    }
}
