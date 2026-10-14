namespace WK7Bot.Services;

using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using WK7Bot.Models;

/// <summary>
/// Parses schema.org <c>Recipe</c> JSON-LD blocks from HTML pages into <see cref="RenalRecipeData"/>.
/// All methods are static and pure — no I/O, no instance state — so the parser is directly
/// unit-testable without HTTP mocking.
/// </summary>
public static class RecipeHtmlParser
{
    private static readonly Regex JsonLdRegex = new(
        @"<script\b[^>]*type\s*=\s*[""']?application/ld\+json[""']?[^>]*>(?<json>.*?)</script>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex DurationRegex = new(
        @"^P(?:(?<days>\d+)D)?(?:T(?:(?<hours>\d+)H)?(?:(?<minutes>\d+)M)?(?:(?<seconds>\d+)S)?)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LeadingIntegerRegex = new(
        @"\d+",
        RegexOptions.Compiled);

    private static readonly Regex MarkupRegex = new(
        "<[^>]+>",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses the first schema.org <c>Recipe</c> node found in the JSON-LD blocks of a page.
    /// </summary>
    /// <param name="html">The raw HTML document to search.</param>
    /// <param name="sourceUrl">The URL the HTML was fetched from, preserved on the result.</param>
    /// <returns>The parsed recipe, or <see langword="null"/> when no usable recipe node exists.</returns>
    public static RenalRecipeData? ParseRecipe(string html, string sourceUrl)
    {
        foreach (Match block in JsonLdRegex.Matches(html))
        {
            var recipe = ParseJsonLdRecipe(block.Groups["json"].Value, sourceUrl);
            if (recipe != null)
            {
                return recipe;
            }
        }

        return null;
    }

    /// <summary>
    /// Maps a single JSON-LD document onto <see cref="RenalRecipeData"/>, returning <see langword="null"/> when the
    /// document contains no usable recipe node.
    /// </summary>
    private static RenalRecipeData? ParseJsonLdRecipe(string json, string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            foreach (var node in EnumerateRecipeNodes(document.RootElement))
            {
                var recipe = MapRecipeNode(node, sourceUrl);
                if (recipe != null)
                {
                    return recipe;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Enumerates the candidate objects of a JSON-LD document that may describe a recipe.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateRecipeNodes(JsonElement root)
    {
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                return root.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList();

            case JsonValueKind.Object when root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array:
                return graph.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Append(root).ToList();

            case JsonValueKind.Object:
                return new[] { root };

            default:
                return Array.Empty<JsonElement>();
        }
    }

    /// <summary>
    /// Converts a JSON-LD object typed as <c>Recipe</c> into a recipe payload, or <see langword="null"/> when the
    /// object is not a recipe or lacks the ingredients and instructions a post needs.
    /// </summary>
    private static RenalRecipeData? MapRecipeNode(JsonElement node, string sourceUrl)
    {
        if (node.ValueKind != JsonValueKind.Object || !HasType(node, "Recipe"))
        {
            return null;
        }

        var ingredients = ReadStringList(node, "recipeIngredient");
        var instructions = ReadInstructions(node);
        var title = ReadString(node, "name");

        if (string.IsNullOrWhiteSpace(title) || ingredients.Count == 0 || instructions.Count == 0)
        {
            return null;
        }

        return new RenalRecipeData
        {
            Title = CleanText(title),
            Description = CleanText(ReadString(node, "description")),
            PrepTime = FormatDuration(ReadString(node, "prepTime")),
            CookTime = FormatDuration(ReadString(node, "cookTime")),
            Servings = ParseServings(ReadString(node, "recipeYield")),
            Ingredients = ingredients.Select(CleanText).Where(t => t.Length > 0).ToList(),
            Instructions = instructions.Select(CleanText).Where(t => t.Length > 0).ToList(),
            ImageUrl = ResolveImageUrl(node),
            SourceUrl = sourceUrl
        };
    }

    /// <summary>
    /// Determines whether a JSON-LD node declares the given schema.org type (string or array form, URL or prefix form).
    /// </summary>
    private static bool HasType(JsonElement node, string typeName)
    {
        if (!node.TryGetProperty("@type", out var typeProperty))
        {
            return false;
        }

        if (typeProperty.ValueKind == JsonValueKind.String)
        {
            return MatchesType(typeProperty.GetString(), typeName);
        }

        if (typeProperty.ValueKind == JsonValueKind.Array)
        {
            return typeProperty.EnumerateArray()
                .Any(entry => entry.ValueKind == JsonValueKind.String && MatchesType(entry.GetString(), typeName));
        }

        return false;
    }

    private static bool MatchesType(string? value, string typeName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var separatorIndex = normalized.LastIndexOfAny(new[] { '/', ':' });
        if (separatorIndex >= 0)
        {
            normalized = normalized[(separatorIndex + 1)..];
        }

        return string.Equals(normalized, typeName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a scalar property (string or number), also accepting arrays such as schema.org
    /// <c>recipeYield</c> (<c>["4", "4 Portionen"]</c>) by taking the first usable entry.
    /// </summary>
    private static string ReadString(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return ReadScalar(property);
    }

    private static string ReadScalar(JsonElement property)
    {
        switch (property.ValueKind)
        {
            case JsonValueKind.String:
                return property.GetString() ?? string.Empty;

            case JsonValueKind.Number:
                return property.GetRawText();

            case JsonValueKind.Array:
                return property.EnumerateArray()
                    .Select(ReadScalar)
                    .FirstOrDefault(value => value.Length > 0) ?? string.Empty;

            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Reads a schema.org text property that may be a single string or an array of strings.
    /// </summary>
    private static List<string> ReadStringList(JsonElement node, string propertyName)
    {
        var values = new List<string>();
        if (!node.TryGetProperty(propertyName, out var property))
        {
            return values;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var single = property.GetString();
            if (!string.IsNullOrWhiteSpace(single))
            {
                values.Add(single);
            }

            return values;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        foreach (var entry in property.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                var text = entry.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    values.Add(text);
                }
            }
            else if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("name", out var name)
                     && name.ValueKind == JsonValueKind.String)
            {
                var text = name.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    values.Add(text);
                }
            }
        }

        return values;
    }

    /// <summary>
    /// Flattens <c>recipeInstructions</c> (string, HowToStep array or nested HowToSection lists) into plain steps.
    /// </summary>
    private static List<string> ReadInstructions(JsonElement node)
    {
        var steps = new List<string>();
        if (!node.TryGetProperty("recipeInstructions", out var property))
        {
            return steps;
        }

        AppendInstructions(property, steps);
        return steps;
    }

    private static void AppendInstructions(JsonElement property, List<string> steps)
    {
        switch (property.ValueKind)
        {
            case JsonValueKind.String:
                var text = property.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    steps.Add(text);
                }

                break;

            case JsonValueKind.Object:
                if (property.TryGetProperty("itemListElement", out var nested))
                {
                    AppendInstructions(nested, steps);
                }
                else
                {
                    var stepText = ReadString(property, "text");
                    if (string.IsNullOrWhiteSpace(stepText))
                    {
                        stepText = ReadString(property, "name");
                    }

                    if (!string.IsNullOrWhiteSpace(stepText))
                    {
                        steps.Add(stepText);
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var entry in property.EnumerateArray())
                {
                    AppendInstructions(entry, steps);
                }

                break;
        }
    }

    /// <summary>
    /// Resolves the first usable https hero image URL of a recipe node.
    /// </summary>
    private static string ResolveImageUrl(JsonElement node)
    {
        if (!node.TryGetProperty("image", out var image))
        {
            return string.Empty;
        }

        var candidates = image.ValueKind switch
        {
            JsonValueKind.String => new[] { image.GetString() ?? string.Empty },
            JsonValueKind.Object => new[] { ReadString(image, "url") },
            JsonValueKind.Array => image.EnumerateArray()
                .Select(entry => entry.ValueKind switch
                {
                    JsonValueKind.String => entry.GetString() ?? string.Empty,
                    JsonValueKind.Object => ReadString(entry, "url"),
                    _ => string.Empty
                })
                .ToArray(),
            _ => Array.Empty<string>()
        };

        return candidates.FirstOrDefault(url =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) ?? string.Empty;
    }

    /// <summary>
    /// Formats an ISO 8601 duration (e.g. <c>PT1H30M</c>) as a German display string (e.g. <c>1 Std. 30 Min.</c>).
    /// </summary>
    private static string FormatDuration(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var match = DurationRegex.Match(value.Trim());
        if (!match.Success)
        {
            return value.Trim();
        }

        var parts = new List<string>();
        if (match.Groups["days"].Success)
        {
            parts.Add($"{match.Groups["days"].Value} Tg.");
        }

        if (match.Groups["hours"].Success)
        {
            parts.Add($"{match.Groups["hours"].Value} Std.");
        }

        if (match.Groups["minutes"].Success)
        {
            parts.Add($"{match.Groups["minutes"].Value} Min.");
        }

        if (match.Groups["seconds"].Success)
        {
            parts.Add($"{match.Groups["seconds"].Value} Sek.");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Extracts the first integer of a schema.org yield string such as <c>4 Portionen</c>.
    /// </summary>
    private static int ParseServings(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var match = LeadingIntegerRegex.Match(value);
        return match.Success && int.TryParse(match.Value, out var servings) ? servings : 0;
    }

    /// <summary>
    /// Strips markup, decodes HTML entities and collapses whitespace in scraped text.
    /// </summary>
    private static string CleanText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decoded = System.Net.WebUtility.HtmlDecode(value);
        var withoutMarkup = MarkupRegex.Replace(decoded, " ");
        return string.Join(" ", withoutMarkup.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
