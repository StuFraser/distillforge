using DistillForge.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace DistillForge.Api.Auth;

/// <summary>
/// Default-deny gate for every HTTP function: no valid token, no entry (401, the function never runs). On success the user's
/// row id is put on the invocation's <see cref="DistillForgeDbContext"/>, whose query filters then scope all data to them.
/// Non-HTTP triggers (the extraction queue) are not callable from outside; the runner sets the owner itself.
/// </summary>
public sealed class AuthMiddleware(IRequestAuthenticator authenticator) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var http = context.GetHttpContext();
        if (http is null)
        {
            await next(context);
            return;
        }

        var ct = http.RequestAborted;
        var result = await authenticator.AuthenticateAsync(http.Request.Headers.Authorization.ToString(), ct);
        if (!result.Succeeded)
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            http.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }

        var provisioner = context.InstanceServices.GetRequiredService<UserProvisioner>();
        var db = context.InstanceServices.GetRequiredService<DistillForgeDbContext>();
        db.CurrentUserId = await provisioner.EnsureUserAsync(result.User!, ct);
        await next(context);
    }
}
