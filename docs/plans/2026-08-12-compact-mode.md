# Compact Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One setting switches the widget between Standard and Compact density, where compact removes addressing and padding but never data.

**Architecture:** A boolean `IsCompact` flows from `AppSettings.Density` through `MainViewModel.ApplySettings` to every `ProviderCardViewModel`, exactly as `ColorBarsByUsage` already does. XAML reads that flag through `DataTrigger`s that swap between existing `*Compact` design tokens. `QuotaRowViewModel` gains nothing — its view reads the card's flag through a `RelativeSource` ancestor binding.

**Tech Stack:** C# 13 / .NET 10 (`net10.0-windows`), WPF, xUnit. No new `PackageReference`.

**Spec:** `docs/specs/2026-08-12-compact-mode-design.md` — read it before Task 1.

## Global Constraints

- `TreatWarningsAsErrors` is on solution-wide. `dotnet build` must produce **0 warnings, 0 errors**.
- No new `PackageReference` in any project.
- Windows-only, WPF, MVVM. Primary shell is **PowerShell 5.1** — no `&&`, no ternary operator.
- **A running widget instance locks `bin/…/AiUsageMonitor.App.exe` and makes every build fail MSB3026 after ten retries.** If that happens, add `-p:BaseOutputPath=C:\Users\sgrig\AppData\Local\Temp\claude\compact-out\` to the `dotnet build` / `dotnet test` command. Never kill the user's running widget to get a build through.
- **WPF ranks a local value ABOVE a style trigger.** Any property a `DataTrigger` must override has to be a `Style` `Setter`, never an element attribute. A trigger competing with an attribute parses, binds, builds clean, and does nothing at runtime.
- **A `Style` may be set as an attribute or as a property element, never both.** Setting both is a runtime XAML error. When adding `<X.Style>` to an element that already has `Style="{StaticResource Y}"`, delete the attribute and carry `Y` over as `BasedOn`.
- **XAML leaves `DataTrigger.Value` as the string `"True"`.** WPF converts at evaluation so triggers fire correctly at runtime, but a test inspecting a `Style` object must compare `trigger.Value.ToString()` against `"True"` — never `Equals(trigger.Value, true)`.
- The `WINDOW / USED / RESETS IN` column captions **stay visible at compact density**. This is a deliberate, approved deviation from the design render, because PRD §16 requires the visible percentage text to state its direction and the caption is what carries it. Do not remove them.
- Missing data is `null` and surfaces as `Waiting`/`Unavailable`, never as `0`.
- Colour never carries a state by itself, at either density.
- No credential is logged, persisted, displayed, or copied. No hardcoded user paths in shipped code.
- Comments explain **why**, not what. Match the surrounding density and tone — this codebase comments decisions and traps, not mechanics.

---

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `src/AiUsageMonitor.App/Themes/Tokens.xaml` | Two new density token pairs (body padding, card gap) | 1 |
| `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs` | `IsCompact` for the window chrome; fans density to every card | 1 |
| `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs` | `IsCompact`, `ShowStatusLine`, `ShowCompactSpacer` | 1 |
| `src/AiUsageMonitor.App/Views/ProviderCardView.xaml` | Card padding, monogram, version, conditional status line | 2 |
| `src/AiUsageMonitor.App/Views/QuotaRowView.xaml` | Row padding, via an ancestor binding to the card | 3 |
| `src/AiUsageMonitor.App/Views/WidgetWindow.xaml` | Title bar, footer, body padding, card gap | 4 |
| `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs` | The `Densities` choice group | 5 |
| `src/AiUsageMonitor.App/Views/SettingsWindow.xaml` | The Density radio pair | 5 |

`src/AiUsageMonitor.App/Views/WidgetWindow.xaml.cs` needs **no change**: its `OnSettingsChanged` already calls `_model.ApplySettings(settings)`.

---

### Task 1: Density tokens and the density flags

**Files:**
- Modify: `src/AiUsageMonitor.App/Themes/Tokens.xaml:30`
- Modify: `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs`
- Modify: `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs`
- Test: `tests/AiUsageMonitor.App.Tests/ProviderCardViewModelTests.cs`
- Test: `tests/AiUsageMonitor.App.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `AppSettings.Density` (type `WidgetDensity`, values `Normal` and `Compact`) from `AiUsageMonitor.Infrastructure.Settings`. Already exists; do not add it.
- Produces, for Tasks 2–4:
  - `MainViewModel.IsCompact` — `bool`, get-only publicly, raises `PropertyChanged`.
  - `ProviderCardViewModel.IsCompact` — `bool`, public get and set, raises `PropertyChanged` for itself, `ShowStatusLine` and `ShowCompactSpacer`.
  - `ProviderCardViewModel.ShowStatusLine` — `bool`, computed.
  - `ProviderCardViewModel.ShowCompactSpacer` — `bool`, computed, always the negation of `ShowStatusLine`.
  - Resource keys `WidgetBodyPadding`, `WidgetBodyPaddingCompact`, `ProviderCardGap`, `ProviderCardGapCompact`, all `Thickness`.

- [ ] **Step 1: Write the failing view-model tests**

Append to `tests/AiUsageMonitor.App.Tests/ProviderCardViewModelTests.cs`, inside the class:

