using System.Text.Json;

namespace StockFlow.Application.Interfaces;

// Implement only against a separately authorized, version-pinned provider contract.
// Credentials and provider authentication/encryption belong in the server-side adapter.
public interface IEWayProvider
{
    string Name { get; }
    IReadOnlySet<string> SupportedOperations { get; }
    Task<EWayProviderOutcome> ExecuteAsync(EWayProviderCommand command, CancellationToken cancellationToken);
}
public sealed record EWayProviderCommand(Guid AttemptId, string Operation, string RegistrationReference, JsonElement Payload);
// UnknownAfterTransmission must be reconciled by the same attempt identity, never blindly regenerated.
public enum EWayProviderState { Confirmed, Rejected, NotTransmitted, UnknownAfterTransmission }
public sealed record EWayProviderOutcome(EWayProviderState State, string? OfficialEbn, string? OfficialStatus,
    DateTimeOffset? GeneratedAt, DateTimeOffset? ValidUntil, string? ProviderReference, JsonElement Evidence, decimal? Fee);
