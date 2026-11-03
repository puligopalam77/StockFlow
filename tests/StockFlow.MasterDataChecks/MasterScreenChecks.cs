using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Interfaces;
namespace StockFlow.MasterDataChecks;
internal static class MasterScreenChecks
{
 public static async Task Run(Factory factory)
 {
  using var admin=factory.CreateClient(new(){AllowAutoRedirect=false});admin.DefaultRequestHeaders.Add("Test-Role","Admin");
  var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;var output=Path.Combine(root!.FullName,"tests/bin/master-screen-fixtures");Directory.CreateDirectory(output);
  foreach(var module in new[]{"Products","Categories","Brands","Suppliers","Customers"})
  {
   foreach(var action in new[]{"","Create","Edit","View"}){
    var url='/'+module+(action.Length>0?'/'+action:"")+(action is "Edit" or "View"?"?id=1":"");var response=await admin.GetAsync(url);Check((int)response.StatusCode==200,module+" "+(action.Length>0?action:"list")+" route renders");var html=await response.Content.ReadAsStringAsync();Check(html.Contains("/js/master-pages.")&&html.Contains("/css/master-pages."),"shared full-page registration assets render");await File.WriteAllTextAsync(Path.Combine(output,module+'-'+(action.Length>0?action:"List")+".html"),html);
   }
   Check((int)(await admin.GetAsync('/'+module+"/Edit")).StatusCode==404,"edit requires a record ID");Check((int)(await admin.GetAsync('/'+module+"/Unknown")).StatusCode==404,"unknown master action rejected");
   admin.DefaultRequestHeaders.Add("Test-Deny-Action",module.ToUpperInvariant()+":CREATE");Check((await admin.GetAsync('/'+module+"/Create")).Headers.Location?.OriginalString=="/Account/AccessDenied","create page enforces "+module+" create permission");admin.DefaultRequestHeaders.Remove("Test-Deny-Action");
   admin.DefaultRequestHeaders.Add("Test-Deny-Action",module.ToUpperInvariant()+":EDIT");Check((await admin.GetAsync('/'+module+"/Edit?id=1")).Headers.Location?.OriginalString=="/Account/AccessDenied","edit page enforces "+module+" edit permission");Check((int)(await admin.GetAsync('/'+module+"/View?id=1")).StatusCode==200,"view stays accessible without edit permission");admin.DefaultRequestHeaders.Remove("Test-Deny-Action");
  }
 }
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
}
