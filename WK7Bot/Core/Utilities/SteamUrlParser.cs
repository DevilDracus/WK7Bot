namespace WK7Bot.Core.Utilities;

using System.Text.RegularExpressions;

/// <summary>
/// Parses Steam profile and wishlist URLs into a SteamID64 or a vanity name.
/// </summary>
public static partial class SteamUrlParser
{
    /// <summary>Matches a bare 17-digit SteamID64.</summary>
    [GeneratedRegex(@"^7656119\d{10}$")]
    private static partial Regex SteamIdRegex();

    /// <summary>Matches a Steam community profile URL.</summary>
    [GeneratedRegex(@"steamcommunity\.com/profiles/(?<id>7656119\d{10})", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileIdRegex();

    /// <summary>Matches a Steam community vanity URL.</summary>
    [GeneratedRegex(@"steamcommunity\.com/id/(?<vanity>[^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileVanityRegex();

    /// <summary>Matches a Steam store wishlist URL by SteamID64.</summary>
    [GeneratedRegex(@"store\.steampowered\.com/wishlist/profiles/(?<id>7656119\d{10})", RegexOptions.IgnoreCase)]
    private static partial Regex WishlistIdRegex();

    /// <summary>Matches a Steam store wishlist URL by vanity name.</summary>
    [GeneratedRegex(@"store\.steampowered\.com/wishlist/id/(?<vanity>[^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex WishlistVanityRegex();

    /// <summary>
    /// Tries to extract a SteamID64 directly from a bare ID or a profile/wishlist URL.
    /// </summary>
    /// <param name="input">The user-supplied ID or URL.</param>
    /// <param name="steamId">The extracted SteamID64 when found.</param>
    /// <returns><see langword="true"/> when a SteamID64 was found.</returns>
    public static bool TryGetSteamId(string? input, out string? steamId)
    {
        steamId = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (SteamIdRegex().IsMatch(trimmed))
        {
            steamId = trimmed;
            return true;
        }

        var profile = ProfileIdRegex().Match(trimmed);
        if (profile.Success)
        {
            steamId = profile.Groups["id"].Value;
            return true;
        }

        var wishlist = WishlistIdRegex().Match(trimmed);
        if (wishlist.Success)
        {
            steamId = wishlist.Groups["id"].Value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to extract a Steam vanity name from a profile or wishlist URL.
    /// </summary>
    /// <param name="input">The user-supplied URL.</param>
    /// <param name="vanity">The vanity name when found.</param>
    /// <returns><see langword="true"/> when a vanity name was found.</returns>
    public static bool TryGetVanity(string? input, out string? vanity)
    {
        vanity = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        var profile = ProfileVanityRegex().Match(trimmed);
        if (profile.Success)
        {
            vanity = profile.Groups["vanity"].Value;
            return true;
        }

        var wishlist = WishlistVanityRegex().Match(trimmed);
        if (wishlist.Success)
        {
            vanity = wishlist.Groups["vanity"].Value;
            return true;
        }

        return false;
    }
}
