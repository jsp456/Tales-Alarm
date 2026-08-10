# Compact Timer Strip Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the card-based compact view with a fixed `520×56` strip without native window chrome that shows both timers and preserves detailed-view, drag, close-to-tray, and persistence behavior.

**Architecture:** Keep the existing `IsCompactView` state and `ToggleCompactViewCommand`. XAML owns the one-row timer presentation, while `MainWindow` code-behind applies window geometry explicitly because a shown WPF `Window` can hold local size values that outrank style triggers. A real STA WPF integration test exercises the rendered window and interactions instead of parsing XAML source text.

**Tech Stack:** C# 14, .NET 10, WPF/XAML, xUnit

## Global Constraints

- Compact mode is a fixed `520×56` WPF device-independent-pixel window.
- Compact mode uses `WindowStyle=None` and `ResizeMode=NoResize`.
- A one-DIP content outline may remain to distinguish the strip edge; “borderless” refers to native window chrome.
- Show only timer number, remaining time, status, `상세`, and `×`; do not show the app title, cards, or timer controls.
- `999:59:59` and `일시정지` must fit without clipping or overlap.
- Dragging either timer-information area moves the window.
- `×` follows the existing close path and hides the app to the tray rather than terminating it.
- Returning to detailed mode restores the existing title bar, resizable behavior, `1100×760` default size, and existing minimum size.
- Do not add ViewModel state, settings fields, dependencies, topmost behavior, snapping, geometry persistence, custom sizing, or compact timer controls.

## Approved Test Amendment

The user approved replacing the original source-parsing XAML test with a rendered WPF integration test. The integration test starts a test `Application` on an STA dispatcher, loads a real `MainWindow`, and observes rendered values and routed interactions. This catches consumer-visible breakage while avoiding a test that only asserts source text.

---

### Task 1: Build and verify the compact timer strip

**Files:**
- Create: `tests/TalesAlarm.Tests/Views/MainWindowTests.cs`
- Modify: `src/TalesAlarm/MainWindow.xaml`
- Modify: `src/TalesAlarm/MainWindow.xaml.cs`
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-08-10-compact-strip-design.md`

**Interfaces:**
- Consumes: `MainViewModel.IsCompactView`, `MainViewModel.ToggleCompactViewCommand`, `TimerViewModel.TimerIndex`, `TimerViewModel.DisplayTime`, `TimerViewModel.StatusText`, and `MainWindow.OnClosing`.
- Produces: XAML elements `CompactView`, `CompactTimerTemplate`, and `CompactDragSurface`; handlers `OnCompactViewIsVisibleChanged`, `OnCompactDragDelta`, and `OnCompactCloseClick`.

- [x] **Step 1: Write the failing real-window integration test**

Create one STA WPF scenario that loads the real `MainWindow` and asserts these literal outcomes:

```csharp
Assert.Equal(520, currentWindow.Width);
Assert.Equal(56, currentWindow.Height);
Assert.Equal(WindowStyle.None, currentWindow.WindowStyle);
Assert.Equal(ResizeMode.NoResize, currentWindow.ResizeMode);
Assert.Contains("1", timerTexts);
Assert.Contains("999:59:59", timerTexts);
Assert.Contains("일시정지", timerTexts);
Assert.InRange(timerControl.DesiredSize.Width, 1, 204);
```

Raise `Thumb.DragDeltaEvent`, and invoke the actual `상세` and `×` buttons through WPF UI Automation. Assert the changed window coordinates, command-driven detailed transition, existing `RequestHide` path, and preserved compact geometry after hide and re-show. Also assert `1100×760`, `SingleBorderWindow`, and `CanResize` in detailed mode; finally switch from `Maximized` to compact and assert `WindowState.Normal`.

- [x] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore --filter "FullyQualifiedName~MainWindowTests"
```

Observed failure before implementation:

```text
Expected: 520
Actual:   1100
```

The value-source investigation showed `Width=1100` was a local WPF value, proving a style-trigger-only size change was not a reliable implementation boundary.

- [x] **Step 3: Implement explicit compact and detailed window geometry**

`MainWindow.xaml.cs` applies geometry from the compact root's real visibility:

```csharp
private void OnCompactViewIsVisibleChanged(
    object sender,
    DependencyPropertyChangedEventArgs eventArgs)
{
    if (CompactView.Visibility == Visibility.Visible)
    {
        ApplyCompactWindowLayout();
    }
    else
    {
        ApplyDetailedWindowLayout();
    }
}

private void ApplyCompactWindowLayout()
{
    if (WindowState != WindowState.Normal)
    {
        WindowState = WindowState.Normal;
    }

    MinWidth = 520;
    MinHeight = 56;
    Width = 520;
    Height = 56;
    ResizeMode = ResizeMode.NoResize;
    WindowStyle = WindowStyle.None;
}

private void ApplyDetailedWindowLayout()
{
    WindowStyle = WindowStyle.SingleBorderWindow;
    ResizeMode = ResizeMode.CanResize;
    MinWidth = 1040;
    MinHeight = 720;
    Width = 1100;
    Height = 760;
}
```

