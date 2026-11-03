using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace StockFlow.MasterDataChecks;

internal static class ScreenLanguageSchemaChecks
{
    public static async Task Run(bool apply = false)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root!.FullName,"src/StockFlow.Web"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional:true).AddEnvironmentVariables().Build();
        var settings = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        var state = await connection.QuerySingleAsync("""
            SELECT current_database() database_name,
              to_regclass('public.billing_drafts') IS NOT NULL baseline_034,
              to_regclass('public.cash_closings') IS NOT NULL migration_035,
              to_regclass('public.screen_language_settings') IS NOT NULL migration_036,
              EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) maintenance_access;
            """);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { host = settings.Host, port = settings.Port, schema = (IDictionary<string,object>)state }));
        if (apply)
        {
            // Explicit maintenance command for the configured LOCAL development database only.
            if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(settings.Host)) throw new Exception("This command only supports a local development database.");
            if (!((bool)state.baseline_034) || !((bool)state.maintenance_access)) throw new Exception("Apply the existing baseline through 034 and use a database maintenance account first.");
            var backupDirectory = Path.Combine(root.FullName,"tests/bin/local-language-setup-backup",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(root.FullName,"database/maintenance/Backup-StockFlow.ps1"), "-DatabaseName", settings.Database!, "-BackupDirectory", backupDirectory, "-ServerHost", settings.Host!, "-Port", settings.Port.ToString(), "-DatabaseUser", settings.Username! }) start.ArgumentList.Add(argument);
            // Authentication stays in the child environment, never in command arguments or logs.
            if (!string.IsNullOrEmpty(settings.Password)) start.Environment["PGPASSWORD"] = settings.Password;
            using var backup = System.Diagnostics.Process.Start(start)!;
            var output = backup.StandardOutput.ReadToEndAsync(); var errors = backup.StandardError.ReadToEndAsync();
            await backup.WaitForExitAsync();
            var result = await output + await errors;
            if (backup.ExitCode != 0) throw new Exception("Backup did not succeed. No migrations were applied. " + result);
            Console.WriteLine("Verified pre-migration backup: " + backupDirectory);
            foreach (var migration in new[] { ("035_cash_closing.sql", "cash_closings"), ("036_screen_languages.sql", "screen_language_settings") })
            {
                if (await connection.ExecuteScalarAsync<bool>("SELECT to_regclass(@Table) IS NOT NULL",new { Table = "public." + migration.Item2 })) continue;
                await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations",migration.Item1)));
                Console.WriteLine("Applied " + migration.Item1);
            }
            if (!await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('public.screen_language_settings') IS NOT NULL")) throw new Exception("Language schema verification failed.");
            Console.WriteLine("PASS: local database language setup is complete. No existing business records were modified.");
            return;
        }
        Console.WriteLine("Read-only schema check. No database changes were performed.");
    }
}
