namespace StockFlow.MasterDataChecks;

internal static class RoleNavigationChecks
{
    // Render current Razor and sidebar without connecting to an application database.
    public static async Task Run()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var directory = Path.Combine("tests", "bin", "role-management-fixtures");
        Directory.CreateDirectory(directory);
        foreach (var (route, name) in new[] {
            ("/Admin/Roles", "List"), ("/Admin/Roles/Create", "Create"),
            ("/Admin/Roles/Edit/2", "Edit"), ("/Admin/Roles/View/2", "View"),
            ("/Admin/Users", "Users"), ("/Admin/Roles/Templates", "Templates") })
        {
            var html = await client.GetStringAsync(route);
            if (name is "Create" or "Edit" or "View" && !html.Contains("/js/common/role-permission-editor."))
                throw new Exception("Role page must load the navigation-aligned permission editor: " + route);
            await File.WriteAllTextAsync(Path.Combine(directory, name + ".html"), html);
        }
        var options=new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var catalogFile=Path.Combine(directory,"Modules.json");
        if(File.Exists(catalogFile)){
            var catalogJson=await File.ReadAllTextAsync(catalogFile);
            var defaults=StockFlow.Application.Common.DefaultRoleTemplates.All.Select(d=>new {
                template=new StockFlow.Application.DTOs.Security.RoleTemplateRow {TemplateKey=d.Key,TemplateName=d.Name,Description=d.Description,Limitations=d.Limitations,IsSystem=true,Version="system-v2"},
                permissions=StockFlow.Application.Common.DefaultRoleTemplates.Apply(d,System.Text.Json.JsonSerializer.Deserialize<List<StockFlow.Application.DTOs.Security.ModulePermission>>(catalogJson,options)!),
                omittedActions=0
            });
            await File.WriteAllTextAsync(Path.Combine(directory,"Templates.json"),System.Text.Json.JsonSerializer.Serialize(defaults,options));
        }
        Console.WriteLine("PASS: Current role pages and sidebar rendered without database access.");
    }
}
