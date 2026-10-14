using Discord;

namespace WK7Bot.Modules;

/// <summary>
/// Channel provisioning helpers shared by the RSS modules: category lookup/creation and
/// collision-free channel naming.
/// </summary>
internal static class RssChannelProvisioner
{
    /// <summary>
    /// Fetches the existing Discord category or creates a new one if it does not exist.
    /// </summary>
    /// <param name="guild">Target guild where the category should exist.</param>
    /// <param name="categoryName">Name of the parent category.</param>
    /// <returns>The existing or newly created category channel.</returns>
    internal static async Task<ICategoryChannel> GetOrCreateCategoryAsync(IGuild guild, string categoryName)
    {
        var categories = await guild.GetCategoriesAsync();
        var existingCategory = categories.FirstOrDefault(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));

        if (existingCategory != null)
        {
            return existingCategory;
        }

        return await guild.CreateCategoryAsync(categoryName);
    }

    /// <summary>
    /// Returns the first channel name not already taken in the guild by appending a counter, so
    /// feeds whose names slug to the same channel name still get distinct channels.
    /// </summary>
    /// <param name="guild">The guild the channel is created in.</param>
    /// <param name="baseChannelName">The sanitized channel name.</param>
    /// <returns>The available channel name.</returns>
    internal static async Task<string> FindAvailableChannelNameAsync(IGuild guild, string baseChannelName)
    {
        var channels = await guild.GetTextChannelsAsync();
        var taken = channels
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (taken.Add(baseChannelName))
        {
            return baseChannelName;
        }

        for (var suffix = 2; suffix < 100; suffix++)
        {
            var candidate = $"{baseChannelName}-{suffix}";
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }

        // Impractical in practice (a guild with 99 identically-slugged channels), but a guaranteed
        // unique suffix beats a Discord 400 that surfaces as the generic error message.
        return $"{baseChannelName}-{Guid.NewGuid():N}"[..Math.Min(baseChannelName.Length + 33, Core.Utilities.NameSanitizer.MaxChannelNameLength)];
    }
}
