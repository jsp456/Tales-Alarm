# Compact View Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persistent information-only compact view for both Tales Alarm timers.

**Architecture:** Extend the existing additive JSON settings record with one optional Boolean and let `MainViewModel` own the persisted view state. Keep one `MainWindow` and switch between detailed and compact XAML layouts so timer instances, hotkeys, tray behavior, and audio behavior remain unchanged.

**Tech Stack:** C# 14, .NET 10, WPF/XAML, xUnit, System.Text.Json

## Global Constraints

- Compact view shows only timer number, remaining `HH:MM:SS`, status, and the control needed to return to detailed view.
- Detailed view remains functionally unchanged.
- Existing settings JSON without `UseCompactView` must load as detailed view.
- Do not add a second window, timer instance, dependency, or customizable geometry.
- Keep verification proportional: targeted tests during TDD, one full Release suite, one publish smoke test.

---

### Task 1: Persist and toggle compact-view state

**Files:**
- Modify: `src/TalesAlarm/Configuration/AppSettings.cs`
- Modify: `src/TalesAlarm/ViewModels/MainViewModel.cs`
- Modify: `tests/TalesAlarm.Tests/Configuration/SettingsServiceTests.cs`
- Modify: `tests/TalesAlarm.Tests/ViewModels/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `ISettingsService.LoadAsync` and `ISettingsService.SaveAsync`
- Produces: `AppSettings.UseCompactView`, `MainViewModel.IsCompactView`, and `MainViewModel.ToggleCompactViewCommand`

- [ ] **Step 1: Write the two focused failing tests**

```csharp
[Fact]
public async Task Load_LegacySettingsWithoutCompactView_UsesDetailedView()
{
    using var temp = new TemporaryDirectory();
    var paths = new AppPaths(temp.Path);
    var json = JsonNode.Parse(JsonSerializer.Serialize(AppSettings.CreateDefault()))!.AsObject();
    json.Remove("UseCompactView");
    await File.WriteAllTextAsync(paths.SettingsFile, json.ToJsonString());

    var result = await new SettingsService(paths, TimeProvider.System)
        .LoadAsync(CancellationToken.None);

    Assert.False(result.Settings.UseCompactView);
}

