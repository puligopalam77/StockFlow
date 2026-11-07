using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;

namespace StockFlow.Web.Filters;

// Feature-specific validation messages; never return PostgreSQL Detail (which can contain row data).
public sealed class GstExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if(context.Exception is System.ComponentModel.DataAnnotations.ValidationException validation && validation.Value is string field && field is "AddressLine1" or "City")
        {
            context.Result=new BadRequestObjectResult(new{message=validation.Message,errors=new Dictionary<string,string[]>{{field,[validation.Message]}}});
            context.ExceptionHandled=true;return;
        }
        if(context.Exception is not PostgresException ex)return;
        if(ex.SqlState==PostgresErrorCodes.InsufficientPrivilege){context.Result=new ObjectResult(new{message="This GST change requires additional authorization."}){StatusCode=403};context.ExceptionHandled=true;return;}
        var message=ex.SqlState switch
        {
            PostgresErrorCodes.CheckViolation=>ex.MessageText,
            PostgresErrorCodes.UniqueViolation=>"This GSTIN, source document number or request has already been recorded. Review the existing record instead of creating a duplicate.",
            PostgresErrorCodes.ForeignKeyViolation=>"The selected legal business, registration, shop or source document is not available.",
            _=>null
        };
        if(message is null)return;
        context.Result=new BadRequestObjectResult(new{message});context.ExceptionHandled=true;
    }
}
