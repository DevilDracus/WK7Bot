using System.Text.Json;
using System.Text.Json.Serialization;
namespace WK7Bot.Models.BattleNet;



/// <summary>
/// Root container model for the Battle.net OAuth token response.
/// </summary>
public class BattleNetTokenResponse
{
    /// <summary>
    /// Gets or sets the issued access token.
    /// </summary>
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    /// <summary>
    /// Gets or sets the token lifetime in seconds.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    /// <summary>
    /// Gets or sets the space-separated scopes granted to the token.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

/// <summary>
/// Root container model for the Battle.net OAuth userinfo response.
/// </summary>
public class BattleNetUserInfoResponse
{
    /// <summary>
    /// Gets or sets the numeric account identifier, or null when the endpoint did not report one.
    /// </summary>
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    /// <summary>
    /// Gets or sets the account BattleTag (e.g. "Arthas#21234").
    /// </summary>
    [JsonPropertyName("battletag")]
    public string? BattleTag { get; set; }

    /// <summary>
    /// Gets or sets the token subject claim.
    /// </summary>
    [JsonPropertyName("sub")]
    public string? Subject { get; set; }
}

/// <summary>
/// Root container model for the WoW account profile summary.
/// </summary>
public class BattleNetWowAccountProfileResponse
{
    /// <summary>
    /// Gets or sets the World of Warcraft accounts linked to the Battle.net account.
    /// </summary>
    [JsonPropertyName("wow_accounts")]
    public List<BattleNetWowAccount>? WowAccounts { get; set; }
}

/// <summary>
/// A single WoW account entry holding its characters.
/// </summary>
public class BattleNetWowAccount
{
    /// <summary>
    /// Gets or sets the character key entries of this WoW account.
    /// </summary>
    [JsonPropertyName("characters")]
    public List<BattleNetWowCharacterKeyEntry>? Characters { get; set; }
}

/// <summary>
/// A character reference as returned by the account profile summary.
/// </summary>
public class BattleNetWowCharacterKeyEntry
{
    /// <summary>
    /// Gets or sets the link to the full character profile.
    /// </summary>
    [JsonPropertyName("key")]
    public BattleNetHrefKey? Key { get; set; }
}

/// <summary>
/// A hyperlink wrapper object.
/// </summary>
public class BattleNetHrefKey
{
    /// <summary>
    /// Gets or sets the target URL.
    /// </summary>
    [JsonPropertyName("href")]
    public string? Href { get; set; }
}

/// <summary>
/// Root container model for the WoW character profile summary.
/// </summary>
public class BattleNetWowCharacterSummaryResponse
{
    /// <summary>
    /// Gets or sets the character name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the realm reference of the character.
    /// </summary>
    [JsonPropertyName("realm")]
    public BattleNetWowRealmRef? Realm { get; set; }

    /// <summary>
    /// Gets or sets the character level.
    /// </summary>
    [JsonPropertyName("level")]
    public int Level { get; set; }

    /// <summary>
    /// Gets or sets the character class reference.
    /// </summary>
    [JsonPropertyName("character_class")]
    public BattleNetWowNameRef? CharacterClass { get; set; }

    /// <summary>
    /// Gets or sets the faction reference of the character.
    /// </summary>
    [JsonPropertyName("faction")]
    public BattleNetWowNameRef? Faction { get; set; }

    /// <summary>
    /// Gets or sets the average item level across all equipment slots.
    /// </summary>
    [JsonPropertyName("average_item_level")]
    public double AverageItemLevel { get; set; }

    /// <summary>
    /// Gets or sets the equipped item level.
    /// </summary>
    [JsonPropertyName("equipped_item_level")]
    public double EquippedItemLevel { get; set; }

    /// <summary>
    /// Gets or sets the guild reference, or null when the character is guildless.
    /// </summary>
    [JsonPropertyName("guild")]
    public BattleNetWowNameRef? Guild { get; set; }

    /// <summary>
    /// Gets or sets the unix timestamp (milliseconds) of the last login.
    /// </summary>
    [JsonPropertyName("last_login_timestamp")]
    public long LastLoginTimestamp { get; set; }
}

/// <summary>
/// A named and identified reference object (class, faction, guild).
/// </summary>
public class BattleNetWowNameRef
{
    /// <summary>
    /// Gets or sets the localized display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// A realm reference including its URL slug.
/// </summary>
public class BattleNetWowRealmRef : BattleNetWowNameRef
{
    /// <summary>
    /// Gets or sets the realm slug used in profile URLs.
    /// </summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
}

/// <summary>
/// Root container model for the WoW character media response.
/// </summary>
public class BattleNetWowCharacterMediaResponse
{
    /// <summary>
    /// Gets or sets the media assets (avatar, bust, profile) of the character.
    /// </summary>
    [JsonPropertyName("assets")]
    public List<BattleNetWowMediaAsset>? Assets { get; set; }
}

/// <summary>
/// A single media asset entry.
/// </summary>
public class BattleNetWowMediaAsset
{
    /// <summary>
    /// Gets or sets the asset key (avatar, bust, profile).
    /// </summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>
    /// Gets or sets the absolute image URL.
    /// </summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

/// <summary>
/// Root container model for the Diablo III community profile response.
/// </summary>
public class BattleNetDiabloProfileResponse
{
    /// <summary>
    /// Gets or sets the account BattleTag reported by the profile.
    /// </summary>
    [JsonPropertyName("battleTag")]
    public string? BattleTag { get; set; }

    /// <summary>
    /// Gets or sets the heroes of the account.
    /// </summary>
    [JsonPropertyName("heroes")]
    public List<BattleNetDiabloHeroEntry>? Heroes { get; set; }
}

/// <summary>
/// A single Diablo hero as returned by the community profile endpoint.
/// </summary>
public class BattleNetDiabloHeroEntry
{
    /// <summary>
    /// Gets or sets the hero name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the hero class identifier (e.g. "wizard").
    /// </summary>
    [JsonPropertyName("class")]
    public string? Class { get; set; }

    /// <summary>
    /// Gets or sets the hero level.
    /// </summary>
    [JsonPropertyName("level")]
    public int? Level { get; set; }

    /// <summary>
    /// Gets or sets the paragon level.
    /// </summary>
    [JsonPropertyName("paragonLevel")]
    public int? ParagonLevel { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the hero is seasonal.
    /// </summary>
    [JsonPropertyName("seasonal")]
    public bool Seasonal { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the hero is hardcore.
    /// </summary>
    [JsonPropertyName("hardcore")]
    public bool Hardcore { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the hero died permanently.
    /// </summary>
    [JsonPropertyName("dead")]
    public bool Dead { get; set; }
}


