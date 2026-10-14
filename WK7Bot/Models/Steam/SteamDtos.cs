using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using WK7Bot.Models;

namespace WK7Bot.Models.Steam;

/// <summary>
/// Root container model for the Steam GetPlayerSummaries Web API response.
/// </summary>
public class SteamPlayerSummariesResponse
{
    /// <summary>
    /// Gets or sets the player summary container returned by Steam.
    /// </summary>
    [JsonPropertyName("response")]
    public PlayerSummaryContainer? Response { get; set; }
}

/// <summary>
/// Inner list container holding player summary items.
/// </summary>
public class PlayerSummaryContainer
{
    /// <summary>
    /// Gets or sets the list of player summaries.
    /// </summary>
    [JsonPropertyName("players")]
    public List<PlayerSummaryItem>? Players { get; set; }
}

/// <summary>
/// Represents individual player summary attributes returned by Steam.
/// </summary>
public class PlayerSummaryItem
{
    /// <summary>
    /// Gets or sets the display persona name of the player.
    /// </summary>
    [JsonPropertyName("personaname")]
    public string? PersonaName { get; set; }

    /// <summary>
    /// Gets or sets the absolute URL of the player's full-size Steam avatar.
    /// </summary>
    [JsonPropertyName("avatarfull")]
    public string? AvatarFull { get; set; }

    /// <summary>
    /// Gets or sets the numeric online status indicator.
    /// </summary>
    [JsonPropertyName("personastate")]
    public int PersonaState { get; set; }

    /// <summary>
    /// Gets or sets extra display information for the currently active game.
    /// </summary>
    [JsonPropertyName("gameextrainfo")]
    public string? GameExtraInfo { get; set; }

    /// <summary>
    /// Gets or sets the application identifier of the currently active game.
    /// </summary>
    [JsonPropertyName("gameid")]
    public string? GameId { get; set; }
}

/// <summary>
/// Root container model for the Steam GetRecentlyPlayedGames Web API response.
/// </summary>
public class SteamRecentlyPlayedGamesResponse
{
    /// <summary>
    /// Gets or sets the recently played games response container.
    /// </summary>
    [JsonPropertyName("response")]
    public RecentlyPlayedGamesContainer? Response { get; set; }
}

/// <summary>
/// Inner list container holding recently played game items.
/// </summary>
public class RecentlyPlayedGamesContainer
{
    /// <summary>
    /// Gets or sets the collection of games played in the last two weeks.
    /// </summary>
    [JsonPropertyName("games")]
    public List<RecentlyPlayedGameItem>? Games { get; set; }
}

/// <summary>
/// Represents playtime statistics for a specific game played recently.
/// </summary>
public class RecentlyPlayedGameItem
{
    /// <summary>
    /// Gets or sets the unique Steam application identifier.
    /// </summary>
    [JsonPropertyName("appid")]
    public uint AppId { get; set; }

    /// <summary>
    /// Gets or sets the title of the game.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the total minutes played during the past two weeks.
    /// </summary>
    [JsonPropertyName("playtime_2weeks")]
    public int PlaytimeTwoWeeks { get; set; }

    /// <summary>
    /// Gets or sets total lifetime minutes played across all time.
    /// </summary>
    [JsonPropertyName("playtime_forever")]
    public int PlaytimeForever { get; set; }
}

/// <summary>
/// Root container model for the Steam GetSchemaForGame Web API response.
/// </summary>
public class SteamSchemaResponse
{
    /// <summary>
    /// Gets or sets game schema metadata container.
    /// </summary>
    [JsonPropertyName("game")]
    public SchemaGameData? Game { get; set; }
}

/// <summary>
/// Holds available game stats definitions for a game schema.
/// </summary>
public class SchemaGameData
{
    /// <summary>
    /// Gets or sets stats and achievement definitions available for the game.
    /// </summary>
    [JsonPropertyName("availableGameStats")]
    public SchemaStatsData? AvailableGameStats { get; set; }
}

/// <summary>
/// Container list for schema achievement definitions.
/// </summary>
public class SchemaStatsData
{
    /// <summary>
    /// Gets or sets the list of achievement definitions defined in the game schema.
    /// </summary>
    [JsonPropertyName("achievements")]
    public List<SchemaAchievementItem>? Achievements { get; set; }
}

