using System.Reflection;
using Discord.Interactions;
using WK7Bot.Modules;
using Xunit;

namespace WK7Bot.Tests;

/// <summary>
/// Guards Discord's limits on slash-command descriptions. One over-long description makes Discord.Net throw while
/// building the command model, which aborts the whole registration call and leaves Discord serving whatever was
/// registered before — the new commands simply never appear.
/// </summary>
public class InteractionCommandLimitTests
{
    private const int MaxDescriptionLength = 100;

    private static IEnumerable<Type> ModuleTypes()
        => typeof(FoodModule).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IInteractionModuleBase).IsAssignableFrom(type));

    private static IEnumerable<MethodInfo> CommandMethods()
        => ModuleTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

    [Fact]
    public void SlashCommandDescriptions_RespectDiscordLimit()
    {
        var descriptions = CommandMethods()
            .Select(method => method.GetCustomAttribute<SlashCommandAttribute>())
            .Where(attribute => attribute != null)
            .Select(attribute => (Command: attribute!.Name, attribute.Description))
            .ToList();

        Assert.NotEmpty(descriptions);
        foreach (var (command, description) in descriptions)
        {
            Assert.False(string.IsNullOrWhiteSpace(description), $"/{command} has no description.");
            Assert.True(
                description.Length <= MaxDescriptionLength,
                $"/{command} description is {description.Length} characters (max {MaxDescriptionLength}): {description}");
        }
    }

    [Fact]
    public void GroupDescriptions_RespectDiscordLimit()
    {
        var descriptions = ModuleTypes()
            .Select(type => type.GetCustomAttribute<GroupAttribute>())
            .Where(attribute => attribute != null)
            .Select(attribute => (Group: attribute!.Name, attribute.Description))
            .ToList();

        foreach (var (group, description) in descriptions)
        {
            Assert.False(string.IsNullOrWhiteSpace(description), $"/{group} group has no description.");
            Assert.True(
                description.Length <= MaxDescriptionLength,
                $"/{group} group description is {description.Length} characters (max {MaxDescriptionLength}): {description}");
        }
    }

    [Fact]
    public void CommandParameterDescriptions_RespectDiscordLimit()
    {
        var summaries = CommandMethods()
            .SelectMany(method => method.GetParameters()
                .Select(parameter => new { parameter.Name, Summary = parameter.GetCustomAttribute<SummaryAttribute>() }))
            .Where(entry => entry.Summary != null)
            .Select(entry => (entry.Name, entry.Summary!.Description))
            .ToList();

        Assert.NotEmpty(summaries);
        Assert.Contains(summaries, entry => entry.Description.Contains("Pfannkuchen", StringComparison.Ordinal));
        foreach (var (parameter, description) in summaries)
        {
            Assert.True(
                description.Length <= MaxDescriptionLength,
                $"Parameter '{parameter}' description is {description.Length} characters (max {MaxDescriptionLength}): {description}");
        }
    }

    [Fact]
    public void CommandNames_RespectDiscordLimit()
    {
        var names = CommandMethods()
            .Select(method => method.GetCustomAttribute<SlashCommandAttribute>())
            .Where(attribute => attribute != null)
            .Select(attribute => attribute!.Name)
            .Concat(ModuleTypes()
                .Select(type => type.GetCustomAttribute<GroupAttribute>())
                .Where(attribute => attribute != null)
                .Select(attribute => attribute!.Name))
            .ToList();

        Assert.NotEmpty(names);
        foreach (var name in names)
        {
            Assert.False(string.IsNullOrWhiteSpace(name), "A command has no name.");
            Assert.True(name.Length <= 32, $"Command name '{name}' is {name.Length} characters (max 32).");
        }
    }
}
