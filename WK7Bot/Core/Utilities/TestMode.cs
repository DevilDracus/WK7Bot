namespace WK7Bot.Core.Utilities;

using System.Threading;

/// <summary>
/// Ambient switch that marks the current execution as an HTTP test-API invocation.
/// While the scope is active, command handlers must not mutate persistent state and
/// every outbound guild route must resolve to the configured test server, so a smoke
/// test can never touch production data.
/// </summary>
/// <remarks>
/// The state is stored in an <see cref="AsyncLocal{T}"/> so it flows into awaited calls
/// without leaking across concurrent Discord interactions handled by the same process.
/// </remarks>
public static class TestMode
{
    private static readonly AsyncLocal<TestModeState?> CurrentState = new();

    /// <summary>
    /// Gets a value indicating whether the current execution runs inside a test-API scope.
    /// </summary>
    public static bool IsActive => CurrentState.Value is not null;

    /// <summary>
    /// Gets the guild ID that must receive every outbound message while the scope is active,
    /// or <see langword="null"/> when no override is set.
    /// </summary>
    public static ulong? GuildOverride => CurrentState.Value?.GuildId;

    /// <summary>
    /// Enters a test-API scope. Dispose the returned handle to restore the previous state.
    /// </summary>
    /// <param name="guildOverride">The guild ID that outbound traffic must be routed to.</param>
    /// <returns>A disposable handle that restores the previous scope on disposal.</returns>
    public static IDisposable Enter(ulong? guildOverride)
    {
        var previous = CurrentState.Value;
        CurrentState.Value = new TestModeState(guildOverride);
        return new Scope(previous);
    }

    /// <summary>
    /// Builds the response text returned by a handler whose side effect was skipped.
    /// </summary>
    /// <param name="action">Short description of the skipped action, for example <c>rss add</c>.</param>
    /// <returns>A human-readable simulation notice.</returns>
    public static string Simulated(string action) =>
        $"[Testmodus] {action}: keine Änderung gespeichert und nichts an den Produktionsserver gesendet.";

    private sealed record TestModeState(ulong? GuildId);

    private sealed class Scope : IDisposable
    {
        private readonly TestModeState? _previous;
        private bool _disposed;

        public Scope(TestModeState? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentState.Value = _previous;
        }
    }
}
