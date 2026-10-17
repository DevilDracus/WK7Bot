using Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

public class CardSenderTests
{
    private static MessageComponent Card()
        => CardBuilder.Build("Titel", "Inhalt");

    private static Mock<IMessageChannel> Channel()
    {
        var channel = new Mock<IMessageChannel>();
        channel
            .Setup(c => c.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<Embed>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<AllowedMentions>(),
                It.IsAny<MessageReference>(),
                It.IsAny<MessageComponent>(),
                It.IsAny<ISticker[]>(),
                It.IsAny<Embed[]>(),
                It.IsAny<MessageFlags>(),
                It.IsAny<PollProperties>()))
            .ReturnsAsync((IUserMessage)null!);
        return channel;
    }

    [Fact]
    public async Task SendCardAsync_SendsTheCardWithTheComponentsV2Flag()
    {
        var channel = Channel();

        var sent = await CardSender.SendCardAsync(channel.Object, Card(), null, NullLogger.Instance);

        Assert.True(sent);
        channel.Verify(
            c => c.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<Embed>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<AllowedMentions>(),
                It.IsAny<MessageReference>(),
                It.IsNotNull<MessageComponent>(),
                It.IsAny<ISticker[]>(),
                It.IsAny<Embed[]>(),
                MessageFlags.ComponentsV2,
                It.IsAny<PollProperties>()),
            Times.Once);
    }

    [Fact]
    public async Task SendCardAsync_FallsBackToTheEmbedWhenTheCardIsRejected()
    {
        var embed = new EmbedBuilder().WithTitle("Fallback").Build();
        var channel = Channel();
        channel
            .SetupSequence(c => c.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<Embed>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<AllowedMentions>(),
                It.IsAny<MessageReference>(),
                It.IsAny<MessageComponent>(),
                It.IsAny<ISticker[]>(),
                It.IsAny<Embed[]>(),
                It.IsAny<MessageFlags>(),
                It.IsAny<PollProperties>()))
            .ThrowsAsync(new InvalidOperationException("components v2 rejected"))
            .ReturnsAsync((IUserMessage)null!);

        var sent = await CardSender.SendCardAsync(channel.Object, Card(), embed, NullLogger.Instance);

        Assert.True(sent);
        channel.Verify(
            c => c.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsNotNull<Embed>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<AllowedMentions>(),
                It.IsAny<MessageReference>(),
                It.IsAny<MessageComponent>(),
                It.IsAny<ISticker[]>(),
                It.IsAny<Embed[]>(),
                It.IsAny<MessageFlags>(),
                It.IsAny<PollProperties>()),
            Times.Once);
    }

    [Fact]
    public async Task SendCardAsync_WithoutFallback_ReportsFailure()
    {
        var channel = Channel();
        channel
            .Setup(c => c.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<Embed>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<AllowedMentions>(),
                It.IsAny<MessageReference>(),
                It.IsAny<MessageComponent>(),
                It.IsAny<ISticker[]>(),
                It.IsAny<Embed[]>(),
                It.IsAny<MessageFlags>(),
                It.IsAny<PollProperties>()))
            .ThrowsAsync(new InvalidOperationException("rejected"));

        var sent = await CardSender.SendCardAsync(channel.Object, Card(), NullLogger.Instance);

        Assert.False(sent);
    }
}
