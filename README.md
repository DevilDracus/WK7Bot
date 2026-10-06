# WK7Bot

A modular Discord automation bot and Home Assistant Add-On built on .NET 10. WK7Bot integrates Discord slash commands, MQTT event pipelines, Steam Rich Presence monitoring, RSS feed syndication, web-searched and AI-generated seasonal food and renal-safe recipes, spontaneous meetup coordination, a weekly Leipzig weekend culture digest with voting polls, local DWD rain and storm warnings for your postal code, and local utility schedule automation into a unified, extensible background framework.

[![Run Unit Tests](https://github.com/DevilDracus/WK7Bot/actions/workflows/tests.yml/badge.svg)](https://github.com/DevilDracus/WK7Bot/actions/workflows/tests.yml)
[![Add repository to Home Assistant][repository-badge]][repository-url]

## Overview

WK7Bot runs as a standalone containerized service or as a native Home Assistant Supervisor Add-On. It uses strongly-typed `IOptions<T>` configuration, hosted background services, and asynchronous event pipelines to handle real-time notifications, Home Assistant automation triggers, and community interactions.

## Key Features

### Discord Integration & Slash Commands
- **`Discord.Interactions` framework** built on `Discord.Net` with reflection-based command module auto-registration (`InteractionHandlingService`).
- **Guild & global commands** – a `Discord:TestGuildId` (or root `TestGuildId`) setting enables instant guild-scoped registration for development; otherwise commands register globally.
- **Interactive UI** – select menus and role management for RSS subscription dashboards, plus one-tap go/pass buttons for spontaneous meetups.
- **Duplicate protection** – `/rss add` rejects a name that already exists before creating channels/roles.

### RSS Feed Syndication
- `RssPollingBackgroundService` polls configured feeds every 5 minutes and publishes rich embeds to dedicated per-feed channels.
- **First-poll baseline seeding** – when a feed has never been polled, the newest item is recorded silently; the historical backlog is *not* posted to Discord.
- Duplicate suppression via `LastItemGuid` / `LastPublishedDate` tracking (pure logic in `FeedDeltaCalculator`).
- `/rss` commands to add/remove feeds (auto-creating a role + private read-only channel) and post a subscription dashboard.
- Interactive select menu (`RssComponentModule`) for users to subscribe/unsubscribe.
- Descriptions are HTML-stripped, entity-decoded, and truncated to 500 characters (`FeedTextFormatter`).

### Leipzig Waste Collection Reminders
- Downloads and parses `.ics` calendar streams from *Stadtreinigung Leipzig* (UTF-8 BOM and leading whitespace tolerated).
- Maps waste categories (*Restabfall*, *Papier*, *Wertstoffe*, *Biogut*) to German display labels and color-coded visuals (`WasteSummaryMapper`).
- Daily confirmation + Monday weekly overview reminders (`LeipzigWasteBackgroundService`) in a `🗑️leipzig-waste` channel.
- **Restart-safe deduplication** – every daily/weekly dispatch per guild is recorded in the `WasteDispatchLogs` table (`WasteDispatchRepository`), so restarting the bot never re-sends today's messages; guilds whose send failed are retried on the next tick, while successful guilds stay suppressed.
- **ICS download caching** – a successfully validated calendar is cached for 1 hour (`IMemoryCache`), so weekly overviews and multi-day `/check-waste` ranges reuse one HTTP download instead of fetching the feed per date; failures are never cached.
- On-demand lookups via the `/check-waste` slash command.

### Spontan-Treff (Spontaneous Meetups)
- `/spontan-treff plan:<text> [ort:<text>] [minuten:<1-1440>]` posts a short-lived meetup with an **`@everyone` ping** into the dedicated `#🎉spontan-treff` channel (created automatically when missing, same pattern as `#🍎food` and `#🗑️leipzig-waste`).
- The embed carries two one-tap buttons — **✅ Auf dem Weg** and **🚫 Absagen** — whose custom IDs are `spontan-treff:go:<id>` / `spontan-treff:pass:<id>` and are handled by `SpontanTreffComponentModule`.
- **Tap-again-to-undo** – tapping the same button removes the answer, tapping the other one switches sides; every state change re-renders the shared embed (`SpontanTreffMessageBuilder`) so both answer lists stay accurate.
- **Fixed open window** – 30 minutes by default, overridable with `minuten` (1–1440); the footer shows `Offen bis HH:mm Uhr` while open and `Beendet • lief bis …` afterwards.
- **`SpontanTreffExpiryService`** sweeps every minute, marks overdue meetups as closed and edits their message so the buttons disappear (late taps are rejected); each meetup is handled independently, so a deleted Discord message only skips its own edit.
- **Ping fallback** – when the bot may not mention `@everyone` (`403 Forbidden`), the meetup is posted as a plain message instead of failing.
- Meetups and answers persist in the `SpontanTreffs` / `SpontanTreffResponses` tables (created by `EnsureCreated` *and* by the raw startup DDL in `Program.cs`, so existing databases pick them up too).
- Gated by the `spontan_treff_enabled` feature flag (the expiry service is the only hosted component; the slash command itself is always registered).

### Leipzig Weekend & Culture Digest
- `WeekendDigestBackgroundService` posts every **Thursday at 14:15** the digest for the upcoming weekend into the dedicated `#📅wochenende` channel (created automatically when missing, same pattern as `#🍎food` and `#🗑️leipzig-waste`).
- **Two listing sources** – the leipzig.de weekend page (rich Saturday/Sunday coverage) and the month overview page of the target Friday (which carries the Friday events the weekend page omits); both share the same `event-card` markup and are parsed by `WeekendEventParser`, merged, deduplicated by URL and reduced to events overlapping Friday–Sunday. When *no* page can be fetched the attempt fails and is retried instead of silently posting an empty digest.
- **Market-first curation** – `WeekendDigestCurator` ranks candidates by the requested market keywords (*Wochenmarkt*, *Flohmarkt*, *Nachtmarkt*, *Abendmarkt*, *Straßenmarkt*, *Street Food*, *Trödelmarkt*, … plus topic bonuses for *Messen*/*Markt*), prefers one pick per day ordered Friday → Sunday, never repeats a venue and falls back to fewer than three picks when candidates run out.
- **Native Discord poll** – `WeekendDigestMessageBuilder` renders one embed field per pick (day label, time, location, topic, source link) plus a three-answer poll ("Wohin gehen wir am Wochenende?") whose duration covers the time until Sunday 23:59 (clamped to Discord's 1–168 h window); poll answers honour Discord's 55-char limit, embed fields their 256/1024-char limits, and a single-pick week posts without a poll.
- **Restart-safe dedup** – each Thursday's dispatch per guild is recorded in the existing `WasteDispatchLogs` table (dispatch kind `weekend_digest`), so restarting the bot never re-posts the digest; failed fetches/postings retry at 15-minute intervals (at most 4 attempts per Thursday), a zero-event weekend is marked handled without posting, partial failures retry only the guilds that did not receive the message, and a build-once cache keeps retries and additional guilds from re-fetching leipzig.de.
- Gated by the `weekend_digest_enabled` feature flag.

### Local DWD Rain & Storm Warnings
- `DwdWarningBackgroundService` checks the DWD open-data CAP feed (`opendata.dwd.de/weather/alerts/cap/COMMUNEUNION_DWD_STAT/`) once at startup and then every 2 minutes: it picks the newest German snapshot (`Z_CAP_C_EDZW_<timestamp>_…_COMMUNEUNION_de.zip`), opens it in memory (a 22-byte archive means *no active warnings*), parses every CAP alert (`CapWarningParser`) and posts a German embed into the dedicated `#⛈️weather-warnings` channel (created automatically when missing, same pattern as `#📅wochenende`) — so you know when to close the balcony windows before the rain starts. No ping, just the embed.
- **Trigger window** – a warning is posted when its onset lies at most `lead_minutes` ahead (default 30, with a 5-minute grace so feed latency does not drop a warning whose onset just slipped into the past), its expiry has not passed, its event matches the configured tokens, and the configured location is covered.
- **Location matching** – point-in-polygon against the CAP polygons (latitude-first `lat,lon` pairs, `EXCLUDE_POLYGON` geocodes vetoed); the configured `area_names` (e.g. `Stadt Leipzig`) only apply when a warning carries no polygon at all.
- **Event matching** – German DWD event names (`STARKREGEN`, `HAGEL`, `GEWITTER`, `STURM`, `BÖEN`) and English CAP group codes (`HEAVY RAIN`, `HAIL`, `THUNDERSTORM`) match case-insensitively after normalising umlauts and separators (`HEAVY_RAIN` = `HEAVY RAIN`), so the token list in `config.yaml` can mix both languages.
- **Embed** – title with event and area, official headline, onset countdown (`ab 14:35 Uhr (in 12 Min.)` / `seit 14:35 Uhr`), validity window, area with the count of additional places, translated severity with colour (Minor/Moderate/Severe/Extreme), official description and recommendation, and the footer `Deutscher Wetterdienst (DWD) • PLZ 04205`; every value honours Discord's 256/4096/1024-char limits.
- **Restart-safe dedup** – every posted warning is recorded per guild in the new `WarningDispatchLogs` table, so a restart never re-posts a warning; failed sends and missing channels retry on the next 2-minute poll, and records older than 7 days are pruned.
- Gated by the `dwd_warning_enabled` feature flag; stays idle (with a log hint) while `dwd_warning.postal_code` is empty. All postal-code-specific values (`postal_code`, `latitude`, `longitude`, `lead_minutes`, `area_names`, `events`) live in `config.yaml`.

### Steam Rich Presence Enrichment
- Maps Discord user IDs to Steam IDs (`DiscordSteamMappings`).
- Fetches player summaries (including the full-size Steam avatar, exposed as `SteamAvatarUrl`), recently played games, and enriched achievement data (icons, hidden flag + unlock timestamps) from the Steam Web API with a 24h schema cache.
- **Display-friendly extras** – `SteamUserData.PlaytimeDisplay` formats the 14-day playtime in German (`15,8 Std.` / `45 Min.` / `0 Std.`), every recent game carries `AchievementsUnlocked`/`AchievementsTotal` progress counters (derived from the cached schema), and the currently active game gets the same counters (`CurrentGameAchievementsUnlocked`/`CurrentGameAchievementsTotal`).
- **Resilient achievement fetching** – achievement-endpoint calls are limited to 2 concurrent requests and retried with linear backoff (up to 3 attempts) on `429`/`5xx`; other errors (e.g. `400` for games without stats) fail fast without retry.
- **Privacy fallback** – when `GetPlayerAchievements` returns `403` (target user's *Game details* setting is friends-only/private, which the Web API refuses), the bot falls back to `IPlayerService/GetTopAchievementsForGames`, which still returns the user's unlocked achievements (name, description, icon, global unlock percentage via `player_percent_unlocked`) — just without unlock timestamps, so those achievements sort after timestamped ones.
- **No cache poisoning** – only *successful* schema responses are cached for 24h; failures are never cached, so transient Steam errors self-heal on the next fetch.
- **Fetch cooldown** – `SteamDataCache` serves Steam data from a 60s per-user cache, so Discord presence-update storms hit Steam at most once per minute per user.
- **Diagnostics** – previously silent failure paths (no games to fetch, non-success responses, `playerstats.success=false` with Steam's error string) are logged, and per-game unlocked-achievement counts are logged at debug level. A `403` from `GetPlayerAchievements` is logged with an actionable hint (the target user's *Game details* privacy setting is likely not Public — friends-only is not enough for the Web API) plus the response body snippet, and a follow-up log reports how many achievements the fallback recovered.

### AI Food & Recipe Automation (Google Gemini)
- `GeminiFoodService` calls the Google Gemini `generateContent` API using JSON-schema structured output to produce German-language seasonal produce lists and dialysis / kidney-transplant-safe weekly recipes.
- **Model fallback & resilience** – a primary model is tried first and a lite fallback model second; transient `429`/`503` responses are retried with exponential backoff, and a missing `gemini_api_key` short-circuits before any HTTP call.
- **Web recipe search (default)** – `/recipe query:<text>` queries DuckDuckGo's keyless HTML endpoint (no API key, no config), then downloads the top candidate pages and parses their schema.org `application/ld+json` `Recipe` metadata: title, description, ISO 8601 prep/cook times (`PT1H30M` → `1 Std. 30 Min.`), yield (`["4", "4 Portionen"]` → `4`), ingredients, flattened `HowToStep`/`HowToSection` steps, and the hero image. Sponsored links without a redirect target and pages without structured recipe data are skipped automatically.
- **Provider fallback** – DuckDuckGo sometimes answers with an HTTP 202 bot-challenge page. That is detected explicitly, and the search falls back to the Chefkoch search result page (`chefkoch.de/rs/s0/…`), whose published recipe URLs (structured `ItemList` data plus listing links, de-duplicated by recipe id) go through the very same JSON-LD parsing. Only when *both* providers fail does the service throw `RecipeSearchUnavailableException`, which `/recipe` and `/recipe-seasonal` report as "temporarily unavailable … use `/recipe-generate`".
- **Randomised variety** – the same query does not keep returning the same dish: the starting point inside the top-ranked candidates is randomised (earlier results stay more likely), and the 12 most recently served source URLs (2-day memory, `IMemoryCache`) are tried last. Randomness goes through the injectable `IRandomSource` (`SystemRandomSource` in production), so tests can steer it deterministically.
- **Weighted seasonal picks** – `SeasonalTermPicker` samples two distinct terms from the whole seasonal pool with a weighted roulette (vegetables ×4, fruits ×3, herbs ×2, nuts ×1), so `/recipe-seasonal` and the weekly post explore herbs and nuts too instead of always starting with the first vegetables.
- **Source link & disclaimer** – the searched embed links its title back to the original page (`Embed.Url`), shows the source URL in the embed, and carries a `⚠️ Hinweis` field stating the recipe was *not* screened for dialysis / transplant safety. Gemini-generated embeds keep the diet tags and renal safety notes instead.
- `/recipe-generate`, `/recipe`, `/recipe-seasonal` and `/seasonal-produce` post rich embeds to the `#🍎food` channel; both recipe embeds are built by the shared `RecipeEmbedBuilder` (field limits, optional sections, timestamps). Searched recipes get a format-only Gemini pass (`FormatRecipeAsync`) that restructures title/times/ingredients without inventing renal diet tags, and falls back to the raw parsed recipe when Gemini is unavailable.
- `FoodPublisherService` publishes automatically: the monthly produce calendar on the 1st at 09:00 (Gemini) and a **seasonal web recipe** on Thursdays at 15:30 — the same weighted seasonal search + Gemini formatting as `/recipe-seasonal`, falling back to the previously used Gemini-generated recipe when the search yields nothing.
- Gated by the `food_service_enabled` feature flag.

### Home Assistant & MQTT Integration
- `HomeAssistantNotifierService` registers every writable Discord text channel and configured DM user as an MQTT `notify` entity using Home Assistant Auto-Discovery.
- Sending to `homeassistant/notify/wk7_<channelId>/set` (or `wk7_dm_<userId>/set`) pushes a message to the target Discord channel/DM.
- `DiscordPresenceMqttService` publishes live user status/activity as MQTT sensors (`wk7bot/presence/users/<userId>`), enriched with Steam data when enabled. The payload carries the full `SteamData` object plus flattened top-level attributes (`CurrentGameTitle`, `CurrentGameAppId`, `PlaytimeLastTwoWeeksMinutes`, `PlaytimeDisplay`, `PersonaState`, `SteamAvatarUrl`) so Lovelace cards can reference them directly — the dashboard view is kept in `HASS.yaml` itself (`wk7` view, regenerated from the local-only `HASS_DiscordView.yaml` and spliced in place, so no manual paste is needed): presence cards with the Discord state on the main button plus a Discord-header-style Steam identity row (avatar on the left, `<steamname> (Steam)` name, online status painted underneath by CSS), game/playtime shortcut rows (the Spielzeit row opens the detail popup — there is no separate `>` button anymore), and detail popups whose markup deliberately only uses what Home Assistant's markdown sanitiser keeps (`<img width height>`, base64 data-URI brand glyphs, `<font>` status chips, `ha-alert`, the Font Awesome `<ha-icon icon="fab:discord">` logo — never `style=` attributes, inline `<svg>`, or non-existent icon names such as `mdi:discord`). The activity box shows the currently played game's art via Steam's `header.jpg` (230x107) and falls back to the Discord-provided `GameThumbnailUrl` when Steam has no app id for it. Popup headers are emitted as a single line joined with explicit `<br>` because `marked` swallows raw line breaks inside HTML blocks, which is what collapsed the header into one squished line.
- `AlexaMentionNotificationService` forwards mentions of a target user to the getnotify.me Alexa notification API.
- Comes with Supervisor configuration for running as a native Add-On.

## Technology Stack

- **Framework:** .NET 10 (C# 14)
- **Discord:** `Discord.Net`, `Discord.Interactions`
- **Persistence:** Entity Framework Core with SQLite (stored at `/data/wk7bot.db` in the Add-On)
- **Messaging:** `MQTTnet`, HTTP client pipelines
- **Parsing:** `CodeHollow.FeedReader`, `Ical.Net`, schema.org JSON-LD recipe blocks (`System.Text.Json`)
- **Web search:** DuckDuckGo HTML endpoint (keyless) for `/recipe`, with the Chefkoch search as fallback provider
- **Weather warnings:** Deutscher Wetterdienst (DWD) open-data CAP alerts (zipped XML, point-in-polygon matching)
- **AI:** Google Gemini `generateContent` (structured JSON schema, primary + fallback model)
- **Testing:** xUnit, Moq, EF Core InMemory
- **Deployment:** Docker, Home Assistant Supervisor Add-On

## Project Structure

```text
WK7Bot
├── Core
│   ├── Entities
│   │   ├── RssDashboardSetting.cs
│   │   ├── RssFeed.cs
│   │   ├── SpontanTreff.cs                     # Short-lived meetup with go/pass button answers
│   │   ├── SpontanTreffResponse.cs             # Per-user "on my way" / "pass" answer (composite key)
│   │   ├── WarningDispatchLog.cs            # Per-guild DWD warning dispatch records
│   │   └── WasteDispatchLog.cs                # Persisted per-guild waste dispatch records
│   ├── Exceptions
│   │   └── RecipeSearchUnavailableException.cs # Both recipe search providers failed to respond
│   ├── Interfaces
│   │   ├── IRssRepository.cs
│   │   ├── ISpontanTreffRepository.cs
│   │   ├── IWarningDispatchRepository.cs
│   │   └── IWasteDispatchRepository.cs
│   └── Utilities
│       ├── CapWarningParser.cs              # CAP XML → warning model, polygon + event matching
│       ├── DietTagFormatter.cs              # Diet-tag identifiers → German embed labels
│       ├── DwdWarningMessageBuilder.cs      # German warning embed (countdown, severity, limits)
│       ├── FeedDeltaCalculator.cs           # Pure RSS baseline/new-item decisions
│       ├── FeedTextFormatter.cs             # HTML strip + truncation for embeds
│       ├── NameSanitizer.cs                 # Channel slugs + MQTT-safe names
│       ├── RecipeEmbedBuilder.cs            # Shared recipe embeds (search link/disclaimer vs. generated)
│       ├── SeasonalTermPicker.cs            # Weighted random seasonal-term selection
│       ├── SpontanTreffMessageBuilder.cs     # Shared meetup embed + button rendering, custom IDs
│       ├── WasteSummaryMapper.cs            # ICS summary → German display label
│       ├── WeekendDigestCurator.cs          # Market-first Fri–Sun pick ranking with day spread
│       ├── WeekendDigestMessageBuilder.cs   # Weekend digest embed + native poll rendering
│       └── WeekendEventParser.cs            # leipzig.de event-card HTML → WeekendEvent list
├── Extensions
│   └── ServiceCollectionExtensions.cs       # DI wiring for DB, Discord, HTTP clients, hosted services
├── Infrastructure
│   └── Data
│       ├── BotDbContext.cs
│       ├── RssRepository.cs
│       ├── SpontanTreffRepository.cs
│       ├── WarningDispatchRepository.cs
│       └── WasteDispatchRepository.cs
├── Models
│   ├── CapWarning.cs                        # DWD CAP warning model (event, times, areas, polygons)
│   ├── FoodModels.cs                         # Seasonal produce + renal recipe models (Gemini schema)
│   ├── SteamAchievement.cs
│   ├── SteamRecentGame.cs
│   ├── SteamUserData.cs
│   ├── UserPresenceEntity.cs
│   └── WeekendEvent.cs                      # Parsed leipzig.de listing (title, date, time, location, topic, URL)
├── Modules
│   ├── FoodModule.cs                         # /recipe, /recipe-generate, /recipe-seasonal and /seasonal-produce slash commands
│   ├── LeipzigWasteModule.cs
│   ├── RssCommandsModule.cs
│   ├── RssComponentModule.cs
│   ├── SpontanTreffComponentModule.cs      # Button handler for go/pass answers (toggle/undo)
│   ├── SpontanTreffModule.cs               # /spontan-treff slash command
│   └── SystemModule.cs
├── Options
│   ├── AlexaNotificationOptions.cs
│   ├── DiscordSteamMappingOptions.cs
│   ├── DwdWarningOptions.cs                 # Postal-code-specific DWD warning settings
│   ├── FeatureOptions.cs                    # All toggles default to enabled
│   └── Wk7BotOptions.cs
├── Services
│   ├── Interfaces
│   │   ├── IDwdWarningService.cs            # DWD CAP warning feed fetch contract
│   │   ├── IGeminiFoodService.cs
│   │   ├── IHomeAssistantService.cs
│   │   ├── ILeipzigWasteService.cs
│   │   ├── IRandomSource.cs                 # Injectable randomness for selection logic
│   │   ├── IRecipeSearchService.cs          # Web recipe lookup contract
│   │   ├── ISteamService.cs
│   │   └── IWeekendEventSource.cs           # leipzig.de weekend + month listing fetch contract
│   ├── AlexaMentionNotificationService.cs
│   ├── DiscordBotWorker.cs
│   ├── DiscordPresenceMqttService.cs
│   ├── DwdWarningBackgroundService.cs       # 2-minute DWD poll → #⛈️weather-warnings (lead window)
│   ├── DwdWarningService.cs                 # Newest-snapshot fetch from opendata.dwd.de + zip parsing
│   ├── FoodPublisherService.cs               # Scheduled produce calendar + seasonal web recipe posts to #🍎food
│   ├── GeminiFoodService.cs                  # Gemini REST client with model fallback
│   ├── HomeAssistantNotifierService.cs
│   ├── HomeAssistantService.cs
│   ├── InteractionHandlingService.cs
│   ├── LeipzigWasteBackgroundService.cs
│   ├── LeipzigWasteService.cs
│   ├── LeipzigWeekendEventSource.cs         # leipzig.de weekend + month fetch, URL dedupe, Fri–Sun filter
│   ├── RssParserService.cs
│   ├── RssPollingBackgroundService.cs
│   ├── SpontanTreffExpiryService.cs        # Closes overdue meetups and strips their buttons
│   ├── SteamDataCache.cs                     # 60s per-user Steam fetch cooldown
│   ├── SteamService.cs
│   ├── SystemRandomSource.cs                 # Random.Shared-backed IRandomSource
│   ├── WebRecipeSearchService.cs            # DuckDuckGo search + Chefkoch fallback + JSON-LD recipe parsing
│   └── WeekendDigestBackgroundService.cs    # Thursday 14:15 digest → #📅wochenende (poll, retry budget)
├── Program.cs                               # Host bootstrap, options binding, /health endpoint
├── appsettings.json
├── appsettings.Development.json
├── config.yaml                              # Home Assistant Add-On configuration
├── Dockerfile
└── WK7Bot.csproj

WK7Bot.Tests                                 # xUnit test project (included in WK7Bot.sln)
├── CapWarningParserTests.cs                 # CAP parse, polygon/area matching, event normalization
├── DietTagFormatterTests.cs
├── DwdWarningBackgroundServiceTests.cs      # Lead window, dedupe, retries, area fallback
├── DwdWarningMessageBuilderTests.cs         # Embed countdown, severity colors, truncation
├── DwdWarningServiceTests.cs                # Newest-snapshot selection, empty zip, malformed entries
├── FeedDeltaCalculatorTests.cs
├── FeedTextFormatterTests.cs
├── FoodModuleTests.cs                       # Slash-command behaviour incl. embed content
├── FormattingUtilityTests.cs
├── GeminiFoodServiceTests.cs                # Gemini envelope parsing, fallback, retries
├── LeipzigWasteBackgroundServiceTests.cs    # Restart dedupe, daily/weekly kinds, partial-failure retry
├── LeipzigWasteServiceTests.cs              # ICS parsing, mapping, feed download caching
├── LeipzigWeekendEventSourceTests.cs        # Two-page fetch, dedupe, Fri–Sun filter, both-pages-fail throw
├── NameSanitizerTests.cs
├── OptionsBindingTests.cs
├── RecipeEmbedBuilderTests.cs               # Shared recipe embed sections, disclaimer, truncation
├── RssRepositoryTests.cs
├── ServiceCollectionExtensionsTests.cs
├── SeasonalTermPickerTests.cs                # Weighted category picks, distinct terms, empty pools
├── SpontanTreffComponentModuleTests.cs       # Button toggle/undo/switch, rejection paths, custom-ID matching
├── SpontanTreffExpiryServiceTests.cs         # Expiry sweep, edit-failure isolation, idempotency
├── SpontanTreffMessageBuilderTests.cs        # Embed/button rendering, field limits, closed states
├── SpontanTreffModuleTests.cs                # /spontan-treff posts, ping fallback, validation paths
├── SpontanTreffRepositoryTests.cs            # CRUD + SQLite DDL replay of the startup sequence
├── SteamDataCacheTests.cs                    # Cooldown caching, TTL expiry, null caching
├── SteamServiceTests.cs                      # API mapping, retry/backoff, schema cache, log assertions
├── StubRandomSource.cs                       # Deterministic IRandomSource for tests
├── WarningDispatchRepositoryTests.cs        # Dispatch dedupe/pruning (EF InMemory) + SQLite DDL replay
├── WasteDispatchRepositoryTests.cs           # Dispatch state persistence (EF InMemory)
├── WasteSummaryMapperTests.cs
├── WebRecipeSearchServiceTests.cs           # Search-result extraction, JSON-LD parsing, skip paths, Chefkoch fallback & provider-unavailable
├── WeekendDigestBackgroundServiceTests.cs   # Thursday schedule, retries, build-once cache, dispatch dedupe, channel creation
├── WeekendDigestCuratorTests.cs             # Market-first ranking, day spread, venue dedupe, pick shortfalls
├── WeekendDigestMessageBuilderTests.cs      # Embed fields, poll answers/duration, Discord length limits
└── WeekendEventParserTests.cs               # Card markup, date/time forms, entities, malformed cards
```

## Slash Commands

| Command | Description | Permission |
| --- | --- | --- |
| `/rss add <name> <url>` | Registers a feed, creates a role + private read-only channel (rejects duplicate names) | `ManageChannels` |
| `/rss remove <name>` | Removes a feed and cleans up its channel + role | `ManageChannels` |
| `/rss dashboard` | Posts the interactive subscription select menu | `ManageRoles` |
| `/check-waste [days] [datum]` | Queries Leipzig waste collection dates (`DD.MM.YYYY` or `YYYY-MM-DD`) | everyone |
| `/recipe query:<text>` | Searches the web for a recipe, parses its structured data and posts it with a source link to `#🍎food` | everyone |
| `/recipe-seasonal [query]` | Searches for a recipe seeded with two randomly weighted seasonal ingredients and posts it with a source link to `#🍎food` | everyone |
| `/recipe-generate` | Generates a seasonal renal & transplant-safe recipe and posts it to `#🍎food` | everyone |
| `/seasonal-produce [month]` | Posts the seasonal fruit/vegetable/herb/nut calendar (month 1-12, default: current) to `#🍎food` | everyone |
| `/spontan-treff <plan> [ort] [minuten]` | Announces a meetup with an `@everyone` ping and go/pass buttons in `#🎉spontan-treff` (open 1-1440 min, default 30) | everyone |
| `/ping` | Gateway latency test | everyone |
| `/ha-status` | Tests Home Assistant Supervisor API connectivity | everyone |

All food commands are ephemeral while the lookup runs, post the result embed into `#🍎food`, and report a clear error when the channel or the Gemini API key is missing. `/recipe` and `/recipe-seasonal` need no API key of their own (a Gemini key only improves formatting): when no candidate page exposes parseable recipe data they say so and point at `/recipe-generate`, and when every search provider is blocked (bot challenge / HTTP errors) they report that the search is temporarily unavailable instead of pretending there were no results.

## Hosted Services

| Hosted Service | Purpose | Feature flag |
| --- | --- | --- |
| `DiscordBotWorker` | Logs in and connects the Discord gateway, forwards `Discord.Net` logs | always on |
| `InteractionHandlingService` | Auto-registers command modules and routes slash command interactions | always on |
| `RssPollingBackgroundService` | Polls RSS feeds and posts update embeds to Discord | `rss_polling_enabled` |
| `HomeAssistantNotifierService` | Bridges MQTT notify commands to Discord channels / DMs | `home_assistant_notifier_enabled` |
| `LeipzigWasteBackgroundService` | Sends daily and weekly waste collection reminders | `leipzig_waste_enabled` |
| `AlexaMentionNotificationService` | Forwards target-user mentions to the Alexa notification API | `alexa_notifications_enabled` |
| `DiscordPresenceMqttService` | Publishes Discord presence snapshots to MQTT for Home Assistant | `discord_presence_mqtt_enabled` |
| `FoodPublisherService` | Posts the monthly produce calendar (Gemini) and the weekly seasonal web recipe to `#🍎food` | `food_service_enabled` |
| `SpontanTreffExpiryService` | Closes overdue spontaneous meetups and removes their buttons every minute | `spontan_treff_enabled` |
| `WeekendDigestBackgroundService` | Posts the Thursday 14:15 weekend digest with a native voting poll to `#📅wochenende` | `weekend_digest_enabled` |
| `DwdWarningBackgroundService` | Polls the DWD CAP feed every 2 minutes and posts local rain/storm warnings to `#⛈️weather-warnings` | `dwd_warning_enabled` |

Feature flags are resolved from the `Wk7Bot:features` section when present (appsettings.json), otherwise from root-level `features` (Home Assistant `options.json`). Services re-check the bound options at runtime, so disabling a flag always takes effect.

## Configuration

Configuration is bound from `appsettings.json`, environment variables, or the Home Assistant Add-On `config.yaml`. The `Wk7Bot` section in `appsettings.json` (and root-level keys in `config.yaml`/`options.json`) is read as `Wk7BotOptions`. All option properties use `ConfigurationKeyName` snake_case aliases to match the Add-On schema.

### Home Assistant `config.yaml`

```yaml
discord_token: "YOUR_DISCORD_BOT_TOKEN"
mqtt_host: "core-mosquitto"
mqtt_port: 1883
mqtt_username: ""
mqtt_password: ""
steam_api_key: ""
gemini_api_key: ""                             # Google AI Studio key: seasonal produce, generated recipes, recipe formatting
discord_steam_mappings:
  - discord_user_id: "123456789012345678"
    steam_id: "76561198000000000"
discord_dm_user_ids:
  - "123456789012345678"
alexa_notification:
  target_user_id: "YOUR_DISCORD_USER_ID"
  api_token: "YOUR_GETNOTIFY_TOKEN"
  api_secret: "YOUR_GETNOTIFY_SECRET"
  endpoint_url: "https://api.getnotify.me/v1/notify"
features:
  rss_polling_enabled: true
  home_assistant_notifier_enabled: true
  leipzig_waste_enabled: true
  alexa_notifications_enabled: true
  discord_presence_mqtt_enabled: true
  steam_presence_enabled: true
  food_service_enabled: true
  spontan_treff_enabled: true
  weekend_digest_enabled: true
  dwd_warning_enabled: true
dwd_warning:
  postal_code: "01234"                          # 5-digit German postal code to watch
  latitude: 00.00                               # Used for point-in-polygon matching (polygon warnings)
  longitude: 00.00
  lead_minutes: 00                              # Post a warning when onset is at most this far ahead
  area_names:                                   # Fallback labels when a warning carries no polygon
    - "City Name"
    - "City"
  events:                                       # Matched case-insensitively (umlauts/separators normalised)
    - "STARKREGEN"
    - "HAGEL"
    - "GEWITTER"
    - "STURM"
    - "BÖEN"
    - "BOEEN"
    - "HEAVY_RAIN"
    - "HAIL"
    - "THUNDER"
    - "STORM"
```

### `appsettings.json` (same keys under `Wk7Bot`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=/data/wk7bot.db"
  },
  "LeipzigWaste": {
    "IcsFeedUrl": "https://stadtreinigung-leipzig.de/..."
  },
  "Wk7Bot": {
    "discord_token": "YOUR_DISCORD_BOT_TOKEN",
    "mqtt_host": "core-mosquitto",
    "mqtt_port": 1883,
    "steam_api_key": "YOUR_STEAM_WEB_API_KEY",
    "gemini_api_key": "YOUR_GOOGLE_AI_STUDIO_KEY",
    "discord_steam_mappings": [
      {
        "discord_user_id": "123456789012345678",
        "steam_id": "76561198000000000"
      }
    ],
    "discord_dm_user_ids": [ "123456789012345678" ],
    "alexa_notification": {
      "target_user_id": "YOUR_DISCORD_USER_ID",
      "api_token": "YOUR_GETNOTIFY_TOKEN",
      "api_secret": "YOUR_GETNOTIFY_SECRET",
      "endpoint_url": "https://api.getnotify.me/v1/notify"
    },
    "features": {
      "rss_polling_enabled": true,
      "home_assistant_notifier_enabled": true,
      "leipzig_waste_enabled": true,
      "alexa_notifications_enabled": true,
      "discord_presence_mqtt_enabled": true,
      "steam_presence_enabled": true,
      "food_service_enabled": true,
      "spontan_treff_enabled": true,
      "weekend_digest_enabled": true,
      "dwd_warning_enabled": true
    },
    "dwd_warning": {
      "postal_code": "04205",
      "latitude": 51.34,
      "longitude": 12.36,
      "lead_minutes": 30,
      "area_names": [ "Stadt Leipzig", "Leipzig" ],
      "events": [ "STARKREGEN", "HAGEL", "GEWITTER", "STURM", "BÖEN", "BOEEN", "HEAVY_RAIN", "HAIL", "THUNDER", "STORM" ]
    }
  }
}
```

If no `Wk7Bot` section exists, `Program.cs` binds `Wk7BotOptions` from the configuration **root** instead — this is what the shipped `appsettings.json` does (it only carries logging, the connection string, and the Leipzig ICS URL). In that case all bot options come from environment variables or the Home Assistant `/data/options.json`.

Secrets may also be supplied via environment variables (`DISCORD_BOT_TOKEN`, `SUPERVISOR_TOKEN`, etc.) or .NET user secrets.

### Important environment variables

| Variable | Used by | Purpose |
| --- | --- | --- |
| `DISCORD_BOT_TOKEN` | `DiscordBotWorker` | Fallback token when `discord_token` is empty |
| `SUPERVISOR_TOKEN` | `HomeAssistantService` | Bearer token for Supervisor-proxied HA API |
| `ASPNETCORE_ENVIRONMENT` | Host | Set to `Development` for local runs |

## Getting Started

### Local Development

1. Clone and enter the repository:
   ```bash
   git clone https://github.com/DevilDracus/WK7Bot.git
   cd WK7Bot
   ```
2. Put your token in `appsettings.Development.json` / user secrets (see config above — never commit real tokens).
3. Build & run:
   ```bash
   dotnet restore
   dotnet build
   dotnet run --project WK7Bot
   ```
4. Health check: `GET http://localhost:5220/health`

HTTPS development profile is also available (`https://localhost:7066`).

> **Note:** The default SQLite path is `/data/wk7bot.db`. On Windows local development, override `ConnectionStrings:DefaultConnection` to a writable path (e.g. `Data Source=wk7bot.db`) unless you run with privileges to create `C:\data`.

### Running Tests

```bash
dotnet test WK7Bot.sln
# or
dotnet test WK7Bot.Tests/WK7Bot.Tests.csproj
```

The suite covers Steam API mapping and resilience (bounded retry on `429`/`5xx`, no-retry on `400`, failed-schema non-caching, failure-path log assertions — against a stubbed HTTP handler), the `SteamDataCache` per-user cooldown (TTL expiry, null caching), RSS repository CRUD (EF InMemory), ICS parsing (including BOM) and ICS feed download caching (single download across lookups, failures and invalid payloads not cached), restart-safe waste dispatch (persisted per-guild dedup, independent daily/weekly kinds, partial-failure retry, missing-channel skip — via a testable subclass and EF InMemory), feature-flag registration (including `food_service_enabled`, `spontan_treff_enabled`, `weekend_digest_enabled` and `dwd_warning_enabled`), options binding, the Gemini food service (structured-output parsing, model fallback, transient-error retries, cancellation — against a stubbed HTTP handler), the `FoodModule` slash commands (channel resolution, embed content, search vs. generate paths, error paths, via Moq), the web recipe search service (DuckDuckGo result extraction incl. ad skipping, JSON-LD `Recipe` parsing, candidate-skip and failure paths, bot-challenge detection with the Chefkoch fallback provider, the provider-unavailable exception, randomised candidate starting points and the recently-served preference — against a stubbed HTTP handler), the weighted `SeasonalTermPicker` (category weights, distinct terms, empty pools), the shared recipe embed builder (source link, disclaimer, empty-section skipping, 1024-char truncation), the Spontan-Treff feature (embed/button rendering and field limits, the slash command with validation, `@everyone`-forbidden fallback and error paths, the button handler with toggle/undo/switch plus expired/closed/malformed rejection and real `InteractionService` custom-ID matching, the expiry sweep incl. edit-failure isolation and idempotency, and repository CRUD together with the raw SQLite startup DDL replayed for both fresh and pre-existing databases), the weekend digest (leipzig.de event-card parsing with all date/time forms and HTML entities, the dual weekend/month page merge with URL dedupe and the both-pages-failure throw, market-weighted curation with Friday→Sunday day spread and venue dedupe, embed/poll rendering incl. Discord's 300/55/256/1024-char limits and the Sunday-23:59 poll duration clamp, and the Thursday 14:15 scheduler with its 15-minute retry budget, zero-event short-circuit, build-once fetch cache, missing-channel skip and restart-safe dispatch — via a testable subclass and EF InMemory), the DWD warning feature (newest-snapshot selection from the feed directory listing, empty-22-byte snapshot handling, malformed-entry skipping, offset-time parsing, point-in-polygon matching with `EXCLUDE_POLYGON` veto and area-name fallback, umlaut/underscore event normalisation, the lead-window/grace/expiry evaluation via a testable `EvaluateAsync` seam, per-warning per-guild restart-safe dedup with pruning, send-failure and missing-channel retries, embed countdown/severity/area-count/truncation formatting, the raw SQLite `WarningDispatchLogs` DDL replay, and `dwd_warning_enabled` flag registration), and pure utilities (`FeedDeltaCalculator`, `WasteSummaryMapper`, `FeedTextFormatter`, `NameSanitizer`, `DietTagFormatter`). Randomised behaviour is driven by a scripted `StubRandomSource`, so these tests never flake.

### Docker

The Dockerfile lives in `WK7Bot/`, so the build context must be that directory:

```bash
docker build -f WK7Bot/Dockerfile -t wk7bot WK7Bot
docker run -d \
  --name wk7bot \
  -v "$(pwd)/WK7Bot/appsettings.json:/app/appsettings.json" \
  wk7bot
```

### Home Assistant Add-On

1. Add the custom repository to your supervisor:
   [![Add repository to Home Assistant][repository-badge]][repository-url]
2. Search for **WK7Bot** in the Add-On Store.
3. Configure your tokens in the Add-On **Configuration** tab and click **Start**.

Installs and updates pull the pre-built image `ghcr.io/devildracus/wk7bot:<version>` instead of compiling .NET on the device, so a Raspberry Pi 4 only downloads the container (architectures `aarch64` and `amd64`). To release a new version, bump `version:` in `WK7Bot/config.yaml` and push — the build workflow skips publishing while that version tag already exists.

## Health & Operations

| Endpoint | Description |
| --- | --- |
| `GET /health` | Returns `200 OK` with `WK7 Bot is healthy.` when the process is up |

Logs from `Discord.Net` are forwarded into the ASP.NET logging pipeline and appear in the add-on log.

## Continuous Integration

`.github/workflows/tests.yml` restores, builds, and runs the xUnit suite with the .NET 10 SDK on every push and pull request to `main`, `master`, and `develop`; the `trx` test results are uploaded as a build artifact. The status badge at the top of this README reflects the latest run, so a red badge means the suite is failing on GitHub Actions (locally: `dotnet test WK7Bot.sln`).

`.github/workflows/builder.yaml` builds the add-on container for `amd64` and `aarch64` with the Home Assistant [builder actions](https://github.com/home-assistant/builder) on every push that touches `WK7Bot/**`, pushes the per-architecture images plus a multi-arch manifest to `ghcr.io/devildracus/wk7bot` tagged with the `version` from `WK7Bot/config.yaml` (plus `latest`), and skips publishing when that version tag already exists. The GHCR package must be set to **public** once (GitHub → Packages → `wk7bot` → Change visibility) so the Supervisor can pull it anonymously.

## License & Copyright

Copyright © 2026 **DevilDracus**

Distributed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**. Any derivative work or hosted service modifications must maintain source availability under this license.

### Attribution

When building upon or referencing this project, please credit the original repository:
[WK7Bot on GitHub](https://github.com/DevilDracus/WK7Bot)

[repository-badge]: https://my.home-assistant.io/badges/supervisor_add_addon_repository.svg
[repository-url]: https://my.home-assistant.io/redirect/supervisor_add_addon_repository/?repository_url=https%3A%2F%2Fgithub.com%2Fdevildracus%2FWK7Bot