```csharp
    [Fact]
    public void AConnectedCardDropsItsStatusLineOnlyWhenCompact()
    {
        ProviderCardViewModel card = Card();
        card.Apply(Snapshot(ConnectionState.Connected, retrievedAt: Now), Now, Policy);

        Assert.True(card.ShowStatusLine);
        Assert.False(card.ShowCompactSpacer);

        card.IsCompact = true;

        Assert.False(card.ShowStatusLine);
        Assert.True(card.ShowCompactSpacer);
    }

    /// <summary>
    /// The rule that makes compact safe to ship. Silence means connected, so anything that is not
    /// connected has to keep saying so - otherwise compact would hide a broken provider, which is
    /// the one thing the user most needs the card to tell them.
    /// </summary>
    [Theory]
    [InlineData(ConnectionState.Error)]
    [InlineData(ConnectionState.Stale)]
    [InlineData(ConnectionState.Waiting)]
    [InlineData(ConnectionState.Unavailable)]
    [InlineData(ConnectionState.Unsupported)]
    [InlineData(ConnectionState.NotInstalled)]
    [InlineData(ConnectionState.Discovering)]
    public void ACardThatIsNotConnectedKeepsItsStatusLineEvenWhenCompact(ConnectionState state)
    {
        ProviderCardViewModel card = Card();
        card.IsCompact = true;
        card.Apply(Snapshot(state, retrievedAt: null), Now, Policy);

        Assert.True(card.ShowStatusLine);
        Assert.False(card.ShowCompactSpacer);
    }

    [Fact]
    public void AStateChangeRepublishesTheStatusLineWithoutADensityChange()
    {
        // State moves on its own - a tick can carry a connected card into Stale with nothing else
        // changing - so the status line has to be raised from the State setter too, not only from
        // the density setter. Without it a compact card would go stale silently.
        ProviderCardViewModel card = Card();
        card.IsCompact = true;
        card.Apply(Snapshot(ConnectionState.Connected, retrievedAt: Now), Now, Policy);

        List<string?> raised = [];
        card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        card.Tick(Now.AddHours(1));

        Assert.Equal(ConnectionState.Stale, card.State);
        Assert.Contains(nameof(ProviderCardViewModel.ShowStatusLine), raised);
        Assert.True(card.ShowStatusLine);
    }
```

Append to `tests/AiUsageMonitor.App.Tests/MainViewModelTests.cs`, inside the class:

```csharp
    [Fact]
    public void DensityReachesEveryCardFromTheSettingsItWasBuiltWith()
    {
        ProviderDescriptor[] providers =
        [
            new("Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, [])),
            new("Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, []))
        ];
        ProviderRefreshService service = new(providers, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
        MainViewModel model = new(
            service,
            providers,
            AppSettings.Default with { Density = WidgetDensity.Compact },
            () => Now);

        Assert.True(model.IsCompact);
        Assert.All(model.Providers, card => Assert.True(card.IsCompact));

        model.Dispose();
    }

    [Fact]
    public void ADensityChangeMadeLaterReachesEveryCardToo()
    {
        (MainViewModel model, _) = Build(
            new ProviderDescriptor("Claude Code", "CC", new StubProbe("Claude Code", ConnectionState.Connected, [])),
            new ProviderDescriptor("Codex", "CX", new StubProbe("Codex", ConnectionState.Connected, [])));

        Assert.False(model.IsCompact);

        model.ApplySettings(AppSettings.Default with { Density = WidgetDensity.Compact });

        Assert.True(model.IsCompact);
        Assert.All(model.Providers, card => Assert.True(card.IsCompact));

        model.ApplySettings(AppSettings.Default with { Density = WidgetDensity.Normal });

        Assert.False(model.IsCompact);
        Assert.All(model.Providers, card => Assert.False(card.IsCompact));

        model.Dispose();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~AiUsageMonitor.App.Tests`
Expected: **compile errors** — `ShowStatusLine`, `ShowCompactSpacer`, `IsCompact` do not exist. That is the correct failure for this step; a compile error is a failing test here.

- [ ] **Step 3: Add the two token pairs**

In `src/AiUsageMonitor.App/Themes/Tokens.xaml`, immediately after the line
`<Thickness x:Key="QuotaRowPaddingCompact">0,4,0,4</Thickness>`, insert:

```xml
  <!-- The body's bottom component is 2 rather than the full body padding in both densities: each
       card carries the gap below it, and the two add up. 2 + 8 = 10 standard, 2 + 6 = 8 compact,
       which is what the design specifies for each. -->
  <Thickness x:Key="WidgetBodyPadding">10,0,10,2</Thickness>
  <Thickness x:Key="WidgetBodyPaddingCompact">8,0,8,2</Thickness>
  <Thickness x:Key="ProviderCardGap">0,0,0,8</Thickness>
  <Thickness x:Key="ProviderCardGapCompact">0,0,0,6</Thickness>
```

- [ ] **Step 4: Add the card's density flag**

In `src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs`:

Add a field beside the other private fields (after `private bool _showWhenUnavailable = true;`):

```csharp
    private bool _isCompact;
```

Replace the existing `State` property line in full:

```csharp
    public ConnectionState State { get => _state; private set { if (Set(ref _state, value)) { Raise(nameof(StateLabel)); Raise(nameof(IsStale)); Raise(nameof(IsHiddenByFilter)); } } }
```

with:

```csharp
    public ConnectionState State { get => _state; private set { if (Set(ref _state, value)) { Raise(nameof(StateLabel)); Raise(nameof(IsStale)); Raise(nameof(IsHiddenByFilter)); Raise(nameof(ShowStatusLine)); Raise(nameof(ShowCompactSpacer)); } } }
```

Then insert the following immediately after the `IsHiddenByFilter` property and before the `State` property:

```csharp
    /// <summary>
    /// Compact density (PRD §17). Set by <see cref="MainViewModel"/> from the one setting, never
    /// from the snapshot - density is a property of how the user wants to read the widget, not of
    /// anything a provider reported.
    /// </summary>
    public bool IsCompact
    {
        get => _isCompact;
        set
        {
            if (Set(ref _isCompact, value))
            {
                Raise(nameof(ShowStatusLine));
                Raise(nameof(ShowCompactSpacer));
            }
        }
    }

    /// <summary>
    /// The state chip and the timestamp beside it. Compact drops them, but only from a Connected
    /// card: silence means connected, and every other state comes straight back.
    /// <para>
    /// The design writes this condition as <c>!(dense &amp;&amp; connected &amp;&amp; !stale)</c>,
    /// because its mockup carries state and staleness as two independent props. Here they are not:
    /// <see cref="ConnectionState.Stale"/> is a value <see cref="State"/> takes, so Connected and
    /// Stale are already mutually exclusive and the third term would be dead.
    /// </para>
    /// </summary>
    public bool ShowStatusLine => !IsCompact || State != ConnectionState.Connected;

    /// <summary>
    /// Replaces the status line's height when it is gone, so the header does not sit directly on
    /// the column captions. Six pixels against the roughly twenty-five the status line occupied -
    /// compact keeps the separation and gives up the rest.
    /// </summary>
    public bool ShowCompactSpacer => !ShowStatusLine;
```

- [ ] **Step 5: Fan density out from the main view model**

In `src/AiUsageMonitor.App/ViewModels/MainViewModel.cs`:

Add a field after `private bool _isRefreshing;`:

