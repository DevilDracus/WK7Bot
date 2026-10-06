namespace WK7Bot.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using WK7Bot.Models;
using WK7Bot.Services.Interfaces;

/// <summary>
/// Picks the seasonal produce terms used to seed recipe searches. Categories are sampled randomly with a weighted
/// roulette so repeated picks explore the whole seasonal range, with vegetables most likely, then fruits, herbs and
/// finally nuts.
/// </summary>
public static class SeasonalTermPicker
{
    /// <summary>Relative weight of a vegetable term.</summary>
    public const int VegetableWeight = 4;

    /// <summary>Relative weight of a fruit term.</summary>
    public const int FruitWeight = 3;

    /// <summary>Relative weight of a herb term.</summary>
    public const int HerbWeight = 2;

    /// <summary>Relative weight of a nut term.</summary>
    public const int NutWeight = 1;

    /// <summary>
    /// Randomly picks up to two distinct seasonal terms from the given produce.
    /// </summary>
    /// <param name="produce">The seasonal produce, or <see langword="null"/> when it could not be loaded.</param>
    /// <param name="randomSource">The randomness used for the weighted pick.</param>
    /// <returns>Up to two distinct ingredient names, or an empty list when no produce is available.</returns>
    public static IReadOnlyList<string> Pick(SeasonalFoodData? produce, IRandomSource randomSource)
    {
        if (produce == null)
        {
            return Array.Empty<string>();
        }

        randomSource ??= new Services.SystemRandomSource();

        var candidates = new List<WeightedTerm>();
        AddCategory(candidates, produce.Vegetables, VegetableWeight);
        AddCategory(candidates, produce.Fruits, FruitWeight);
        AddCategory(candidates, produce.Herbs, HerbWeight);
        AddCategory(candidates, produce.Nuts, NutWeight);

        if (candidates.Count == 0)
        {
            return Array.Empty<string>();
        }

        var terms = new List<string>(Math.Min(2, candidates.Count));
        int take = Math.Min(2, candidates.Count);
        while (terms.Count < take)
        {
            int totalWeight = 0;
            foreach (var candidate in candidates)
            {
                totalWeight += candidate.Weight;
            }

            int roll = randomSource.Next(totalWeight);
            int pickedIndex = candidates.Count - 1;
            int cumulative = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                cumulative += candidates[i].Weight;
                if (roll < cumulative)
                {
                    pickedIndex = i;
                    break;
                }
            }

            terms.Add(candidates[pickedIndex].Term);
            candidates.RemoveAt(pickedIndex);
        }

        return terms;
    }

    /// <summary>
    /// Appends a category's terms to the weighted candidate pool, skipping blank entries and duplicates across categories.
    /// </summary>
    private static void AddCategory(List<WeightedTerm> candidates, IEnumerable<string>? items, int weight)
    {
        var terms = (items ?? Enumerable.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms)
        {
            if (candidates.Any(candidate => string.Equals(candidate.Term, term, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            candidates.Add(new WeightedTerm(term, weight));
        }
    }

    /// <summary>
    /// A seasonal term paired with the relative likelihood of it being picked.
    /// </summary>
    private readonly record struct WeightedTerm(string Term, int Weight);
}
