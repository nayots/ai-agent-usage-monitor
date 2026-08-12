# Settings Window and System Tray Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every application-owned setting editable from a themed settings window, and let the widget live in the system tray instead of ending its process when closed.

**Architecture:** `AppSettings` stops being an immutable value captured by five startup consumers and gains one owner, `SettingsService`, which persists and raises `Changed`. Consumers subscribe and re-apply. The tray is hand-rolled `Shell_NotifyIcon` interop hosted on the widget's own HWND, so its context menu is an ordinary themed WPF `ContextMenu`.

**Tech Stack:** .NET 10, WPF, `System.Text.Json`, xUnit. No new NuGet packages.

**Spec:** `docs/specs/2026-08-12-settings-and-tray-design.md`. Read it before Task 1.

## Global Constraints

- `dotnet build` must be clean. `TreatWarningsAsErrors` is on solution-wide — a warning is a build failure.
- No new `PackageReference` in any project. `Microsoft.Win32.Registry` is already in the `net10.0` reference pack; no package is needed.
- `AiUsageMonitor.Domain` keeps zero package references and stays provider-neutral. Nothing in this plan touches it.
- Never name a domain property after a plan period. Nothing here adds domain properties.
- Missing data is `null` and surfaces as `Waiting`/`Unavailable` — never as `0`.
- Credentials are never logged, persisted, cached, displayed or copied. Nothing in this plan reads a credential; the settings file must never gain one.
- The app never modifies provider configuration. `HKCU\…\Run` is application-owned, not provider-owned.
- No administrator privileges. Registry writes are `HKCU` only.
- User- and machine-agnostic: no hardcoded user paths. Resolve per-user locations with `Environment.GetFolderPath` / `Environment.ProcessPath`.
- Copy is en-US: "Color bars by usage", not "Colour".
- Windows-only. PowerShell 5.1 is the shell: no `&&`, no ternary. Run `dotnet build` and `dotnet test` as two separate commands, never chained.
- Test files already have their own helpers, and their signatures differ between files. Each task that appends tests states the exact signatures to call. Use them as given; never assume a helper exists because the prose implies one, and never invent one to make a call site compile.
- `tests/AiUsageMonitor.App.Tests` does **not** get `System.IO` from implicit usings — verified, `Path` fails to resolve there with CS0103. Any file in that project touching `Path`, `File` or `Directory` needs an explicit `using System.IO;`. The Infrastructure test project does not have this problem.

---

### Task 1: `SettingsService` — one owner for application settings

**Files:**
- Create: `src/AiUsageMonitor.Infrastructure/Settings/SettingsService.cs`
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/SettingsServiceTests.cs`

**Interfaces:**
- Consumes: `AppSettings`, `AppSettingsStore` (existing, unchanged).
- Produces: `SettingsService(AppSettingsStore store, AppSettings initial, ILogger<SettingsService>? logger = null)` with `AppSettings Current { get; }`, `event EventHandler<AppSettings>? Changed`, `void Update(Func<AppSettings, AppSettings> change)`. Tasks 3, 4, 5, 6, 7 consume it.

- [ ] **Step 1: Write the failing tests**

Create `tests/AiUsageMonitor.Infrastructure.Tests/SettingsServiceTests.cs`:

```csharp
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.Infrastructure.Tests;

public class SettingsServiceTests
{
    private static SettingsService Service(TempDirectory dir, AppSettings? initial = null) =>
        new(new AppSettingsStore(dir.File("settings.json")), initial ?? AppSettings.Default);

    [Fact]
    public void AnUpdateChangesCurrentPersistsAndRaises()
    {
        using TempDirectory dir = new();
        SettingsService service = Service(dir);
        AppSettings? raised = null;
        service.Changed += (_, settings) => raised = settings;

        service.Update(s => s with { AlwaysOnTop = true });

        Assert.True(service.Current.AlwaysOnTop);
        Assert.NotNull(raised);
        Assert.True(raised!.AlwaysOnTop);
        Assert.True(new AppSettingsStore(dir.File("settings.json")).Load().Settings.AlwaysOnTop);
    }

    [Fact]
    public void EachUpdateComposesAgainstCurrentNotAgainstACapturedCopy()
    {
        // This is the whole reason Update takes a function. A caller holding an AppSettings from
        // startup - which WidgetWindow.SavePlacement does - would otherwise write back a state that
        // silently reverts every change made since.
        using TempDirectory dir = new();
        SettingsService service = Service(dir);

        service.Update(s => s with { AlwaysOnTop = true });
        service.Update(s => s with { WindowLeft = 42 });

        Assert.True(service.Current.AlwaysOnTop);
        Assert.Equal(42, service.Current.WindowLeft);
    }

    [Fact]
    public void AnUpdateThatChangesNothingIsNotAnnounced()
    {
        using TempDirectory dir = new();
        SettingsService service = Service(dir);
        int raises = 0;
        service.Changed += (_, _) => raises++;

        service.Update(s => s with { AlwaysOnTop = false });

        Assert.Equal(0, raises);
    }