The drag and close handlers remain window-level operations:

```csharp
private void OnCompactDragDelta(object sender, DragDeltaEventArgs eventArgs)
{
    Left += eventArgs.HorizontalChange;
    Top += eventArgs.VerticalChange;
}

private void OnCompactCloseClick(object sender, RoutedEventArgs eventArgs)
{
    Close();
}
```

- [x] **Step 4: Replace compact cards with the single-row XAML strip**

Use a three-column `CompactTimerTemplate` for number, `22px` Consolas time, and a small status pill. Place two template instances in equal star columns, separated by a one-pixel divider. Overlay a `Thumb` with an explicitly transparent custom template across the timer columns so the platform theme cannot cover the information, then place `상세` plus `×` buttons in fixed trailing columns. Override the application button minimum height with a compact-only `32px` button style.

- [x] **Step 5: Verify the focused test and WPF build GREEN**

Commands:

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore --filter "FullyQualifiedName~MainWindowTests"
dotnet build src/TalesAlarm/TalesAlarm.csproj -c Release --no-restore
```

Observed: one focused test passed; WPF build completed with zero warnings and zero errors. The test also renders the strip to a bitmap and requires visible dark pixels in both timer regions, preventing a drag-surface theme from covering the information.

- [x] **Step 6: Update user-facing documentation**

README usage step 6 describes the taskbar-height strip, timer-area dragging, `상세`, `×`, tray hiding, and persisted view mode. The design spec records the approved real-window integration test.

- [x] **Step 7: Run full release and publish verification**

```powershell
dotnet test TalesAlarm.sln -c Release --no-restore
dotnet publish src/TalesAlarm/TalesAlarm.csproj -p:PublishProfile=win-x64 --no-restore
powershell -ExecutionPolicy Bypass -File tests/Verify-PublishArtifact.ps1 -PublishDirectory artifacts/TalesAlarm-win-x64
```

Observed: all `107` tests passed, publish succeeded, and artifact verification confirmed a working `173,196,361` byte single-file `artifacts/TalesAlarm-win-x64/TalesAlarm.exe` with no loose runtime or asset files.

- [x] **Step 8: Inspect isolated compact UI and published launch behavior**

The Release single-file EXE was launched successfully by the artifact smoke test. To avoid mutating the user's real LocalAppData settings, the identical Debug UI code was launched with the supported isolated `--data-root` option and captured at `96` DPI. Verify:

1. `간단 보기` changes the window to a single `520×56` row with no native title bar.
2. Both maximum-length time and status values remain readable without overlap.
3. Dragging either timer area moves the window; `상세` and `×` remain clickable.
4. `×` hides to tray and the tray icon shows the window again.
5. A maximized detailed window switches to a normal compact strip.
6. Returning to detailed mode restores its title bar, resizable layout, and controls.

Observed: the captured compact window measured exactly `520×56`; both timers, `999:59:59`, both status pills, `상세`, and `×` were visible without overlap. Automated WPF interaction coverage verified dragging, tray hiding, maximized-to-compact normalization, and detailed restoration.

- [x] **Step 9: Commit the tested feature**

```powershell
git add src/TalesAlarm/MainWindow.xaml src/TalesAlarm/MainWindow.xaml.cs tests/TalesAlarm.Tests/Views/MainWindowTests.cs README.md docs/superpowers/specs/2026-08-10-compact-strip-design.md docs/superpowers/plans/2026-08-10-compact-strip.md
git commit -m "feat: shrink compact view to timer strip"
```

Committed as `9f22a81`.

### Task 2: Harden tray-hide lifecycle coverage after review

- [x] **Step 10: Add real button invocation and a hide/re-show regression test**

The test command now toggles `IsCompactView`, and `ButtonAutomationPeer` with `IInvokeProvider` exercises the buttons' real click path. The first focused run reproduced the lifecycle bug after `×` hid the window:

```text
Expected: 520
Actual:   1100
```

- [x] **Step 11: Preserve compact geometry while the parent window is hidden**

`IsVisibleChanged` also fires when the parent window hides. The handler now checks `CompactView.Visibility`, which represents the bound compact-mode state, rather than treating inherited `IsVisible=false` as a request for detailed mode. The focused integration test passes through `상세`, `×`, hide, and re-show.

- [x] **Step 12: Repeat full verification and prepare the review fix commit**

Observed: the focused lifecycle test passed, all `107` Release tests passed, the single-file publish verifier passed for the `173,196,361` byte EXE, and follow-up review reported no remaining Critical, Important, or Minor findings.
