namespace WK7Bot.Core.Utilities;

using System;
using System.Threading.Tasks;
using MQTTnet;

/// <summary>
/// Shared MQTT publish helpers: every payload the bot publishes is retained so Home Assistant (and a
/// restarted broker) sees the last known state without waiting for the next update.
/// </summary>
public static class MqttClientExtensions
{
    /// <summary>
    /// Publishes a retained message to the given topic.
    /// </summary>
    /// <param name="client">The MQTT client.</param>
    /// <param name="topic">The target topic.</param>
    /// <param name="payload">The payload text.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    public static Task PublishRetainedAsync(this IMqttClient client, string topic, string payload)
        => client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(true)
            .Build());

    /// <summary>
    /// Publishes a retained message with a binary payload (an empty payload clears a retained topic).
    /// </summary>
    /// <param name="client">The MQTT client.</param>
    /// <param name="topic">The target topic.</param>
    /// <param name="payload">The payload bytes.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    public static Task PublishRetainedAsync(this IMqttClient client, string topic, byte[] payload)
        => client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(true)
            .Build());
}
