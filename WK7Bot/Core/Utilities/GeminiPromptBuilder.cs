using System.Text.Json.Nodes;

namespace WK7Bot.Core.Utilities;

/// <summary>
/// Pure prompt and JSON-schema factory for the Gemini food-generation endpoints.
/// No external dependencies, no side effects — safe to call from any thread.
/// </summary>
public static class GeminiPromptBuilder
{
    /// <summary>
    /// Builds the prompt and schema for the seasonal-produce endpoint.
    /// </summary>
    /// <param name="monthName">German month name (e.g. "Oktober").</param>
    /// <returns>A tuple of (prompt, jsonSchema).</returns>
    public static (string Prompt, JsonObject Schema) BuildSeasonalProduce(string monthName)
    {
        string prompt = $"Provide a complete list of seasonal produce for the month of {monthName} in Central Europe (Germany/Leipzig region). " +
                       "Categorize every item strictly into fruits, vegetables, herbs, and nuts.\n" +
                       "IMPORTANT: Use the GERMAN names for fruits, vegetables, herbs and nuts!";

        var schema = new JsonObject
        {
            ["type"] = "OBJECT",
            ["properties"] = new JsonObject
            {
                ["month"] = new JsonObject { ["type"] = "STRING" },
                ["fruits"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } },
                ["vegetables"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } },
                ["herbs"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } },
                ["nuts"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } }
            },
            ["required"] = new JsonArray { "month", "fruits", "vegetables", "herbs", "nuts" }
        };

        return (prompt, schema);
    }

    /// <summary>
    /// Builds the prompt and schema for the weekly renal-safe recipe endpoint.
    /// </summary>
    /// <param name="monthName">German month name (e.g. "Oktober").</param>
    /// <returns>A tuple of (prompt, jsonSchema).</returns>
    public static (string Prompt, JsonObject Schema) BuildWeeklyRenalRecipe(string monthName)
    {
        string prompt = $"Create a delicious recipe for the month of {monthName} using local Central European seasonal produce (Germany/Leipzig region), featuring at least one seasonal fruit or vegetable. " +
                        "CRITICAL DIETARY RESTRICTIONS:\n" +
                        "1. Tailor the recipe for individuals on dialysis or kidney transplant recipients taking immunosuppressants.\n" +
                        "2. Keep Potassium (Kalium) moderate/controlled, Phosphorus as low as practical, and Sodium under strict limits.\n" +
                        "3. Strictly EXCLUDE: any raw or undercooked ingredients which cannot be cleaned thoroughly and washed with vinegar, raw sprouts, raw strawberries, grapefruit, pomegranate, star fruit (Karambole), AND ALL MOLD CHEESES (Schimmelkäse like Gorgonzola, Roquefort, Brie, Camembert are an absolute NO-GO).\n" +
                        "4. Rohmilchkäse (raw milk cheese) is strictly prohibited unless it is thoroughly heated, cooked in a boiling sauce, or fully baked.\n" +
                        "5. Every ingredient should be fully cooked (boiled, baked, or fried through). In the case of raw salads they need to be thoroughly cleaned and washed with vinegar.\n" +
                        "6. Prefer low-potassium ingredients and avoid obvious high-potassium items where possible (e.g., banana, potato, tomato paste, dried fruit, nuts). Even better: use preparation methods that leach potassium out of the ingredients (e.g., soak vegetables and boil them in plenty of water, then discard the water) and include those extra steps in the instructions.\n" +
                        "OUTPUT REQUIREMENTS:\n" +
                        "7. Generate exactly 4 servings and assign every applicable diet tag from this fixed list, using the exact English identifiers: low_potassium, low_phosphate, low_sodium, low_carb, protein_rich, high_fiber (omit tags that do not apply).\n" +
                        "8. In transplant_safety_notes, briefly explain why the dish fits these restrictions and add a short disclaimer to consult one's doctor or dietitian.\n\n" +
                        "IMPORTANT: Write ALL text fields (title, description, prep_time, cook_time, seasonal_ingredients_used, ingredients, instructions, transplant_safety_notes) in GERMAN! Keep diet_tags as the exact English identifiers from the list above.";

        var schema = new JsonObject
        {
            ["type"] = "OBJECT",
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject { ["type"] = "STRING" },
                ["description"] = new JsonObject { ["type"] = "STRING" },
                ["prep_time"] = new JsonObject { ["type"] = "STRING" },
                ["cook_time"] = new JsonObject { ["type"] = "STRING" },
                ["servings"] = new JsonObject { ["type"] = "INTEGER" },
                ["seasonal_ingredients_used"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } },
                ["ingredients"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } },
                ["instructions"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } },
                ["transplant_safety_notes"] = new JsonObject { ["type"] = "STRING" },
                ["diet_tags"] = new JsonObject
                {
                    ["type"] = "ARRAY",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "STRING",
                        ["enum"] = new JsonArray
                        {
                            "low_potassium",
                            "low_phosphate",
                            "low_sodium",
                            "low_carb",
                            "protein_rich",
                            "high_fiber"
                        }
                    }
                }
            },
            ["required"] = new JsonArray { "title", "description", "prep_time", "cook_time", "servings", "seasonal_ingredients_used", "ingredients", "instructions", "transplant_safety_notes", "diet_tags" }
        };

        return (prompt, schema);
    }

    /// <summary>
    /// Builds the prompt and schema for the recipe-formatting endpoint.
    /// </summary>
    /// <param name="scrapedRecipe">The raw recipe JSON to be reformatted.</param>
    /// <returns>A tuple of (prompt, jsonSchema).</returns>
    public static (string Prompt, JsonObject Schema) BuildFormatRecipe(JsonObject scrapedRecipe)
    {
        string prompt = "Below is a recipe scraped from a website (JSON). Re-format it into the schema you are given.\n" +
                        "RULES:\n" +
                        "1. FAITHFUL: keep the same dish. Never invent, drop, merge or translate ingredients or steps. Keep the original language (usually German) and return EVERY step of the original list, in the same order.\n" +
                        "2. STRUCTURE: one ingredient per list entry, starting with quantity and unit when given (e.g. \"200 g Mehl\"). Split run-on text into one short, ordered step per entry.\n" +
                        "3. TITLE: keep it, but trim site-specific suffixes such as an author name after \"von\". DESCRIPTION: one or two short sentences summarising the dish.\n" +
                        "4. TIMES: short German strings such as \"20 Minuten\"; use an empty string when unknown. SERVINGS: the portion count, 0 when unknown.\n" +
                        "5. Do NOT add diet tags, safety notes, seasonal claims or any medical/dietary advice - formatting only.\n\n" +
                        $"Recipe JSON:\n{scrapedRecipe.ToJsonString()}";

        var schema = new JsonObject
        {
            ["type"] = "OBJECT",
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject { ["type"] = "STRING" },
                ["description"] = new JsonObject { ["type"] = "STRING" },
                ["prep_time"] = new JsonObject { ["type"] = "STRING" },
                ["cook_time"] = new JsonObject { ["type"] = "STRING" },
                ["servings"] = new JsonObject { ["type"] = "INTEGER" },
                ["ingredients"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } },
                ["instructions"] = new JsonObject { ["type"] = "ARRAY", ["items"] = new JsonObject { ["type"] = "STRING" } }
            },
            ["required"] = new JsonArray { "title", "description", "prep_time", "cook_time", "servings", "ingredients", "instructions" }
        };

        return (prompt, schema);
    }
}
