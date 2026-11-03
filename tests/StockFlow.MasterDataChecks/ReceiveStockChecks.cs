using System.Net.Http.Json;
using System.Text.Json.Nodes;
namespace StockFlow.MasterDataChecks;
internal static class ReceiveStockChecks
{
 public static async Task Run(Factory factory){
  using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");
  var html=await client.GetStringAsync("/Purchases/Create");
  foreach(var caption in new[]{"Supplier &amp; Receipt Info","Find or Add Products","Items in This Receipt","Invoice Charges &amp; Attachment","Receipt Summary &amp; Actions","Review Receipt","Confirm &amp; Receive Stock"})Check(html.Contains(caption),caption+" renders");
  Check(!html.Contains("id=\"goods-bulk\"")&&html.Contains("href=\"/Products/Create\""),"variants are selected from registered catalog; creation uses existing Product screen");
  Check((await client.GetFromJsonAsync<JsonArray>("/api/products/variants?receiving=true&term="))!.Count==0,"empty receiving search does not load the catalog");
  Check((int)(await client.GetAsync("/api/products/variants?receiving=true&term="+new string('x',201))).StatusCode==400,"receiving search bounds input length");
  client.DefaultRequestHeaders.Add("Test-Deny","PRODUCT_VARIANTS");Check((int)(await client.GetAsync("/api/products/variants?receiving=true&term=helmet")).StatusCode==403,"receiving search retains variant permissions");
  await VariantUiFixtures.Write(factory);
 }
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
}
