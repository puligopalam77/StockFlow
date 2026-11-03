using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using StockFlow.Application.Common;

namespace StockFlow.Web.Help;

// Bounded feedback receipts contain no question text, customer names, answers or audio.
public class HelpTelemetry(IConfiguration config, ILogger<HelpTelemetry> logger)
{
    private sealed record Receipt(string Owner, string Status, long Milliseconds, DateTimeOffset Expires)
    { public int Voted; }
    private readonly ConcurrentDictionary<string,Receipt> receipts=new();
    private readonly object fileGate=new();
    public string Record(string owner,string status,long elapsed,string? metric)
    {
        var id=Guid.NewGuid().ToString("N");
        var now=DateTimeOffset.UtcNow;
        foreach(var item in receipts.Where(item=>item.Value.Expires<now).ToArray()) receipts.TryRemove(item.Key,out _);
        if(receipts.Count>=2000) foreach(var item in receipts.OrderBy(item=>item.Value.Expires).Take(100).ToArray()) receipts.TryRemove(item.Key,out _);
        receipts[id]=new(owner,status,elapsed,now.AddMinutes(15));
        Write(new {time=now,eventType="answer",answerId=id,status,milliseconds=elapsed,metric});
        return id;
    }
    public bool Feedback(string owner,string id,bool helpful)
    {
        if(!receipts.TryGetValue(id,out var receipt) || receipt.Owner!=owner || receipt.Expires<DateTimeOffset.UtcNow ||
            Interlocked.Exchange(ref receipt.Voted,1)!=0) return false;
        Write(new {time=DateTimeOffset.UtcNow,eventType="feedback",answerId=id,helpful,status=receipt.Status,milliseconds=receipt.Milliseconds});
        return true;
    }
    private void Write(object entry)
    {
        var json=JsonSerializer.Serialize(entry);
        logger.LogInformation("StockBot event {Event}",json);
        if(config.GetValue<bool>("HelpMonitoring:WriteFile",true))
        {
            try
            {
                var directory=Path.GetFullPath(config["HelpMonitoring:Directory"] ?? "HelpLog",AppContext.BaseDirectory);
                lock(fileGate)
                {
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(Path.Combine(directory,$"help-{DateTimeOffset.UtcNow:yyyy-MM-dd}.jsonl"),json+Environment.NewLine);
                    foreach(var file in Directory.EnumerateFiles(directory,"help-*.jsonl"))
                        if(DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file)[5..],"yyyy-MM-dd",null,System.Globalization.DateTimeStyles.None,out var day) && day<DateTime.UtcNow.Date.AddDays(-30)) File.Delete(file);
                }
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) {logger.LogWarning("StockBot monitoring file unavailable ({Type})",error.GetType().Name);}
        }
    }
    public static string Owner(BusinessScope scope,System.Security.Claims.ClaimsPrincipal user) =>
        $"{scope.TenantId}:{scope.UserId}:{scope.StoreId}:{scope.LocationId}:{scope.Consolidated}:"+user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
}

public class HelpTelemetryFilter(HelpTelemetry telemetry,BusinessScope scope) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next)
    {
        if(context.ActionDescriptor.RouteValues["action"]!="Ask") {await next();return;}
        var watch=Stopwatch.StartNew();
        var result=await next();
        var answer=(result.Result as ObjectResult)?.Value as HelpAnswer;
        var status=answer?.Status ?? (result.Exception is not null ? "failure" : $"http-{(result.Result as ObjectResult)?.StatusCode ?? 400}");
        var metric=context.HttpContext.Items["help.metric"] as string ?? (answer?.Sections.FirstOrDefault() is {} section ? "guide:"+section.ArticleId : null);
        var id=telemetry.Record(HelpTelemetry.Owner(scope,context.HttpContext.User),status,watch.ElapsedMilliseconds,metric);
        if(answer is not null && result.Result is ObjectResult objectResult) objectResult.Value=answer with {AnswerId=id};
    }
}