```csharp
    private bool _isCompact;
```

In the constructor, replace the provider loop:

```csharp
        foreach (ProviderDescriptor provider in providers)
        {
            ProviderCardViewModel card = new(provider, settings.ColorBarsByUsage, RetryOne)
            {
                ShowWhenUnavailable = settings.ShowUnavailableProviders
            };
            _cards[provider] = card;
            Providers.Add(card);
        }
```

with:

```csharp
        _isCompact = settings.Density == WidgetDensity.Compact;

        foreach (ProviderDescriptor provider in providers)
        {
            ProviderCardViewModel card = new(provider, settings.ColorBarsByUsage, RetryOne)
            {
                ShowWhenUnavailable = settings.ShowUnavailableProviders,
                IsCompact = _isCompact
            };
            _cards[provider] = card;
            Providers.Add(card);
        }
```

Add the property immediately after the `FooterText` property:

```csharp
    /// <summary>
    /// Compact density (PRD §17), for the window chrome. The cards carry their own copy rather than
    /// binding up to this one, because a card is also rendered outside this window - in tests and in
    /// the render harness - and a binding that resolves to nothing there would silently read as
    /// standard.
    /// </summary>
    public bool IsCompact { get => _isCompact; private set => Set(ref _isCompact, value); }
```

In `ApplySettings`, replace the body's loop and the line above it:

```csharp
        _freshness = new FreshnessPolicy(settings.StaleAfter);

        foreach (ProviderCardViewModel card in Providers)
        {
            card.ColorBarsByUsage = settings.ColorBarsByUsage;
            card.ShowWhenUnavailable = settings.ShowUnavailableProviders;
        }
```

with:

```csharp
        _freshness = new FreshnessPolicy(settings.StaleAfter);
        IsCompact = settings.Density == WidgetDensity.Compact;

        foreach (ProviderCardViewModel card in Providers)
        {
            card.ColorBarsByUsage = settings.ColorBarsByUsage;
            card.ShowWhenUnavailable = settings.ShowUnavailableProviders;
            card.IsCompact = IsCompact;
        }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~AiUsageMonitor.App.Tests`
Expected: PASS, with the five new tests green and every pre-existing test still green.

- [ ] **Step 7: Verify the build is clean**

Run: `dotnet build`
Expected: **0 warnings, 0 errors.**

- [ ] **Step 8: Commit**

```bash
git add src/AiUsageMonitor.App/Themes/Tokens.xaml src/AiUsageMonitor.App/ViewModels/MainViewModel.cs src/AiUsageMonitor.App/ViewModels/ProviderCardViewModel.cs tests/AiUsageMonitor.App.Tests/ProviderCardViewModelTests.cs tests/AiUsageMonitor.App.Tests/MainViewModelTests.cs
git commit -m "feat: carry compact density from settings to every card"
```

---

### Task 2: The provider card at compact density

**Files:**
- Modify: `src/AiUsageMonitor.App/Views/ProviderCardView.xaml:6` (outer `Border`), `:9` (monogram), `:11` (version), `:14` (status line)
- Test: `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs`

**Interfaces:**
- Consumes from Task 1: `ProviderCardViewModel.IsCompact`, `.ShowStatusLine`, `.ShowCompactSpacer`; resource keys `ProviderCardPadding` and `ProviderCardPaddingCompact` (both already in `Tokens.xaml`).
- Produces for Task 3: a `ProviderCardView` whose `DataContext` is the `ProviderCardViewModel`, which the row binds to by `AncestorType`.
- Produces named elements for tests: `Monogram`, `VersionLine`, `StatusLine`, `CompactHeaderSpacer`, `ColumnCaptions`.

- [ ] **Step 1: Write the failing view tests**

Append to `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs`, inside the class:

```csharp
    private static ProviderCardViewModel Card(ConnectionState state, bool compact)
    {
        ProviderCardViewModel card = new(
            new ProviderDescriptor("Claude Code", "CC", new SilentProbe("Claude Code")),
            colorBarsByUsage: true,
            _ => { })
        {
            IsCompact = compact
        };
        card.Apply(Snapshot(state, [Window(47d, false, true), Window(62d, false, true)]), Now, FreshnessPolicy.Default);
        return card;
    }

    private static ProviderCardView Rendered(ProviderCardViewModel card) =>
        ControlLoadingTests.Measured(new ProviderCardView { DataContext = card, Width = 340 });

    /// <summary>
    /// The assertion that catches the trap this whole change turns on. WPF ranks a local value above
    /// a style trigger, so a compact trigger competing with a Padding attribute builds clean, binds
    /// clean, and changes nothing - which shows up here, and only here, as two identical heights.
    /// </summary>
    [Fact]
    public void ACompactCardIsShorterThanTheSameCardAtStandardDensity() => wpf.Invoke(() =>
    {
        double standard = Rendered(Card(ConnectionState.Connected, compact: false)).DesiredSize.Height;
        double compact = Rendered(Card(ConnectionState.Connected, compact: true)).DesiredSize.Height;

        Assert.True(compact < standard, $"compact measured {compact} against standard {standard}");
    });

    [Fact]
    public void ACompactCardDropsItsMonogramAndVersion() => wpf.Invoke(() =>
    {
        ProviderCardView standard = Rendered(Card(ConnectionState.Connected, compact: false));
        Assert.Equal(Visibility.Visible, ((FrameworkElement)standard.FindName("Monogram")).Visibility);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)standard.FindName("VersionLine")).Visibility);

        ProviderCardView compact = Rendered(Card(ConnectionState.Connected, compact: true));
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)compact.FindName("Monogram")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)compact.FindName("VersionLine")).Visibility);
    });

    [Fact]
    public void ACompactConnectedCardDropsItsStatusLineForASpacer() => wpf.Invoke(() =>
    {
        ProviderCardView view = Rendered(Card(ConnectionState.Connected, compact: true));

        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("StatusLine")).Visibility);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("CompactHeaderSpacer")).Visibility);
    });

    /// <summary>Compact hides the confirmation that nothing is wrong. It never hides that something is.</summary>
    [Theory]
    [InlineData(ConnectionState.Error)]
    [InlineData(ConnectionState.Stale)]
    [InlineData(ConnectionState.Unavailable)]
    public void ACompactCardWithAProblemKeepsItsStatusLine(ConnectionState state) => wpf.Invoke(() =>
    {
        ProviderCardView view = Rendered(Card(state, compact: true));

        Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("StatusLine")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("CompactHeaderSpacer")).Visibility);
    });

    /// <summary>
    /// A deliberate, approved deviation from the design render, which hides these when dense. PRD §16
    /// requires the visible percentage to state its direction, and under no caption "47%" states
    /// nothing. This test exists so a later tidy-up cannot quietly reopen that hole.
    /// </summary>
    [Fact]
    public void TheColumnCaptionsSurviveCompactDensity() => wpf.Invoke(() =>
    {
        ProviderCardView view = Rendered(Card(ConnectionState.Connected, compact: true));

        Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("ColumnCaptions")).Visibility);
        Assert.Contains("USED", Texts(view));
    });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ViewLoadingTests`
