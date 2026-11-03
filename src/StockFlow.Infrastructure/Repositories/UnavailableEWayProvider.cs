using StockFlow.Application.Interfaces;

namespace StockFlow.Infrastructure.Repositories;

public sealed class UnavailableEWayProvider : IEWayProvider
{
    public string Name => "No authorized adapter installed";
    public IReadOnlySet<string> SupportedOperations { get; } = new HashSet<string>();
    public Task<EWayProviderOutcome> ExecuteAsync(EWayProviderCommand command, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Automatic e-way operations require an authorized version-specific provider adapter and verified sandbox connection.");
}
