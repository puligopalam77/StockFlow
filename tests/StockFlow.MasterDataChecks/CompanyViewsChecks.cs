using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Invoices;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;

internal static class CompanyViewsChecks
{
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
 static CompanySettings Copy(CompanySettings s)=>JsonSerializer.Deserialize<CompanySettings>(JsonSerializer.Serialize(s))!;
 static async Task Reject(Func<Task> action,string label){try{await action();}catch(Exception e)when(e is ValidationException or UnauthorizedAccessException or PostgresException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
 public static async Task Run()
 {
  const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";var db="company_views_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+db+" TEMPLATE product_edit_069_test");
  try{
   await using var c=new NpgsqlConnection(host+db);await c.OpenAsync();foreach(var f in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await c.ExecuteAsync(await File.ReadAllTextAsync(f));
   await c.ExecuteAsync("UPDATE users SET must_change_password=false WHERE user_id=1; UPDATE user_action_permissions SET is_allowed=true WHERE user_id=1; SET app.tenant_id='1'; SET app.user_id='1'; SET app.store_id='1'; SET app.location_id='1'; SET app.business_profile_code='ELECTRICAL'");
   var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+db}}).Build();var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,IsOwner=true,CanReviewStores=true,ProfileDataVisibilityAvailable=true,BusinessProfileCode="ELECTRICAL"};var factory=new DbConnectionFactory(config,scope);var repo=new CompanySettingsRepository(factory);
   var original=new CompanySettings{CompanyName="Synthetic business",LegalBusinessName="Synthetic legal fallback",Address="Unsplit original\naddress",City="Mumbai",State="Maharashtra",PinCode="400001",MobileNumber="9876543210",Email="synthetic@example.test",InvoicePrefix="ORIGINAL-",ReceiptPrefix="RECEIPT-",InvoiceFooter="Saved footer",TermsAndConditions="Saved terms",LogoDataUrl="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="};await repo.SaveAsync(original,1);
   var history=await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(s) ORDER BY sale_id)::text FROM sales s");var stock=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions");var numbering=await c.ExecuteScalarAsync<long>("SELECT last_value FROM invoice_number_seq");
   var before=await repo.GetAsync();var business=Copy(before);business.CompanyName="Concurrent business";business.InvoicePrefix="FORGED-";var billing=Copy(before);billing.InvoicePrefix="CONCURRENT-";billing.CompanyName="Forged identity";billing.LogoDataUrl=null;billing.InvoiceFooter="Concurrent footer";
   await Task.WhenAll(repo.SaveSectionAsync(business,"business",CompanySettingsViews.Version(before,"business"),1),repo.SaveSectionAsync(billing,"billing",CompanySettingsViews.Version(before,"billing"),1));var saved=await repo.GetAsync();
   Check(saved.CompanyName=="Concurrent business"&&saved.InvoicePrefix=="CONCURRENT-"&&saved.InvoiceFooter=="Concurrent footer"&&saved.LogoDataUrl==before.LogoDataUrl&&saved.Address==before.Address,"Concurrent different-view saves merge the latest locked row and ignore forged unrelated fields");
   await Reject(()=>repo.SaveSectionAsync(business,"business",CompanySettingsViews.Version(before,"business"),1),"Same-view stale version cannot silently overwrite newer changes");
   await Reject(()=>repo.SaveSectionAsync(billing,"unexpected","anything",1),"Repository rejects arbitrary settings section");await Reject(()=>repo.SaveSectionAsync(billing,"billing","",1),"Missing edit version rejected");
   var invalid=Copy(saved);invalid.InvoicePrefix="{BAD}";await Reject(()=>repo.SaveSectionAsync(invalid,"billing",CompanySettingsViews.Version(saved,"billing"),1),"Scoped saves retain invoice prefix validation");
   await c.ExecuteAsync("INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT 1,2,a.module_action_id,false FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='COMPANY_SETTINGS' AND a.action_code='EDIT' ON CONFLICT(user_id,module_action_id) DO UPDATE SET is_allowed=false");
   var staffScope=new BusinessScope{TenantId=1,UserId=2,StoreId=1,LocationId=1};var staff=new CompanySettingsRepository(new DbConnectionFactory(config,staffScope));await Reject(()=>staff.SaveSectionAsync(saved,"business",CompanySettingsViews.Version(saved,"business"),2),"Unauthorized company edit denied at repository");await Reject(()=>staff.SaveSectionAsync(saved,"billing",CompanySettingsViews.Version(saved,"billing"),2),"Unauthorized billing edit denied at repository");
   scope.TenantId=999;await Reject(()=>repo.SaveSectionAsync(saved,"business",CompanySettingsViews.Version(saved,"business"),1),"Cross-tenant settings write denied");scope.TenantId=1;
   using var baseWeb=new Factory();using var web=baseWeb.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();s.RemoveAll<DbConnectionFactory>();s.AddScoped(sp=>new DbConnectionFactory(config,sp.GetRequiredService<BusinessScope>()));s.RemoveAll<IBranchRepository>();s.AddScoped<IBranchRepository,BranchRepository>();s.RemoveAll<ISecurityRepository>();s.AddScoped<ISecurityRepository,SecurityRepository>();s.RemoveAll<IBusinessProfileRepository>();s.AddScoped<IBusinessProfileRepository,BusinessProfileRepository>();s.RemoveAll<ICompanySettingsRepository>();s.AddScoped<ICompanySettingsRepository,CompanySettingsRepository>();}));
   using var client=web.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");client.DefaultRequestHeaders.Add("Test-Version",(await c.ExecuteScalarAsync<long>("SELECT security_version FROM users WHERE user_id=1")).ToString());var csrf=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf!["requestToken"]!.GetValue<string>());
   var fixtures="tests/bin/company-views-fixtures";Directory.CreateDirectory(fixtures);
   foreach(var section in new[]{"business","billing"}){
    var html=await client.GetStringAsync("/Settings/Company?section="+section);await File.WriteAllTextAsync(fixtures+"/"+section+".html",html);var title=section=="business"?"Business Details":"Invoice Settings";
    Check(html.Contains(title)&&!html.Contains("Shop Details &amp; Bill Settings"),"Distinct heading and document title for "+section);
    Check(html.Contains(">Settings</span>")&&html.Contains(">Business Setup</span>"),"Shared breadcrumbs include non-link Settings/Business Setup for "+section);
    var nav=Regex.Match(html,"<nav class=\"sf-nav sf-shop-nav\"[\\s\\S]*?</nav>").Value;Check(Regex.Matches(nav,"aria-current=\"page\"").Count==1&&Regex.IsMatch(nav,"href=\"/Settings/Company\\?section="+section+"\" aria-current=\"page\""),"Only correct sidebar destination is current for "+section);
    Check(section=="business"?!html.Contains("id=\"Input_InvoicePrefix\"")&&!html.Contains("id=\"billing-policy-form\""):!html.Contains("id=\"Input_CompanyName\"")&&!html.Contains("id=\"LogoUpload\""),"Unrelated form controls are absent from "+section);
   }
   var plain=await client.GetStringAsync("/Settings/Company");Check(plain.Contains("data-section=\"business\"")&&plain.Contains("data-validation-failed=\"false\""),"Plain legacy route defaults to Business Information without spurious validation errors");Check((int)(await client.GetAsync("/Settings/Company?section=forged")).StatusCode==400,"Invalid view rejected by actual HTTP handler");
   string Version(string html)=>System.Net.WebUtility.HtmlDecode(Regex.Match(html,"id=\"EditVersion\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
   async Task<HttpResponseMessage> Post(string section,CompanySettings values,string version){var fields=CompanySettingsViews.Fields(section).Where(k=>k!="LogoDataUrl").ToDictionary(k=>"Input."+k,k=>typeof(CompanySettings).GetProperty(k)!.GetValue(values)?.ToString()??"");fields["EditVersion"]=version;fields["Input."+(section=="business"?"InvoicePrefix":"CompanyName")]="FORGED";return await client.PostAsync("/Settings/Company?section="+section,new FormUrlEncodedContent(fields));}
   var businessHtml=await client.GetStringAsync("/Settings/Company?section=business");var billingHtml=await client.GetStringAsync("/Settings/Company?section=billing");saved=await repo.GetAsync();var first=Copy(saved);first.CompanyName="HTTP business only";var second=Copy(saved);second.InvoiceFooter="HTTP billing footer";
   var results=await Task.WhenAll(Post("business",first,Version(businessHtml)),Post("billing",second,Version(billingHtml)));Check(results.All(r=>(int)r.StatusCode==302),"Actual simultaneous partial HTTP forms both save through existing repository");saved=await repo.GetAsync();Check(saved.CompanyName==first.CompanyName&&saved.InvoiceFooter==second.InvoiceFooter&&saved.InvoicePrefix=="CONCURRENT-"&&saved.Address==original.Address&&saved.LogoDataUrl==original.LogoDataUrl,"Partial HTTP saves preserve identity/logo/address and unrelated billing fields");
   var stale=await Post("business",first,Version(businessHtml));var staleHtml=await stale.Content.ReadAsStringAsync();await File.WriteAllTextAsync(fixtures+"/business-invalid.html",staleHtml);Check((int)stale.StatusCode==200&&staleHtml.Contains("changed since")&&staleHtml.Contains("HTTP business only"),"Stale actual HTTP save retains entered values and actionable validation");
   var currentBilling=await client.GetStringAsync("/Settings/Company?section=billing");var policyBefore=await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(p))::text FROM billing_policy p");var invalidResponse=await Post("billing",invalid,Version(currentBilling));await File.WriteAllTextAsync(fixtures+"/billing-invalid.html",await invalidResponse.Content.ReadAsStringAsync());Check((int)invalidResponse.StatusCode==200&&(await repo.GetAsync()).InvoicePrefix=="CONCURRENT-","Invalid partial HTTP billing payload cannot reset the stored prefix");
   Check(await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(p))::text FROM billing_policy p")==policyBefore,"Company-view saves never save or reset the separate active-shop policy");
   var missing=await client.PostAsync("/Settings/Company?section=billing",new FormUrlEncodedContent(new Dictionary<string,string>{{"EditVersion",Version(currentBilling)},{"Input.InvoiceFooter","Incomplete form"}}));Check((int)missing.StatusCode==200&&(await repo.GetAsync()).InvoicePrefix=="CONCURRENT-","Missing required billing controls cannot reset values to DTO defaults");
   client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Check((int)(await client.PostAsync("/Settings/Company?section=billing",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode==400,"Partial form mutation still requires antiforgery");
   Check(await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(s) ORDER BY sale_id)::text FROM sales s")==history&&await c.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions")==stock&&await c.ExecuteScalarAsync<long>("SELECT last_value FROM invoice_number_seq")==numbering,"View saves preserve complete historical sale rows, stock and numbering sequence");Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM company_settings")==1,"Views reuse one existing company record");Console.WriteLine("Company views HTTP/PostgreSQL checks passed in disposable clone.");
   await c.ExecuteAsync("UPDATE company_settings SET company_name='Padded legacy name  ',address='Exact saved combined address',profile=profile||'{\"AddressLine1\":\"Separate saved line\"}'::jsonb WHERE company_settings_id=1");
   var legacy=await repo.GetAsync();var legacyBilling=Copy(legacy);legacyBilling.InvoiceFooter="Billing-only legacy update";await repo.SaveSectionAsync(legacyBilling,"billing",CompanySettingsViews.Version(legacy,"billing"),1);
   var retained=await repo.GetAsync();Check(retained.CompanyName==legacy.CompanyName&&retained.Address==legacy.Address&&retained.AddressLine1==legacy.AddressLine1,"Billing save does not normalize or reformat existing business name/address values");
  }finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH(FORCE)");}
 }
}
