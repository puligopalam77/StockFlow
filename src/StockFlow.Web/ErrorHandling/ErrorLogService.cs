using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StockFlow.Web.ErrorHandling;

public sealed class ErrorLogService(IConfiguration configuration, ILogger<ErrorLogService> logger)
{
    internal const string DataKey = "StockFlow.ErrorLog.RequestData";
    private const string LoggedKey = "StockFlow.ErrorLog.Recorded";
    private readonly object gate = new();
    // Resolve relative paths against the deployed application, regardless of the working directory.
    public string DirectoryPath
    {
        get;
    } = Path.GetFullPath(string.IsNullOrWhiteSpace(configuration["ErrorLog:Directory"]) ? "ErrorLog" : configuration["ErrorLog:Directory"]!,
AppContext.BaseDirectory);

    public void Record(HttpContext context, Exception? exception, int statusCode, string? message = null)
    {
        if (context.Items.ContainsKey(LoggedKey))
        {
            return;
        }

        context.Items[LoggedKey] = true;
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5));
        var entry = new
        {
            Time = now,
            UtcTime = now.ToUniversalTime(),
            RequestId = context.TraceIdentifier,
            Username = context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : "Anonymous",
            UserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
            TenantId = context.User.FindFirstValue("TenantId"),
            LocationId = context.User.FindFirstValue("LocationId"),
            Method = context.Request.Method,
            Path = context.Request.Path.Value,
            StatusCode = statusCode,
            Message = message ?? exception?.Message ?? "Request returned an error status.",
            ExceptionType = exception?.GetType().FullName,
            StackTrace = exception?.StackTrace,
            // Database Detail/Data can contain credentials or entire records; omit them.
            InnerExceptionType = exception?.InnerException?.GetType().FullName,
            RequestData = context.Items[DataKey]
        };
        try
        {
            var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            lock (gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(Path.Combine(DirectoryPath, $"error-{now:yyyy-MM-dd}.jsonl"), line);
            }
        }
        catch (Exception writeError)
        {
            logger.LogError(writeError, "Could not write error log for request {RequestId}", context.TraceIdentifier);
            if (exception is not null)
            {
                logger.LogError(exception, "Original error for request {RequestId}", context.TraceIdentifier);
            }
        }
    }

    internal static bool Sensitive(string name)
    {
        var key = name.Replace("_", "").Replace("-", "").ToLowerInvariant();
        return key.Split('.').Last() == "code" || new[]
        {
            "password",
            "secret",
            "token",
            "cookie",
            "sessionid",
            "authorization",
            "connectionstring",
            "recovery",
            "otp",
            "apikey",
            "credential",
            "verificationcode",
            "authenticatorcode",
            "twofactorcode"
        }.Any(key.Contains);
    }

    internal static JsonNode? Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToArray())
            {
                obj[key] = Sensitive(key) ? JsonValue.Create("[REDACTED]") : Redact(obj[key]?.DeepClone());
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                array[i] = Redact(array[i]?.DeepClone());
            }
        }

        return node;
    }
}
