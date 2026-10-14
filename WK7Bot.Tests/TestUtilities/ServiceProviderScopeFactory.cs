namespace WK7Bot.Tests;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Adapts a <see cref="ServiceProvider"/> to <see cref="IServiceScopeFactory"/> for test fixtures
/// that need to pass a scope factory to services under test.
/// </summary>
internal sealed class ServiceProviderScopeFactory(IServiceProvider provider) : IServiceScopeFactory
{
    public IServiceScope CreateScope() => provider.CreateScope();
}
