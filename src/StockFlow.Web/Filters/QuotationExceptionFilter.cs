using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;
namespace StockFlow.Web.Filters;

public sealed class QuotationExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if(context.Exception is not PostgresException ex)return;
        var status=ex.SqlState switch {"42501"=>403,"40001"=>409,"23514" or "23505" or "23503"=>400,_=>0};
        if(status==0)return;
        context.Result=new ObjectResult(new{message=ex.SqlState is "23503" or "23505"?"Quotation source or retry conflict. Reload the saved record.":ex.SqlState=="42501"?"Quotation action is not permitted in this shop.":ex.MessageText}){StatusCode=status};
        context.ExceptionHandled=true;
    }
}
