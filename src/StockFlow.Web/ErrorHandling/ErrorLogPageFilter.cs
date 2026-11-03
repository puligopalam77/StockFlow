using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Filters;

namespace StockFlow.Web.ErrorHandling;
// Razor forms often return HTTP 200 with validation messages rather than throwing.
public sealed class ErrorLogPageFilter : IAsyncPageFilter
{
    private readonly ErrorLogService logs;
    // ASP.NET supplies these services when it creates this class.
    public ErrorLogPageFilter(ErrorLogService logs)
    {
        this.logs = logs;
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
    {
        return Task.CompletedTask;
    }

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        await next();
        if (!context.ModelState.IsValid)
        {
            var messages = context.ModelState.Values.SelectMany(value => value.Errors).Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "A submitted field is invalid." : error.ErrorMessage)
.Take(20);
            logs.Record(context.HttpContext, new ValidationException(string.Join(" ", messages)), 400);
        }
    }
}