    [Fact]
    public void AFailedSaveKeepsTheChangeInMemory()
    {
        // A directory where the settings file should be makes every write fail. Losing a setting
        // because a disk write failed is worse than losing it at restart: the user watched the
        // toggle move.
        using TempDirectory dir = new();
        Directory.CreateDirectory(dir.File("settings.json"));
        SettingsService service = new(new AppSettingsStore(dir.File("settings.json")), AppSettings.Default);

        service.Update(s => s with { AlwaysOnTop = true });

        Assert.True(service.Current.AlwaysOnTop);
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/AiUsageMonitor.Infrastructure.Tests --filter FullyQualifiedName~SettingsServiceTests`
Expected: build failure — `SettingsService` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/AiUsageMonitor.Infrastructure/Settings/SettingsService.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace AiUsageMonitor.Infrastructure.Settings;

/// <summary>
/// The single owner of application settings for the life of the process.
/// <para>
/// <see cref="Update"/> takes a function rather than a value on purpose: every caller edits the
/// state that exists now instead of one it captured earlier. A caller that held an
/// <see cref="AppSettings"/> from startup and wrote it back - which saving the window position
/// does on every shutdown - would otherwise revert every change made in between.
/// </para>
/// </summary>
public sealed class SettingsService
{
    private readonly AppSettingsStore _store;
    private readonly ILogger<SettingsService>? _logger;

    public SettingsService(AppSettingsStore store, AppSettings initial, ILogger<SettingsService>? logger = null)
    {
        _store = store;
        _logger = logger;
        Current = initial;
    }

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? Changed;

    /// <summary>
    /// Applies <paramref name="change"/> to <see cref="Current"/>, announces it, then persists.
    /// <para>
    /// In that order, and deliberately. A settings file that cannot be written is a bad reason to
    /// refuse a change the user has already watched take effect on screen; the change stands for
    /// this session and is lost at restart, which is the failure the user can actually understand.
    /// <see cref="AppSettings"/> is a record, so an update that produces an equal value is not a
    /// change and is not announced.
    /// </para>
    /// </summary>
    public void Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings updated = change(Current);

        if (updated == Current)
        {
            return;
        }

        Current = updated;
        Changed?.Invoke(this, updated);

        try
        {
            _store.Save(updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Settings could not be saved; the change applies to this session only.");
        }
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test tests/AiUsageMonitor.Infrastructure.Tests --filter FullyQualifiedName~SettingsServiceTests`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/AiUsageMonitor.Infrastructure/Settings/SettingsService.cs tests/AiUsageMonitor.Infrastructure.Tests/SettingsServiceTests.cs
git commit -m "feat: give application settings a single owner"
```

---

### Task 2: `StartupRegistration` — Start with Windows

**Files:**
- Create: `src/AiUsageMonitor.App/Interop/StartupRegistration.cs`
- Test: `tests/AiUsageMonitor.App.Tests/StartupRegistrationTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `StartupRegistration(string keyPath, string valueName, string? executablePath)`, `static StartupRegistration ForThisProcess()`, `bool IsSupported { get; }`, `bool IsEnabled { get; }`, `void Enable()`, `void Disable()`. Task 5 consumes it.

**Why this lives in `App/Interop` and not in Infrastructure:** `AiUsageMonitor.Infrastructure` targets plain `net10.0`. Calling `Microsoft.Win32.Registry` from a platform-neutral assembly raises CA1416, which is an error here, and fixing it properly would mean retargeting Infrastructure, its test project and the POC to `net10.0-windows`. `App/Interop` already holds `DwmWindowChrome`, the same kind of Win32 integration, in a project that is already `net10.0-windows`. The spec named `Infrastructure/Startup`; this is the correction.

- [ ] **Step 1: Write the failing tests**

Create `tests/AiUsageMonitor.App.Tests/StartupRegistrationTests.cs`:

```csharp
using AiUsageMonitor.App.Interop;
using Microsoft.Win32;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// Exercises the real registry against a scratch key under HKCU rather than a mock of the API
/// under test. The key is deleted in Dispose whatever the test did.
/// </summary>
public sealed class StartupRegistrationTests : IDisposable
{
    private const string ScratchKey = @"Software\AiUsageMonitor\tests\Run";
    private const string ValueName = "AiUsageMonitorTest";

    private static StartupRegistration Registration(string? path) => new(ScratchKey, ValueName, path);

    [Fact]
    public void ANewMachineStartsDisabled() => Assert.False(Registration(@"C:\app\widget.exe").IsEnabled);

    [Fact]
    public void EnablingThenReadingReportsEnabled()
    {
        StartupRegistration registration = Registration(@"C:\app\widget.exe");

        registration.Enable();

        Assert.True(registration.IsEnabled);
    }

    [Fact]
    public void EnablingTwiceIsNotAnError()
    {
        StartupRegistration registration = Registration(@"C:\app\widget.exe");

        registration.Enable();
        registration.Enable();

        Assert.True(registration.IsEnabled);
    }

    [Fact]
    public void DisablingRemovesTheValueAndIsSafeWhenAbsent()
    {
        StartupRegistration registration = Registration(@"C:\app\widget.exe");
        registration.Enable();

        registration.Disable();
        registration.Disable();

        Assert.False(registration.IsEnabled);
    }

    [Fact]
    public void AnEntryPointingAtADifferentExecutableReadsAsDisabled()
    {
        // A moved or reinstalled app must show the checkbox off, so that turning it on rewrites the
        // entry to the new location. Reporting a third "registered elsewhere" state would be one
        // more thing the UI has to explain for no gain.
        Registration(@"C:\old\widget.exe").Enable();

        Assert.False(Registration(@"C:\new\widget.exe").IsEnabled);
    }

    [Fact]
    public void EnablingAfterAMoveOverwritesTheOldPath()
    {
        Registration(@"C:\old\widget.exe").Enable();
        StartupRegistration moved = Registration(@"C:\new\widget.exe");

        moved.Enable();

        Assert.True(moved.IsEnabled);
        Assert.False(Registration(@"C:\old\widget.exe").IsEnabled);
    }

    [Fact]
    public void WithoutAKnownExecutableTheFeatureReportsItselfUnsupported()
    {
        StartupRegistration registration = Registration(null);

        Assert.False(registration.IsSupported);
        Assert.False(registration.IsEnabled);
    }

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\AiUsageMonitor\tests", throwOnMissingSubKey: false);
        }
        catch (UnauthorizedAccessException)
        {
            // A locked key must not fail an otherwise passing test.
        }
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~StartupRegistrationTests`
Expected: build failure — `StartupRegistration` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/AiUsageMonitor.App/Interop/StartupRegistration.cs`:

```csharp
using Microsoft.Win32;

namespace AiUsageMonitor.App.Interop;

/// <summary>
/// The Start with Windows setting, as a per-user Run entry.
/// <para>
/// HKEY_CURRENT_USER only, so no administrator rights are involved and nothing machine-wide or
/// policy-owned is touched. This is application-owned configuration; it is not, and must never
/// become, a way to modify a provider's own configuration.
/// </para>
/// </summary>
public sealed class StartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultValueName = "AiUsageMonitor";

    private readonly string _keyPath;
    private readonly string _valueName;
    private readonly string? _executablePath;

    public StartupRegistration(string keyPath, string valueName, string? executablePath)
    {
        _keyPath = keyPath;
        _valueName = valueName;
        _executablePath = executablePath;
    }

    /// <summary>
    /// Resolved at run time, never hardcoded: the release artifact has to work on a machine that is
    /// not the author's, from whatever folder its owner unpacked it into.
    /// </summary>
    public static StartupRegistration ForThisProcess() =>
        new(RunKeyPath, DefaultValueName, Environment.ProcessPath);

    /// <summary>
    /// False when the process cannot name its own executable, in which case there is nothing
    /// truthful to register. The UI disables the control and says why rather than offering a
    /// toggle that silently does nothing.
    /// </summary>
    public bool IsSupported => _executablePath is not null;

    /// <summary>
    /// True only when the stored command line is this executable. An entry left by a copy of the
    /// app that has since moved is not this app starting with Windows, so it reads as off and
    /// <see cref="Enable"/> overwrites it.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            if (_executablePath is null)
            {
                return false;
            }

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_keyPath);
            return key?.GetValue(_valueName) is string stored
                && string.Equals(stored, Command(_executablePath), StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Enable()
    {
        if (_executablePath is null)
        {
            return;
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
        key.SetValue(_valueName, Command(_executablePath), RegistryValueKind.String);
    }

    /// <summary>
    /// Deletes this application's own value name, whatever it currently contains, and never the
    /// key: Run is shared with every other application the user has.
    /// </summary>
    public void Disable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    /// <summary>Quoted, or a path containing a space is read as a program plus arguments.</summary>
    private static string Command(string executablePath) => "\"" + executablePath + "\"";
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~StartupRegistrationTests`
Expected: 7 passed.

- [ ] **Step 5: Commit**

```bash
git add src/AiUsageMonitor.App/Interop/StartupRegistration.cs tests/AiUsageMonitor.App.Tests/StartupRegistrationTests.cs
git commit -m "feat: register the widget to start with Windows, per user"
```

---

### Task 3: Make the existing consumers respond to a settings change

**Files:**
- Modify: `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs` (the `_baseInterval` field and constructor)
- Modify: `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs`
- Test: `tests/AiUsageMonitor.App.Tests/ProviderCardViewModelTests.cs` (add), `tests/AiUsageMonitor.App.Tests/MainViewModelTests.cs` (add)

**Interfaces:**
- Consumes: `SettingsService` (Task 1).
- Produces: `ProviderRefreshService.BaseInterval { get; set; }`; `ProviderCardViewModel.ColorBarsByUsage { get; set; }`, `.ShowWhenUnavailable { get; set; }`, `.IsHiddenByFilter { get; }`; `MainViewModel.ApplySettings(AppSettings settings)`. Tasks 5 and 6 consume these.

- [ ] **Step 1: Write the failing tests**

Append to `tests/AiUsageMonitor.App.Tests/ProviderCardViewModelTests.cs`, inside the existing class. Its helpers are `Card()`, `Snapshot(ConnectionState state = Connected, string? version = "2.1.227", IReadOnlyList<QuotaWindow>? windows = null, DateTimeOffset? retrievedAt = null, string? error = null)`, `Window(string id, int order, double used)` and the `Policy` field — call them exactly as written below, with the named arguments shown:

```csharp
    [Fact]
    public void TurningColourBandsOffRebuildsTheRowsThatRenderThem()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(windows: [Window("five_hour", 0, 82)], retrievedAt: Now), Now, Policy);

        card.ColorBarsByUsage = false;

        Assert.Single(card.Windows);
        Assert.False(card.Windows[0].ColorBarsByUsage);
        Assert.Equal(82, card.Windows[0].UsedPercent);
    }

    [Fact]
    public void AHiddenProviderIsOnlyHiddenWhenItIsActuallyAbsent()
    {
        // NotInstalled and Unsupported are facts about the machine. An Error is a provider that is
        // present and broken, and hiding it would hide the one card the user needs to see.
        ProviderCardViewModel card = Card();
        card.ShowWhenUnavailable = false;

        card.Apply(Snapshot(ConnectionState.Error, error: "boom"), Now, Policy);
        Assert.False(card.IsHiddenByFilter);

        card.Apply(Snapshot(ConnectionState.NotInstalled), Now, Policy);
        Assert.True(card.IsHiddenByFilter);

        card.ShowWhenUnavailable = true;
        Assert.False(card.IsHiddenByFilter);
    }
```

Append to `tests/AiUsageMonitor.App.Tests/MainViewModelTests.cs`, inside the existing class. Its helpers are `Build(params ProviderDescriptor[] providers)` returning `(MainViewModel Model, IReadOnlyList<ProviderDescriptor> Providers)`, `StubProbe(string name, ConnectionState state, IReadOnlyList<QuotaWindow> windows)` and `Window()` taking no arguments. There is no `Model(AppSettings)` helper — do not invent one:

```csharp
    [Fact]
    public async Task ASettingsChangeReachesTheRowsThatRenderIt()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [Window()])));
        await model.RefreshAsync(force: true);

        Assert.True(model.Providers[0].Windows[0].ColorBarsByUsage);

        model.ApplySettings(AppSettings.Default with { ColorBarsByUsage = false });

        Assert.False(model.Providers[0].Windows[0].ColorBarsByUsage);
        Assert.Equal(47, model.Providers[0].Windows[0].UsedPercent);
    }

    [Fact]
    public async Task HidingUnavailableProvidersDropsThemFromTheFooterCount()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, [Window()])),
            new ProviderDescriptor("Codex", "CX", new StubProbe("Codex", ConnectionState.NotInstalled, [])));
        await model.RefreshAsync(force: true);

        Assert.Equal("2 providers", model.FooterText);

        model.ApplySettings(AppSettings.Default with { ShowUnavailableProviders = false });

        Assert.Equal("1 provider", model.FooterText);
        Assert.True(model.Providers[1].IsHiddenByFilter);
    }
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~ProviderCardViewModelTests`
Expected: build failure — `ColorBarsByUsage` has no setter, `ShowWhenUnavailable` and `IsHiddenByFilter` do not exist.

- [ ] **Step 3: Make `ProviderRefreshService.BaseInterval` settable**

In `src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs`, replace the `private readonly TimeSpan _baseInterval;` field and its constructor assignment with a property, and change the one use site at the `NextAttempt` computation:

```csharp
    /// <summary>
    /// The polling cadence backoff is measured against. Settable because the user can change the
    /// refresh interval while the process runs; a provider already in backoff simply measures its
    /// next attempt against the new value.
    /// </summary>
    public TimeSpan BaseInterval { get; set; }
```

Constructor: `BaseInterval = baseInterval;` replaces `_baseInterval = baseInterval;`. The use site becomes:

```csharp
            state.NextAttempt = now + BackoffFor(state.ConsecutiveFailures, BaseInterval);
