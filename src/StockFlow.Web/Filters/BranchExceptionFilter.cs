using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;
namespace StockFlow.Web.Filters;
public sealed class BranchExceptionFilter:IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var message=context.Exception is System.Data.DBConcurrencyException?context.Exception.Message:context.Exception is PostgresException p?p.SqlState switch
        {
            "23505"=>"This store code already exists. Review its details before retrying.",
            "23503"=>"The selected store, location or user is unavailable in this business.",
            "23514"=>p.MessageText,
            "40P01" or "55P03"=>"Store data is busy. Reload and retry this change.",
            _=>null
        }:null;
        if(message is null)return;
        context.Result=new ObjectResult(new{message}){StatusCode=context.Exception is System.Data.DBConcurrencyException||context.Exception is PostgresException e&&e.SqlState is "23505" or "40P01" or "55P03"?409:400};context.ExceptionHandled=true;
    }
}
