# WK7Bot
A feature-rich Discord bot built with .NET that combines RSS feed monitoring, Home Assistant notifications, and local utility integrations for Discord communities.

## Features

### RSS Feed Monitoring
WK7Bot can subscribe to RSS feeds and automatically publish updates to Discord channels.

Features include:

- RSS feed management commands
- Scheduled feed polling
- Automatic message posting
- Dashboard configuration support
- Persistent feed storage

### Home Assistant Integration
Connect your Home Assistant instance to Discord and receive automated notifications and status updates.

Capabilities include:

- Home Assistant event notifications
- Service integration
- Real-time automation alerts

### Leipzig Waste Collection Notifications
Provides automated reminders for waste collection schedules in Leipzig.

Features:

- Scheduled background processing
- Automatic reminder messages
- Localized waste collection information

### Discord Integrations

- Slash commands
- Interactive components
- Background services
- Automated notifications
- Administrative commands

## Architecture
The project follows a layered architecture:

```text
WK7Bot
├── Core
│ ├── Entities
│ └── Interfaces
├── Infrastructure
│ ├── Data
│ └── Extensions
├── Modules
├── Services
└── Extensions
```

### Core
Contains business entities and contracts.

### Infrastructure
Database access, repositories, and persistence implementations.

### Modules
Discord command and interaction handlers.

### Services
Background workers and integration services.

## Technology Stack

- .NET 10
- Discord.Net
- Entity Framework Core
- SQLite
- SQL Server
- Docker
- RSS Feed Reader

## Configuration
The bot uses:

```text
appsettings.json
appsettings.Development.json
config.yaml
```
Configure:

- Discord Bot Token
- Database connection
- RSS settings
- Home Assistant connection information
- Module-specific settings

## Running Locally

### Prerequisites

- .NET 10 SDK
- Discord Bot Token

### Build

```bash
dotnet restore
dotnet build
```

### Run

```bash
dotnet run --project WK7Bot
```

## Docker
Build image:

```bash
docker build -t wk7bot .
```
Run container:

```bash
docker run -d --name wk7bot wk7bot
```

## Database
Entity Framework Core is used for persistence.

Repositories:

- RSS Feeds
- Dashboard Settings

Supported providers:

- SQLite
- SQL Server

## Background Workers
WK7Bot contains several hosted services:

| Service | Purpose |
|----------|----------|
| DiscordBotWorker | Discord connection management |
| RssPollingBackgroundService | RSS feed polling |
| LeipzigWasteBackgroundService | Waste collection reminders |
| HomeAssistantNotifierService | Home Assistant notifications |
| AlexaMentionNotificationService | Alexa-related notifications |

## Project Goals
WK7Bot is designed as a modular Discord automation platform where new integrations and notification providers can be added with minimal effort.

## Development

```bash
git clone https://github.com/DevilDracus/WK7Bot.git
cd WK7Bot
```
Open:

```text
WK7Bot.sln
```
in Rider or Visual Studio.

## Author
Created and maintained by DevilDracus.

## Copyright
Copyright © 2026 DevilDracus

## License
WK7Bot is licensed under the GNU Affero General Public License v3.0 (AGPL-3.0).

Any modified or derivative versions distributed or provided as a service must also make their source code available under the same license.

## Attribution
If you build upon WK7Bot, please include a reference to the original project:

[WK7Bot Repository](https://github.com/DevilDracus/WK7Bot)