using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Web.Services;

namespace StockFlow.MasterDataChecks;

internal static class GstImportHttpChecks
{
    // Called only with the disposable GST test database, never application configuration.
    public static async Task Run(string connectionString,string completedCsv)
    {
        using var factory=new Factory();using var app=factory.WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,cfg)=>cfg.AddInMemoryCollection(new Dictionary<string,string?>
        {{"ConnectionStrings:DefaultConnection",connectionString},{"ConnectionStrings:NotificationWorker",connectionString},{"Notifications:WorkerEnabled","false"},{"Notifications:WhatsApp:Enabled","false"}})));
        using var client=app.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var csrf=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf!["requestToken"]!.GetValue<string>());
        var template=await client.GetAsync("/api/gst/settings/catalog/template?format=xlsx&Search=GST%20Perf&Page=2&PageSize=10");template.EnsureSuccessStatusCode();
        using var csv=await client.GetAsync("/api/gst/settings/catalog/template?format=csv&Search=GST%20Perf");csv.EnsureSuccessStatusCode();
        var rows=GstMappingSpreadsheet.ReadCsv(Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync()).TrimStart('\ufeff'));
        Check(rows.Count==2001,"Authenticated CSV template endpoint returns all 2,000 scoped products");
        Check(template.Content.Headers.ContentType!.MediaType=="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","Excel template endpoint returns a real XLSX workbook");
        using(var zip=new System.IO.Compression.ZipArchive(new MemoryStream(await template.Content.ReadAsByteArrayAsync())))Check(zip.GetEntry("xl/worksheets/sheet2.xml") is not null,"Downloaded Excel file includes instructions");
        await using var c=new NpgsqlConnection(connectionString);await c.OpenAsync();var before=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_tax_configurations");
        async Task<HttpResponseMessage> Upload(byte[] bytes,string name)
        {
            using var body=new MultipartFormDataContent();body.Add(new ByteArrayContent(bytes),"file",name);body.Add(new StringContent(Guid.NewGuid().ToString()),"requestId");return await client.PostAsync("/api/gst/settings/catalog/import",body);
        }
        foreach(var (bytes,name) in new[]{(Encoding.UTF8.GetBytes(completedCsv),"filled.csv"),(GstMappingSpreadsheet.Xlsx(completedCsv,true),"filled.xlsx")})
        {
            using var preview=await Upload(bytes,name);preview.EnsureSuccessStatusCode();var result=await preview.Content.ReadFromJsonAsync<JsonObject>();Check(result!["counts"]!["READY"]!.GetValue<int>()==2000,"Real multipart "+name+" uploads validate 2,000 rows without applying mappings");
        }
        Check(await c.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_tax_configurations")==before,"File validation endpoints perform no tax mapping writes");
        Check((int)(await Upload(Encoding.UTF8.GetBytes("broken"),"corrupt.xlsx")).StatusCode==400,"Corrupt XLSX gets a clear server validation response");
        Check((int)(await Upload([],"empty.csv")).StatusCode==400,"Empty uploads are rejected on the server");
        Check((int)(await Upload(Encoding.UTF8.GetBytes(completedCsv),"bad.txt")).StatusCode==400,"Server rejects unsupported extensions independently of the browser");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","GST_SETTINGS:IMPORT_TAX");
        Check((int)(await client.GetAsync("/api/gst/settings/catalog/template?format=csv")).StatusCode==403,"Unauthorized users cannot download the prefilled catalog template");
        Check((int)(await Upload(Encoding.UTF8.GetBytes(completedCsv),"filled.csv")).StatusCode==403,"Unauthorized users cannot validate an import");
    }
    private static void Check(bool success,string name){if(!success)throw new Exception(name);Console.WriteLine("PASS: "+name);}
}
