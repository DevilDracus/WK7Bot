namespace WK7Bot.Services.Interfaces;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines operations for checking the health of external API dependencies.
/// </summary>
public interface IExternalDependencyHealthChecker
{
    /// <summary>
    /// Checks the health of all external dependencies.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for task cancellation.</param>
    /// <returns>A read-only list of dependency health statuses.</returns>
    Task<IReadOnlyList<DependencyHealthStatus>> CheckAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the health status of a single external dependency.
/// </summary>
public sealed record DependencyHealthStatus(
    string Name,
    bool IsHealthy,
    string Detail,
    TimeSpan? Latency = null);