```

- [ ] **Step 4: Make the card's colour flag and filter live**

In `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs`:

Change `private readonly bool _colorBarsByUsage;` to `private bool _colorBarsByUsage;` and add `private bool _showWhenUnavailable = true;`.

Add these members after `HasWindows`:

```csharp
    /// <summary>
    /// Setting this rebuilds the rows rather than mutating them. A <see cref="QuotaRowViewModel"/>
    /// is a pure projection of one <see cref="QuotaWindow"/>, and the rows are rebuilt on every
    /// snapshot anyway; making the row's own flag mutable would add observable state to the one
    /// class that has none. The caller ticks afterwards to refill the countdowns the rebuild cleared.
    /// </summary>
    public bool ColorBarsByUsage
    {
        get => _colorBarsByUsage;
        set
        {
            if (Set(ref _colorBarsByUsage, value) && _snapshot is ProviderSnapshot snapshot)
            {
                RebuildWindows(snapshot);
            }
        }
    }

    /// <summary>PRD §15: an unavailable provider keeps its card unless the user hides it.</summary>
    public bool ShowWhenUnavailable
    {
        get => _showWhenUnavailable;
        set
        {
            if (Set(ref _showWhenUnavailable, value))
            {
                Raise(nameof(IsHiddenByFilter));
            }
        }
    }

    /// <summary>
    /// Only a provider that is absent from the machine can be hidden. An Error or Unavailable
    /// provider is installed and not working, which is exactly the card the user needs to see.
    /// </summary>
    public bool IsHiddenByFilter =>
        !ShowWhenUnavailable && State is ConnectionState.NotInstalled or ConnectionState.Unsupported;
```

In the `State` setter, add `Raise(nameof(IsHiddenByFilter));` alongside the existing `Raise(nameof(StateLabel)); Raise(nameof(IsStale));`.

Extract the row rebuild out of `Apply` so both callers share it. `Apply` keeps everything else and calls `RebuildWindows(snapshot);` where the `Windows.Clear()` block used to be:

```csharp
    private void RebuildWindows(ProviderSnapshot snapshot)
    {
        Windows.Clear();

        foreach (QuotaWindow window in QuotaOrdering.InProviderOrder(snapshot.Windows))
        {
            Windows.Add(new QuotaRowViewModel(window, _colorBarsByUsage) { IsStale = IsStale });
        }

        Raise(nameof(HasWindows));
    }
```

- [ ] **Step 5: Give `MainViewModel` an `ApplySettings`**

In `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs`, change `private readonly FreshnessPolicy _freshness;` to `private FreshnessPolicy _freshness;`, add `using System.Linq;` if the file does not already have it, and add:

```csharp
    /// <summary>
    /// Re-applies everything a settings change can reach. Costs no provider call: freshness, bar
    /// colour and the visibility filter are all derived from data already held.
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        _freshness = new FreshnessPolicy(settings.StaleAfter);

        foreach (ProviderCardViewModel card in Providers)
        {
            card.ColorBarsByUsage = settings.ColorBarsByUsage;
            card.ShowWhenUnavailable = settings.ShowUnavailableProviders;
        }

        Tick();
        Raise(nameof(FooterText));
    }
```

Change `FooterText` to count what is on screen, and pass the initial filter value in the constructor loop:

```csharp
    public string FooterText
    {
        get
        {
            int visible = Providers.Count(card => !card.IsHiddenByFilter);
            return visible == 1 ? "1 provider" : $"{visible} providers";
        }
    }
```

In the constructor, replace `ProviderCardViewModel card = new(provider, settings.ColorBarsByUsage, RetryOne);` with:

```csharp
            ProviderCardViewModel card = new(provider, settings.ColorBarsByUsage, RetryOne)
            {
                ShowWhenUnavailable = settings.ShowUnavailableProviders
            };
```

In `OnRefreshed`, after `card.Apply(...)`, raise the footer so a provider that turns out to be absent stops being counted:

```csharp
        _dispatch(() =>
        {
            card.Apply(e.Snapshot, _clock(), _freshness);
            Raise(nameof(FooterText));
        });
```

- [ ] **Step 6: Run the whole suite**

Run: `dotnet build` then `dotnet test`
Expected: build clean, every test passes.

- [ ] **Step 7: Commit**

```bash
git add src/AiUsageMonitor.Infrastructure/Refresh/ProviderRefreshService.cs src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs src/AiUsageMonitor.App/ViewModels/MainViewModel.cs tests/AiUsageMonitor.App.Tests/ProviderCardViewModelTests.cs tests/AiUsageMonitor.App.Tests/MainViewModelTests.cs
git commit -m "feat: let a settings change reach the cards and the refresh cadence"
```

---

### Task 4: `SettingsViewModel`

**Files:**
- Create: `src/AiUsageMonitor.App/ViewModels/ChoiceViewModel.cs`
- Create: `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`
- Test: `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `SettingsService` (Task 1), `StartupRegistration` (Task 2), `ObservableObject`, `RelayCommand` (existing).
- Produces: `ChoiceViewModel(string label, int value, string groupName, Func<int> read, Action<int> write)` with `Label`, `Value`, `GroupName`, `IsSelected`, `Refresh()`; `SettingsViewModel(SettingsService settings, StartupRegistration startup, Action resetPosition, Action recheckProviders, Action openLogs)` with `AlwaysOnTop`, `ColorBarsByUsage`, `ShowUnavailableProviders`, `StartWithWindows`, `CanStartWithWindows`, `StartWithWindowsUnavailableReason`, `Themes`, `RefreshIntervals`, `StaleThresholds`, `ResetPositionCommand`, `RecheckProvidersCommand`, `OpenLogsCommand`. Task 5 binds to these.

**Design note — why choices, not text boxes.** The spec said numeric fields commit on lost focus. Presets are better and remove a whole class of problems: no parse failures, no partially typed value being clamped under the cursor, and the clamp ranges (15–3600s refresh, 30–3600s stale) are honoured by construction. A hand-edited settings file holding a value outside the presets keeps that value as an extra choice, so the window never silently rewrites a deliberate edit.

- [ ] **Step 1: Write the failing tests**

Create `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`:

```csharp
using System.IO;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.Tests;

public class SettingsViewModelTests
{
    private const string ScratchKey = @"Software\AiUsageMonitor\tests\SettingsVm";

    private static SettingsViewModel Model(out SettingsService service, AppSettings? initial = null)
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-vm-" + Guid.NewGuid().ToString("N"), "settings.json");
        service = new SettingsService(new AppSettingsStore(path), initial ?? AppSettings.Default);
        return new SettingsViewModel(
            service,
            new StartupRegistration(ScratchKey, "AiUsageMonitorTest", null),
            resetPosition: () => { },
            recheckProviders: () => { },
            openLogs: () => { });
    }

    [Fact]
    public void ATogglePersistsThroughTheService()
    {
        SettingsViewModel model = Model(out SettingsService service);

        model.AlwaysOnTop = true;

        Assert.True(service.Current.AlwaysOnTop);
    }

    [Fact]
    public void AChangeMadeElsewhereIsReflectedBack()
    {
        SettingsViewModel model = Model(out SettingsService service);

        service.Update(s => s with { ColorBarsByUsage = false });

        Assert.False(model.ColorBarsByUsage);
    }

    [Fact]
    public void SelectingARefreshIntervalWritesItsValue()
    {
        SettingsViewModel model = Model(out SettingsService service);

        ChoiceViewModel choice = model.RefreshIntervals.Single(c => c.Value == 300);
        choice.IsSelected = true;

        Assert.Equal(300, service.Current.RefreshIntervalSeconds);
        Assert.True(model.RefreshIntervals.Single(c => c.Value == 300).IsSelected);
    }

    [Fact]
    public void DeselectingAChoiceChangesNothing()
    {
        // Radio buttons report both sides of a move. Acting on the deselection would write the
        // outgoing value back over the incoming one, and which one won would depend on event order.
        SettingsViewModel model = Model(out SettingsService service);
        int before = service.Current.RefreshIntervalSeconds;

        model.RefreshIntervals.Single(c => c.Value == before).IsSelected = false;

        Assert.Equal(before, service.Current.RefreshIntervalSeconds);
    }

    [Fact]
    public void AHandEditedValueOutsideThePresetsSurvivesAsItsOwnChoice()
    {
        SettingsViewModel model = Model(out _, AppSettings.Default with { RefreshIntervalSeconds = 45 });

        ChoiceViewModel selected = model.RefreshIntervals.Single(c => c.IsSelected);

        Assert.Equal(45, selected.Value);
    }

    [Fact]
    public void EveryThemeIsOfferedAndTheCurrentOneIsSelected()
    {
        SettingsViewModel model = Model(out SettingsService service);

        Assert.Equal(3, model.Themes.Count);
        model.Themes.Single(c => c.Value == (int)ThemePreference.Dark).IsSelected = true;

        Assert.Equal(ThemePreference.Dark, service.Current.Theme);
    }

    [Fact]
    public void WithoutAKnownExecutableStartWithWindowsIsOfferedButDisabled()
    {
        SettingsViewModel model = Model(out _);

        Assert.False(model.CanStartWithWindows);
        Assert.False(model.StartWithWindows);
        Assert.False(string.IsNullOrWhiteSpace(model.StartWithWindowsUnavailableReason));
    }

    [Fact]
    public void TheActionsCallWhatTheyClaimTo()
    {
        string path = Path.Combine(Path.GetTempPath(), "aium-vm-" + Guid.NewGuid().ToString("N"), "settings.json");
        SettingsService service = new(new AppSettingsStore(path), AppSettings.Default);
        int reset = 0, recheck = 0, logs = 0;
        SettingsViewModel model = new(
            service,
            new StartupRegistration(ScratchKey, "AiUsageMonitorTest", null),
            resetPosition: () => reset++,
            recheckProviders: () => recheck++,
            openLogs: () => logs++);

        model.ResetPositionCommand.Execute(null);
        model.RecheckProvidersCommand.Execute(null);
        model.OpenLogsCommand.Execute(null);

        Assert.Equal(1, reset);
        Assert.Equal(1, recheck);
        Assert.Equal(1, logs);
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~SettingsViewModelTests`
Expected: build failure — neither view model exists.

