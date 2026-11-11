using System.Security.Cryptography;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StockFlow.Web.Controllers;

namespace StockFlow.MasterDataChecks;

internal static class DatabaseUnavailableChecks
{
    public static async Task Run()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient(); // Test host uses fake repositories and ephemeral keys.
        using var scope = factory.Services.CreateScope();
        foreach (var path in new[] { "/Account/Login", "/api/bankaccounts" })
        {
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, TraceIdentifier = "synthetic-outage-<id>" };
            context.Response.Body = new MemoryStream();
            context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature
            {
                Path = path,
                Error = new CryptographicException("private key details", new NpgsqlException("private database endpoint", new TimeoutException()))
            });
            var controller = new ErrorLogController { ControllerContext = new ControllerContext { HttpContext = context } };
            var result = controller.Error();
            if (context.Response.StatusCode != 503) throw new Exception("Database outage must return 503.");
            await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));
            context.Response.Body.Position = 0;
            var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
            if (!body.Contains("cannot reach its database") || body.Contains("private database") || body.Contains("private key")) throw new Exception("Unsafe or missing outage response.");
            if (!path.StartsWith("/api") && (!body.Contains("&lt;id&gt;") || body.Contains("csrf-token") || body.Contains("<form"))) throw new Exception("Fallback view must encode request ID and render without the shared layout.");
            Console.WriteLine("PASS: wrapped database timeout returns safe rendered 503 on " + path);
        }
        var generic = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        generic.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature { Path = "/Inventory", Error = new CryptographicException("unrelated cryptography failure") });
        var fallback = new ErrorLogController { ControllerContext = new ControllerContext { HttpContext = generic } };
        fallback.Error();
        if (generic.Response.StatusCode != 500) throw new Exception("Unrelated errors must retain HTTP 500.");
        Console.WriteLine("PASS: unrelated encryption errors are not classified as database outages; no database connected.");
    }
}
