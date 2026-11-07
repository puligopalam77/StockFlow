using Microsoft.AspNetCore.Authentication;

namespace StockFlow.Web.Security;

// Debug restarts require fresh credentials for browser-session logins.
// Remembered logins still pass the shared session's expiry and revocation checks.
public sealed class DevelopmentLoginLifetime(IHostEnvironment environment)
{
    private const string Key = "StockFlow.DebugRun";
    public bool Enabled { get; } = environment.IsDevelopment();
    public string RunId { get; } = Guid.NewGuid().ToString("N");
    public void Stamp(AuthenticationProperties properties)
    {
        if (Enabled) properties.Items[Key] = RunId;
    }
    public bool Accepts(AuthenticationProperties properties) => !Enabled || properties.IsPersistent ||
        properties.Items.TryGetValue(Key, out var value) && value == RunId;
}