Expected: FAIL. `FindName` returns `null` for every new name, producing `NullReferenceException` on the casts, and `ACompactCardIsShorterThanTheSameCardAtStandardDensity` fails with two equal heights.

- [ ] **Step 3: Move the card's padding into a style**

In `src/AiUsageMonitor.App/Views/ProviderCardView.xaml`, replace line 6:

```xml
  <Border Background="{DynamicResource WidgetLayerBackgroundBrush}" BorderBrush="{DynamicResource WidgetCardStrokeBrush}" BorderThickness="1" CornerRadius="{DynamicResource RadiusCard}" Padding="{DynamicResource ProviderCardPadding}">
```

with:

```xml
  <Border Background="{DynamicResource WidgetLayerBackgroundBrush}" BorderBrush="{DynamicResource WidgetCardStrokeBrush}" BorderThickness="1" CornerRadius="{DynamicResource RadiusCard}">
    <!-- Padding is a Style setter rather than an attribute, and has to be: WPF ranks a local value
         above a style trigger, so an attribute here would win over the compact trigger silently -
         no warning, no exception, just a card that never gets tighter. -->
    <Border.Style>
      <Style TargetType="Border">
        <Setter Property="Padding" Value="{DynamicResource ProviderCardPadding}" />
        <Style.Triggers>
          <DataTrigger Binding="{Binding IsCompact}" Value="True">
            <Setter Property="Padding" Value="{DynamicResource ProviderCardPaddingCompact}" />
          </DataTrigger>
        </Style.Triggers>
      </Style>
    </Border.Style>
```

- [ ] **Step 4: Hide the monogram and the version at compact density**

Replace line 9 (the monogram `Border`) in full:

```xml
        <Border Grid.Column="0" Width="16" Height="16" CornerRadius="{DynamicResource RadiusChip}" Background="{DynamicResource WidgetTokenChipBackgroundBrush}" BorderBrush="{DynamicResource WidgetTokenChipStrokeBrush}" BorderThickness="1" VerticalAlignment="Center"><TextBlock Text="{Binding Monogram}" FontSize="8.5" FontWeight="Bold" HorizontalAlignment="Center" VerticalAlignment="Center" Foreground="{DynamicResource TextSecondaryBrush}" /></Border>
```

with:

```xml
        <Border x:Name="Monogram" Grid.Column="0" Width="16" Height="16" CornerRadius="{DynamicResource RadiusChip}" Background="{DynamicResource WidgetTokenChipBackgroundBrush}" BorderBrush="{DynamicResource WidgetTokenChipStrokeBrush}" BorderThickness="1" VerticalAlignment="Center">
          <!-- The name is already beside it, so compact spends the 23px on the rows instead. -->
          <Border.Style>
            <Style TargetType="Border">
              <Setter Property="Visibility" Value="Visible" />
              <Style.Triggers>
                <DataTrigger Binding="{Binding IsCompact}" Value="True">
                  <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
              </Style.Triggers>
            </Style>
          </Border.Style>
          <TextBlock Text="{Binding Monogram}" FontSize="8.5" FontWeight="Bold" HorizontalAlignment="Center" VerticalAlignment="Center" Foreground="{DynamicResource TextSecondaryBrush}" />
        </Border>
```

Replace line 11 (the version `TextBlock`) in full:

```xml
        <TextBlock Grid.Column="2" Margin="7,0,0,0" VerticalAlignment="Center" Style="{StaticResource CaptionTextStyle}" Text="{Binding VersionText}" Foreground="{DynamicResource TextTertiaryBrush}" />
```

with:

```xml
        <!-- The Style attribute is gone deliberately: a Style cannot be set as both an attribute and
             a property element, so CaptionTextStyle comes through BasedOn instead. -->
        <TextBlock x:Name="VersionLine" Grid.Column="2" Margin="7,0,0,0" VerticalAlignment="Center" Text="{Binding VersionText}" Foreground="{DynamicResource TextTertiaryBrush}">
          <TextBlock.Style>
            <Style TargetType="TextBlock" BasedOn="{StaticResource CaptionTextStyle}">
              <Setter Property="Visibility" Value="Visible" />
              <Style.Triggers>
                <DataTrigger Binding="{Binding IsCompact}" Value="True">
                  <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
              </Style.Triggers>
            </Style>
          </TextBlock.Style>
        </TextBlock>
```

- [ ] **Step 5: Make the status line conditional and add the spacer**

Replace line 14 (the status `StackPanel`) in full:

```xml
      <StackPanel Orientation="Horizontal" Margin="0,4,0,7"><controls:StateChip State="{Binding State}" Label="{Binding StateLabel}" VerticalAlignment="Center" /><TextBlock x:Name="TimestampLine" Margin="6,0,0,0" VerticalAlignment="Center" Style="{StaticResource CaptionTextStyle}" Foreground="{DynamicResource TextTertiaryBrush}" Visibility="{Binding HasTimestampText, Converter={StaticResource BooleanToVisibility}}"><Run Text="&#x00B7;" /><Run Text=" " /><Run Text="{Binding TimestampText, Mode=OneWay}" /></TextBlock></StackPanel>
```

with:

