namespace WK7Bot.Core.Utilities;

using System.Globalization;
using Discord;

/// <summary>
/// Renders Discord Components V2 cards and converts existing embeds into them. Components V2 messages
/// carry neither <c>content</c> nor <c>embeds</c>, so everything — title, description, fields, image,
/// footer and the title link — is projected into a single <see cref="ContainerBuilder"/> with
/// <see cref="TextDisplayBuilder"/> children, a section accessory for the thumbnail and a media
/// gallery for the full-width image.
/// </summary>
/// <remarks>
/// The conversion is deliberately lossless: every piece of information an embed can carry has a
/// component equivalent here, so a call site only has to swap <c>embed:</c> for
/// <c>components: CardBuilder.FromEmbed(embed)</c> plus <see cref="MessageFlags.ComponentsV2"/>.
/// </remarks>
public static class CardBuilder
{
    /// <summary>Discord's limit on children inside one container.</summary>
    public const int MaxChildrenPerContainer = 40;

    /// <summary>Discord's per-text-display character ceiling.</summary>
    public const int MaxTextDisplayLength = 4000;

    /// <summary>Total character budget kept below the per-message ceiling so long cards still send.</summary>
    public const int MaxTotalTextLength = 3900;

    /// <summary>Accent colour used when an embed declares none.</summary>
    public static readonly Color DefaultAccent = new(0x58, 0x65, 0xF2);

    /// <summary>
    /// Converts an embed into a Components V2 container.
    /// </summary>
    /// <param name="embed">The embed to convert.</param>
    /// <returns>The card to send with <see cref="MessageFlags.ComponentsV2"/>.</returns>
    public static MessageComponent FromEmbed(Embed embed)
    {
        ArgumentNullException.ThrowIfNull(embed);

        var budget = new TextBudget(MaxTotalTextLength);
        var head = new List<IMessageComponentBuilder>();
        var fields = new List<string>();
        var tail = new List<IMessageComponentBuilder>();

        if (!string.IsNullOrWhiteSpace(embed.Title))
        {
            head.Add(Display(budget, $"# {embed.Title.Trim()}"));
        }

        var description = embed.Description;
        var thumbnailUrl = embed.Thumbnail?.Url;
        var hasThumbnail = !string.IsNullOrWhiteSpace(thumbnailUrl);

        if (!string.IsNullOrWhiteSpace(description) && hasThumbnail)
        {
            // A section pairs the description with the thumbnail as its accessory.
            head.Add(new SectionBuilder()
                .AddComponent(Display(budget, description!))
                .WithAccessory(new ThumbnailBuilder().WithMedia(new UnfurledMediaItemProperties(thumbnailUrl!))));
        }
        else if (!string.IsNullOrWhiteSpace(description))
        {
            head.Add(Display(budget, description!));
        }
        else if (hasThumbnail)
        {
            // A thumbnail cannot be a direct container child, so it rides along in a section.
            head.Add(new SectionBuilder()
                .AddComponent(Display(budget, "\u200b"))
                .WithAccessory(new ThumbnailBuilder().WithMedia(new UnfurledMediaItemProperties(thumbnailUrl!))));
        }

        foreach (var field in embed.Fields)
        {
            var text = string.IsNullOrWhiteSpace(field.Name)
                ? field.Value ?? string.Empty
                : $"**{field.Name}**\n{field.Value}";

            if (!string.IsNullOrWhiteSpace(text))
            {
                fields.Add(text);
            }
        }

        if (embed.Image?.Url is { Length: > 0 } imageUrl)
        {
            tail.Add(new MediaGalleryBuilder().AddItem(imageUrl, null, false));
        }

        if (embed.Url is { Length: > 0 } titleUrl)
        {
            tail.Add(Display(budget, $"-# 🔗 {titleUrl}"));
        }

        var footer = BuildFooter(embed);
        if (footer is not null)
        {
            tail.Add(Display(budget, footer));
        }

        var container = new ContainerBuilder().WithAccentColor(embed.Color ?? DefaultAccent);

        foreach (var child in head)
        {
            container.AddComponent(child);
        }

        if (fields.Count > 0)
        {
            // Separate displays read better, but a container only holds 40 children.
            var asSeparateDisplays = head.Count + fields.Count + tail.Count <= MaxChildrenPerContainer;
            if (asSeparateDisplays)
            {
                foreach (var field in fields)
                {
                    container.AddComponent(Display(budget, field));
                }
            }
            else
            {
                container.AddComponent(Display(budget, string.Join("\n\n", fields)));
            }
        }

        foreach (var child in tail)
        {
            container.AddComponent(child);
        }

        return new ComponentBuilderV2().AddComponent(container).Build();
    }

