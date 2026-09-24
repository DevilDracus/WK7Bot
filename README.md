# WK7Bot

A modular, enterprise-grade Discord automation bot and Home Assistant Add-On built on .NET 10. WK7Bot integrates Discord slash commands, MQTT event pipelines, Steam Rich Presence monitoring, RSS feed syndication, and local utility schedule automation into a unified, extensible background framework.

[![Add repository to Home Assistant][repository-badge]][repository-url]

## Overview

WK7Bot operates as a standalone containerized service or a native Home Assistant Add-On. It leverages strongly-typed Options patterns (`IOptions<T>`), background hosted services, and asynchronous event-driven pipelines to handle real-time notifications, Home Assistant automation triggers, and community interactions.

## Key Features

### 🎮 Steam Rich Presence Enrichment & Mappings
* **Activity Sync:** Tracks active Steam games and enriched game details across configured accounts using the Steam Web API.
* **Discord Profile Sync:** Maps Steam IDs directly to Discord user profiles for automated status updates and community presence enrichment.

### 📡 Home Assistant & MQTT Integration
* **MQTT Event Pipeline:** Connects directly to Mosquitto MQTT brokers to process real-time Home Assistant events and trigger alerts.
* **Alexa Mention Routing:** Intercepts specific Alexa events and routes direct alerts to targeted Discord users.
* **Home Assistant Add-On Ready:** Includes an integrated supervisor add-on distribution structure with `config.yaml` integration.

### 📰 RSS Feed Syndication
* **Polling Service:** `RssPollingBackgroundService` periodically checks configured feeds for updates.
* **Automated Publishing:** Automatically formats, enriches, and posts new feed items directly to targeted Discord text channels.
* **Dashboard Support:** Provides underlying persistence for feed subscriptions and state tracking.

### 🗑️ Leipzig Waste Collection Reminders
* **ICS Feed Parser:** Downloads, parses, and evaluates dynamic `.ics` calendar streams from *Stadtreinigung Leipzig*.
* **Localized Notifications:** Automatically maps waste categories (*Restabfall*, *Papier*, *Wertstoffe*, *Biogut*) to German display labels and visual indicators for daily reminder delivery.

### 🤖 Discord Interaction & Slash Command Handling
* **Discord.Interactions Framework:** Built on `Discord.Addons.Hosting` and `Discord.Net` using reflection-based command module auto-registration.
* **Guild & Global Commands:** Supports instant guild-scoped registration for development alongside global slash command deployments.
* **Interactive UI:** Supports Discord buttons, select menus, and modal interactions with dependency-injected execution contexts.

---

## Technology Stack

