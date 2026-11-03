using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Operations;

namespace StockFlow.MasterDataChecks;

internal static class OperationalReadinessChecks
{
    public static async Task Run(bool databaseChecks = false)
    {
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            Console.WriteLine("PASS: " + message);
        }
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var output = Path.Combine(root!.FullName, "tests/bin/operational-readiness");
        Directory.CreateDirectory(output);
        var file = Path.Combine(output, "evidence-" + Guid.NewGuid().ToString("N") + ".json");
        var now = DateTimeOffset.UtcNow;
        void Evidence(DateTimeOffset observed, DateTimeOffset backup, DateTimeOffset? restored) => File.WriteAllText(file,
            JsonSerializer.Serialize(new { schemaVersion = 1, observedUtc = observed, backupUtc = backup, restoreTestUtc = restored,
                password = "private-diagnostic-must-not-leak", archive = "private-archive-path" }));
        Check(RecoveryEvidence.Read(null, now).Status == "NotConfigured", "Missing recovery configuration is unknown, never passed");
        Evidence(now, now.AddHours(-2), null);
        Check(RecoveryEvidence.Read(file, now).Status == "ArchiveOnly", "Archive evidence alone never claims a restore test");
        Evidence(now, now.AddHours(-3), now.AddHours(-2));
        Check(RecoveryEvidence.Read(file, now).Status == "DatabaseRestoreTestRecorded", "Matching collector evidence records database-only restore scope");
        Evidence(now.AddHours(-2), now.AddDays(-2), null);
        var stale = RecoveryEvidence.Read(file, now);
        Check(stale.Status == "StaleEvidence" && stale.BackupOverdue, "Stale collector and overdue backup are independent warnings");
        Evidence(now, now.AddMinutes(1), null);
        Check(RecoveryEvidence.Read(file, now).Status == "InvalidEvidence", "Future backup evidence rejected");
        Evidence(now, now.AddHours(-2), now.AddHours(-3));
        Check(RecoveryEvidence.Read(file, now).Status == "InvalidEvidence", "Restore evidence cannot predate the matching archive");
        File.WriteAllText(file, "{broken");
        Check(RecoveryEvidence.Read(file, now).Status == "Unavailable", "Malformed private evidence fails closed");
        File.WriteAllText(file, new string('x', 9000));
        Check(RecoveryEvidence.Read(file, now).Status == "InvalidEvidence", "Oversized evidence not loaded");
        Evidence(now, now.AddHours(-2), null);
        using var factory = new Factory();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=private_database;Username=private_user;Password=private_password;Timeout=1;Pooling=false",
            ["Operations:Tenants:1:RecoveryEvidenceFile"] = file
        })));
        using var anonymous = app.CreateClient(new() { AllowAutoRedirect = false });
        var live = await anonymous.GetAsync("/api/health");
        var liveText = await live.Content.ReadAsStringAsync();
        Check(live.IsSuccessStatusCode && !liveText.Contains("database", StringComparison.OrdinalIgnoreCase), "Public liveness has no database or backup diagnostics");
        foreach (var route in new[] { "/api/database/health", "/api/database/operations" })
            Check((await anonymous.GetAsync(route)).StatusCode == HttpStatusCode.Unauthorized, "Anonymous denied " + route);
        foreach (var role in new[] { "Sales", "Service" })
        {
            using var staff = app.CreateClient(new() { AllowAutoRedirect = false });
            staff.DefaultRequestHeaders.Add("Test-Role", role);
            Check((await staff.GetAsync("/api/database/operations")).StatusCode == HttpStatusCode.Forbidden, role + " cannot read operational evidence");
            Check((await staff.GetAsync("/api/database/health")).StatusCode == HttpStatusCode.Forbidden, role + " cannot probe database readiness");
        }
        using var admin = app.CreateClient(new() { AllowAutoRedirect = false });
        admin.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var readiness = await admin.GetAsync("/api/database/operations");
        var text = await readiness.Content.ReadAsStringAsync();
        Check(readiness.StatusCode == HttpStatusCode.ServiceUnavailable && JsonNode.Parse(text)?["database"]?.GetValue<string>() == "Unavailable", "Real connection failure returns readiness 503");
        Check(!text.Contains("private_") && !text.Contains("private-") && !text.Contains(file), "Readiness response omits credentials, paths and unrecognized evidence fields");
        Check(readiness.Headers.CacheControl?.NoStore == true, "Operational response is not cached");
        var database = await admin.GetAsync("/api/database/health");
        var dbText = await database.Content.ReadAsStringAsync();
        Check(database.StatusCode == HttpStatusCode.ServiceUnavailable && !dbText.Contains("private_"), "Database failure omits provider diagnostics");
        admin.DefaultRequestHeaders.Add("Test-Deny", "SECURITY");
        Check((await admin.GetAsync("/api/database/operations")).StatusCode == HttpStatusCode.Forbidden, "Owner/admin status does not override denied SECURITY permission");
        admin.DefaultRequestHeaders.Remove("Test-Deny");
        admin.DefaultRequestHeaders.Add("Test-Tenant", "2");
        var other = JsonNode.Parse(await (await admin.GetAsync("/api/database/operations")).Content.ReadAsStringAsync());
        Check(other?["recovery"]?["status"]?.GetValue<string>() == "NotConfigured", "Tenant evidence mapping cannot fall back to another tenant's file");
        foreach (var profile in BusinessProfiles.Codes)
        {
            using var profileApp = app.WithWebHostBuilder(b => b.ConfigureServices(s => {
                s.RemoveAll<IBranchRepository>();
                s.AddSingleton<IBranchRepository>(new FakeBranchRepository { State = new() { IsActive = true, IsOwner = true, BusinessProfileCode = profile, BusinessProfileConfigurationAvailable = true, BusinessProfilePersisted = true } });
            }));
            using var client = profileApp.CreateClient();
            client.DefaultRequestHeaders.Add("Test-Role", "Admin");
            var html = await client.GetStringAsync("/Admin/Pilot");
            Check(html.Contains("data-profile=\"" + profile + "\"") && html.Contains("stockflow.pilot.v2."), profile + " has profile/shop-specific onboarding context");
            await File.WriteAllTextAsync(Path.Combine(output, profile + ".html"), html);
        }
        if (databaseChecks)
        {
            using var databaseApp = app.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=55443;Database=product_edit_069_test;Username=postgres;Timeout=2;Pooling=false"
            })));
            using var client = databaseApp.CreateClient();
            client.DefaultRequestHeaders.Add("Test-Role", "Admin");
            var response = await client.GetAsync("/api/database/operations");
            var data = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            Check(response.IsSuccessStatusCode && data?["database"]?.GetValue<string>() == "Ready", "Real test database succeeds through runtime-scoped readiness probe");
            Check(data?["migrationCapabilities"]?["BillingPolicy082"] is JsonValue && data?["migrationCapabilities"]?["Quotations083"] is JsonValue,
                "Actual SQL capability results serialize with the documented fields");
            Check((await client.GetAsync("/api/database/health")).IsSuccessStatusCode, "Actual runtime database readiness returns 200");
        }
        File.Delete(file);
        Console.WriteLine("Operational readiness checks passed. No production database, backup or task was modified.");
    }
}
