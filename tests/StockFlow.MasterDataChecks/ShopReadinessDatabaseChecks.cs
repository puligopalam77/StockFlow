using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class ShopReadinessDatabaseChecks
{
    // This check uses the local disposable PostgreSQL test server, never the application connection string.
    public static async Task Run()
    {
        var database = "readiness_test_" + Guid.NewGuid().ToString("N");
        var connectionString = $"Host=127.0.0.1;Port=55441;Database={database};Username=postgres";
        await using var admin = new NpgsqlConnection(connectionString.Replace(database, "postgres"));
        await admin.OpenAsync();
        await admin.ExecuteAsync("CREATE DATABASE " + database);
        try
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            foreach (var file in Directory.GetFiles(Path.Combine(root!.FullName, "database/scripts"), "*.sql").Order())
            {
                if (Path.GetFileName(file).StartsWith("001") || Path.GetFileName(file).StartsWith("006")) continue;
                await connection.ExecuteAsync(await File.ReadAllTextAsync(file));
            }
            foreach (var file in Directory.GetFiles(Path.Combine(root.FullName, "database/migrations"), "*.sql").Order())
            {
                if (Path.GetFileName(file).StartsWith("020"))
                    await connection.ExecuteAsync("INSERT INTO roles(role_name) VALUES('Admin'); INSERT INTO users(role_id,full_name,username,password_hash) VALUES(1,'Owner','owner','unused'); INSERT INTO categories(category_name) VALUES('General'); INSERT INTO products(category_id,product_name,sku,selling_price,tax_percentage) VALUES(1,'Shirt','SHIRT',100,18);");
                await connection.ExecuteAsync(await File.ReadAllTextAsync(file));
            }
            await connection.ExecuteAsync("SELECT set_config('app.tenant_id','1',false); SELECT set_config('app.user_id','1',false); SELECT set_config('app.location_id','1',false);");
            await connection.ExecuteAsync("""
                INSERT INTO customers(customer_name) VALUES('Test customer');
                INSERT INTO suppliers(supplier_name) VALUES('Test supplier');
                INSERT INTO sales(invoice_number,created_by,subtotal,total_amount) VALUES('READINESS',1,1500,1500);
                INSERT INTO payments(sale_id,payment_mode,amount,created_by) VALUES(1,'CASH',1000,1),(1,'UPI',500,1);
                INSERT INTO purchases(supplier_id,purchase_number,purchase_date,created_by) VALUES(1,'READINESS',current_date,1);
                INSERT INTO supplier_payments(purchase_id,payment_mode,amount,request_id,request_payload,created_by)
                  VALUES(1,'CASH',50,gen_random_uuid(),'{}',1),(1,'UPI',80,gen_random_uuid(),'{}',1);
                INSERT INTO service_requests(service_number,customer_id,product_id,complaint,created_by) VALUES('READINESS',1,1,'Test repair',1);
                INSERT INTO service_payments(service_request_id,payment_mode,amount,request_id,request_payload,created_by)
                  VALUES(1,'CASH',100,gen_random_uuid(),'{}',1),(1,'CARD',90,gen_random_uuid(),'{}',1);
                INSERT INTO sale_returns(sale_id,return_number,return_total,refund_mode,refund_amount,notes,request_id,request_payload,created_by)
                  VALUES(1,'READINESS',30,'CASH',30,'Test refund',gen_random_uuid(),'{}',1);
                """);
            var scope = new BusinessScope { TenantId = 1, UserId = 1, StoreId = 1, LocationId = 1, IsOwner = true, CanReviewStores = true };
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"] = connectionString }).Build();
            var factory = new DbConnectionFactory(config, scope);
            var languages = new ScreenLanguageRepository(factory);
            await languages.SaveAsync(new() { Screen = "purchases-create", Language = "te", Captions = new() { ["Supplier"] = "సరఫరాదారు" } });
            await languages.SaveAsync(new() { Screen = "sales-create", Language = "hi", Captions = new() { ["Supplier"] = "आपूर्तिकर्ता" } });
            Check((await languages.GetAsync()).Count == 2, "Screen languages are persisted separately in the business database");
            var isolatedScope = new BusinessScope { TenantId = 999, UserId = 1, StoreId = 1, LocationId = 1 };
            Check((await new ScreenLanguageRepository(new DbConnectionFactory(config, isolatedScope)).GetAsync()).Count == 0, "Another business cannot read the configured screen languages");
            var time = new ShopTimeOptions();
            var repository = new CashClosingRepository(factory, scope, Options.Create(time), TimeProvider.System);
            var date = time.Today(TimeProvider.System);
            var report = await repository.GetAsync(date);
            Check(report.Totals.NetCash == 1020 && report.Totals.MovementCount == 4, "Cash report counts cash only, including refunds and supplier payments");
            var request = new CashClosingRequest { Date = date, LocationId = 1, RequestId = Guid.NewGuid(), Fingerprint = report.Totals.Fingerprint, OpeningCash = 100, CashAdded = 20, CashRemoved = 10, CountedCash = 1130, Reason = "Owner added float" };
            var saved = await Task.WhenAll(repository.SaveAsync(request), repository.SaveAsync(request));
            Check(saved[0].ClosingId == saved[1].ClosingId && saved[0].Difference == 0, "Concurrent retry records one balanced closing");
            request.CountedCash++;
            await Reject(() => repository.SaveAsync(request), "Changed payload cannot reuse a request ID");
            request.CountedCash--;
            await connection.ExecuteAsync("INSERT INTO payments(sale_id,payment_mode,amount,created_by) VALUES(1,'CASH',5,1)");
            report = await repository.GetAsync(date);
            Check(report.ChangedSinceClosing && report.Closings.Count == 1, "Late collections flag changed activity without overwriting closing history");
            request.RequestId = Guid.NewGuid(); request.ExpectedVersion = 1;
            await Reject(() => repository.SaveAsync(request), "Stale cash totals cannot be saved");
            request.Fingerprint = report.Totals.Fingerprint; request.CountedCash = 1135; request.Reason = "Recount after late collection";
            var updated = await repository.SaveAsync(request);
            Check(updated.Version == 2 && (await repository.GetAsync(date)).Closings.Count == 2, "A recount saves a new history version");
            var zone = TimeZoneInfo.FindSystemTimeZoneById(time.TimeZoneId);
            var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), zone);
            await connection.ExecuteAsync("""
                INSERT INTO payments(sale_id,payment_mode,amount,created_by,payment_date) VALUES
                (1,'CASH',7,1,(@Start AT TIME ZONE current_setting('TimeZone'))-interval '1 microsecond'),
                (1,'CASH',13,1,@Start AT TIME ZONE current_setting('TimeZone')),
                (1,'CASH',17,1,(@End AT TIME ZONE current_setting('TimeZone'))-interval '1 microsecond'),
                (1,'CASH',19,1,@End AT TIME ZONE current_setting('TimeZone'));
                """, new { Start = start, End = start.AddDays(1) });
            report = await repository.GetAsync(date);
            Check(report.Totals.SalesCash == 1035, "India business-day boundaries include midnight and exclude the next day's midnight");
            request.ExpectedVersion = 2; request.Fingerprint = report.Totals.Fingerprint; request.CountedCash = 1165;
            var competing = System.Text.Json.JsonSerializer.Deserialize<CashClosingRequest>(System.Text.Json.JsonSerializer.Serialize(request))!;
            request.RequestId = Guid.NewGuid(); competing.RequestId = Guid.NewGuid();
            async Task<bool> TryClosing(CashClosingRequest candidate)
            {
                try { await repository.SaveAsync(candidate); return true; }
                catch (ValidationException) { return false; }
            }
            Check((await Task.WhenAll(TryClosing(request), TryClosing(competing))).Count(passed => passed) == 1, "Two different concurrent recounts cannot overwrite each other");
            await using (var runtime = factory.CreateConnection())
            {
                try { await runtime.ExecuteAsync("UPDATE cash_closings SET version=3"); throw new Exception("Closing history was editable"); }
                catch (PostgresException error) when (error.SqlState == "42501") { Check(true, "Runtime cannot edit or delete closing history"); }
            }
            var branches = new BranchRepository(factory, scope);
            var store = await branches.CreateStoreAsync(new() { StoreCode = "SECOND", StoreName = "Second shop" });
            scope.LocationId = (await branches.LocationsAsync()).Single(location => location.StoreId == store).LocationId;
            scope.StoreId = store;
            Check((await repository.GetAsync(date)).Closings.Count == 0 && (await repository.GetAsync(date)).Totals.NetCash == 0, "Another shop cannot see the first shop's cash or closings");
            await Reject(() => repository.SaveAsync(request), "A closing cannot be saved to a different selected location");
            var backupDirectory = Path.Combine(root.FullName, "tests/bin/maintenance-verification", database);
            await RunScript(root.FullName, "Backup-StockFlow.ps1", "-DatabaseName", database, "-BackupDirectory", backupDirectory, "-Port", "55441");
            var archive = Directory.GetFiles(backupDirectory, "*.dump").Single();
            await RunScript(root.FullName, "Test-StockFlowRestore.ps1", "-Archive", archive, "-Port", "55441");
            Check(File.ReadAllText(archive + ".restore-test.json").Contains("\"passed\":  true") || File.ReadAllText(archive + ".restore-test.json").Contains("\"passed\": true"), "Real backup restores into a disposable database with schema and cash history intact");
            await RunScript(root.FullName, "Backup-StockFlow.ps1", ["-DatabaseName", "missing_" + Guid.NewGuid().ToString("N"), "-BackupDirectory", backupDirectory, "-Port", "55441"], expectedFailure: true);
            Check(File.Exists(archive) && Directory.GetFiles(backupDirectory, "*.partial").Length == 0, "Failed backup retains the previous archive and removes incomplete output");
            var damaged = Path.Combine(backupDirectory, "damaged.dump");
            File.Copy(archive, damaged); File.Copy(archive + ".json", damaged + ".json");
            await File.AppendAllTextAsync(damaged, "tampered");
            await RunScript(root.FullName, "Test-StockFlowRestore.ps1", ["-Archive", damaged, "-Port", "55441"], expectedFailure: true);
            Console.WriteLine("Shop readiness database and backup/restore checks passed on an isolated temporary database.");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await admin.ExecuteAsync("DROP DATABASE " + database + " WITH (FORCE)");
        }
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); }
        catch (ValidationException) { Check(true, message); return; }
        throw new Exception("FAIL: " + message);
    }

    private static Task RunScript(string root, string script, params string[] arguments) => RunScript(root, script, arguments, false);

    private static async Task RunScript(string root, string script, string[] arguments, bool expectedFailure)
    {
        var start = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(root, "database/maintenance", script) }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var details = await output + await errors;
        if ((process.ExitCode != 0) != expectedFailure) throw new Exception(script + " failed: " + details);
        Check(true, expectedFailure ? script + " rejected invalid input" : script + " completed");
    }
}