    /// <summary>
    /// Builds a card from an explicit specification, for call sites that do not start from an embed.
    /// </summary>
    /// <param name="title">The card heading.</param>
    /// <param name="body">The introductory body text.</param>
    /// <param name="sections">The named sections rendered as separate text displays.</param>
    /// <param name="accent">The container accent colour.</param>
    /// <param name="footer">The small footer line.</param>
    /// <returns>The card to send with <see cref="MessageFlags.ComponentsV2"/>.</returns>
    public static MessageComponent Build(
        string title,
        string? body = null,
        IReadOnlyList<(string Heading, string Body)>? sections = null,
        Color? accent = null,
        string? footer = null)
    {
        var budget = new TextBudget(MaxTotalTextLength);
        var container = new ContainerBuilder().WithAccentColor(accent ?? DefaultAccent);

        if (!string.IsNullOrWhiteSpace(title))
        {
            container.AddComponent(Display(budget, $"# {title}"));
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            container.AddComponent(Display(budget, body!));
        }

        if (sections is not null)
        {
            var count = 0;
            foreach (var (heading, sectionBody) in sections)
            {
                if (++count > MaxChildrenPerContainer)
                {
                    break;
                }

                container.AddComponent(Display(budget, $"**{heading}**\n{sectionBody}"));
            }
        }

        if (!string.IsNullOrWhiteSpace(footer))
        {
            container.AddComponent(Display(budget, $"-# {footer}"));
        }

        return new ComponentBuilderV2().AddComponent(container).Build();
    }

    /// <summary>Creates one text display child, charging the shared character budget.</summary>
    /// <param name="budget">The remaining-character budget.</param>
    /// <param name="text">The text to render.</param>
    /// <returns>The text display builder.</returns>
    private static TextDisplayBuilder Display(TextBudget budget, string text)
        => new TextDisplayBuilder().WithContent(budget.Take(text));

    /// <summary>Renders the footer line, including the timestamp as a Discord relative time.</summary>
    /// <param name="embed">The source embed.</param>
    /// <returns>The footer text, or <see langword="null"/> when the embed has neither footer nor timestamp.</returns>
    private static string? BuildFooter(Embed embed)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(embed.Footer?.Text))
        {
            parts.Add(embed.Footer.Value.Text);
        }

        if (embed.Timestamp is { } timestamp)
        {
            parts.Add($"<t:{timestamp.ToUnixTimeSeconds()}:R>");
        }

        return parts.Count == 0 ? null : $"-# {string.Join(" · ", parts)}";
    }

    /// <summary>
    /// Tracks the remaining character budget so a long embed cannot push the card past Discord's
    /// per-message text ceiling (which would make the send fail outright).
    /// </summary>
    private sealed class TextBudget
    {
        private int _remaining;

        /// <summary>Initializes a new instance of the <see cref="TextBudget"/> class.</summary>
        /// <param name="total">The total character budget.</param>
        public TextBudget(int total) => _remaining = total;

        /// <summary>Clamps one text block to the remaining budget and charges it.</summary>
        /// <param name="text">The text to clamp.</param>
        /// <returns>The clamped text.</returns>
        public string Take(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var allowed = Math.Clamp(Math.Min(_remaining, MaxTextDisplayLength), 0, text.Length);
            _remaining -= allowed;
            return text[..allowed];
        }
    }
}
