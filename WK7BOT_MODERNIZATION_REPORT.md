# WK7Bot .NET 10 Modernization Scan Report

**Project:** `WK7Bot/` (net10.0, C# 14, `Nullable=enable`, `ImplicitUsings=enable`)  
**Baseline:** `dotnet build` succeeds with **0 warnings / 0 errors**; test suite **717/717 passed**.  
**Scope:** All 219 `.cs` files under `WK7Bot/` (excluding `obj/`).

## Executive Summary

The codebase is already in good shape: file-scoped namespaces, implicit usings, NRT enabled, async/await pervasive, `using var`/`await using`, switch expressions, records, `is`/property patterns, `IHttpClientFactory`, Polly resilience. The main gaps are **cancellation-token propagation holes**, an **exception filter that accidentally swallows cancellation**, **no `IAsyncDisposable`**, **no primary constructors**, **minimal collection expressions / target-typed `new`**, and **no source-generated JSON**. No sync-over-async (`.Result`/`.Wait`) was found.

## Findings by Category

### 1. Improper exception handling — High
- **Anti-pattern filter (26 sites):** `catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)` in 9 modules:
  - `Modules/DealsModule.cs`, `FoodModule.cs`, `LeipzigWasteModule.cs`, `RemindModule.cs`, `RssComponentModule.cs`, `RssModule.cs`, `SpontanTreffComponentModule.cs`, `SpontanTreffModule.cs`, `SystemModule.cs`
  - The `|| ex is TaskCanceledException` branch makes the filter `true` for `TaskCanceledException` (a subtype of `OperationCanceledException`), so cancellation exceptions are **caught and swallowed** instead of propagating. Fix: `when (ex is not OperationCanceledException)` (and rethrow cancellation).
- **Broad `catch (Exception)` (124 sites):** many are intentional resilience boundaries; review each for logging + rethrow. No empty catches found.
- No `throw ex;` (stack-trace loss) found. Good.

### 2. Missing CancellationToken propagation — Medium
- 301 async method occurrences; 113 public async method declarations take **no** `CancellationToken` parameter.
- `Task.Delay` without token in `Infrastructure/Data/DatabaseSchemaInitializer.cs` (2 sites, lines 115, 147) — cannot be cancelled during DB init retries.
- `Task.Delay(-1, stoppingToken)` in `DiscordBotWorker.cs` is correct.
- Recommend threading `CancellationToken` through service methods and all `Task.Delay`/`HttpClient`/EF calls.

### 3. Missing IAsyncDisposable — Medium
- 0 types implement `IAsyncDisposable` (and 0 implement `IDisposable`).
- Services holding `HttpClient` (via factory, OK), MQTT clients, `DbContext` (scoped, OK) and `ZipArchive`/streams could implement `IAsyncDisposable` for deterministic cleanup. Especially `MqttConnectionCoordinator`, `HomeAssistantService`, `BattleNetService`-adjacent caches.
- Recommend `await using` at call sites where resources are created inline (e.g., `Services/DwdWarningService.cs` `ZipArchive` already uses `using (archive)`; convert to `await using` where async dispose available).

### 4. Blocking calls in async methods — Low
- `File.Exists` (sync) in `Services/AddOnOptionsWriter.cs:42` and `Services/DatabaseMaintenanceService.cs:115` inside async paths. Minor; consider `File.Exists` is fast and no async equivalent exists in BCL — acceptable, but document.
- No `Thread.Sleep`, `.Wait()`, `.Result`, `GetAwaiter().GetResult()` sync-over-async found. Good.

### 5. Missing nullable reference types discipline — Low
- `<Nullable>enable</Nullable>` is set; no `#nullable disable`. Good.
- 16 `null!` suppressions, all in `TestCommandEndpoints/TestCommandRunner.cs` (test stubs) — acceptable.
- Many DTOs use `= string.Empty` / `= new()` defaults instead of `required`/nullable; JSON deserialization forces setters. Consider `required init` for mandatory fields (see §10).

### 6. Outdated patterns / newer .NET 10 features — Medium
- **Primary constructors:** 0 of 263 classes use them, despite 83 classes with `private readonly` fields and 75 files doing `?? throw new ArgumentNullException(...)`. Modernize service classes to primary constructors (C# 12).
- **Collection expressions:** only 1 usage; 22 `new List<string>()`, 6 `new List<SteamAchievement>()`, etc. and `new Dictionary<...>()` (11 sites) could become `[]` / target-typed collection expressions.
- **Target-typed `new`:** 49 uses; 231 `var x = new Foo()` could be `new()` where type is obvious.
- **Source-generated JSON:** 0 `JsonSerializerContext`. `JsonSerializer.Serialize/Deserialize` (15 sites) use reflection. Add `[JsonSerializable]` contexts for hot paths (Gemini DTOs, Steam/BattleNet DTOs) for perf/AOT.
- **Sealed:** few classes sealed; marking leaf services `sealed` enables devirtualization. Low priority.
- **BOM:** 23 files start with UTF-8 BOM (`ef bb bf`). Remove for consistency/tooling.
- **`ConfigureAwait(false)`:** 116 occurrences in an ASP.NET Core app (no synchronization context) — harmless but noise. Could remove to reduce clutter.

### 7. Missing global usings — Low
- No custom `GlobalUsings.cs`; implicit usings enabled. 75+ service files repeat `using Microsoft.Extensions.Logging;`, `using WK7Bot...`. Consider a `GlobalUsings.cs` for project namespaces to reduce per-file using noise.

### 8. Missing init-only / required properties — Low
- Only 23 `init;` usages (mostly `CapWarning`, `WeekendEvent`, `FeatureHealthTracker`). 391 `{ get; set; }` auto-properties, including DTOs/options. For immutable-ish models, prefer `init`; for mandatory config/DTO fields, use `required`.
- 13 `required` keyword occurrences are all in comments, not property modifiers.

### 9. Missing pattern matching / switch expressions — Low (mostly done)
- Pattern matching already used heavily (`is not`, `is and/or`, 145 sites).
- Only 7 traditional `switch` statements remain (could become switch expressions where they return values): `DwdResiliencePolicy.cs:199`, `WeekendEventParser.cs:84`, `RssModule.cs:258`, `RecipeHtmlParser.cs` (3 sites), `TestCommandEndpoints/TestCommandRunner.cs:69`.
- `with` expressions: 0 usage — records could benefit.

### 10. Outdated collection initialization — Low
- `new List<T>()` (49 sites across types) and `new Dictionary<...>()` (11 sites) can become `[]` / collection expressions.
- `Array.Empty<T>()` (26 sites) is already modern.

### 11. Missing `using` statements / disposal — Low
- `using var` (127) and `await using` present. `MemoryStream` in `RssModule.cs:194` uses `await using`. `DwdWarningService.cs:136` wraps `ZipArchive` in `using (archive)`. Good.
- No undisposed `FileStream`/`StreamReader`/`StreamWriter` found.

### 12. DateTime usage — Low
- `DateTime.Now` (15 sites) vs `DateTime.UtcNow` (38). In a bot, prefer `UtcNow` for storage/comparison; `DateTime.Now` in 9 files (modules/background services) may cause TZ-dependent behavior. Review.

## Prioritized Modernization Refactors

1. **Fix cancellation-swallowing catch filter** (High, easy) — change 26 sites in 9 modules from `when (ex is not OperationCanceledException || ex is TaskCanceledException)` to `when (ex is not OperationCanceledException)`, rethrowing cancellation.
2. **Thread CancellationToken** (Medium) — add `CancellationToken` params to the 113 async methods lacking them; fix the 2 `Task.Delay` in `DatabaseSchemaInitializer`.
3. **Primary constructors** (Medium) — convert ~75 service classes (`AccountLinkService` pattern) to primary constructors, removing `ArgumentNullException` boilerplate.
4. **Source-generated JSON** (Medium) — add `JsonSerializerContext` for Gemini/Steam/BattleNet DTOs.
5. **Collection expressions + target-typed `new`** (Low) — mechanical refactor across ~49 `new List<>` and 231 `new Foo()`.
6. **`IAsyncDisposable`** (Medium) — implement on resource-owning services.
7. **`required`/`init` on DTOs** (Low) — tighten model mutability.
8. **Remove BOM** (Low) — 23 files.
9. **`DateTime.UtcNow`** (Low) — replace 15 `DateTime.Now`.
10. **Drop redundant `ConfigureAwait(false)`** (Low) — optional cleanup.

## Verification
- Build: `dotnet build` → 0 warnings, 0 errors.
- Tests: 717/717 passed.
- No sync-over-async detected.

*Generated by automated scan; counts are approximate and should be re-verified before large refactors.*
</content>