- [ ] **Step 3: Write `ChoiceViewModel`**

Create `src/AiUsageMonitor.App/ViewModels/ChoiceViewModel.cs`:

```csharp
namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// One option in a mutually exclusive group, rendered as a radio button. Deliberately generic over
/// nothing: every setting it serves - theme, refresh interval, stale threshold - is an int or an
/// enum backed by one, so a shared int keeps a single control template serving all three.
/// </summary>
public sealed class ChoiceViewModel : ObservableObject
{
    private readonly Func<int> _read;
    private readonly Action<int> _write;

    public ChoiceViewModel(string label, int value, string groupName, Func<int> read, Action<int> write)
    {
        Label = label;
        Value = value;
        GroupName = groupName;
        _read = read;
        _write = write;
    }

    public string Label { get; }

    public int Value { get; }

    /// <summary>WPF scopes radio buttons by name, not by container, so every group needs its own.</summary>
    public string GroupName { get; }

    /// <summary>
    /// Writes only on selection. A radio button reports both sides of a move - the outgoing option
    /// goes false as the incoming one goes true - and acting on the false would write the old value
    /// back over the new one, with event order deciding which survived.
    /// </summary>
    public bool IsSelected
    {
        get => _read() == Value;
        set
        {
            if (value)
            {
                _write(Value);
            }
        }
    }

    public void Refresh() => Raise(nameof(IsSelected));
}
```

- [ ] **Step 4: Write `SettingsViewModel`**

Create `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// The settings window's view model. Every property reads through to
/// <see cref="SettingsService.Current"/> and writes through <see cref="SettingsService.Update"/>,
/// so there is no working copy to get out of step and no Apply button to forget.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private static readonly int[] RefreshPresets = [15, 30, 60, 120, 300, 600];
    private static readonly int[] StalePresets = [60, 120, 300, 600, 1800, 3600];

    private readonly SettingsService _settings;
    private readonly StartupRegistration _startup;

    public SettingsViewModel(
        SettingsService settings,
        StartupRegistration startup,
        Action resetPosition,
        Action recheckProviders,
        Action openLogs)
    {
        _settings = settings;
        _startup = startup;

        Themes =
        [
            Theme("System", ThemePreference.System),
            Theme("Light", ThemePreference.Light),
            Theme("Dark", ThemePreference.Dark)
        ];

        RefreshIntervals = Durations(
            "refresh",
            RefreshPresets,
            settings.Current.RefreshIntervalSeconds,
            seconds => _settings.Update(s => s with { RefreshIntervalSeconds = seconds }),
            () => _settings.Current.RefreshIntervalSeconds);

        StaleThresholds = Durations(
            "stale",
            StalePresets,
            settings.Current.StaleAfterSeconds,
            seconds => _settings.Update(s => s with { StaleAfterSeconds = seconds }),
            () => _settings.Current.StaleAfterSeconds);

        ResetPositionCommand = new RelayCommand(resetPosition);
        RecheckProvidersCommand = new RelayCommand(recheckProviders);
        OpenLogsCommand = new RelayCommand(openLogs);

        _settings.Changed += OnSettingsChanged;
    }

    public bool AlwaysOnTop
    {
        get => _settings.Current.AlwaysOnTop;
        set => _settings.Update(s => s with { AlwaysOnTop = value });
    }

    public bool ColorBarsByUsage
    {
        get => _settings.Current.ColorBarsByUsage;
        set => _settings.Update(s => s with { ColorBarsByUsage = value });
    }

    public bool ShowUnavailableProviders
    {
        get => _settings.Current.ShowUnavailableProviders;
        set => _settings.Update(s => s with { ShowUnavailableProviders = value });
    }

    /// <summary>
    /// Reads from the registry rather than from the settings file, because the registry is where
    /// the fact lives. A settings file copied to another machine, or an app the user moved, would
    /// otherwise show a checkbox that does not match what Windows will actually do.
    /// </summary>
    public bool StartWithWindows
    {
        get => _startup.IsEnabled;
        set
        {
            if (value)
            {
                _startup.Enable();
            }
            else
            {
                _startup.Disable();
            }

            _settings.Update(s => s with { StartWithWindows = _startup.IsEnabled });
            Raise();
        }
    }

    public bool CanStartWithWindows => _startup.IsSupported;

    public string? StartWithWindowsUnavailableReason => _startup.IsSupported
        ? null
        : "Unavailable: this build cannot determine its own location.";

    public IReadOnlyList<ChoiceViewModel> Themes { get; }

    public ObservableCollection<ChoiceViewModel> RefreshIntervals { get; }

    public ObservableCollection<ChoiceViewModel> StaleThresholds { get; }

    public RelayCommand ResetPositionCommand { get; }

    public RelayCommand RecheckProvidersCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private ChoiceViewModel Theme(string label, ThemePreference preference) => new(
        label,
        (int)preference,
        "theme",
        () => (int)_settings.Current.Theme,
        value => _settings.Update(s => s with { Theme = (ThemePreference)value }));

    /// <summary>
    /// The presets, plus <paramref name="current"/> when a hand-edited settings file holds
    /// something else. A value the user typed into the file deliberately must not vanish because
    /// this window offers a shorter list.
    /// </summary>
    private static ObservableCollection<ChoiceViewModel> Durations(
        string groupName,
        IReadOnlyList<int> presets,
        int current,
        Action<int> write,
        Func<int> read)
    {
        List<int> values = [.. presets];

        if (!values.Contains(current))
        {
            values.Add(current);
            values.Sort();
        }

        return [.. values.Select(seconds => new ChoiceViewModel(DurationLabel(seconds), seconds, groupName, read, write))];
    }

    private static string DurationLabel(int seconds) => seconds < 60
        ? seconds + "s"
        : seconds % 60 == 0 ? seconds / 60 + "m" : seconds / 60 + "m " + seconds % 60 + "s";

    /// <summary>
    /// A null property name tells WPF every binding on this object is out of date, which is exactly
    /// true: a settings change can come from anywhere, and every property here is a projection of
    /// the same record. The choice lists are separate objects and are refreshed by hand.
    /// </summary>
    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Raise(null);

        foreach (ChoiceViewModel choice in Themes.Concat(RefreshIntervals).Concat(StaleThresholds))
        {
            choice.Refresh();
        }
    }
}
```

- [ ] **Step 5: Run the tests and watch them pass**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~SettingsViewModelTests`
Expected: 8 passed.

- [ ] **Step 6: Commit**

```bash
git add src/AiUsageMonitor.App/ViewModels/ChoiceViewModel.cs src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs
git commit -m "feat: add the settings view model"
```

---

### Task 5: The settings window

**Files:**
- Modify: `src/AiUsageMonitor.App/Themes/Controls.xaml` (add `SettingsCheckBoxStyle`, `SettingsRadioButtonStyle`, `SettingsSectionTextStyle`, `SettingsActionButtonStyle`)
- Create: `src/AiUsageMonitor.App/Views/SettingsWindow.xaml`, `src/AiUsageMonitor.App/Views/SettingsWindow.xaml.cs`
- Modify: `src/AiUsageMonitor.App/App.xaml.cs` (register `SettingsService`, wire it up)
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs` (subscribe to settings, open the window, fix `SavePlacement`)
- Test: `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs` (add)

**Interfaces:**
- Consumes: `SettingsViewModel`, `ChoiceViewModel` (Task 4), `SettingsService` (Task 1), `StartupRegistration` (Task 2), `MainViewModel.ApplySettings` (Task 3), `ThemeManager` (existing).
- Produces: `SettingsWindow(SettingsViewModel model)`; `WidgetWindow.ShowSettings()`. Task 7 calls `ShowSettings`.

- [ ] **Step 1: Write the failing test**

Append to `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs`:

```csharp
    [Theory]
    [InlineData("Themes/Light.xaml")]
    [InlineData("Themes/Dark.xaml")]
    [InlineData("Themes/HighContrast.xaml")]
    public void TheSettingsWindowRendersInEveryPalette(string palette) => wpf.Invoke(() =>
    {
        ResourceDictionary dictionary = new()
        {
            Source = new Uri($"pack://application:,,,/AiUsageMonitor.App;component/{palette}", UriKind.Absolute)
        };
        Application.Current.Resources.MergedDictionaries.Add(dictionary);

        try
        {
            string path = Path.Combine(Path.GetTempPath(), "aium-view-" + Guid.NewGuid().ToString("N"), "settings.json");
            SettingsViewModel model = new(
                new SettingsService(new AppSettingsStore(path), AppSettings.Default),
                new StartupRegistration(@"Software\AiUsageMonitor\tests\ViewLoading", "AiUsageMonitorTest", null),
                resetPosition: () => { },
                recheckProviders: () => { },
                openLogs: () => { });

            SettingsWindow window = new(model);
            window.Measure(new Size(420, 640));
            window.Arrange(new Rect(0, 0, 420, 640));
            window.UpdateLayout();

            Assert.True(window.DesiredSize.Height > 0);
        }
        finally
        {
            Application.Current.Resources.MergedDictionaries.Remove(dictionary);
        }
    });
```

