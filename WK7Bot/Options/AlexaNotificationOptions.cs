namespace WK7Bot.Options;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Configuration options for third-party Alexa notification integrations.
/// </summary>
public class AlexaNotificationOptions
{
    /// <summary>
    /// Gets or sets the target user identifier.
    /// </summary>
    [ConfigurationKeyName("target_user_id")]
    public string? TargetUserId { get; init; }

    /// <summary>
    /// Gets or sets the API token used for authenticating notification requests.
    /// </summary>
    [ConfigurationKeyName("api_token")]
    public string? ApiToken { get; init; }

    /// <summary>
    /// Gets or sets the shared secret key for notification signature validation.
    /// </summary>
    [ConfigurationKeyName("api_secret")]
    public string? ApiSecret { get; init; }

    /// <summary>
    /// Gets or sets the target API endpoint URL.
    /// </summary>
    [ConfigurationKeyName("endpoint_url")]
    public string? EndpointUrl { get; init; } = "https://api.getnotify.me/v1/notify";
}