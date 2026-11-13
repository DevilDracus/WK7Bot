using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Modules;
using WK7Bot.Services.Interfaces;
using Xunit;

namespace WK7Bot.Tests;

public class ProfileModuleTests
{
    private sealed class TestProfileModule : ProfileModule
    {
        public int DeferCount { get; private set; }

        public List<string> Followups { get; } = new();

        public List<(MessageComponent Components, MessageFlags Flags)> ComponentPosts { get; } = new();

        public TestProfileModule(IProfileService profileService)
            : base(profileService, NullLogger<ProfileModule>.Instance)
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
            if (text != null)
            {
                Followups.Add(text);
            }

            if (components != null)
            {
                ComponentPosts.Add((components, flags));
            }

            return Task.FromResult<IUserMessage>(null!);
        }
    }

    private static TestProfileModule CreateModule(
        IProfileService profileService,
        ulong? guildId = 1,
        string username = "Tester",
        ulong userId = 42)
    {
        var module = new TestProfileModule(profileService);
        var context = DiscordTestDoubles.CreateContext();
        DiscordTestDoubles.SetUser(context, username, userId);
        if (guildId.HasValue)
        {
            DiscordTestDoubles.SetGuild(context, guildId.Value);
        }

        ((IInteractionModuleBase)module).SetContext(context);
        return module;
    }

    private static ProfileResult CreateEmptyProfile()
        => new("Tester", null, null);

    private static ProfileResult CreateSteamProfile()
        => new(
            "Tester",
            new SteamProfileSection(
                "SteamUser",
                "https://avatar.url",
                "Online",
                null,
                120,
                "2 h",
                5,
                10,
                new List<SteamProfileGame>()),
            null);

    [Fact]
    public void Constructor_Throws_WhenDependenciesAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ProfileModule(null!, NullLogger<ProfileModule>.Instance));
        Assert.Throws<ArgumentNullException>(() => new ProfileModule(new Mock<IProfileService>().Object, null!));
    }

    [Fact]
    public void ProfileCommand_IsExposedAsSlashCommand_AndRequiresGuildContext()
    {
        var method = typeof(ProfileModule).GetMethod(nameof(ProfileModule.ProfileAsync))!;
        var slash = method.GetCustomAttribute<SlashCommandAttribute>();
        var requireContext = method.GetCustomAttribute<RequireContextAttribute>();

        Assert.NotNull(slash);
        Assert.Equal("profile", slash!.Name);
        Assert.Contains("Profilkarte", slash.Description);

        Assert.NotNull(requireContext);
        Assert.NotNull(requireContext);
    }

    [Fact]
    public async Task ProfileAsync_ReportsNoLinkedAccounts_WhenProfileIsEmpty()
    {
        var service = new Mock<IProfileService>();
        service.Setup(s => s.GetAsync(42ul, 1ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyProfile());
        var module = CreateModule(service.Object);

        await module.ProfileAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Empty(module.ComponentPosts);
        var followup = Assert.Single(module.Followups);
        Assert.Contains("kein Spielkonto", followup);
    }

    [Fact]
    public async Task ProfileAsync_PostsComponentsV2_WhenProfileHasData()
    {
        var service = new Mock<IProfileService>();
        service.Setup(s => s.GetAsync(42ul, 1ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSteamProfile());
        var module = CreateModule(service.Object);

        await module.ProfileAsync();

        Assert.Equal(1, module.DeferCount);
        Assert.Empty(module.Followups);
        var post = Assert.Single(module.ComponentPosts);
        Assert.Equal(MessageFlags.ComponentsV2, post.Flags);
        Assert.NotNull(post.Components);
    }

    [Fact]
    public async Task ProfileAsync_UsesMentionedUser_WhenProvided()
    {
        var service = new Mock<IProfileService>();
        service.Setup(s => s.GetAsync(99ul, 1ul, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSteamProfile());
        var module = CreateModule(service.Object);
        var mentionedUser = Mock.Of<IUser>(u => u.Id == 99ul && u.Username == "OtherUser");

        await module.ProfileAsync(mentionedUser);

        service.Verify(s => s.GetAsync(99ul, 1ul, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(module.ComponentPosts);
    }

    [Fact]
    public async Task ProfileAsync_ReportsError_WhenServiceThrows()
    {
        var service = new Mock<IProfileService>();
        service.Setup(s => s.GetAsync(It.IsAny<ulong>(), It.IsAny<ulong>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("service down"));
        var module = CreateModule(service.Object);

        await module.ProfileAsync();

        Assert.Equal(1, module.DeferCount);
        var followup = Assert.Single(module.Followups);
        Assert.Contains("konnte nicht geladen werden", followup);
    }
}
