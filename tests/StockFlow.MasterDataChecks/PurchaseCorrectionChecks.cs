using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.Interfaces;
namespace StockFlow.MasterDataChecks;
internal static class PurchaseCorrectionChecks
{
 public static async Task Run(Factory original)
 {
  using var factory=original.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPurchaseChangeRepository>();s.AddSingleton<IPurchaseChangeRepository,ChangeFake>();}));
  using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
  var request=new PurchaseChangeRequest{RequestId=Guid.NewGuid(),Reason="Invoice correction"};
  Check((int)(await client.PostAsJsonAsync("/api/purchases/1/changes",request)).StatusCode==200,"purchase edit action reaches correction writer");
  client.DefaultRequestHeaders.Add("Test-Deny-Action","PURCHASES:EDIT");
  Check((int)(await client.PostAsJsonAsync("/api/purchases/1/changes",request)).StatusCode==403,"purchase edit permission enforced");
  var html=await client.GetStringAsync("/Purchases/Details/1");Check(!html.Contains("id=\"purchase-change-dialog\""),"edit dialog hidden without permission");
  client.DefaultRequestHeaders.Remove("Test-Deny-Action");client.DefaultRequestHeaders.Add("Test-Deny-Action","PURCHASES:APPROVE");request.Kind="FINANCIAL";
  Check((int)(await client.PostAsJsonAsync("/api/purchases/1/changes",request)).StatusCode==403,"financial correction requires approval");
  request.Kind="DETAILS";Check((int)(await client.PostAsJsonAsync("/api/purchases/1/changes",request)).StatusCode==200,"safe detail edit does not require stock approval");
  request.Reason="";Check((int)(await client.PostAsJsonAsync("/api/purchases/1/changes",request)).StatusCode==400,"change reason required");
  client.DefaultRequestHeaders.Remove("Test-Deny-Action");client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");request.Reason="Invoice correction";
  Check((int)(await client.PostAsJsonAsync("/api/purchases/1/changes",request)).StatusCode==400,"correction requires CSRF protection");
 }
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
 private sealed class ChangeFake:IPurchaseChangeRepository
 {
  public Task<IReadOnlyList<PurchaseChange>> HistoryAsync(long id)=>Task.FromResult<IReadOnlyList<PurchaseChange>>([]);
  public Task<long> ChangeAsync(long id,PurchaseChangeRequest input,int userId)=>Task.FromResult(1L);
 }
}
