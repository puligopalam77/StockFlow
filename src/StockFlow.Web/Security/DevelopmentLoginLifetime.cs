using Microsoft.AspNetCore.Authentication;

namespace StockFlow.Web.Security;

// Debug restarts require fresh credentials without invalidating production sessions.
public sealed class DevelopmentLoginLifetime(IHostEnvironment environment)
{
    private const string Key = "StockFlow.DebugRun";
    public bool Enabled { get; } = environment.IsDevelopment();
    public string RunId { get; } = Guid.NewGuid().ToString("N");
    public void Stamp(AuthenticationProperties properties)
    {
        if (Enabled) properties.Items[Key] = RunId;
    }
    public bool Accepts(AuthenticationProperties properties) => !Enabled ||
        properties.Items.TryGetValue(Key, out var value) && value == RunId;
}