[Fact]
public async Task ToggleCompactView_FromSavedCompactMode_UpdatesAndPersistsDetailedMode()
{
    var settings = AppSettings.CreateDefault() with { UseCompactView = true };
    using var fixture = Fixture.Create(settings);
    await fixture.ViewModel.InitializeAsync();

    await ((AsyncRelayCommand)fixture.ViewModel.ToggleCompactViewCommand).ExecuteAsync();

    Assert.False(fixture.ViewModel.IsCompactView);
    Assert.False(Assert.Single(fixture.Settings.SavedSettings).UseCompactView);
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test TalesAlarm.sln -c Release --filter "FullyQualifiedName~Load_LegacySettingsWithoutCompactView|FullyQualifiedName~ToggleCompactView_FromSavedCompactMode"`

Expected: compilation fails because `UseCompactView`, `IsCompactView`, and `ToggleCompactViewCommand` do not exist.

- [ ] **Step 3: Add the minimal settings and ViewModel implementation**

```csharp
public sealed record AppSettings(
    int SchemaVersion,
    TimerSettings Timer1,
    TimerSettings Timer2,
    AlarmSettings Alarm,
    bool UseCompactView = false);
```

```csharp
public bool IsCompactView
{
    get => isCompactView;
    private set => SetProperty(ref isCompactView, value);
}

private async Task ToggleCompactViewAsync()
{
    IsCompactView = !IsCompactView;
    var candidate = savedSettings with { UseCompactView = IsCompactView };
    try
    {
        await settingsService.SaveAsync(candidate, CancellationToken.None).ConfigureAwait(true);
        savedSettings = candidate;
        ErrorMessage = null;
    }
    catch (Exception exception)
    {
        ErrorMessage = $"보기 모드를 저장하지 못했습니다: {exception.Message}";
    }
}
```

Initialize `IsCompactView` from loaded settings, construct `ToggleCompactViewCommand` with `AsyncRelayCommand`, and create applied timer/audio settings with `savedSettings with { ... }` so the UI preference is preserved.

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run: `dotnet test TalesAlarm.sln -c Release --filter "FullyQualifiedName~Load_LegacySettingsWithoutCompactView|FullyQualifiedName~ToggleCompactView_FromSavedCompactMode"`

Expected: 2 passed, 0 failed.

- [ ] **Step 5: Commit the state behavior**

```powershell
git add src/TalesAlarm/Configuration/AppSettings.cs src/TalesAlarm/ViewModels/MainViewModel.cs tests/TalesAlarm.Tests/Configuration/SettingsServiceTests.cs tests/TalesAlarm.Tests/ViewModels/MainViewModelTests.cs
git commit -m "feat: persist compact view preference"
```

### Task 2: Add the compact WPF layout and publish

**Files:**
- Modify: `src/TalesAlarm/MainWindow.xaml`
- Modify: `tests/TalesAlarm.Tests/Helpers/ProjectFiles.cs`
- Create: `tests/TalesAlarm.Tests/Views/MainWindowLayoutTests.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `MainViewModel.IsCompactView`, `MainViewModel.ToggleCompactViewCommand`, and each timer's `TimerIndex`, `DisplayTime`, and `StatusText`
- Produces: named XAML roots `DetailedView` and `CompactView`, plus detailed/compact window sizing triggers

- [ ] **Step 1: Write the failing compact-layout structure test**

```csharp
[Fact]
public void MainWindow_DefinesDetailedAndCompactViewsWithModeControls()
{
    var xaml = File.ReadAllText(ProjectFiles.MainWindowXaml);

    Assert.Contains("x:Name=\"DetailedView\"", xaml);
    Assert.Contains("x:Name=\"CompactView\"", xaml);
    Assert.Contains("Content=\"간단 보기\"", xaml);
    Assert.Contains("Content=\"상세 보기\"", xaml);
    Assert.Contains("Text=\"{Binding DisplayTime}\"", xaml);
    Assert.Contains("Text=\"{Binding StatusText}\"", xaml);
}
```

Add `ProjectFiles.MainWindowXaml` as `Path.Combine(RepositoryRoot, "src", "TalesAlarm", "MainWindow.xaml")`.

- [ ] **Step 2: Run the layout test and verify RED**

Run: `dotnet test TalesAlarm.sln -c Release --filter "FullyQualifiedName~MainWindow_DefinesDetailedAndCompactViewsWithModeControls"`

Expected: FAIL because the named view roots and mode buttons are absent.

- [ ] **Step 3: Implement the two XAML layouts**

Add `Window.Style` setters for detailed size `1100x760` / minimum `1040x720`, with an `IsCompactView=True` data trigger for size `620x260` / minimum `560x230`. Wrap the existing `ScrollViewer` as `DetailedView`, collapse it when compact, and add a compact root that becomes visible in the inverse state.

```xml
<DataTemplate x:Key="CompactTimerTemplate">
    <Border Style="{StaticResource CardBorder}" Margin="6">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>
            <Grid>
                <TextBlock Text="{Binding TimerIndex, StringFormat='타이머 {0}'}" />
                <TextBlock HorizontalAlignment="Right" Text="{Binding StatusText}" />
            </Grid>
            <TextBlock Grid.Row="1"
                       HorizontalAlignment="Center"
                       VerticalAlignment="Center"
                       FontFamily="Consolas"
                       FontSize="34"
                       Text="{Binding DisplayTime}" />
        </Grid>
    </Border>
</DataTemplate>
```

Bind both `간단 보기` and `상세 보기` buttons to `ToggleCompactViewCommand`. Keep settings fields and timer operation buttons only under `DetailedView`.

- [ ] **Step 4: Run the layout test and verify GREEN**

Run: `dotnet test TalesAlarm.sln -c Release --filter "FullyQualifiedName~MainWindow_DefinesDetailedAndCompactViewsWithModeControls"`

Expected: 1 passed, 0 failed.

- [ ] **Step 5: Document, run one full suite, and publish**

Add a short README note describing the two view buttons and persistence. Then run:

```powershell
dotnet test TalesAlarm.sln -c Release
dotnet publish src/TalesAlarm/TalesAlarm.csproj -p:PublishProfile=win-x64
powershell -ExecutionPolicy Bypass -File tests/Verify-PublishArtifact.ps1 -PublishDirectory artifacts/TalesAlarm-win-x64
```

Expected: all tests pass, publish succeeds, and the single EXE smoke test passes.

- [ ] **Step 6: Commit the UI and documentation**

```powershell
git add src/TalesAlarm/MainWindow.xaml tests/TalesAlarm.Tests/Helpers/ProjectFiles.cs tests/TalesAlarm.Tests/Views/MainWindowLayoutTests.cs README.md
git commit -m "feat: add compact timer view"
```
