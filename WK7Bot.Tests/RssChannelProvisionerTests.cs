using Discord;
using Moq;
using WK7Bot.Modules;
using Xunit;

namespace WK7Bot.Tests;

public class RssChannelProvisionerTests
{
    [Fact]
    public async Task GetOrCreateCategoryAsync_ExistingCategory_ReturnsIt()
    {
        var existing = new Mock<ICategoryChannel>();
        existing.SetupGet(c => c.Name).Returns("RSS Feeds");
        var guild = new Mock<IGuild>();
        guild.Setup(g => g.GetCategoriesAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
            .ReturnsAsync(new List<ICategoryChannel> { existing.Object });

        var result = await RssChannelProvisioner.GetOrCreateCategoryAsync(guild.Object, "RSS Feeds");

        Assert.Same(existing.Object, result);
        guild.Verify(g => g.CreateCategoryAsync(It.IsAny<string>(), It.IsAny<Action<GuildChannelProperties>>(), It.IsAny<RequestOptions>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateCategoryAsync_NoCategory_CreatesOne()
    {
        var created = new Mock<ICategoryChannel>();
        var guild = new Mock<IGuild>();
        guild.Setup(g => g.GetCategoriesAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
            .ReturnsAsync(new List<ICategoryChannel>());
        guild.Setup(g => g.CreateCategoryAsync("RSS Feeds", It.IsAny<Action<GuildChannelProperties>>(), It.IsAny<RequestOptions>()))
            .ReturnsAsync(created.Object);

        var result = await RssChannelProvisioner.GetOrCreateCategoryAsync(guild.Object, "RSS Feeds");

        Assert.Same(created.Object, result);
    }

    [Fact]
    public async Task FindAvailableChannelNameAsync_FreeName_ReturnsBaseName()
    {
        var guild = new Mock<IGuild>();
        guild.Setup(g => g.GetTextChannelsAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
            .ReturnsAsync(new List<ITextChannel>());

        var name = await RssChannelProvisioner.FindAvailableChannelNameAsync(guild.Object, "pd-leipzig");

        Assert.Equal("pd-leipzig", name);
    }

    [Fact]
    public async Task FindAvailableChannelNameAsync_TakenName_AppendsSuffix()
    {
        var channel = new Mock<ITextChannel>();
        channel.SetupGet(c => c.Name).Returns("pd-leipzig");
        var guild = new Mock<IGuild>();
        guild.Setup(g => g.GetTextChannelsAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
            .ReturnsAsync(new List<ITextChannel> { channel.Object });

        var name = await RssChannelProvisioner.FindAvailableChannelNameAsync(guild.Object, "pd-leipzig");

        Assert.Equal("pd-leipzig-2", name);
    }
}
