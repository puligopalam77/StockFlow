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
  var fields=new Dictionary<string,string>{["Input.CompanyName"]="Test Store",["Input.LegalBusinessName"]="Example Legal Name",["Input.Address"]="123 Test Road",["Input.City"]="Bengaluru",["Input.State"]="Karnataka",["Input.PinCode"]="560038",["Input.MobileNumber"]="9876543210",["Input.AlternatePhone"]="9876543211",["Input.Email"]="shop@example.com",["Input.Website"]="https://example.com",["Input.PanNumber"]="ABCDE1234F",["Input.GstNumber"]="29ABCDE1234F1ZW",["Input.InvoicePrefix"]="BILL-{YYYY}-",["Input.ReceiptPrefix"]="RECEIVE-{YYYY}-",["Input.Currency"]="INR",["Input.DefaultTaxMode"]="INCLUSIVE",["Input.DefaultPaymentTermsDays"]="30",["Input.DefaultSalespersonId"]="1",["Input.InvoiceFooter"]="Thank you.",["Input.TermsAndConditions"]="Warranty follows manufacturer policy."};
  repo.Settings.InvoicePrefix="BILL-{YYYY}-";repo.Settings.ReceiptPrefix="RECEIVE-{YYYY}-";repo.Settings.DefaultTaxMode="INCLUSIVE";repo.Settings.DefaultSalespersonId=1;repo.Settings.DefaultPaymentTermsDays=30;repo.Settings.InvoiceFooter="Thank you.";repo.Settings.TermsAndConditions="Warranty follows manufacturer policy.";
  var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
  async Task<HttpResponseMessage> Post(byte[]? image=null,string type="image/png",bool remove=false){using var body=new MultipartFormDataContent();body.Add(new StringContent(CompanySettingsViews.Version(repo.Settings,"business")),"EditVersion");foreach(var pair in fields)body.Add(new StringContent(pair.Value),pair.Key);body.Add(new StringContent(remove?"true":"false"),"RemoveLogo");if(image is not null){var bytes=new ByteArrayContent(image);bytes.Headers.ContentType=new(type);body.Add(bytes,"LogoUpload","logo.png");}return await client.PostAsync("/Settings/Company",body);}
  var firstSave=await Post(png);Check((int)firstSave.StatusCode==302 && repo.Saves==1,"Business fields save through the real form while existing billing values remain intact: "+System.Text.RegularExpressions.Regex.Match(await firstSave.Content.ReadAsStringAsync(),"<div class=\"text-danger validation-summary-errors\"[\\s\\S]*?</div>").Value);
  Check(repo.Settings.City=="Bengaluru" && repo.Settings.DefaultSalespersonId==1 && repo.Settings.DefaultPaymentTermsDays==30 && repo.Settings.PanNumber=="ABCDE1234F" && repo.Settings.LogoDataUrl!.StartsWith("data:image/png;base64,"),"Saved profile includes logo and all defaults");
  var html=await client.GetStringAsync("/Settings/Company");var billingHtml=await client.GetStringAsync("/Settings/Company?section=billing");Check(html.Contains("data:image/png;base64,")&&!html.Contains("Terms &amp; conditions")&&html.Contains("Example Legal Name")&&billingHtml.Contains("Terms &amp; conditions"),"Reopening each settings view restores its own business or printing fields");
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
  var invalidLogo=await Post(new byte[12]);Check((int)invalidLogo.StatusCode==200&&repo.Settings.LogoDataUrl==old&&System.Net.WebUtility.HtmlDecode(await invalidLogo.Content.ReadAsStringAsync()).Contains(old!),"Invalid logo content cannot replace storage or hide the saved logo on the failed form");
  fields["Input.PinCode"]="bad";Check((int)(await Post()).StatusCode==200 && repo.Saves==2,"Invalid mandatory fields cannot save");fields["Input.PinCode"]="560038";
  Check((int)(await Post(remove:true)).StatusCode==302 && repo.Settings.LogoDataUrl is null,"Logo removal is persisted");
  Check(!(await client.GetStringAsync("/Sales/Invoice/4")).Contains("invoice-business-logo"),"Removing the shop logo also removes previously captured logos from document branding");
  var request=new CreateSaleRequest{PricesIncludeTax=true,Items=[new(){ProductId=1,Quantity=3,SellingPrice=100}],Payments=[new(){PaymentMode="CASH",Amount=300}],RequestId=Guid.NewGuid()};
  var quote=SaleRules.CalculateLines(request,[18],true);Check(quote.TotalAmount==300 && quote.Subtotal==254.24m && quote.TaxAmount==45.76m,"Inclusive tax extracts tax without adding it twice");
  var line=quote.Items[0];Check(line.NetUnitPrice*3-line.NetDiscountAmount+line.TaxAmount==line.LineTotal,"Stored net rates, rounding adjustment and tax reconcile exactly");
  request.PricesIncludeTax=false;request.Payments[0].Amount=354;Check(SaleRules.CalculateLines(request,[18],true).TotalAmount==354,"Exclusive tax calculation remains unchanged");
  var snapshot=JsonSerializer.Serialize(repo.Settings);repo.Settings.CompanyName="Changed";Check(JsonSerializer.Deserialize<StockFlow.Application.DTOs.Invoices.CompanySettings>(snapshot)!.CompanyName=="Test Store","Company snapshots preserve earlier settings");
  repo.GstIdentity=new(42,"Authoritative GST legal name","27AAPFU0939F1ZV","AAPFU0939F","REGULAR","27","Synthetic active shop","ELECTRICAL");
  var mappedHtml=await client.GetStringAsync("/Settings/Company");Check(mappedHtml.Contains("Authoritative GST legal name")&&mappedHtml.Contains("Manage GST Settings")&&mappedHtml.Contains("Example Legal Name"),"Effective GST identity is read-only and distinct from editable company fallback");
  await File.WriteAllTextAsync(Path.Combine(output,"CompanyMapped.html"),mappedHtml);repo.GstIdentity=null;
  fields["UseStructuredAddress"]="true";fields["Input.AddressLine1"]="12 New Road";fields["Input.AddressLine2"]="Locality";fields["Input.District"]="Urban District";
  Check((int)(await Post()).StatusCode==302&&repo.Settings.Address=="12 New Road\nLocality\nUrban District","Real form explicitly saves structured company address without duplicating city/state in print lines");
  var converted=repo.Settings.Address;fields["Input.AddressLine1"]="";var invalidResponse=await Post();await File.WriteAllTextAsync(Path.Combine(output,"CompanyInvalid.html"),await invalidResponse.Content.ReadAsStringAsync());Check((int)invalidResponse.StatusCode==200&&repo.Settings.Address==converted,"Missing structured line 1 rejects save and preserves existing stored address");fields["Input.AddressLine1"]="12 New Road";
  using var deniedClient=factory.CreateClient(new(){AllowAutoRedirect=false});deniedClient.DefaultRequestHeaders.Add("Test-Role","Sales");var denied=await deniedClient.GetAsync("/Settings/Company");Check((int)denied.StatusCode==403||(int)denied.StatusCode==302&&denied.Headers.Location!.ToString().Contains("AccessDenied"),"Company settings read requires its own permission");
  Console.WriteLine("Company profile checks passed with in-memory repositories; no application database writes.");
 }
}
