namespace Masuit.MyBlogs.Core.Extensions;

/// <summary>
/// Serves immutable release metadata from the application root, not mounted wwwroot.
/// </summary>
public static class ReleaseMetadataExtensions
{
    public static IApplicationBuilder UseReleaseMetadata(this IApplicationBuilder app, IWebHostEnvironment environment)
    {
        return app.MapWhen(context => context.Request.Path.Equals("/version.json"), branch => branch.Run(async context =>
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                context.Response.Headers.Allow = "GET, HEAD";
                return;
            }

            var file = Path.Combine(environment.ContentRootPath, "version.json");
            if (!File.Exists(file))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.ContentType = "application/json; charset=utf-8";
                if (!HttpMethods.IsHead(context.Request.Method))
                {
                    await context.Response.WriteAsync("{\"error\":\"Release metadata is unavailable\"}");
                }
                return;
            }

            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength = new FileInfo(file).Length;
            if (!HttpMethods.IsHead(context.Request.Method))
            {
                await context.Response.SendFileAsync(file, context.RequestAborted);
            }
        }));
    }
}
