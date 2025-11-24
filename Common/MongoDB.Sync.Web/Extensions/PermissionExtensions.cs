namespace MongoDB.Sync.Web.Extensions
{

    public record RequiresPermissionMetadata(string Permission);


    public static class PermissionExtensions
    {
        public static RouteHandlerBuilder RequiresPermission(
            this RouteHandlerBuilder builder,
            string permission)
        {
            builder.Add(endpointBuilder =>
            {
                endpointBuilder.Metadata.Add(new RequiresPermissionMetadata(permission));
            });

            // You probably also want to ensure it's authorized
            return builder.RequireAuthorization();
        }
    }
}