* **Framework:** .NET 10 (C# 14)
* **Discord Framework:** `Discord.Net`, `Discord.Addons.Hosting`, `Discord.Interactions`
* **Persistence & ORM:** Entity Framework Core (SQLite / Microsoft SQL Server)
* **Messaging & Protocols:** MQTT (`MQTTnet`), `Ical.Net`, HTTP Client Pipelines
* **Containerization & Deployment:** Docker, Home Assistant Supervisor Add-On Architecture
* **Logging & Telemetry:** `Microsoft.Extensions.Logging` with structured logging sinks

---

## Project Structure

```text
WK7Bot
├── Core
│   ├── Entities
│   └── Interfaces
├── Infrastructure
│   ├── Data
│   └── Extensions
├── Modules
│   └── InteractionModules
├── Options
│   ├── AlexaNotificationOptions.cs
│   ├── FeatureOptions.cs
│   ├── LeipzigWasteOptions.cs
│   └── Wk7BotOptions.cs
├── Services
│   ├── Interfaces
│   ├── AlexaMentionNotificationService.cs
│   ├── HomeAssistantNotifierService.cs
│   ├── InteractionHandlingService.cs
│   ├── LeipzigWasteBackgroundService.cs
│   ├── LeipzigWasteService.cs
│   ├── RssPollingBackgroundService.cs
│   └── SteamPresenceBackgroundService.cs
├── appsettings.json
├── appsettings.Development.json
├── config.yaml
└── Dockerfile
```

---

## Hosted Services Summary

WK7Bot runs multiple background tasks using `IHostedService` and `BackgroundService`:

| Hosted Service | Purpose |
| --- | --- |
| `InteractionHandlingService` | Hooks socket events, auto-registers command modules, and routes slash command interactions |
| `RssPollingBackgroundService` | Periodically fetches RSS feeds and posts update summaries to Discord |
| `LeipzigWasteBackgroundService` | Evaluates waste schedules daily and triggers localized reminders |
| `HomeAssistantNotifierService` | Consumes MQTT events from Home Assistant and posts automated notifications |
| `AlexaMentionNotificationService` | Listens for targeted Alexa trigger events and routes direct user alerts |
| `SteamPresenceBackgroundService` | Polls Steam Web API endpoints to enrich Discord user presence |

---

## Configuration

WK7Bot uses ASP.NET Core strongly-typed configuration options bound from `appsettings.json`, environment variables, or Home Assistant `config.yaml`.

### `appsettings.json` Example

```json
{
  "Wk7Bot": {
    "DiscordToken": "YOUR_DISCORD_BOT_TOKEN",
    "MqttHost": "core-mosquitto",
    "MqttPort": 1883,
    "MqttUsername": null,
    "MqttPassword": null,
    "SteamApiKey": "YOUR_STEAM_WEB_API_KEY",
    "DiscordDmUserIds": [
      "123456789012345678"
    ],
    "DiscordSteamMappings": [
      {
        "DiscordUserId": 123456789012345678,
        "SteamId": "76561198000000000"
      }
    ],
    "Features": {
      "RssPollingEnabled": true,
      "LeipzigWasteEnabled": true,
      "HomeAssistantEnabled": true,
      "SteamPresenceEnabled": true
    },
    "AlexaNotification": {
      "Enabled": true
    }
  },
  "LeipzigWaste": {
    "IcsFeedUrl": "[https://www.stadtreinigung-leipzig.de/ics-feed-url](https://www.stadtreinigung-leipzig.de/ics-feed-url)"
  },
  "TestGuildId": "123456789012345678"
}

```

---

## Getting Started

### Local Development Setup

1. **Clone the Repository:**
```bash
git clone [https://github.com/DevilDracus/WK7Bot.git](https://github.com/DevilDracus/WK7Bot.git)
cd WK7Bot

```


2. **Configure Settings:**
   Copy `appsettings.json` to `appsettings.Development.json` and insert your Discord Bot Token and connection credentials.
3. **Build & Run:**
```bash
dotnet restore
dotnet build
dotnet run --project WK7Bot

```



### Docker Setup

Build and execute the container using Docker:

```bash
docker build -t wk7bot .
docker run -d \
  --name wk7bot \
  -v $(pwd)/appsettings.json:/app/appsettings.json \
  wk7bot

```

### Home Assistant Installation

1. Add the custom repository to your Home Assistant supervisor:
   [](https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%253A%252F%252Fgithub.com%252Fdevildracus%252FWK7Bot&utm_source=gemini)
2. Search for **WK7Bot** in the Add-On Store.
3. Configure your tokens in the add-on **Configuration** tab and click **Start**.

---

## License & Copyright

Copyright © 2026 **DevilDracus**

Distributed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**. Any derivative work or hosted service modifications must maintain source availability under this same license.

### Attribution

When building upon or referencing this project, please credit the original repository:
[WK7Bot on GitHub](https://github.com/DevilDracus/WK7Bot?utm_source=gemini)


[repository-badge]: https://my.home-assistant.io/badges/supervisor_add_addon_repository.svg
[repository-url]: https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%3A%2F%2Fgithub.com%2Fdevildracus%2FWK7Bot
