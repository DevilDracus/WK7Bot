namespace WK7Bot.Core.Utilities;

using Microsoft.Extensions.Caching.Memory;

/// <summary>
/// Per-user rate limiter for expensive Discord slash commands. Uses an in-memory sliding window
/// backed by <see cref="IMemoryCache"/> so no database schema changes are required.
/// </summary>
public sealed class CommandRateLimiter
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CommandRateLimiter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandRateLimiter"/> class
    /// with a default in-memory cache and null logger. Intended for test environments.
    /// </summary>
    public CommandRateLimiter()
        : this(new MemoryCache(new MemoryCacheOptions()),
               Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandRateLimiter>.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandRateLimiter"/> class.
    /// </summary>
    /// <param name="cache">The shared memory cache instance.</param>
    /// <param name="logger">The logger instance for diagnostics.</param>
    public CommandRateLimiter(IMemoryCache cache, ILogger<CommandRateLimiter> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks whether the user is currently rate-limited for the given command.
    /// </summary>
    /// <param name="userId">The Discord user ID.</param>
    /// <param name="commandName">The slash command name.</param>
    /// <param name="window">The cooldown window.</param>
    /// <returns>
    /// <see langword="true"/> when the user may execute the command;
    /// <see langword="false"/> when they are still within the cooldown window.
    /// </returns>
    public bool CanExecute(ulong userId, string commandName, TimeSpan window)
    {
        var key = $"ratelimit:{userId}:{commandName}";

        if (_cache.TryGetValue(key, out _))
        {
            _logger.LogDebug("Rate limit hit for user {UserId} on command {CommandName}.", userId, commandName);
            return false;
        }

        _cache.Set(key, DateTimeOffset.UtcNow, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = window, Size = 1 });
        return true;
    }

    /// <summary>
    /// Resets the rate limit for a specific user and command, allowing immediate re-execution.
    /// </summary>
    /// <param name="userId">The Discord user ID.</param>
    /// <param name="commandName">The slash command name.</param>
    public void Reset(ulong userId, string commandName)
    {
        var key = $"ratelimit:{userId}:{commandName}";
        _cache.Remove(key);
    }
}
