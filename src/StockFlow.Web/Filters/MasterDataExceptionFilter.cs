using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;

namespace StockFlow.Web.Filters;

public class MasterDataExceptionFilter : IExceptionFilter
{
    private readonly StockFlow.Web.ErrorHandling.ErrorLogService logs;
    // ASP.NET supplies these services when it creates this class.
    public MasterDataExceptionFilter(StockFlow.Web.ErrorHandling.ErrorLogService logs)
    {
        this.logs = logs;
    }

    public void OnException(ExceptionContext context)
    {
        var (status, message) = context.Exception switch
        {
            UnauthorizedAccessException => (403, "You do not have permission for this action."),
            PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege } => (403, "You do not have access to this business, location, or action."),
            ValidationException ex => (400, ex.Message),
            KeyNotFoundException ex => (404, ex.Message),
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => (409, "A record with this name, username, email, SKU, barcode, serial number, purchase number, or invoice number already exists."),
            PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } => (400, "The selected related record is not available."),
            PostgresException { SqlState: PostgresErrorCodes.CheckViolation } => (400, "One or more values are outside the allowed range."),
            _ => (0, "")
        };
        if (status == 0)
        {
            return;
        }

        logs.Record(context.HttpContext, context.Exception, status);
        context.Result = new ObjectResult(new
        {
            message
        })
        {
            StatusCode = status
        };
        context.ExceptionHandled = true;
    }
}
