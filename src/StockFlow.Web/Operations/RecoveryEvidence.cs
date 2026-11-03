using System.Text.Json;

namespace StockFlow.Web.Operations;

// The OS administrator owns this private evidence file. It is never a public upload.
// Return only allowlisted fields, never paths, SQL output, archive contents or credentials.
public sealed record RecoveryEvidence(string Status, DateTimeOffset? ObservedUtc = null,
    DateTimeOffset? BackupUtc = null, DateTimeOffset? RestoreTestUtc = null, bool BackupOverdue = false)
{
    public static RecoveryEvidence Read(string? file, DateTimeOffset now, int maximumAgeHours = 26)
    {
        if (string.IsNullOrWhiteSpace(file)) return new("NotConfigured");
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists) return new("Unavailable");
            if (info.Length > 8192) return new("InvalidEvidence");
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            DateTimeOffset? Date(string name) => root.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var date) ? date : null;
            var observed = Date("observedUtc");
            var backup = Date("backupUtc");
            var restored = Date("restoreTestUtc");
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || observed is null || backup is null
                || observed > now.AddMinutes(5) || backup > observed || restored > observed || restored < backup)
                return new("InvalidEvidence");
            var stale = now - observed > TimeSpan.FromHours(1);
            return new(stale ? "StaleEvidence" : restored is null ? "ArchiveOnly" : "DatabaseRestoreTestRecorded",
                observed, backup, restored, now - backup > TimeSpan.FromHours(maximumAgeHours));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or OverflowException)
        {
            return new("Unavailable");
        }
    }
}