Add `using System.IO;`, `using AiUsageMonitor.App.Interop;` and `using AiUsageMonitor.Infrastructure.Settings;` to that file.

- [ ] **Step 2: Run the test and watch it fail**

Run: `dotnet test tests/AiUsageMonitor.App.Tests --filter FullyQualifiedName~TheSettingsWindowRendersInEveryPalette`
Expected: build failure — `SettingsWindow` does not exist.

- [ ] **Step 3: Add the control styles**

Append to `src/AiUsageMonitor.App/Themes/Controls.xaml`, before `</ResourceDictionary>`. Every brush is `DynamicResource` so all three palettes drive it, and every indicator carries shape as well as colour — in high contrast every one of these brushes collapses onto a `SystemColors` value, so colour alone would carry nothing:

```xml
  <Style x:Key="SettingsSectionTextStyle" TargetType="TextBlock" BasedOn="{StaticResource CaptionMicroTextStyle}">
    <Setter Property="Foreground" Value="{DynamicResource TextTertiaryBrush}" />
    <Setter Property="Margin" Value="0,12,0,4" />
  </Style>

  <Style x:Key="SettingsCheckBoxStyle" TargetType="CheckBox">
    <Setter Property="Height" Value="{DynamicResource SettingsRowHeight}" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="VerticalContentAlignment" Value="Center" />
    <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="CheckBox">
          <Grid Background="Transparent">
            <Grid.ColumnDefinitions><ColumnDefinition Width="*" /><ColumnDefinition Width="Auto" /></Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" VerticalAlignment="Center" Text="{TemplateBinding Content}" Style="{StaticResource BodySmallTextStyle}" Foreground="{TemplateBinding Foreground}" TextWrapping="Wrap" />
            <Border Grid.Column="1" x:Name="Box" Width="16" Height="16" Margin="10,0,0,0" VerticalAlignment="Center" CornerRadius="{DynamicResource RadiusChip}" Background="{DynamicResource WidgetLayerAltBackgroundBrush}" BorderBrush="{DynamicResource WidgetCardStrokeBrush}" BorderThickness="1">
              <TextBlock x:Name="Tick" Text="&#x2713;" FontSize="11" FontWeight="Bold" HorizontalAlignment="Center" VerticalAlignment="Center" Foreground="{DynamicResource TextPrimaryBrush}" Visibility="Collapsed" />
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Tick" Property="Visibility" Value="Visible" />
              <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource TextSecondaryBrush}" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource TextPrimaryBrush}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Foreground" Value="{DynamicResource TextTertiaryBrush}" />
              <Setter Property="Cursor" Value="Arrow" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="SettingsRadioButtonStyle" TargetType="RadioButton">
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="Margin" Value="0,0,4,0" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RadioButton">
          <Border x:Name="Chip" Padding="8,4" CornerRadius="{DynamicResource RadiusControl}" Background="Transparent" BorderBrush="{DynamicResource WidgetCardStrokeBrush}" BorderThickness="1">
            <TextBlock x:Name="Label" Text="{TemplateBinding Content}" Style="{StaticResource CaptionTextStyle}" Foreground="{DynamicResource TextSecondaryBrush}" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Chip" Property="Background" Value="{DynamicResource WidgetLayerAltBackgroundBrush}" />
              <Setter TargetName="Chip" Property="BorderBrush" Value="{DynamicResource TextSecondaryBrush}" />
              <Setter TargetName="Label" Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
              <Setter TargetName="Label" Property="FontWeight" Value="SemiBold" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Chip" Property="BorderBrush" Value="{DynamicResource TextPrimaryBrush}" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="SettingsActionButtonStyle" TargetType="Button" BasedOn="{StaticResource LinkButtonStyle}">
    <Setter Property="HorizontalAlignment" Value="Left" />
    <Setter Property="Padding" Value="0,6" />
  </Style>
```

- [ ] **Step 4: Write the settings window**

Create `src/AiUsageMonitor.App/Views/SettingsWindow.xaml`:

```xml
<Window x:Class="AiUsageMonitor.App.Views.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Quota Monitor settings"
        Width="380" MaxHeight="640" SizeToContent="Height"
        WindowStyle="ToolWindow" ResizeMode="NoResize" ShowInTaskbar="False"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource WidgetWindowBackgroundBrush}"
        Foreground="{DynamicResource TextPrimaryBrush}"
        FontFamily="{StaticResource WidgetFontFamily}"
        UseLayoutRounding="True" SnapsToDevicePixels="True">
  <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
    <StackPanel Margin="14,10,14,14">

      <TextBlock Text="APPEARANCE" Style="{StaticResource SettingsSectionTextStyle}" Margin="0,0,0,4" />
      <TextBlock Text="Theme" Style="{StaticResource BodySmallTextStyle}" Foreground="{DynamicResource TextPrimaryBrush}" />
      <ItemsControl ItemsSource="{Binding Themes}" Margin="0,5,0,0" Focusable="False">
        <ItemsControl.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation="Horizontal" /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <RadioButton Style="{StaticResource SettingsRadioButtonStyle}" Content="{Binding Label}" GroupName="{Binding GroupName}" IsChecked="{Binding IsSelected, Mode=TwoWay}" AutomationProperties.Name="{Binding Label}" />
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
      <CheckBox Style="{StaticResource SettingsCheckBoxStyle}" Content="Color bars by usage" IsChecked="{Binding ColorBarsByUsage, Mode=TwoWay}" AutomationProperties.Name="Color bars by usage" />

      <TextBlock Text="WINDOW" Style="{StaticResource SettingsSectionTextStyle}" />
      <CheckBox Style="{StaticResource SettingsCheckBoxStyle}" Content="Always on top" IsChecked="{Binding AlwaysOnTop, Mode=TwoWay}" AutomationProperties.Name="Always on top" />
      <CheckBox Style="{StaticResource SettingsCheckBoxStyle}" Content="Show providers that are not installed" IsChecked="{Binding ShowUnavailableProviders, Mode=TwoWay}" AutomationProperties.Name="Show providers that are not installed" />
      <CheckBox x:Name="StartWithWindowsBox" Style="{StaticResource SettingsCheckBoxStyle}" Content="Start with Windows" IsChecked="{Binding StartWithWindows, Mode=TwoWay}" IsEnabled="{Binding CanStartWithWindows}" AutomationProperties.Name="Start with Windows" />
      <!-- Style is set only as a property element here. Setting the Style attribute as well and
           then nesting TextBlock.Style is MC3024 - the property can be set once. -->
      <TextBlock Text="{Binding StartWithWindowsUnavailableReason}" TextWrapping="Wrap" Margin="0,0,0,4" Foreground="{DynamicResource TextTertiaryBrush}">
        <TextBlock.Style>
          <Style TargetType="TextBlock" BasedOn="{StaticResource CaptionTextStyle}">
            <Setter Property="Visibility" Value="Visible" />
            <Style.Triggers><DataTrigger Binding="{Binding CanStartWithWindows}" Value="True"><Setter Property="Visibility" Value="Collapsed" /></DataTrigger></Style.Triggers>
          </Style>
        </TextBlock.Style>
      </TextBlock>

      <TextBlock Text="REFRESH" Style="{StaticResource SettingsSectionTextStyle}" />
      <TextBlock Text="Check providers every" Style="{StaticResource BodySmallTextStyle}" Foreground="{DynamicResource TextPrimaryBrush}" />
      <ItemsControl ItemsSource="{Binding RefreshIntervals}" Margin="0,5,0,0" Focusable="False">
        <ItemsControl.ItemsPanel><ItemsPanelTemplate><WrapPanel /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <RadioButton Style="{StaticResource SettingsRadioButtonStyle}" Margin="0,0,4,4" Content="{Binding Label}" GroupName="{Binding GroupName}" IsChecked="{Binding IsSelected, Mode=TwoWay}" AutomationProperties.Name="{Binding Label}" />
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
      <TextBlock Text="Call values stale after" Margin="0,8,0,0" Style="{StaticResource BodySmallTextStyle}" Foreground="{DynamicResource TextPrimaryBrush}" />
      <ItemsControl ItemsSource="{Binding StaleThresholds}" Margin="0,5,0,0" Focusable="False">
        <ItemsControl.ItemsPanel><ItemsPanelTemplate><WrapPanel /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <RadioButton Style="{StaticResource SettingsRadioButtonStyle}" Margin="0,0,4,4" Content="{Binding Label}" GroupName="{Binding GroupName}" IsChecked="{Binding IsSelected, Mode=TwoWay}" AutomationProperties.Name="{Binding Label}" />
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>

      <TextBlock Text="ACTIONS" Style="{StaticResource SettingsSectionTextStyle}" />
      <Button Style="{StaticResource SettingsActionButtonStyle}" Content="Re-check providers" Command="{Binding RecheckProvidersCommand}" AutomationProperties.Name="Re-check providers" />
      <Button Style="{StaticResource SettingsActionButtonStyle}" Content="Reset window position" Command="{Binding ResetPositionCommand}" AutomationProperties.Name="Reset window position" />
      <Button Style="{StaticResource SettingsActionButtonStyle}" Content="Open logs folder" Command="{Binding OpenLogsCommand}" AutomationProperties.Name="Open logs folder" />

    </StackPanel>
  </ScrollViewer>
</Window>
```

