using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;
namespace StockFlow.MasterDataChecks;
internal static class CompanyProfileChecks
{
 public static async Task Run(Factory factory)
 {
  static void Check(bool good,string message){if(!good)throw new Exception(message);Console.WriteLine("PASS: "+message);}
  using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
  var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
  var repo=(FakeCompanySettingsRepository)factory.Services.GetRequiredService<ICompanySettingsRepository>();
  var fields=new Dictionary<string,string>{["Input.CompanyName"]="Test Store",["Input.LegalBusinessName"]="Example Legal Name",["Input.Address"]="123 Test Road",["Input.City"]="Bengaluru",["Input.State"]="Karnataka",["Input.PinCode"]="560038",["Input.MobileNumber"]="9876543210",["Input.AlternatePhone"]="9876543211",["Input.Email"]="shop@example.com",["Input.Website"]="https://example.com",["Input.PanNumber"]="ABCDE1234F",["Input.GstNumber"]="29ABCDE1234F1Z5",["Input.InvoicePrefix"]="BILL-{YYYY}-",["Input.ReceiptPrefix"]="RECEIVE-{YYYY}-",["Input.Currency"]="INR",["Input.DefaultTaxMode"]="INCLUSIVE",["Input.DefaultPaymentTermsDays"]="30",["Input.DefaultSalespersonId"]="1",["Input.InvoiceFooter"]="Thank you.",["Input.TermsAndConditions"]="Warranty follows manufacturer policy."};
  var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
  async Task<HttpResponseMessage> Post(byte[]? image=null,string type="image/png",bool remove=false){using var body=new MultipartFormDataContent();foreach(var pair in fields)body.Add(new StringContent(pair.Value),pair.Key);body.Add(new StringContent(remove?"true":"false"),"RemoveLogo");if(image is not null){var bytes=new ByteArrayContent(image);bytes.Headers.ContentType=new(type);body.Add(bytes,"LogoUpload","logo.png");}return await client.PostAsync("/Settings/Company",body);}
  Check((int)(await Post(png)).StatusCode==302 && repo.Saves==1,"All business and billing fields save through the real settings form");
  Check(repo.Settings.City=="Bengaluru" && repo.Settings.DefaultSalespersonId==1 && repo.Settings.DefaultPaymentTermsDays==30 && repo.Settings.PanNumber=="ABCDE1234F" && repo.Settings.LogoDataUrl!.StartsWith("data:image/png;base64,"),"Saved profile includes logo and all defaults");
  var html=await client.GetStringAsync("/Settings/Company");Check(html.Contains("data:image/png;base64,") && html.Contains("Terms &amp; conditions") && html.Contains("Example Legal Name"),"Reopening settings restores logo, business details and print terms");
  var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;var output=Path.Combine(root!.FullName,"tests/bin/company-profile-fixtures");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"Company.html"),html);
  Check((await client.GetStringAsync("/Sales/Create")).Contains("data-tax-inclusive=\"true\"") && (await client.GetStringAsync("/Sales/Create")).Contains("data-payment-terms=\"30\""),"New bills load saved tax mode and payment terms");
  var old=repo.Settings.LogoDataUrl;Check((int)(await Post()).StatusCode==302 && repo.Settings.LogoDataUrl==old,"Saving without a replacement retains the logo");
  var invoiceHtml=await client.GetStringAsync("/Sales/Invoice/4");Check(invoiceHtml.Contains("invoice-business-logo") && invoiceHtml.Contains("Snapshot Legal Name") && invoiceHtml.Contains("Saved Salesperson") && invoiceHtml.Contains("Saved warranty terms.") && invoiceHtml.Contains("ABCDE1234F"),"Printed invoices render the saved logo, business information, salesperson and terms");
  var legacyInvoice=await client.GetStringAsync("/Sales/Invoice/3");
  Check(System.Net.WebUtility.HtmlDecode(legacyInvoice).Contains("src=\""+old+"\"") && legacyInvoice.Contains("Rajesh Kumar"),"Older invoices automatically use the current saved business logo");
  await File.WriteAllTextAsync(Path.Combine(output,"Invoice.html"),legacyInvoice);
  var apiInvoice=await client.GetFromJsonAsync<StockFlow.Application.DTOs.Invoices.InvoiceResponse>("/api/invoices/4");
  Check(apiInvoice!.Company.LogoDataUrl==old && apiInvoice.Company.CompanyName=="Snapshot Shop" && apiInvoice.Company.LegalBusinessName=="Snapshot Legal Name" && apiInvoice.TotalAmount==118,"Invoice API updates branding without changing historical company details or amounts");
  foreach(var document in new[]{("Receipt","/StockInflow/Details/PURCHASE/1"),("Service","/Service/Details/1"),("Report","/Reports/ProductSales")})
  {
   var documentHtml=await client.GetStringAsync(document.Item2);
   Check(documentHtml.Contains("sf-document-brand") && System.Net.WebUtility.HtmlDecode(documentHtml).Contains("src=\""+old+"\""),document.Item1+" documents load the saved logo");
   await File.WriteAllTextAsync(Path.Combine(output,document.Item1+".html"),documentHtml);
  }
  Check((int)(await Post(png,"image/svg+xml")).StatusCode==200 && repo.Saves==2,"Unsupported image types cannot save");
  Check((int)(await Post(new byte[2000001])).StatusCode==200 && repo.Saves==2,"Oversized images cannot save");
  fields["Input.PinCode"]="bad";Check((int)(await Post()).StatusCode==200 && repo.Saves==2,"Invalid mandatory fields cannot save");fields["Input.PinCode"]="560038";
  Check((int)(await Post(remove:true)).StatusCode==302 && repo.Settings.LogoDataUrl is null,"Logo removal is persisted");
  Check(!(await client.GetStringAsync("/Sales/Invoice/4")).Contains("invoice-business-logo"),"Removing the shop logo also removes previously captured logos from document branding");
  var request=new CreateSaleRequest{PricesIncludeTax=true,Items=[new(){ProductId=1,Quantity=3,SellingPrice=100}],Payments=[new(){PaymentMode="CASH",Amount=300}],RequestId=Guid.NewGuid()};
  var quote=SaleRules.CalculateLines(request,[18],true);Check(quote.TotalAmount==300 && quote.Subtotal==254.24m && quote.TaxAmount==45.76m,"Inclusive tax extracts tax without adding it twice");
  var line=quote.Items[0];Check(line.NetUnitPrice*3-line.NetDiscountAmount+line.TaxAmount==line.LineTotal,"Stored net rates, rounding adjustment and tax reconcile exactly");
  request.PricesIncludeTax=false;request.Payments[0].Amount=354;Check(SaleRules.CalculateLines(request,[18],true).TotalAmount==354,"Exclusive tax calculation remains unchanged");
  var snapshot=JsonSerializer.Serialize(repo.Settings);repo.Settings.CompanyName="Changed";Check(JsonSerializer.Deserialize<StockFlow.Application.DTOs.Invoices.CompanySettings>(snapshot)!.CompanyName=="Test Store","Company snapshots preserve earlier settings");
  Console.WriteLine("Company profile checks passed with in-memory repositories; no application database writes.");
 }
}
