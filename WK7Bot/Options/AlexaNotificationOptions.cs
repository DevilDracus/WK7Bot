namespace WK7Bot.Options;

/// <summary>
/// Configuration options for third-party Alexa notification integrations.
/// </summary>
public class AlexaNotificationOptions
{
    /// <summary>
    /// Gets or sets the target user identifier.
    /// </summary>
    public string? TargetUserId { get; set; }

    /// <summary>
    /// Gets or sets the API token used for authenticating notification requests.
    /// </summary>
    public string? ApiToken { get; set; }

    /// <summary>
    /// Gets or sets the shared secret key for notification signature validation.
    /// </summary>
    public string? ApiSecret { get; set; }

    /// <summary>
    /// Gets or sets the target API endpoint URL.
    /// </summary>
    public string? EndpointUrl { get; set; }
}