Create `src/AiUsageMonitor.App/Views/SettingsWindow.xaml.cs`:

```csharp
using System.Windows;
using AiUsageMonitor.App.ViewModels;

namespace AiUsageMonitor.App.Views;

/// <summary>
/// Settings apply as they are changed. There is no OK, Cancel or Apply: the widget is visible
/// behind this window, so every change is already on screen by the time the user could press one,
/// and a commit step would only add a way to be wrong about whether a change had taken.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _model;

    public SettingsWindow(SettingsViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
    }

    protected override void OnClosed(EventArgs e)
    {
        _model.Dispose();
        base.OnClosed(e);
    }
}
```

- [ ] **Step 5: Wire it into the application**

In `src/AiUsageMonitor.App/App.xaml.cs`, register the service after `services.AddSingleton(loaded.Settings);`:

```csharp
        services.AddSingleton(provider => new SettingsService(
            provider.GetRequiredService<AppSettingsStore>(),
            loaded.Settings,
            provider.GetRequiredService<ILogger<SettingsService>>()));
```

and pass it to the window, replacing the existing `new WidgetWindow(...)` call:

```csharp
            new WidgetWindow(
                model,
                _services.GetRequiredService<SettingsService>(),
                _services.GetRequiredService<ProviderRefreshService>(),
                _services.GetRequiredService<ThemeManager>()).Show();
```

In `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`, replace the constructor's `AppSettings settings, AppSettingsStore? store` parameters with the service, keep the existing optional-argument shape for tests, and add the settings subscription:

```csharp
    private readonly SettingsService _settings;
    private readonly ProviderRefreshService? _refresh;
    private SettingsWindow? _settingsWindow;

    public WidgetWindow(MainViewModel model, SettingsService settings, ProviderRefreshService? refresh = null, ThemeManager? theme = null)
    {
        _model = model;
        _settings = settings;
        _refresh = refresh;
        _theme = theme;

        InitializeComponent();
        DataContext = model;

        Topmost = settings.Current.AlwaysOnTop;
        RestorePlacement(settings.Current);

        _tick.Tick += (_, _) => _model.Tick();
        _poll.Interval = settings.Current.RefreshInterval;
        _poll.Tick += (_, _) => _ = _model.RefreshAsync(force: false);

        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>
    /// Everything a settings change reaches from here. The poll timer's interval is reassigned
    /// rather than the timer restarted: DispatcherTimer applies a new interval on its next tick,
    /// which is the behaviour wanted - a user shortening the interval should not also force an
    /// immediate provider call.
    /// </summary>
    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Topmost = settings.AlwaysOnTop;
        _poll.Interval = settings.RefreshInterval;

        if (_refresh is not null)
        {
            _refresh.BaseInterval = settings.RefreshInterval;
        }

        _theme?.Apply(settings.Theme);
        _model.ApplySettings(settings);
    }

    /// <summary>Opens the settings window, or focuses the one already open.</summary>
    public void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        SettingsViewModel model = new(
            _settings,
            StartupRegistration.ForThisProcess(),
            resetPosition: ResetPlacement,
            recheckProviders: () => _ = _model.RefreshAsync(force: true),
            openLogs: OpenLogsFolder);

        _settingsWindow = new SettingsWindow(model) { Owner = this };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ResetPlacement()
    {
        _settings.Update(s => s with { WindowLeft = null, WindowTop = null });
        Left = (SystemParameters.WorkArea.Width - Width) / 2;
        Top = (SystemParameters.WorkArea.Height - ActualHeight) / 2;
    }

    /// <summary>
    /// UseShellExecute is required: without it the explorer.exe launch is treated as a raw process
    /// start and the folder argument is ignored.
    /// </summary>
    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(RollingFileLoggerProvider.DefaultDirectory);
            Process.Start(new ProcessStartInfo(RollingFileLoggerProvider.DefaultDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Failing to open a folder is not a reason to take the widget down.
        }
    }
```

Add `using System.Diagnostics;`, `using AiUsageMonitor.App.Interop;` and `using AiUsageMonitor.Infrastructure.Logging;` to that file.

Rewrite `SavePlacement` to go through the service, and detach the subscription in `OnClosed`:

```csharp
    private void SavePlacement() =>
        _settings.Update(s => s with { WindowLeft = Left, WindowTop = Top });
```

In `OnClosed`, add `_settings.Changed -= OnSettingsChanged;` next to the existing `_theme.Changed -= OnThemeChanged;` detach.

Update `tests/AiUsageMonitor.App.Tests/WidgetWindowTests.cs` for the new constructor: build a `SettingsService` over a temp path in place of the `AppSettings`/`AppSettingsStore` pair.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet build` then `dotnet test`
Expected: build clean, every test passes.

- [ ] **Step 7: Commit**

```bash
git add src/AiUsageMonitor.App/Themes/Controls.xaml src/AiUsageMonitor.App/Views/SettingsWindow.xaml src/AiUsageMonitor.App/Views/SettingsWindow.xaml.cs src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs src/AiUsageMonitor.App/App.xaml.cs tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs tests/AiUsageMonitor.App.Tests/WidgetWindowTests.cs
git commit -m "feat: add the settings window"
```

---

### Task 6: The application icon and the tray icon

**Files:**
- Create: `build/make-icon.ps1`
- Create: `src/AiUsageMonitor.App/Assets/app.ico` (generated by the script, committed)
- Modify: `src/AiUsageMonitor.App/AiUsageMonitor.App.csproj` (`ApplicationIcon`, `Resource`)
- Create: `src/AiUsageMonitor.App/Interop/TrayIcon.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `TrayIcon(Window owner, string tooltip)` implementing `IDisposable`, with `event EventHandler? Activated`, `event EventHandler? ContextMenuRequested`, `void Show()`, `void ShowHint(string title, string text)`. Task 7 consumes it.

- [ ] **Step 1: Write the icon generator**

Create `build/make-icon.ps1`. It draws the widget's motif — three stacked quota bars on a rounded dark plate — at each size Windows asks for, and packs the frames into a single `.ico`. Windows 10 and 11 both accept PNG-compressed frames at every size.

```powershell
#requires -Version 5.1
# Generates src/AiUsageMonitor.App/Assets/app.ico. Re-runnable and deterministic: the .ico is
# committed, so this script exists to make the asset reproducible, not to run during a build.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$outPath  = Join-Path $repoRoot 'src\AiUsageMonitor.App\Assets\app.ico'
$outDir   = Split-Path -Parent $outPath
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$frames = New-Object System.Collections.Generic.List[byte[]]

foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 16.0
    $plate = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 24, 27))
    $g.FillRectangle($plate, 0, 0, $size, $size)

    # Three bars at descending fill, the widget's own motif at icon scale.
    $track = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 63, 63, 70))
    $fill  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 129, 140, 248))
    $barHeight = [Math]::Max(1.0, 2.0 * $s)
    $left = 3.0 * $s
    $width = $size - (6.0 * $s)
    $top = 4.0 * $s
    foreach ($pct in @(0.85, 0.55, 0.3)) {
        $g.FillRectangle($track, $left, $top, $width, $barHeight)
        $g.FillRectangle($fill,  $left, $top, $width * $pct, $barHeight)
        $top = $top + $barHeight + (1.5 * $s)
    }

    $plate.Dispose(); $track.Dispose(); $fill.Dispose(); $g.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($stream.ToArray())
    $stream.Dispose(); $bitmap.Dispose()
}

# ICO container: 6-byte header, then one 16-byte directory entry per frame, then the frame bytes.
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($out)
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = $sizes[$i]
    # 256 is written as 0: the field is one byte.
    $writer.Write([byte]($(if ($dim -ge 256) { 0 } else { $dim })))
    $writer.Write([byte]($(if ($dim -ge 256) { 0 } else { $dim })))
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frames[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset = $offset + $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }

$writer.Flush()
[System.IO.File]::WriteAllBytes($outPath, $out.ToArray())
$writer.Dispose(); $out.Dispose()

Write-Host "Wrote $outPath ($($sizes.Count) frames)"
```

- [ ] **Step 2: Generate the icon and register it**

Run: `powershell -File build/make-icon.ps1`
Expected: `Wrote …\app.ico (8 frames)`. Open it and confirm it is not blank.

In `src/AiUsageMonitor.App/AiUsageMonitor.App.csproj`, add to the existing first `PropertyGroup`:

```xml
    <ApplicationIcon>Assets\app.ico</ApplicationIcon>
```

and add a new `ItemGroup`:

```xml
  <ItemGroup>
    <Resource Include="Assets\app.ico" />
  </ItemGroup>
```

- [ ] **Step 3: Write the tray interop**

Create `src/AiUsageMonitor.App/Interop/TrayIcon.cs`:

