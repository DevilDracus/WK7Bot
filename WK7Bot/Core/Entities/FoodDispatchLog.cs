namespace WK7Bot.Core.Entities;

/// <summary>
/// Records that an automatic food publication of a given kind was already sent on a specific date,
/// providing restart-safe duplicate suppression across process restarts.
/// </summary>
public class FoodDispatchLog
{
    /// <summary>
    /// Gets or sets the dispatch kind (see <see cref="FoodDispatchKinds"/>).
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the calendar date the message was sent on (normalized to midnight).
    /// </summary>
    public DateTime SentOn { get; set; }
}

/// <summary>
/// Well-known dispatch kind constants used with <see cref="FoodDispatchLog"/>.
/// </summary>
public static class FoodDispatchKinds
{
    /// <summary>
    /// Kind for the monthly seasonal produce list posted on the first day of each month.
    /// </summary>
    public const string MonthlyProduce = "monthly_produce";

    /// <summary>
    /// Kind for the Thursday seasonal web recipe post.
    /// </summary>
    public const string WeeklyRecipe = "weekly_recipe";
}
