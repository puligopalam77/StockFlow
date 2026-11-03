using System.Net.Http.Json;
using System.Text.Json.Nodes;
namespace StockFlow.MasterDataChecks;
internal static class PurchaseReceivingChecks
{
 public static async Task Run(Factory factory)
 {
  using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
  var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
  var html=await client.GetStringAsync("/Purchases/Create");
  Check(html.Contains("id=\"purchase-options\"")&&html.Contains("id=\"goods-register\"")&&html.Contains("href=\"/Products\"")&&!html.Contains("id=\"purchase-registration\"")&&html.Contains("id=\"receipt-confirm\"")&&html.Contains("stockflow.goods."),"receiving includes review, scoped draft recovery and navigation to existing product registration");
  foreach(var (url,content) in new[]{("/Purchases","id=\"purchase-message\""),("/Purchases/Create","id=\"purchase-workspace\""),("/Purchases/Details/1","id=\"purchase-message\""),("/StockInflow","id=\"stock-receipt-history\""),("/StockInflow/Details/PURCHASE/1","id=\"stock-receipt-detail\"")})
  {
   var page=await client.GetStringAsync(url);var caption=page.IndexOf("class=\"sf-page-heading",StringComparison.Ordinal);var breadcrumb=page.IndexOf("id=\"app-breadcrumb\"",StringComparison.Ordinal);var body=page.IndexOf(content,StringComparison.Ordinal);
   Check(caption>=0&&caption<breadcrumb&&breadcrumb<body,"receive stock caption precedes breadcrumb and content on "+url);
  }
  html=await client.GetStringAsync("/Purchases/Details/1");
  Check(html.Contains("purchase-details.")&&html.Contains("id=\"purchase-products\"")&&html.Contains("id=\"purchase-bottom\"")&&html.Contains("Purchase Orders"),"purchase dashboard renders cards, compact items and Purchase Orders navigation");
  foreach(var (action,control) in new[]{("PURCHASES:PAY","supplier-payment-form"),("PURCHASES:RETURN","purchase-return-form"),("STOCK_INFLOW:RECEIVE","receipt-form")})
  {
   client.DefaultRequestHeaders.Add("Test-Deny-Action",action);
   html=await client.GetStringAsync("/Purchases/Details/1");
   Check(!html.Contains("id=\""+control+"\""),"dashboard hides unauthorized form for "+action);
   client.DefaultRequestHeaders.Remove("Test-Deny-Action");
  }
  client.DefaultRequestHeaders.Add("Test-Deny-Action","PRODUCT_VARIANTS:CREATE");
  html=await client.GetStringAsync("/Purchases/Create");Check(!html.Contains("id=\"goods-register\"")&&!html.Contains("id=\"purchase-registration\""),"product registration link requires variant creation permission");
  Check((int)(await client.PostAsJsonAsync("/api/products",new{productName="Denied inline product",categoryId=1,trackingType="SERIAL",variants=new[]{new{variantName="DEFAULT",sku="DENIED-INLINE"}}})).StatusCode==403,"direct product API cannot bypass inline configuration permissions");
  client.DefaultRequestHeaders.Remove("Test-Deny-Action");client.DefaultRequestHeaders.Add("Test-Deny-Action","PURCHASES:APPROVE");
  html=await client.GetStringAsync("/Purchases/Create");Check(html.Contains("id=\"purchase-save\"")&&html.Contains("disabled=\"disabled\""),"direct receiving is disabled without stock approval");
  html=await client.GetStringAsync("/Purchases/Create?mode=order");Check(html.Contains("Save purchase order")&&html.Contains("data-direct-receipt=\"false\""),"staff without approval can create purchase orders separately");
  Check((int)(await client.PostAsJsonAsync("/api/purchases/1/complete",new{})).StatusCode==403,"stock completion API remains approval protected");
  client.DefaultRequestHeaders.Remove("Test-Deny-Action");client.DefaultRequestHeaders.Add("Test-Deny","SCAN");
  html=await client.GetStringAsync("/Purchases/Create");Check(html.Contains("data-can-scan=\"false\"")&&html.Contains("id=\"goods-search\""),"manual item lookup remains available without scanner permission");
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