```csharp
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Resources;

namespace AiUsageMonitor.App.Interop;

/// <summary>
/// A notification-area icon, hosted on an existing window's HWND.
/// <para>
/// Hand-rolled rather than taken from WinForms or a package, for one product reason: the context
/// menu stays an ordinary WPF <c>ContextMenu</c> and therefore honours all three palettes,
/// including high contrast. A <c>System.Windows.Forms.NotifyIcon</c> menu ignores them entirely.
/// </para>
/// <para>
/// The callback window is the owner's own HWND rather than a message-only window. A message-only
/// window cannot receive <c>HWND_BROADCAST</c>, which the single-instance handshake needs, and the
/// widget's window now lives for the whole process anyway.
/// </para>
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int WM_TRAYICON = 0x0400 + 1024;   // WM_APP + 1024
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    private const int NIM_ADD = 0x0;
    private const int NIM_MODIFY = 0x1;
    private const int NIM_DELETE = 0x2;

    private const int NIF_MESSAGE = 0x1;
    private const int NIF_ICON = 0x2;
    private const int NIF_TIP = 0x4;
    private const int NIF_INFO = 0x10;

    private readonly Window _owner;
    private readonly string _tooltip;
    private readonly uint _taskbarCreated;
    private HwndSource? _source;
    private IntPtr _icon;
    private bool _added;
    private bool _disposed;

    public TrayIcon(Window owner, string tooltip)
    {
        _owner = owner;
        _tooltip = tooltip;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    }

    /// <summary>Raised on left click: the user wants the widget.</summary>
    public event EventHandler? Activated;

    /// <summary>Raised on right click, on the UI thread, for the caller to open its own menu.</summary>
    public event EventHandler? ContextMenuRequested;

    /// <summary>
    /// Adds the icon. The owner window must already have an HWND, so call this no earlier than
    /// <c>OnSourceInitialized</c>.
    /// </summary>
    public void Show()
    {
        IntPtr handle = new WindowInteropHelper(_owner).Handle;

        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("The owner window has no handle yet.");
        }

        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        _icon = LoadIcon();

        Send(NIM_ADD);
        _added = true;
    }

    /// <summary>A one-off balloon. Used once, to say where the window went the first time it hides.</summary>
    public void ShowHint(string title, string text)
    {
        if (!_added)
        {
            return;
        }

        NOTIFYICONDATA data = Data(NIF_INFO);
        data.szInfoTitle = title;
        data.szInfo = text;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_added)
        {
            Send(NIM_DELETE);
            _added = false;
        }

        _source?.RemoveHook(WndProc);
        _source = null;

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Explorer restarts and every tray icon in the session is dropped. Without this the widget
        // is unreachable for the rest of the session, and the only way to end it is Task Manager.
        if (msg == _taskbarCreated && _added)
        {
            Send(NIM_ADD);
            return IntPtr.Zero;
        }

        if (msg != WM_TRAYICON)
        {
            return IntPtr.Zero;
        }

        switch ((int)lParam)
        {
            case WM_LBUTTONUP:
                Activated?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
            case WM_RBUTTONUP:
                ContextMenuRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void Send(int message)
    {
        NOTIFYICONDATA data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        Shell_NotifyIcon(message, ref data);
    }

    /// <summary>
    /// Every field is assigned, including the ones this app never varies. An interop struct field
    /// that is never written raises CS0649, and warnings are errors here — so the padding fields
    /// are set explicitly rather than left to their defaults.
    /// </summary>
    private NOTIFYICONDATA Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = new WindowInteropHelper(_owner).Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_TRAYICON,
        hIcon = _icon,
        szTip = _tooltip,
        dwState = 0,
        dwStateMask = 0,
        szInfo = string.Empty,
        uVersion = 0,
        szInfoTitle = string.Empty,
        dwInfoFlags = 0
    };

    /// <summary>
    /// Loads the packed .ico and lets the shell pick the frame for the current DPI, rather than
    /// assuming 16x16 - which is wrong on every scaled display.
    /// </summary>
    private static IntPtr LoadIcon()
    {
        // Nullable: GetResourceStream is annotated to return null, and an unguarded dereference is
        // CS8602, which is an error here. A missing icon must not take the process down either -
        // a zero handle renders as the shell's default icon, which is worse than ours but visible.
        StreamResourceInfo? info = Application.GetResourceStream(
            new Uri("pack://application:,,,/AiUsageMonitor.App;component/Assets/app.ico", UriKind.Absolute));

        if (info?.Stream is null)
        {
            return IntPtr.Zero;
        }

        using MemoryStream buffer = new();
        info.Stream.CopyTo(buffer);

        string temporary = Path.Combine(Path.GetTempPath(), "aium-tray-" + Guid.NewGuid().ToString("N") + ".ico");
        File.WriteAllBytes(temporary, buffer.ToArray());

        try
        {
            int width = GetSystemMetrics(SM_CXSMICON);
            int height = GetSystemMetrics(SM_CYSMICON);
            return LoadImage(IntPtr.Zero, temporary, IMAGE_ICON, width, height, LR_LOADFROMFILE);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint load);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: clean. There is no unit test here — the shell APIs need a real desktop session. Task 7 makes it observable, and the session owner verifies it.

- [ ] **Step 5: Commit**

```bash
git add build/make-icon.ps1 src/AiUsageMonitor.App/Assets/app.ico src/AiUsageMonitor.App/AiUsageMonitor.App.csproj src/AiUsageMonitor.App/Interop/TrayIcon.cs
git commit -m "feat: add an application icon and the notification-area icon"
```

---

### Task 7: Hide to tray, exit, and the second instance

**Files:**
- Create: `src/AiUsageMonitor.App/Interop/SingleInstance.cs`
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml` (the close button's tooltip and name, the tray context menu)
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs` (hide, tray wiring, shutdown path)
- Modify: `src/AiUsageMonitor.App/App.xaml.cs` (single instance, shutdown mode)
- Modify: `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs` (add `TrayHintShown`)
- Test: `tests/AiUsageMonitor.Infrastructure.Tests/AppSettingsStoreTests.cs` (extend the round-trip test)

**Interfaces:**
- Consumes: `TrayIcon` (Task 6), `SettingsService` (Task 1), `WidgetWindow.ShowSettings` (Task 5).
- Produces: `SingleInstance.TryAcquire(string name, out SingleInstance? instance)`, `SingleInstance.ShowMessage` (a `uint`), `SingleInstance.BroadcastShow()`; `WidgetWindow.HideToTray()`, `WidgetWindow.ExitApplication()`.

- [ ] **Step 1: Add the settings flag**

In `src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs`, add:

```csharp
    /// <summary>
    /// Whether the "the widget is in the notification area" balloon has been shown. Application
    /// state rather than a preference, and deliberately not offered in the settings window: it
    /// answers "has this user been told once", which no one needs to configure.
    /// </summary>
    public bool TrayHintShown { get; init; }
```

Extend the existing `RoundTripsEveryProperty` test in `tests/AiUsageMonitor.Infrastructure.Tests/AppSettingsStoreTests.cs` to set and assert `TrayHintShown = true`.

- [ ] **Step 2: Write the single-instance handshake**

Create `src/AiUsageMonitor.App/Interop/SingleInstance.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Threading;

namespace AiUsageMonitor.App.Interop;

/// <summary>
/// One running widget per user session.
/// <para>
/// This matters more than it looks: with Start with Windows on and the window hidden in the tray,
/// launching the app again would otherwise start a second copy with a second tray icon, while the
/// click that started it appeared to do nothing at all.
/// </para>
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>
    /// Broadcast by a second instance to ask the first to show itself. Registered rather than
    /// invented, so the value cannot collide with another application's private message.
    /// </summary>
    public static readonly uint ShowMessage = RegisterWindowMessage("AiUsageMonitor.Show");

    private const int HWND_BROADCAST = 0xFFFF;

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// True when this process is the first. The mutex is session-local, not global: two users
    /// logged into the same machine each get their own widget.
    /// </summary>
    public static bool TryAcquire(string name, out SingleInstance? instance)
    {
        Mutex mutex = new(initiallyOwned: true, name, out bool created);

        if (!created)
        {
            mutex.Dispose();
            instance = null;
            return false;
        }

        instance = new SingleInstance(mutex);
        return true;
    }

    /// <summary>
    /// Asks whichever instance already exists to show itself. Posted rather than sent, so a first
    /// instance that is busy cannot block this one from exiting.
    /// </summary>
    public static void BroadcastShow() => PostMessage(new IntPtr(HWND_BROADCAST), ShowMessage, IntPtr.Zero, IntPtr.Zero);

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
}
```

- [ ] **Step 3: Add the tray menu to the widget's XAML**

In `src/AiUsageMonitor.App/Views/WidgetWindow.xaml`, add a resource for the menu inside the `Window` element, before `<Border …>`:

```xml
  <Window.Resources>
    <ContextMenu x:Key="TrayMenu">
      <MenuItem Header="Open" Click="TrayOpen_Click" />
      <MenuItem Header="Refresh all providers" Click="TrayRefresh_Click" />
      <MenuItem Header="Settings" Click="TraySettings_Click" />
      <Separator />
      <MenuItem Header="Exit" Click="TrayExit_Click" />
    </ContextMenu>
  </Window.Resources>
```

Change the close button so it states what it does before it does it:

```xml
          <Button Width="30" Height="26" Content="&#x2715;" FontSize="11" Click="Close_Click"
                  Style="{StaticResource LinkButtonStyle}"
                  Foreground="{DynamicResource TextSecondaryBrush}"
                  ToolTip="Hide to the notification area"
                  AutomationProperties.Name="Hide to the notification area" />
```

- [ ] **Step 4: Rework the window's lifetime**

In `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs`:

Add fields `private TrayIcon? _tray;` and `private bool _shuttingDown;`.

In `OnSourceInitialized`, after the existing chrome calls, start the tray and hook the show broadcast:

```csharp
        _tray = new TrayIcon(this, "Quota Monitor");
        _tray.Activated += (_, _) => ShowFromTray();
        _tray.ContextMenuRequested += OnTrayContextMenuRequested;
        _tray.Show();

        HwndSource.FromHwnd(handle)?.AddHook(OnWindowMessage);
