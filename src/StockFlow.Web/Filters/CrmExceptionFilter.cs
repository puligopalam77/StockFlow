using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;
namespace StockFlow.Web.Filters;
public sealed class CrmExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if(context.Exception is not PostgresException ex)return;
        var message=ex.SqlState switch
        {
            PostgresErrorCodes.CheckViolation=>ex.MessageText,
            PostgresErrorCodes.UniqueViolation=>"This CRM request or invoice link already exists. Refresh and review the saved record.",
            PostgresErrorCodes.ForeignKeyViolation=>"The selected customer, staff, product or enquiry is unavailable in this shop.",
            PostgresErrorCodes.InsufficientPrivilege=>"You do not have permission for this CRM action.",
            _=>null
        };
        if(message is null)return;context.Result=new ObjectResult(new{message}){StatusCode=ex.SqlState==PostgresErrorCodes.InsufficientPrivilege?403:400};context.ExceptionHandled=true;
    }
}