```xml
      <StackPanel x:Name="StatusLine" Orientation="Horizontal" Margin="0,4,0,7" Visibility="{Binding ShowStatusLine, Converter={StaticResource BooleanToVisibility}}"><controls:StateChip State="{Binding State}" Label="{Binding StateLabel}" VerticalAlignment="Center" /><TextBlock x:Name="TimestampLine" Margin="6,0,0,0" VerticalAlignment="Center" Style="{StaticResource CaptionTextStyle}" Foreground="{DynamicResource TextTertiaryBrush}" Visibility="{Binding HasTimestampText, Converter={StaticResource BooleanToVisibility}}"><Run Text="&#x00B7;" /><Run Text=" " /><Run Text="{Binding TimestampText, Mode=OneWay}" /></TextBlock></StackPanel>
      <!-- Stands in for the status line's height when compact has taken it away, so the header does
           not land directly on the column captions. Six pixels against roughly twenty-five. -->
      <Border x:Name="CompactHeaderSpacer" Height="6" Visibility="{Binding ShowCompactSpacer, Converter={StaticResource BooleanToVisibility}}" />
```

- [ ] **Step 6: Name the column captions so the deviation is testable**

On line 24, the captions `Grid` currently begins:

```xml
      <Grid Margin="0,0,0,3" Visibility="{Binding HasWindows, Converter={StaticResource BooleanToVisibility}}">
```

Change only its opening tag to add the name — everything after `<Grid` stays exactly as it is:

```xml
      <Grid x:Name="ColumnCaptions" Margin="0,0,0,3" Visibility="{Binding HasWindows, Converter={StaticResource BooleanToVisibility}}">
```

