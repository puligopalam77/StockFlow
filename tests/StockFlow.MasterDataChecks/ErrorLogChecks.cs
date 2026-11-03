using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using StockFlow.Application.Interfaces;
using StockFlow.Web.ErrorHandling;
using StockFlow.Web.Filters;

namespace StockFlow.MasterDataChecks;

static class ErrorLogChecks
{
    public static async Task Run()
    {
        var directory = Path.GetFullPath(Path.Combine("tests", "bin", "errorlog-checks", Guid.NewGuid().ToString("N")));
        var branches = new FakeBranchRepository { Failure = new InvalidOperationException("Simulated database outage") };
        using var factory = new Factory();
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["ErrorLog:Directory"] = directory, ["StockFlow:OpenBrowserTabs"] = "false" }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBranchRepository>();
                services.AddSingleton<IBranchRepository>(branches);
            });
        });
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var swagger = await client.GetAsync("/swagger/v1/swagger.json");
        var definition = JsonNode.Parse(await swagger.Content.ReadAsStringAsync());
        Check(swagger.StatusCode == HttpStatusCode.OK && definition?["paths"] is JsonObject paths
            && paths.Count > 0 && !paths.ContainsKey("/error-log"), "Swagger definition loads and excludes internal error endpoint");
        var api = await client.PostAsync("/api/products?token=hidden-query&search=desk",
            new StringContent("{\"name\":\"Desk\",\"SessionId\":\"hidden-session\",\"nested\":{\"password\":\"hidden-password\",\"session_id\":\"hidden-nested-session\"},\"items\":[{\"secret\":\"hidden-secret\"}]}", Encoding.UTF8, "application/json"));
        var apiText = await api.Content.ReadAsStringAsync();
        Check(api.StatusCode == HttpStatusCode.InternalServerError && api.Content.Headers.ContentType?.MediaType == "application/json", "middleware failure returns API JSON 500");
        Check(!apiText.Contains("Simulated database outage") && JsonNode.Parse(apiText)?["requestId"] is not null, "response hides exception and includes request ID");
        var files = Directory.GetFiles(directory, "error-*.jsonl");
        var lines = File.ReadAllLines(files.Single());
        var entry = JsonNode.Parse(lines.Single())!;
        Check(entry["Username"]!.GetValue<string>() == "Admin" && entry["Path"]!.GetValue<string>() == "/api/products", "log preserves user and original path, with no duplicate");
        Check(entry["Time"]!.GetValue<string>().EndsWith("+05:30") && entry["StackTrace"] is not null, "log includes India time and stack trace");
        Check(!lines[0].Contains("hidden-") && lines[0].Contains("Desk") && lines[0].Contains("desk"), "nested secrets and query token redacted, business data retained");
        Check(!lines[0].Contains("hidden-session") && !lines[0].Contains("hidden-nested-session"),"raw session identifiers are redacted from error details");
        var browser = await client.GetAsync("/");
        Check(browser.StatusCode == HttpStatusCode.InternalServerError && (await browser.Content.ReadAsStringAsync()).Contains("We could not complete your request"), "error controller renders page despite database failure");
        var form = await client.PostAsync("/api/products", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Name"] = "Chair", ["Password"] = "hidden-form" }));
        Check(form.StatusCode == HttpStatusCode.InternalServerError && !File.ReadAllText(files[0]).Contains("hidden-form"), "form data redacted");
        var barcode = await client.PostAsync("/api/products", new StringContent("{\"barcode\":\"8900123456789\",\"code\":\"hidden-otp\"}", Encoding.UTF8, "application/json"));
        Check(barcode.StatusCode == HttpStatusCode.InternalServerError && File.ReadAllText(files[0]).Contains("8900123456789") && !File.ReadAllText(files[0]).Contains("hidden-otp"), "business barcode retained while verification code redacted");
        var account = await client.PostAsync("/Account/ChangePassword", new StringContent("{\"value\":\"hidden-account\"}", Encoding.UTF8, "application/json"));
        Check(account.StatusCode == HttpStatusCode.InternalServerError && !File.ReadAllText(files[0]).Contains("hidden-account"), "account body omitted");
        var help=await client.PostAsync("/api/help/questions?term=private-help-customer",
            new StringContent("{\"question\":\"private-help-question\"}",Encoding.UTF8,"application/json"));
        var helpLog=File.ReadAllText(files[0]);
        var eway = await client.PostAsync("/api/ewaybills?term=private-eway-party",
            new StringContent("{\"evidence\":\"private-eway-evidence\",\"address\":\"private-eway-address\"}",Encoding.UTF8,"application/json"));
        var ewayLog = File.ReadAllText(files[0]);
        Check(eway.StatusCode == HttpStatusCode.InternalServerError && ewayLog.Contains("E-way party, transport and evidence data omitted") && !ewayLog.Contains("private-eway-"), "E-way addresses, evidence and search terms are omitted from generic error logs");
        Check(help.StatusCode==HttpStatusCode.InternalServerError && helpLog.Contains("Help content omitted") && !helpLog.Contains("private-help-"),"Assistant question text and search terms omitted from generic error logs");
        using var anonymous = app.CreateClient(new() { AllowAutoRedirect = false });
        var denied = await anonymous.GetAsync("/api/products");
        Check(denied.StatusCode == HttpStatusCode.Unauthorized && File.ReadAllLines(files[0]).Any(l => JsonNode.Parse(l)?["StatusCode"]?.GetValue<int>() == 401), "handled HTTP error logged");
        var loginHtml = await anonymous.GetStringAsync("/Account/Login");
        var tokenMatch = System.Text.RegularExpressions.Regex.Match(loginHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Check(tokenMatch.Success, "login antiforgery token available");
        var countBeforeValidation = File.ReadAllLines(files[0]).Length;
        var invalidLogin = await anonymous.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value), ["Input.Username"] = "", ["Input.Password"] = "" }));
        Check(invalidLogin.StatusCode == HttpStatusCode.OK && File.ReadAllLines(files[0]).Length == countBeforeValidation + 1,
            "Razor validation error logged when form returns HTTP 200");

        var logs = app.Services.GetRequiredService<ErrorLogService>();
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/example";
        var exceptionContext = new ExceptionContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [])
        { Exception = new ValidationException("Choose an active product.") };
        new MasterDataExceptionFilter(logs).OnException(exceptionContext);
        Check(exceptionContext.ExceptionHandled && ((ObjectResult)exceptionContext.Result!).StatusCode == 400, "known exception keeps validation response");
        logs.Record(http, exceptionContext.Exception, 400);
        Check(File.ReadAllLines(files[0]).Count(l => JsonNode.Parse(l)?["RequestId"]?.GetValue<string>() == http.TraceIdentifier) == 1, "handled exception recorded once");

        var blocked = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(blocked, "file");
        var failedWriter = new ErrorLogService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ErrorLog:Directory"] = blocked }).Build(), NullLogger<ErrorLogService>.Instance);
        failedWriter.Record(new DefaultHttpContext(), new Exception("Original failure"), 500);
        Check(true, "log write failure does not replace application error");
        Console.WriteLine("Error log checks passed. Test logs: " + directory);
    }

    private static void Check(bool passed, string name)
    { if (!passed) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
}
