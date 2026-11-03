using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StockFlow.Web.ErrorHandling;

public sealed class ErrorLogMiddleware
{
    private readonly RequestDelegate next;
    // ASP.NET supplies these services when it creates this class.
    public ErrorLogMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    private const int BodyLimit = 16 * 1024;
    public async Task InvokeAsync(HttpContext context, ErrorLogService logs)
    {
        try
        {
            if (!context.Items.ContainsKey(ErrorLogService.DataKey))
            {
                context.Items[ErrorLogService.DataKey] = await CaptureAsync(context.Request);
            }

            await next(context);
            if (context.Response.StatusCode >= 400)
            {
                logs.Record(context, null, context.Response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logs.Record(context, exception, 500);
            throw;
        }
    }

    private static async Task<object> CaptureAsync(HttpRequest request)
    {
        if(request.Path.StartsWithSegments("/api/ewaybills"))
            return new {Query="[E-way query omitted]",Body="[E-way party, transport and evidence data omitted]"};
        // Meta signup codes, webhook verification tokens and customer message content are private.
        if(request.Path.StartsWithSegments("/api/configuration/whatsapp") || request.Path.StartsWithSegments("/api/whatsapp"))
            return new {Query="[WhatsApp data omitted]",Body="[WhatsApp data omitted]"};
        // Help questions may contain account names or accidentally dictated private details.
        // Keep them out of generic error logs as well as assistant telemetry.
        if(request.Path.StartsWithSegments("/api/help"))
            return new {Query="[Help query omitted]",Body="[Help content omitted]"};
        var query = request.Query.Take(50).ToDictionary(p => p.Key, p => ErrorLogService.Sensitive(p.Key) ? "[REDACTED]" : Limit(p.Value.ToString()));
        object? body = null;
        // Never capture account credentials, uploads, unknown-length or oversized bodies.
        if (request.Path.StartsWithSegments("/Account"))
        {
            body = "[Account data omitted]";
        }
        else if (request.ContentLength is > 0 and <= BodyLimit)
        {
            var isJson = request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true;
            var isForm = request.ContentType?.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) == true;
            if (isJson || isForm)
            {
                request.EnableBuffering();
                try
                {
                    if (isForm)
                    {
                        var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
                        body = form.Take(50).ToDictionary(p => p.Key, p => ErrorLogService.Sensitive(p.Key) ? "[REDACTED]" : Limit(p.Value.ToString()));
                    }
                    else
                    {
                        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
                        var buffer = new char[BodyLimit + 1];
                        var count = await reader.ReadBlockAsync(buffer.AsMemory(), request.HttpContext.RequestAborted);
                        body = count > BodyLimit ? "[Body too large]" : ErrorLogService.Redact(JsonNode.Parse(new string(buffer, 0, count)));
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
                {
                    body = "[Request data could not be captured]";
                }
                finally
                {
                    request.Body.Position = 0;
                }
            }
            else
            {
                body = "[Unsupported body type omitted]";
            }
        }
        else if (request.ContentLength != 0)
        {
            body = "[Unknown-length or oversized body omitted]";
        }

        return new
        {
            Query = query,
            Body = body
        };
    }

    private static string Limit(string value)
    {
        if (value.Length > 1000)
        {
            return value[..1000] + "[truncated]";
        }
        else
        {
            return value;
        }
    }
}