Do **not** add a density trigger here. The captions stay at both densities; see Global Constraints.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ViewLoadingTests`
Expected: PASS, including the pre-existing `TheTimestampLineDropsItsSeparatorWhenThereIsNothingToTimestamp` and `TheTimestampLineKeepsItsSeparatorWhenThereIsATimestamp`, which are unaffected because their cards are at standard density.

- [ ] **Step 8: Verify the build is clean**

Run: `dotnet build`
Expected: **0 warnings, 0 errors.**

- [ ] **Step 9: Commit**

```bash
git add src/AiUsageMonitor.App/Views/ProviderCardView.xaml tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs
git commit -m "feat: tighten the provider card at compact density"
```

---

### Task 3: The quota row at compact density

**Files:**
- Modify: `src/AiUsageMonitor.App/Views/QuotaRowView.xaml:1-6` (add the `views` namespace), `:20` (row `Border`)
- Test: `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs`

**Interfaces:**
- Consumes from Task 1: resource keys `QuotaRowPadding` and `QuotaRowPaddingCompact` (both already in `Tokens.xaml`).
- Consumes from Task 2: a `ProviderCardView` ancestor whose `DataContext` is the `ProviderCardViewModel`.
- `QuotaRowViewModel` is **not** modified. It stays a pure projection with no observable state, which is why the row reads density from its ancestor instead.

- [ ] **Step 1: Write the failing test**

Append to `tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs`, inside the class:

```csharp
    /// <summary>
    /// Rows read density from the card above them rather than from their own view model, so that
    /// QuotaRowViewModel stays the pure projection it is documented to be. That makes the ancestor
    /// binding load-bearing, and this is what proves it resolves.
    /// </summary>
    [Fact]
    public void RowsInsideACompactCardTakeTheTighterPadding() => wpf.Invoke(() =>
    {
        Thickness standard = FirstRowPadding(Rendered(Card(ConnectionState.Connected, compact: false)));
        Thickness compact = FirstRowPadding(Rendered(Card(ConnectionState.Connected, compact: true)));

        Assert.Equal(new Thickness(0, 6, 0, 5), standard);
        Assert.Equal(new Thickness(0, 4, 0, 4), compact);
    });

    /// <summary>
    /// A row measured on its own has no card above it, which is how the existing row tests build
    /// one. The ancestor binding then resolves to nothing and the row keeps standard padding -
    /// the safe fallback, and asserted here so it stays deliberate rather than incidental.
    /// </summary>
    [Fact]
    public void ARowWithNoCardAboveItFallsBackToStandardPadding() => wpf.Invoke(() =>
    {
        QuotaRowViewModel row = new(Window(47d, false, true), colorBarsByUsage: true);
        row.Tick(Now);

        QuotaRowView view = ControlLoadingTests.Measured(new QuotaRowView { DataContext = row, Width = 320 });

        Assert.Equal(new Thickness(0, 6, 0, 5), ((Border)view.FindName("RowFrame")).Padding);
    });

    private static Thickness FirstRowPadding(ProviderCardView card) =>
        Descendants(card).OfType<QuotaRowView>()
            .Select(row => ((Border)row.FindName("RowFrame")).Padding)
            .First();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;

            foreach (DependencyObject descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
```

Add `using System.Linq;` to the top of `ViewLoadingTests.cs` if it is not already present.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ViewLoadingTests`
Expected: FAIL — both new tests throw `NullReferenceException`, because the row's `Border` has no `x:Name="RowFrame"` yet and `FindName` returns `null`. Once Step 4 adds the name, `ARowWithNoCardAboveItFallsBackToStandardPadding` passes immediately — it is a regression guard on the fallback, not a driver of new behaviour — while `RowsInsideACompactCardTakeTheTighterPadding` is the one the ancestor binding has to satisfy.

- [ ] **Step 3: Add the views namespace to the row**

In `src/AiUsageMonitor.App/Views/QuotaRowView.xaml`, replace the opening element:

```xml
<UserControl x:Class="AiUsageMonitor.App.Views.QuotaRowView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:AiUsageMonitor.App.Controls"
             AutomationProperties.Name="{Binding AccessibleName}"
             ToolTip="{Binding IdentifierTooltip}">
```

with:

```xml
<UserControl x:Class="AiUsageMonitor.App.Views.QuotaRowView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:AiUsageMonitor.App.Controls"
             xmlns:views="clr-namespace:AiUsageMonitor.App.Views"
             AutomationProperties.Name="{Binding AccessibleName}"
             ToolTip="{Binding IdentifierTooltip}">
```

- [ ] **Step 4: Move the row's padding into a style with an ancestor binding**

Replace line 20:

```xml
  <Border BorderBrush="{DynamicResource WidgetRowDividerBrush}" BorderThickness="0,1,0,0" Padding="{DynamicResource QuotaRowPadding}">
```

with:

```xml
  <Border x:Name="RowFrame" BorderBrush="{DynamicResource WidgetRowDividerBrush}" BorderThickness="0,1,0,0">
    <!-- Density comes from the card above rather than from this row's own view model, which is a
         pure projection of one QuotaWindow and is deliberately kept free of observable state. A row
         rendered with no card above it - which is how the row tests build one - resolves this to
         nothing and keeps the standard padding, which is the right fallback. -->
    <Border.Style>
      <Style TargetType="Border">
        <Setter Property="Padding" Value="{DynamicResource QuotaRowPadding}" />
        <Style.Triggers>
          <DataTrigger Binding="{Binding DataContext.IsCompact, RelativeSource={RelativeSource AncestorType=views:ProviderCardView}}" Value="True">
            <Setter Property="Padding" Value="{DynamicResource QuotaRowPaddingCompact}" />
          </DataTrigger>
        </Style.Triggers>
      </Style>
    </Border.Style>
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ViewLoadingTests`
Expected: PASS, with `ACompactCardIsShorterThanTheSameCardAtStandardDensity` from Task 2 still green and now clearing by a larger margin.

- [ ] **Step 6: Verify the build is clean**

Run: `dotnet build`
Expected: **0 warnings, 0 errors.**

- [ ] **Step 7: Commit**

```bash
git add src/AiUsageMonitor.App/Views/QuotaRowView.xaml tests/AiUsageMonitor.App.Tests/ViewLoadingTests.cs
git commit -m "feat: tighten quota rows at compact density"
```

---

### Task 4: The window chrome at compact density

**Files:**
- Modify: `src/AiUsageMonitor.App/Views/WidgetWindow.xaml:25` (title bar), `:49` (footer), `:67-72` (body padding and card gap)
- Test: `tests/AiUsageMonitor.App.Tests/WidgetWindowTests.cs`

**Interfaces:**
- Consumes from Task 1: `MainViewModel.IsCompact`, `ProviderCardViewModel.IsCompact`, and resource keys `WidgetTitleBarHeight`, `WidgetTitleBarHeightCompact`, `WidgetFooterHeight`, `WidgetFooterHeightCompact`, `WidgetBodyPadding`, `WidgetBodyPaddingCompact`, `ProviderCardGap`, `ProviderCardGapCompact`.
- Produces named elements for tests: `TitleBar`, `Footer`, `ProviderList`.
- The window's `Width` (360) and `MaxHeight` (520) are unchanged by density. Do not touch them.

- [ ] **Step 1: Write the failing tests**

Append to `tests/AiUsageMonitor.App.Tests/WidgetWindowTests.cs`, inside the class:

```csharp
    private static FrameworkElement Content(WidgetWindow window)
    {
        // Measured on the window's content, not the window: a WPF Window's own DesiredSize stays
        // zero until it has an HWND, so measuring the Window itself asserts nothing.
        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(360, 520));
        content.Arrange(new Rect(0, 0, 360, 520));
        content.UpdateLayout();
        return content;
    }

    [Fact]
    public void TheChromeShrinksAtCompactDensity() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default with { Density = WidgetDensity.Compact });

        WidgetWindow window = new(model, Settings(AppSettings.Default with { Density = WidgetDensity.Compact }));
        FrameworkElement content = Content(window);

        Assert.Equal(28d, ((FrameworkElement)window.FindName("TitleBar")).Height);
        Assert.Equal(24d, ((FrameworkElement)window.FindName("Footer")).Height);
        Assert.Equal(new Thickness(8, 0, 8, 2), ((FrameworkElement)window.FindName("ProviderList")).Margin);
        Assert.True(content.DesiredSize.Height > 0);

        model.Dispose();
    });

    [Fact]
    public void TheChromeKeepsItsFullSizeAtStandardDensity() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);

        WidgetWindow window = new(model, Settings(AppSettings.Default));
        Content(window);

        Assert.Equal(32d, ((FrameworkElement)window.FindName("TitleBar")).Height);
        Assert.Equal(26d, ((FrameworkElement)window.FindName("Footer")).Height);
        Assert.Equal(new Thickness(10, 0, 10, 2), ((FrameworkElement)window.FindName("ProviderList")).Margin);

        model.Dispose();
    });

    /// <summary>
    /// The whole point of the feature, asserted end to end rather than piece by piece: the same two
    /// providers, the same data, less height. This is also the assertion that fails if any of the
    /// chrome triggers lost to a local attribute value.
    /// </summary>
    [Fact]
    public void TheWholeWidgetIsShorterAtCompactDensity() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> standardProviders = Providers();
        MainViewModel standardModel = Model(standardProviders, AppSettings.Default);
        WidgetWindow standardWindow = new(standardModel, Settings(AppSettings.Default));
        double standard = Content(standardWindow).DesiredSize.Height;

        AppSettings compactSettings = AppSettings.Default with { Density = WidgetDensity.Compact };
        IReadOnlyList<ProviderDescriptor> compactProviders = Providers();
        MainViewModel compactModel = Model(compactProviders, compactSettings);
        WidgetWindow compactWindow = new(compactModel, Settings(compactSettings));
        double compact = Content(compactWindow).DesiredSize.Height;

        Assert.True(compact < standard, $"compact measured {compact} against standard {standard}");

        standardModel.Dispose();
        compactModel.Dispose();
    });

    [Fact]
    public void ChangingDensityMovesTheChromeWithoutRebuildingTheWindow() => wpf.Invoke(() =>
    {
        IReadOnlyList<ProviderDescriptor> providers = Providers();
        MainViewModel model = Model(providers, AppSettings.Default);
        SettingsService settings = Settings(AppSettings.Default);

        WidgetWindow window = new(model, settings);
        Content(window);

        Assert.Equal(32d, ((FrameworkElement)window.FindName("TitleBar")).Height);

        settings.Update(s => s with { Density = WidgetDensity.Compact });
        Content(window);

        Assert.Equal(28d, ((FrameworkElement)window.FindName("TitleBar")).Height);

        model.Dispose();
    });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~WidgetWindowTests`
Expected: FAIL — `FindName` returns `null` for `TitleBar`, `Footer` and `ProviderList`, and the height comparison finds two equal heights.

- [ ] **Step 3: Make the title bar's height density-aware**

In `src/AiUsageMonitor.App/Views/WidgetWindow.xaml`, replace the title-bar `Grid`'s opening tag:

```xml
      <Grid DockPanel.Dock="Top" Height="32" Background="Transparent"
            MouseLeftButtonDown="TitleBar_MouseLeftButtonDown">
```

with:

```xml
      <!-- Height is a Style setter, not an attribute: WPF ranks a local value above a style trigger,
           so an attribute would beat the compact trigger silently. Same reason in the footer and the
           provider list below. -->
      <Grid x:Name="TitleBar" DockPanel.Dock="Top" Background="Transparent"
            MouseLeftButtonDown="TitleBar_MouseLeftButtonDown">
        <Grid.Style>
          <Style TargetType="Grid">
            <Setter Property="Height" Value="{DynamicResource WidgetTitleBarHeight}" />
            <Style.Triggers>
              <DataTrigger Binding="{Binding IsCompact}" Value="True">
                <Setter Property="Height" Value="{DynamicResource WidgetTitleBarHeightCompact}" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </Grid.Style>
```

- [ ] **Step 4: Make the footer's height density-aware**

Replace the footer `Border`'s opening tag:

```xml
      <Border DockPanel.Dock="Bottom" Height="26" BorderThickness="0,1,0,0"
              BorderBrush="{DynamicResource WidgetWindowStrokeBrush}">
```

with:

```xml
      <Border x:Name="Footer" DockPanel.Dock="Bottom" BorderThickness="0,1,0,0"
              BorderBrush="{DynamicResource WidgetWindowStrokeBrush}">
        <Border.Style>
          <Style TargetType="Border">
            <Setter Property="Height" Value="{DynamicResource WidgetFooterHeight}" />
            <Style.Triggers>
              <DataTrigger Binding="{Binding IsCompact}" Value="True">
                <Setter Property="Height" Value="{DynamicResource WidgetFooterHeightCompact}" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </Border.Style>
```

- [ ] **Step 5: Make the body padding and card gap density-aware**

Replace the provider `ItemsControl` and its container style:

```xml
        <!-- Cards carry an 8px bottom margin each, so the list's own bottom margin is 2 rather
             than 10 - together they make the 10px body padding the design specifies. -->
        <ItemsControl ItemsSource="{Binding Providers}" Margin="10,0,10,2" Focusable="False">
          <ItemsControl.ItemContainerStyle>
            <Style TargetType="ContentPresenter">
              <Setter Property="Margin" Value="0,0,0,8" />
            </Style>
          </ItemsControl.ItemContainerStyle>
```

with:

```xml
        <!-- Cards carry the gap below them, so the list's own bottom margin is 2 rather than the
             full body padding - together they make the body padding the design specifies for each
             density. 2 + 8 = 10 standard, 2 + 6 = 8 compact. -->
        <ItemsControl x:Name="ProviderList" ItemsSource="{Binding Providers}" Focusable="False">
          <ItemsControl.Style>
            <Style TargetType="ItemsControl">
              <Setter Property="Margin" Value="{DynamicResource WidgetBodyPadding}" />
              <Style.Triggers>
                <DataTrigger Binding="{Binding IsCompact}" Value="True">
                  <Setter Property="Margin" Value="{DynamicResource WidgetBodyPaddingCompact}" />
                </DataTrigger>
              </Style.Triggers>
            </Style>
          </ItemsControl.Style>
          <ItemsControl.ItemContainerStyle>
            <!-- This trigger binds straight to IsCompact because a container's DataContext is the
                 card it presents, not the window's view model. -->
            <Style TargetType="ContentPresenter">
              <Setter Property="Margin" Value="{DynamicResource ProviderCardGap}" />
              <Style.Triggers>
                <DataTrigger Binding="{Binding IsCompact}" Value="True">
                  <Setter Property="Margin" Value="{DynamicResource ProviderCardGapCompact}" />
                </DataTrigger>
              </Style.Triggers>
            </Style>
          </ItemsControl.ItemContainerStyle>
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~WidgetWindowTests`
Expected: PASS, including the pre-existing `TheWindowConstructsWithoutXamlErrors`, which still asserts `Width` 360 and `MaxHeight` 520.

- [ ] **Step 7: Run the whole suite and verify the build is clean**

Run: `dotnet test`
Expected: PASS — every project green, with the new tests added.

Run: `dotnet build`
Expected: **0 warnings, 0 errors.**

- [ ] **Step 8: Commit**

```bash
git add src/AiUsageMonitor.App/Views/WidgetWindow.xaml tests/AiUsageMonitor.App.Tests/WidgetWindowTests.cs
git commit -m "feat: tighten the widget chrome at compact density"
```

---

### Task 5: The Density setting

**Files:**
- Modify: `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AiUsageMonitor.App/Views/SettingsWindow.xaml:24` (after the Theme group)
- Test: `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `ChoiceViewModel(string label, int value, string groupName, Func<int> read, Action<int> write)` — already exists, used by `Themes`, `RefreshIntervals` and `StaleThresholds`.
- Consumes: `AppSettings.Density` and `WidgetDensity` from `AiUsageMonitor.Infrastructure.Settings`.
- Produces: `SettingsViewModel.Densities` — `IReadOnlyList<ChoiceViewModel>`, two entries, group name `"density"`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs`, inside the class:

```csharp
    [Fact]
    public void TheDensityChoicesOfferStandardAndCompactAndWriteThrough()
    {
        SettingsViewModel model = Model(out SettingsService service);

        Assert.Equal(new[] { "Standard", "Compact" }, model.Densities.Select(choice => choice.Label));
        Assert.True(model.Densities[0].IsSelected);

        model.Densities[1].IsSelected = true;

        Assert.Equal(WidgetDensity.Compact, service.Current.Density);
    }

    /// <summary>
    /// The choice lists are separate objects, so a settings change made anywhere else has to be
    /// pushed into them by hand. Density joining Themes, RefreshIntervals and StaleThresholds in
    /// that refresh is the difference between the radio following the widget and the two disagreeing.
    /// </summary>
    [Fact]
    public void ADensityChangeMadeElsewhereMovesTheRadio()
    {
        SettingsViewModel model = Model(out SettingsService service);

        service.Update(s => s with { Density = WidgetDensity.Compact });

        Assert.False(model.Densities[0].IsSelected);
        Assert.True(model.Densities[1].IsSelected);
    }
```

Add `using System.Linq;` to the top of `SettingsViewModelTests.cs` if it is not already present.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~SettingsViewModelTests`
Expected: **compile error** — `Densities` does not exist.

- [ ] **Step 3: Add the density choice group**

In `src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs`:

In the constructor, immediately after the `Themes = [...]` assignment, insert:

```csharp
        Densities =
        [
            Density("Standard", WidgetDensity.Normal),
            Density("Compact", WidgetDensity.Compact)
        ];
```

Add the property immediately after `public IReadOnlyList<ChoiceViewModel> Themes { get; }`:

```csharp
    public IReadOnlyList<ChoiceViewModel> Densities { get; }
```

Add the factory immediately after the `Theme` factory method:

```csharp
    private ChoiceViewModel Density(string label, WidgetDensity density) => new(
        label,
        (int)density,
        "density",
        () => (int)_settings.Current.Density,
        value => _settings.Update(s => s with { Density = (WidgetDensity)value }));
```

In `OnSettingsChanged`, replace:

```csharp
        foreach (ChoiceViewModel choice in Themes.Concat(RefreshIntervals).Concat(StaleThresholds))
```

with:

```csharp
        foreach (ChoiceViewModel choice in Themes.Concat(Densities).Concat(RefreshIntervals).Concat(StaleThresholds))
```

- [ ] **Step 4: Add the Density control to the settings window**

In `src/AiUsageMonitor.App/Views/SettingsWindow.xaml`, immediately after the closing `</ItemsControl>` of the Theme group (line 24) and before the `Color bars by usage` `CheckBox`, insert:

```xml
      <TextBlock Text="Density" Margin="0,10,0,0" Style="{StaticResource BodySmallTextStyle}" Foreground="{DynamicResource TextPrimaryBrush}" />
      <TextBlock Text="Compact hides versions, the monogram and the connected chip" TextWrapping="Wrap" Style="{StaticResource CaptionTextStyle}" Foreground="{DynamicResource TextTertiaryBrush}" />
      <ItemsControl ItemsSource="{Binding Densities}" Margin="0,5,0,0" Focusable="False">
        <ItemsControl.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation="Horizontal" /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <RadioButton Style="{StaticResource SettingsRadioButtonStyle}" Content="{Binding Label}" GroupName="{Binding GroupName}" IsChecked="{Binding IsSelected, Mode=TwoWay}" AutomationProperties.Name="{Binding Label}" />
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
```

The helper line is the design's own settings copy. It describes what compact does here and must not claim anything else — compact does not hide the column captions and does not collapse rows.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~AiUsageMonitor.App.Tests`
Expected: PASS, including the pre-existing `TheSettingsWindowRendersInEveryPalette`, which now exercises the new control in Light, Dark and HighContrast.

- [ ] **Step 6: Run the whole suite and verify the build is clean**

Run: `dotnet test`
Expected: PASS across all four test projects.

Run: `dotnet build`
Expected: **0 warnings, 0 errors.**

- [ ] **Step 7: Commit**

```bash
git add src/AiUsageMonitor.App/ViewModels/SettingsViewModel.cs src/AiUsageMonitor.App/Views/SettingsWindow.xaml tests/AiUsageMonitor.App.Tests/SettingsViewModelTests.cs
git commit -m "feat: add the density setting"
```

---

## Task 6: Verify at the screen

Automated tests prove the flags compute and the layout measures. They cannot say whether compact
mode looks right. This task is inspection, and it produces the measured height the spec asks for.

**Files:** none in the repository. The render harness is scratchpad-only and must **not** be
committed.

- [ ] **Step 1: Render both densities across all three palettes**

Build a standalone WPF exe in the scratchpad directory with a `ProjectReference` to
`src/AiUsageMonitor.App/AiUsageMonitor.App.csproj`. Merge the theme dictionaries in their component
form — the short form resolves against the entry assembly, which is now the harness:

```csharp
new ResourceDictionary
{
    Source = new Uri("pack://application:,,,/AiUsageMonitor.App;component/Themes/Tokens.xaml", UriKind.Absolute)
}
```

Merge `Themes/Tokens.xaml`, then `Themes/Controls.xaml`, then one of `Themes/Light.xaml`,
`Themes/Dark.xaml`, `Themes/HighContrast.xaml`. Add them with
`application.Resources.MergedDictionaries.Add(...)` — **never** assign `application.Resources`
wholesale, which breaks template-level `StaticResource` lookups.

Render `ProviderCardView` at `Width = 340` for each combination of:
- density: standard, compact
- state: Connected, Error, Stale, and a card with no windows

`RenderTargetBitmap` at 3x, save PNGs, and read them back. Confirm by eye that compact keeps the
column captions, keeps the tier badge, keeps every row, and shows the state chip on the Error and
Stale cards but not the Connected one.

- [ ] **Step 2: Measure the real compact height**

Launch the built widget, switch to Compact in settings, and record the actual window height at 100%
scaling. Compare it against the design's 326px target and account for the retained column captions.

Record the measured number in the SDD ledger and in the final report. **Do not assert 326.**

- [ ] **Step 3: Check the acceptance list**

Walk `docs/specs/2026-08-12-compact-mode-design.md` §9 item by item and confirm each one at the
screen, not from the code.

- [ ] **Step 4: Confirm nothing scratchpad leaked into the repository**

Run: `git status --short`
Expected: clean. The harness lives outside the repository; nothing from it is committed.

---

## Notes for the reviewer

Three things in this plan are easy to approve on paper and wrong in the running application. All
three are guarded by a test, and a review should confirm the guard is still there:

1. **The precedence trap.** Every `Padding`, `Height` and `Margin` that compact overrides must be a
   `Style` `Setter`, never an element attribute. `ACompactCardIsShorterThanTheSameCardAtStandardDensity`
   and `TheWholeWidgetIsShorterAtCompactDensity` are what fail if one slips back.
2. **The column captions.** They stay at compact density, against the design render, because PRD §16
   needs them. `TheColumnCaptionsSurviveCompactDensity` guards it.
3. **The status line.** It goes only when the provider is Connected.
   `ACompactCardWithAProblemKeepsItsStatusLine` and
   `ACardThatIsNotConnectedKeepsItsStatusLineEvenWhenCompact` guard it from both layers.

Sacrifice level 6 — collapsing rows beyond three into "N more windows — expand" — is **deliberately
out of scope**, per the approved spec §2. A review must not treat its absence as a gap.