/// <summary>
/// Represents achievement display definitions including icons and descriptions.
/// </summary>
public class SchemaAchievementItem
{
    /// <summary>
    /// Gets or sets the internal API identifier matching player stats achievement names.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable display title.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the localized text describing unlock requirements.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute URL pointing to the unlocked achievement icon graphic.
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the achievement is hidden on the community site.
    /// </summary>
    [JsonPropertyName("hidden")]
    [JsonConverter(typeof(FlexibleBoolConverter))]
    public bool Hidden { get; set; }
}

/// <summary>
/// Root container model for the Steam GetPlayerAchievements Web API response.
/// </summary>
public class SteamPlayerAchievementsResponse
{
    /// <summary>
    /// Gets or sets player stats achievement progress container.
    /// </summary>
    [JsonPropertyName("playerstats")]
    public PlayerStatsData? PlayerStats { get; set; }
}

/// <summary>
/// Inner player statistics payload returned by Steam GetPlayerAchievements.
/// </summary>
public class PlayerStatsData
{
    /// <summary>
    /// Gets or sets the 64-bit target Steam identifier.
    /// </summary>
    [JsonPropertyName("steamID")]
    public string? SteamId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the target game.
    /// </summary>
    [JsonPropertyName("gameName")]
    public string? GameName { get; set; }

    /// <summary>
    /// Gets or sets the collection of achievement unlock statuses for the user.
    /// </summary>
    [JsonPropertyName("achievements")]
    public List<PlayerAchievementItem>? Achievements { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the request succeeded on Steam's backend.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional error message string if request processing failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Root container model for the Steam GetTopAchievementsForGames Web API response.
/// </summary>
public class SteamTopAchievementsResponse
{
    /// <summary>
    /// Gets or sets the payload containing per-game unlocked achievements.
    /// </summary>
    [JsonPropertyName("response")]
    public SteamTopAchievementsPayload? Response { get; set; }
}

/// <summary>
/// Inner payload returned by Steam GetTopAchievementsForGames.
/// </summary>
public class SteamTopAchievementsPayload
{
    /// <summary>
    /// Gets or sets the list of games with unlocked achievement details for the requested user.
    /// </summary>
    [JsonPropertyName("games")]
    public List<SteamTopAchievementGame>? Games { get; set; }
}

/// <summary>
/// Per-game unlocked achievement data returned by Steam GetTopAchievementsForGames.
/// </summary>
public class SteamTopAchievementGame
{
    /// <summary>
    /// Gets or sets the application identifier of the game.
    /// </summary>
    [JsonPropertyName("appid")]
    public uint AppId { get; set; }

    /// <summary>
    /// Gets or sets the total number of achievements defined for the game.
    /// </summary>
    [JsonPropertyName("total_achievements")]
    public int TotalAchievements { get; set; }

    /// <summary>
    /// Gets or sets the achievements the user has unlocked; absent when none are unlocked.
    /// </summary>
    [JsonPropertyName("achievements")]
    public List<SteamTopAchievementItem>? Achievements { get; set; }
}

/// <summary>
/// Individual unlocked achievement entry returned by Steam GetTopAchievementsForGames.
/// </summary>
public class SteamTopAchievementItem
{
    /// <summary>
    /// Gets or sets the localized display title of the achievement.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the localized description of the achievement.
    /// </summary>
    [JsonPropertyName("desc")]
    public string? Desc { get; set; }

    /// <summary>
    /// Gets or sets the unlocked icon file name (without CDN prefix).
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>
    /// Gets or sets the locked/grayscale icon file name (without CDN prefix).
    /// </summary>
    [JsonPropertyName("icon_gray")]
    public string? IconGray { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the achievement is hidden on the Steam community site.
    /// </summary>
    [JsonPropertyName("hidden")]
    [JsonConverter(typeof(FlexibleBoolConverter))]
    public bool Hidden { get; set; }

    /// <summary>
    /// Gets or sets the global percentage of Steam players who unlocked this achievement, as a numeric string (e.g. "12.0").
    /// </summary>
    [JsonPropertyName("player_percent_unlocked")]
    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double PercentUnlocked { get; set; }
}

/// <summary>
/// Converts JSON numbers, booleans, or string representations into an integer value.
/// </summary>
public class FlexibleIntConverter : JsonConverter<int>
{
    /// <summary>
    /// Reads and converts JSON token values to an integer representation.
    /// </summary>
    /// <param name="reader">The JSON reader instance.</param>
    /// <param name="typeToConvert">The target object type.</param>
    /// <param name="options">Serializer options in context.</param>
    /// <returns>An integer representation of the JSON token value.</returns>
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.GetInt32();
        if (reader.TokenType == JsonTokenType.True) return 1;
        if (reader.TokenType == JsonTokenType.False) return 0;
        if (reader.TokenType == JsonTokenType.String
            && int.TryParse(reader.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int val))
        {
            return val;
        }

        return 0;
    }

    /// <summary>
    /// Writes an integer value to the JSON target stream.
    /// </summary>
    /// <param name="writer">The JSON writer instance.</param>
    /// <param name="value">The integer value to write.</param>
    /// <param name="options">Serializer options in context.</param>
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

/// <summary>
/// Converts JSON numbers or string representations into a double value.
/// </summary>
public class FlexibleDoubleConverter : JsonConverter<double>
{
    /// <summary>
    /// Reads and converts JSON token values to a double representation.
    /// </summary>
    /// <param name="reader">The JSON reader instance.</param>
    /// <param name="typeToConvert">The target object type.</param>
    /// <param name="options">Serializer options in context.</param>
    /// <returns>A double representation of the JSON token value.</returns>
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.GetDouble();
        if (reader.TokenType == JsonTokenType.String
            && double.TryParse(reader.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double val))
        {
            return val;
        }

        return 0;
    }

    /// <summary>
    /// Writes a double value to the JSON target stream.
    /// </summary>
    /// <param name="writer">The JSON writer instance.</param>
    /// <param name="value">The double value to write.</param>
    /// <param name="options">Serializer options in context.</param>
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

/// <summary>
/// Converts JSON booleans, numbers, or string representations into a boolean value.
/// </summary>
public class FlexibleBoolConverter : JsonConverter<bool>
{
    /// <summary>
    /// Reads and converts JSON token values to a boolean representation.
    /// </summary>
    /// <param name="reader">The JSON reader instance.</param>
    /// <param name="typeToConvert">The target object type.</param>
    /// <param name="options">Serializer options in context.</param>
    /// <returns>A boolean representation of the JSON token value.</returns>
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True) return true;
        if (reader.TokenType == JsonTokenType.False) return false;
        if (reader.TokenType == JsonTokenType.Number) return reader.GetDouble() != 0;
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (bool.TryParse(value, out bool parsed)) return parsed;
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double numeric)) return numeric != 0;
        }

        return false;
    }

    /// <summary>
    /// Writes a boolean value to the JSON target stream.
    /// </summary>
    /// <param name="writer">The JSON writer instance.</param>
    /// <param name="value">The boolean value to write.</param>
    /// <param name="options">Serializer options in context.</param>
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}