```

```csharp
    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == SingleInstance.ShowMessage)
        {
            ShowFromTray();
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// A tray menu has no window to be placed relative to, so it is positioned by the mouse and
    /// given a placement target explicitly. StaysOpen false plus focusing the window first is what
    /// makes it dismiss on an outside click - a menu opened from a tray icon otherwise stays up.
    /// </summary>
    private void OnTrayContextMenuRequested(object? sender, EventArgs e)
    {
        if (Resources["TrayMenu"] is not ContextMenu menu)
        {
            return;
        }

        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.MousePoint;
        menu.StaysOpen = false;
        SetForegroundWindow(new WindowInteropHelper(this).Handle);
        menu.IsOpen = true;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>
    /// Hiding, not closing. The process keeps polling, so the tray icon can go on saying something
    /// true. The first time only, a balloon says where the window went: a widget that vanishes with
    /// no explanation reads as a crash.
    /// </summary>
    public void HideToTray()
    {
        Hide();

        if (!_settings.Current.TrayHintShown)
        {
            _tray?.ShowHint("Quota Monitor", "Still running in the notification area. Click the icon to bring it back.");
            _settings.Update(s => s with { TrayHintShown = true });
        }
    }

    /// <summary>
    /// The one path that actually ends the process. Everything OnClosed used to own on a close now
    /// happens here, because a close no longer means an exit.
    /// </summary>
    public void ExitApplication()
    {
        _shuttingDown = true;
        Close();
        Application.Current.Shutdown();
    }

    private void TrayOpen_Click(object sender, RoutedEventArgs e) => ShowFromTray();

    private void TrayRefresh_Click(object sender, RoutedEventArgs e) => _ = _model.RefreshAsync(force: true);

    private void TraySettings_Click(object sender, RoutedEventArgs e)
    {
        ShowFromTray();
        ShowSettings();
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e) => ExitApplication();
```

Change `Close_Click` to hide, and make a real close (session logoff, Alt+F4) hide too unless the app is shutting down:

```csharp
    private void Close_Click(object sender, RoutedEventArgs e) => HideToTray();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Alt+F4 and the system menu both reach here. Neither should end a widget that lives in the
        // tray; only the tray's own Exit does.
        if (!_shuttingDown)
        {
            e.Cancel = true;
            HideToTray();
        }

        base.OnClosing(e);
    }
```

In `OnClosed`, dispose the tray alongside the existing teardown: `_tray?.Dispose();`.

Add `using System.Windows.Controls;`, `using System.Windows.Controls.Primitives;` and the P/Invoke:

```csharp
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
```

- [ ] **Step 5: Wire single-instance and shutdown mode into `App`**

In `src/AiUsageMonitor.App/App.xaml.cs`, add a `private SingleInstance? _instance;` field and, as the first statements of `OnStartup` after `base.OnStartup(e)`:

```csharp
        if (!SingleInstance.TryAcquire("AiUsageMonitor.SingleInstance", out _instance))
        {
            SingleInstance.BroadcastShow();
            Shutdown();
            return;
        }

        // The widget is hidden, not closed, when the user dismisses it, so WPF must not treat a
        // window disappearing as the end of the application.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
```

Dispose it in `OnExit`, before the existing disposals:

```csharp
        _instance?.Dispose();
```

- [ ] **Step 6: Run the whole suite**

Run: `dotnet build` then `dotnet test`
Expected: build clean, every test passes.

- [ ] **Step 7: Commit**

```bash
git add src/AiUsageMonitor.Infrastructure/Settings/AppSettings.cs src/AiUsageMonitor.App/Interop/SingleInstance.cs src/AiUsageMonitor.App/Views/WidgetWindow.xaml src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs src/AiUsageMonitor.App/App.xaml.cs tests/AiUsageMonitor.Infrastructure.Tests/AppSettingsStoreTests.cs
git commit -m "feat: keep the widget alive in the notification area"
```

---

### Task 8: Visual and live verification (session owner, not delegated)

**Do not run the application from a delegated worker.** `dotnet run --project src/AiUsageMonitor.App` opens a GUI that never exits on its own; a background worker cannot see it and will hang. A worker that reaches this task should stop and report.

- [ ] Widget starts, tray icon appears, tooltip reads "Quota Monitor".
- [ ] `✕` hides the window; the balloon appears once and never again.
- [ ] Left-clicking the tray icon brings it back; right-clicking opens a themed menu that dismisses on an outside click.
- [ ] Every tray menu entry does what it says. Exit ends the process — confirm in Task Manager.
- [ ] Launching the exe a second time shows the existing widget instead of starting a second one.
- [ ] Settings: every toggle takes effect immediately on the widget behind the window. Theme switches all three ways. Colour bars off returns every bar below 100% to the single accent.
- [ ] Refresh interval and stale threshold take effect without a restart.
- [ ] Start with Windows: the checkbox writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; unchecking removes it. Verify with `reg query`.
- [ ] Reset window position re-centres. Open logs folder opens Explorer.
- [ ] Settings window is legible in light, dark and high contrast, and reachable by keyboard alone.
- [ ] Close and reopen: every setting persisted.

---

### Task 9 (optional — drop without consequence): the state-carrying tray glyph

The spec deferred this on the grounds that it needed an icon design. That was wrong: `docs/design/TrayGlyph.dc.html` already specifies it — a stack of 2px bars, one per quota window, filled by the same three bands as the quota rows, with the highest percentage as digits above and a cross or triangle overlay for the error and alert states.

It matters more than it looks. Hiding to tray is the feature Task 7 adds, and with a static icon a hidden widget tells the user nothing at all — which is most of the reason to have a tray icon. Left for last so it can be dropped.

**Files:**
- Create: `src/AiUsageMonitor.App/Interop/TrayGlyphRenderer.cs`
- Modify: `src/AiUsageMonitor.App/Interop/TrayIcon.cs` (accept a rendered icon)
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs` (re-render on refresh)
- Test: `tests/AiUsageMonitor.App.Tests/TrayGlyphRendererTests.cs`

**Interfaces:**
- Consumes: `TrayIcon` (Task 6), `ProviderCardViewModel` (Task 3), `QuotaBarFillSelector` (existing).
- Produces: `TrayGlyphRenderer.Render(IReadOnlyList<double?> percentages, string? digits, TrayOverlay overlay, int size)` returning `IntPtr` (an `HICON` the caller destroys); `TrayIcon.SetIcon(IntPtr icon)`.

- [ ] **Step 1:** Render a `DrawingVisual` matching `TrayGlyph.dc.html` at `GetSystemMetrics(SM_CXSMICON)`, to a `RenderTargetBitmap`, then to an `HICON` via `CreateIconIndirect`. Every previous `HICON` must be destroyed — this runs on every refresh, and a leaked icon handle per minute is a leak the user eventually notices.
- [ ] **Step 2:** Assert the renderer returns a non-zero handle for every combination of 0, 1, 2 and 3 windows, `null` percentages, and each overlay; assert the handle is destroyable.
- [ ] **Step 3:** Re-render from `MainViewModel` after each refresh, using the worst state across providers for the overlay and the highest percentage for the digits.
- [ ] **Step 4:** `dotnet build`, `dotnet test`, then visual verification at 100%, 125% and 150% scaling.
- [ ] **Step 5:** Commit.

---

## Self-review

**Spec coverage.** §1 (one owner, read-modify-write, failed-save behaviour, every consumer's response) — Tasks 1, 3, 5. §2 (window, live apply, the §19 table, accessibility) — Tasks 4, 5; the numeric-field decision changed from text boxes to presets, recorded in Task 4. §3 (Shell_NotifyIcon, menu, `TaskbarCreated`, no message-only window, icon) — Task 6. §4 (`StartupRegistration`, HKCU, self-healing path check, `IsSupported`, testable key path) — Task 2, with the assembly moved from Infrastructure to `App/Interop` and the reason recorded there. §5 (hide, one-time balloon, exit path, mutex plus broadcast) — Task 7. §6 (testing) — Tasks 1–5, 7; tray interop verified in Task 8 as the spec says. §7 (out of scope) — honoured, except that the deferred tray glyph is reinstated as optional Task 9 with its reasoning stated.

**Placeholder scan.** No TBDs. Every code step carries the code. Task 9's steps are one line each by design — it is optional and its detail is in `TrayGlyph.dc.html`, which is quoted rather than paraphrased.

**Type consistency.** `SettingsService.Update(Func<AppSettings, AppSettings>)` is used identically in Tasks 1, 4, 5, 7. `SettingsService.Changed` is `EventHandler<AppSettings>` in Tasks 1, 4, 5. `ProviderCardViewModel.ColorBarsByUsage`/`ShowWhenUnavailable`/`IsHiddenByFilter` match between Tasks 3 and 5. `MainViewModel.ApplySettings(AppSettings)` matches between Tasks 3 and 5. `StartupRegistration(string, string, string?)` matches between Tasks 2, 4 and 5. `TrayIcon(Window, string)` with `Activated`, `ContextMenuRequested`, `Show()`, `ShowHint(string, string)` matches between Tasks 6 and 7. `WidgetWindow(MainViewModel, SettingsService, ProviderRefreshService?, ThemeManager?)` is introduced in Task 5 and unchanged in Task 7.

**Known risk.** Task 5 changes `WidgetWindow`'s constructor, so `WidgetWindowTests` must be updated in the same commit or the build breaks. That is called out in Task 5 Step 5.
