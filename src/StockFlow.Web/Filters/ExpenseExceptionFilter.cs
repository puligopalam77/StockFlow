using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;
namespace StockFlow.Web.Filters;
public sealed class ExpenseExceptionFilter:IExceptionFilter
{
    public void OnException(ExceptionContext c)
    {
        if(c.Exception is not PostgresException p)return;
        var status=p.SqlState switch{"42501"=>403,"40001"=>409,"23514" or "23505" or "23503" or "23502" or "22P02" or "22007" or "22008" or "22003"=>400,_=>0};
        if(status==0)return;
        c.Result=new ObjectResult(new{message=status==409?"Record changed; reload before continuing.":status==403?"Action denied in the selected shop.":"Invalid request or conflicting retry. Check the details and reload."}){StatusCode=status};c.ExceptionHandled=true;
    }
}