/// <summary>
/// Converts JSON numbers or string representations into a long integer timestamp.
/// </summary>
public class FlexibleLongConverter : JsonConverter<long>
{
    /// <summary>
    /// Reads and converts JSON token values to a 64-bit integer timestamp.
    /// </summary>
    /// <param name="reader">The JSON reader instance.</param>
    /// <param name="typeToConvert">The target object type.</param>
    /// <param name="options">Serializer options in context.</param>
    /// <returns>A 64-bit integer representation of the JSON token value.</returns>
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.GetInt64();
        if (reader.TokenType == JsonTokenType.String
            && long.TryParse(reader.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long val))
        {
            return val;
        }

        return 0;
    }

    /// <summary>
    /// Writes a 64-bit integer value to the JSON target stream.
    /// </summary>
    /// <param name="writer">The JSON writer instance.</param>
    /// <param name="value">The integer value to write.</param>
    /// <param name="options">Serializer options in context.</param>
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

/// <summary>
/// Represents individual player achievement progress and unlock timing.
/// </summary>
public class PlayerAchievementItem
{
    /// <summary>
    /// Gets or sets the internal API identifier of the achievement.
    /// </summary>
    [JsonPropertyName("apiname")]
    public string ApiName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the achievement unlock status (1 for unlocked, 0 for locked).
    /// </summary>
    [JsonPropertyName("achieved")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int Achieved { get; set; }

    /// <summary>
    /// Gets or sets the Unix epoch timestamp indicating when the achievement was unlocked.
    /// </summary>
    [JsonPropertyName("unlocktime")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long UnlockTime { get; set; }

    /// <summary>
    /// Gets or sets the optional display name returned by the player stats endpoint.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the optional description returned by the player stats endpoint.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets a value indicating whether the achievement has been unlocked by the player.
    /// </summary>
    [JsonIgnore]
    public bool IsAchieved => Achieved == 1;
}

