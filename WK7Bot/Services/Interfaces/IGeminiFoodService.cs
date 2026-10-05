namespace WK7Bot.Services.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;
using WK7Bot.Models;

/// <summary>
/// Provides methods for generating structured seasonal food lists and dialysis/transplant-safe recipes using Google Gemini structured JSON output mode.
/// </summary>
public interface IGeminiFoodService
{
    /// <summary>
    /// Queries the Gemini API for seasonal fruits, vegetables, herbs, and nuts for a given month and region.
    /// </summary>
    /// <param name="dateTime">The target month date instance.</param>
    /// <param name="cancellationToken">A token to monitor for task cancellation.</param>
    /// <returns>A structured <see cref="SeasonalFoodData"/> instance or null if processing fails.</returns>
    Task<SeasonalFoodData?> GetSeasonalProduceAsync(DateTime dateTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the Gemini API to generate a dialysis and immunosuppressive recipient-friendly recipe using current seasonal produce.
    /// </summary>
    /// <param name="dateTime">The target date used to determine seasonal ingredients.</param>
    /// <param name="cancellationToken">A token to monitor for task cancellation.</param>
    /// <returns>A structured <see cref="RenalRecipeData"/> instance containing diet tags and instructions, or null if processing fails.</returns>
    Task<RenalRecipeData?> GetWeeklyRenalRecipeAsync(DateTime dateTime, CancellationToken cancellationToken = default);
}