using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;

namespace StockFlow.Web.Filters;
public sealed class EWayExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not PostgresException ex) return;
        var status = ex.SqlState switch { "42501" => 403, "40001" => 409, "23514" or "23505" or "23503" or "22P02" => 400, _ => 0 };
        if (status == 0) return;
        context.Result = new ObjectResult(new { message = ex.SqlState is "23514" or "40001" ? ex.MessageText : "E-way source, action or retry is unavailable in this shop. Reload the saved preparation." }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
