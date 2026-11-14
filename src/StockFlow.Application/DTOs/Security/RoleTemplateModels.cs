using System.ComponentModel.DataAnnotations;
namespace StockFlow.Application.DTOs.Security;

public sealed class RoleTemplateRow
{
    public int? TemplateId { get; set; }
    public string TemplateKey { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }
    public string Version { get; set; } = "";
    public string? Limitations { get; set; }
}
public sealed record RoleTemplateDetails(RoleTemplateRow Template, IEnumerable<ModulePermission> Permissions, int OmittedActions);
public sealed class SaveRoleTemplateRequest
{
    public string? TemplateKey { get; set; }
    [Required, StringLength(50, MinimumLength = 2)] public string TemplateName { get; set; } = "";
    [StringLength(200)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ExpectedVersion { get; set; }
    [Required] public List<ModulePermission> Permissions { get; set; } = [];
}
