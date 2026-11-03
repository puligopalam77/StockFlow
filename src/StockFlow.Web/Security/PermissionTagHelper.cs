using Microsoft.AspNetCore.Razor.TagHelpers;

namespace StockFlow.Web.Security;

[HtmlTargetElement("a")]
[HtmlTargetElement("*", Attributes = "permission-module")]
public class PermissionTagHelper : TagHelper
{
    private readonly PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public PermissionTagHelper(PermissionContext access)
    {
        this.access = access;
    }

    public override int Order => int.MaxValue;
    public string? PermissionModule
    {
        get; set;
    }
    public string PermissionAction { get; set; } = "VIEW";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (PermissionModule is not null)
        {
            if (!access.Can(PermissionModule, PermissionAction))
            {
                output.SuppressOutput();
            }

            return;
        }

        var href = output.Attributes["href"]?.Value?.ToString();
        if (href is null || !href.StartsWith('/') || href.StartsWith("//") || href.StartsWith("/Account/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var (module, action) = PermissionMiddleware.Requirement(href.Split('?', '#')[0], "GET");
        if (module is not null && !access.Can(module, action))
        {
            output.SuppressOutput();
        }
    }
}
