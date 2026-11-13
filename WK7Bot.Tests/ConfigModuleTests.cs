using System.Net;
using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WK7Bot.Core.Utilities;
using WK7Bot.Options;
using WK7Bot.Services;
using WK7Bot.Modules;
using Xunit;

namespace WK7Bot.Tests;

public class ConfigModuleTests
{
    private sealed class TestConfigModule : ConfigModule
    {
        public int DeferCount { get; private set; }
        public List<string> Followups { get; } = new();

        public TestConfigModule(FeatureGate featureGate, AddOnOptionsWriter optionsWriter)
            : base(featureGate, optionsWriter, NullLogger<ConfigModule>.Instance)
        {
        }

        protected override Task DeferAsync(bool ephemeral, RequestOptions options)
        {
            DeferCount++;
            return Task.CompletedTask;
        }

        protected override Task<IUserMessage> FollowupAsync(
            string text,
            Embed[] embeds,
            bool isTTS,
            bool ephemeral,
            AllowedMentions allowedMentions,
            RequestOptions options,
            MessageComponent components,
            Embed embed,
            PollProperties? poll = null,
            MessageFlags flags = MessageFlags.None)
        {
            Followups.Add(text ?? string.Empty);
            return Task.FromResult<IUserMessage>(null!);
        }
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public TestHttpMessageHandler(HttpStatusCode statusCode) => _statusCode = statusCode;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_statusCode));
    }

    private static AddOnOptionsWriter CreateWriter(HttpStatusCode statusCode)
    {
        Environment.SetEnvironmentVariable("SUPERVISOR_TOKEN", "test-token");
        return new AddOnOptionsWriter(
            new HttpClient(new TestHttpMessageHandler(statusCode)) { BaseAddress = new Uri("http://supervisor/") },
            NullLogger<AddOnOptionsWriter>.Instance);
    }

    private static TestConfigModule CreateModule(
        FeatureGate? featureGate = null,
        AddOnOptionsWriter? optionsWriter = null)
    {
        var gate = featureGate ?? new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()));
        var writer = optionsWriter ?? new AddOnOptionsWriter(new HttpClient(), NullLogger<AddOnOptionsWriter>.Instance);
        var module = new TestConfigModule(gate, writer);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, "Tester", 1);
        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        var gate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()));
        var writer = new AddOnOptionsWriter(new HttpClient(), NullLogger<AddOnOptionsWriter>.Instance);
        Assert.Throws<ArgumentNullException>(() => new ConfigModule(null!, writer, NullLogger<ConfigModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ConfigModule(gate, null!, NullLogger<ConfigModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ConfigModule(gate, writer, null!));
    }

    [Fact]
    public void FeatureCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(ConfigModule).GetMethod(nameof(ConfigModule.FeatureAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("feature", attr!.Name);
    }

    [Fact]
    public void ListCommand_IsExposedAsSlashCommand()
    {
        var attr = typeof(ConfigModule).GetMethod(nameof(ConfigModule.ListAsync))!
            .GetCustomAttribute<SlashCommandAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("list", attr!.Name);
    }

    [Fact]
    public void FeatureCommand_RequiresAdministrator()
    {
        var attr = typeof(ConfigModule).GetMethod(nameof(ConfigModule.FeatureAsync))!
            .GetCustomAttribute<RequireUserPermissionAttribute>();
        Assert.NotNull(attr);
        Assert.True((attr!.GuildPermission & GuildPermission.Administrator) == GuildPermission.Administrator);
    }

    [Fact]
    public void ListCommand_RequiresAdministrator()
    {
        var attr = typeof(ConfigModule).GetMethod(nameof(ConfigModule.ListAsync))!
            .GetCustomAttribute<RequireUserPermissionAttribute>();
        Assert.NotNull(attr);
        Assert.True((attr!.GuildPermission & GuildPermission.Administrator) == GuildPermission.Administrator);
    }

    [Fact]
    public async Task FeatureAsync_TogglesKnownFeature_AndPersists()
    {
        var gate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()));
        var writer = CreateWriter(HttpStatusCode.OK);
        var module = CreateModule(gate, writer);

        await module.FeatureAsync(FeatureKeys.FoodService, "on");

        Assert.Equal(1, module.DeferCount);
        Assert.True(gate.IsEnabled(FeatureKeys.FoodService));
        Assert.Single(module.Followups);
        Assert.Contains("aktiviert", module.Followups[0]);
    }

    [Fact]
    public async Task FeatureAsync_RejectsUnknownKey()
    {
        var module = CreateModule();

        await module.FeatureAsync("not_a_feature", "on");

        Assert.Equal(1, module.DeferCount);
        Assert.Single(module.Followups);
        Assert.Contains("Unbekannter Feature-Schlüssel", module.Followups[0]);
    }

    [Fact]
    public async Task FeatureAsync_ReportsPersistenceWarning_WhenSupervisorUnavailable()
    {
        var gate = new FeatureGate(Microsoft.Extensions.Options.Options.Create(new Wk7BotOptions()));
        var writer = CreateWriter(HttpStatusCode.ServiceUnavailable);
        var module = CreateModule(gate, writer);

        await module.FeatureAsync(FeatureKeys.DealWatchlist, "off");

        Assert.False(gate.IsEnabled(FeatureKeys.DealWatchlist));
        Assert.Single(module.Followups);
        Assert.Contains("Konnte nicht in der Add-on-Konfiguration", module.Followups[0]);
    }

    [Fact]
    public async Task ListAsync_ListsKnownFeatures()
    {
        var module = CreateModule();

        await module.ListAsync();

        Assert.Equal(1, module.DeferCount);
        var followup = Assert.Single(module.Followups);
        Assert.Contains(FeatureKeys.FoodService, followup);
        Assert.Contains(FeatureKeys.DealWatchlist, followup);
    }
}
