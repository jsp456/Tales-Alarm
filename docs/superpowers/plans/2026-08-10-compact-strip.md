# Compact Timer Strip Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current card-based compact view with a fixed `520×56` borderless strip that shows both timers and preserves detailed-view, drag, close-to-tray, and persistence behavior.

**Architecture:** Keep the existing `IsCompactView` state and `ToggleCompactViewCommand`. Change only the `MainWindow` presentation and window-level interactions: XAML owns the exact compact geometry and one-row layout, while code-behind handles native window dragging, restoring a maximized window to normal, and routing the custom close button through the existing `OnClosing` behavior.

**Tech Stack:** C# 14, .NET 10, WPF/XAML, xUnit, LINQ to XML, PowerShell publish verification

## Global Constraints

- Compact mode is a fixed `520×56` WPF device-independent-pixel window.
- Compact mode uses `WindowStyle=None` and `ResizeMode=NoResize`.
- Show only timer number, remaining time, status, `상세`, and `×`; do not show the app title, cards, or timer controls.
- `999:59:59` and `일시정지` must fit without clipping or overlap.
- Dragging any non-button strip area moves the window.
- `×` follows the existing close path and hides the app to the tray rather than terminating it.
- Returning to detailed mode restores the existing title bar, resizable behavior, `1100×760` default size, and existing minimum size.
- Do not add ViewModel state, settings fields, dependencies, topmost behavior, snapping, geometry persistence, custom sizing, or compact timer controls.

---

### Task 1: Build and verify the compact timer strip

**Files:**
- Modify: `tests/TalesAlarm.Tests/Helpers/ProjectFiles.cs`
- Create: `tests/TalesAlarm.Tests/Views/MainWindowXamlTests.cs`
- Modify: `src/TalesAlarm/MainWindow.xaml:13-27,160-190,331-376`
- Modify: `src/TalesAlarm/MainWindow.xaml.cs:1-62`
- Modify: `README.md:17`

**Interfaces:**
- Consumes: `MainViewModel.IsCompactView`, `MainViewModel.ToggleCompactViewCommand`, `TimerViewModel.TimerIndex`, `TimerViewModel.DisplayTime`, `TimerViewModel.StatusText`, and the existing `MainWindow.OnClosing` handler.
- Produces: XAML elements named `CompactView` and `CompactTimerTemplate`; event handlers `OnCompactViewMouseLeftButtonDown(object, MouseButtonEventArgs)`, `OnCompactViewIsVisibleChanged(object, DependencyPropertyChangedEventArgs)`, and `OnCompactCloseClick(object, RoutedEventArgs)`.

- [ ] **Step 1: Write the failing XAML contract test**

Add this path next to the existing asset paths in `ProjectFiles.cs`:

```csharp
public static string MainWindowXaml => Path.Combine(
    RepositoryRoot,
    "src",
    "TalesAlarm",
    "MainWindow.xaml");
```

Create `tests/TalesAlarm.Tests/Views/MainWindowXamlTests.cs`:

```csharp
using System.Xml.Linq;
using TalesAlarm.Tests.Helpers;

namespace TalesAlarm.Tests.Views;

public sealed class MainWindowXamlTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    // Break caught: compact mode regrows into a card window or loses its custom window controls.
    [Fact]
    public void CompactView_IsFixedSingleRowInformationStrip()
    {
        var document = XDocument.Load(ProjectFiles.MainWindowXaml);
        var compactWindowTrigger = document.Root!
            .Element(Presentation + "Window.Style")!
            .Descendants(Presentation + "DataTrigger")
            .Single(trigger => (string?)trigger.Attribute("Binding") == "{Binding IsCompactView}"
                && (string?)trigger.Attribute("Value") == "True");
        var setters = compactWindowTrigger
            .Elements(Presentation + "Setter")
            .ToDictionary(
                setter => (string)setter.Attribute("Property")!,
                setter => (string)setter.Attribute("Value")!);

        Assert.Equal("520", setters["Width"]);
        Assert.Equal("56", setters["Height"]);
        Assert.Equal("520", setters["MinWidth"]);
        Assert.Equal("56", setters["MinHeight"]);
        Assert.Equal("None", setters["WindowStyle"]);
        Assert.Equal("NoResize", setters["ResizeMode"]);

        var compactView = document
            .Descendants()
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "CompactView");
        Assert.Equal(
            "OnCompactViewMouseLeftButtonDown",
            (string?)compactView.Attribute("MouseLeftButtonDown"));
        Assert.Equal(
            "OnCompactViewIsVisibleChanged",
            (string?)compactView.Attribute("IsVisibleChanged"));
        Assert.Empty(compactView.Descendants(Presentation + "RowDefinition"));
        Assert.DoesNotContain(
            compactView.Descendants(Presentation + "TextBlock"),
            element => (string?)element.Attribute("Text") == "Tales Alarm");

        var timerTemplate = document
            .Descendants(Presentation + "DataTemplate")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == "CompactTimerTemplate");
        Assert.Contains(
            timerTemplate.Descendants(Presentation + "TextBlock"),
            element => (string?)element.Attribute("Text") == "{Binding TimerIndex}");
        Assert.Contains(
            timerTemplate.Descendants(Presentation + "TextBlock"),
            element => (string?)element.Attribute("Text") == "{Binding DisplayTime}");
        Assert.Contains(
            timerTemplate.Descendants(Presentation + "TextBlock"),
            element => (string?)element.Attribute("Text") == "{Binding StatusText}");

        var compactButtons = compactView.Descendants(Presentation + "Button").ToArray();
        Assert.Contains(
            compactButtons,
            button => (string?)button.Attribute("Content") == "상세"
                && (string?)button.Attribute("Command") == "{Binding ToggleCompactViewCommand}");
        Assert.Contains(
            compactButtons,
            button => (string?)button.Attribute("Content") == "×"
                && (string?)button.Attribute("Click") == "OnCompactCloseClick");
    }
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test TalesAlarm.sln -c Release --filter "FullyQualifiedName~MainWindowXamlTests"
```

Expected: one failed test. The first relevant assertion reports `Expected: 520` and `Actual: 620` because the current compact trigger still uses the card-window dimensions.

- [ ] **Step 3: Replace the compact window style and resources**

In `MainWindow.xaml`, keep the detailed setters and replace the compact trigger with:

```xml
<DataTrigger Binding="{Binding IsCompactView}" Value="True">
    <Setter Property="Width" Value="520" />
    <Setter Property="Height" Value="56" />
    <Setter Property="MinWidth" Value="520" />
    <Setter Property="MinHeight" Value="56" />
    <Setter Property="WindowStyle" Value="None" />
    <Setter Property="ResizeMode" Value="NoResize" />
</DataTrigger>
```

Add a compact-only button style inside `Window.Resources`, overriding the application-wide `MinHeight=44` and large padding:

```xml
<Style x:Key="CompactButton"
       TargetType="{x:Type Button}"
       BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="MinWidth" Value="0" />
    <Setter Property="MinHeight" Value="0" />
    <Setter Property="Height" Value="32" />
    <Setter Property="Margin" Value="3,0,0,0" />
    <Setter Property="Padding" Value="8,0" />
    <Setter Property="FontSize" Value="12" />
</Style>
```

Replace `CompactTimerTemplate` with the one-row timer presentation:

```xml
<DataTemplate x:Key="CompactTimerTemplate">
    <Grid VerticalAlignment="Center">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <TextBlock VerticalAlignment="Center"
                   FontSize="11"
                   FontWeight="Bold"
                   Foreground="#475569"
                   Text="{Binding TimerIndex}" />
        <TextBlock Grid.Column="1"
                   Margin="6,0,0,0"
                   VerticalAlignment="Center"
                   FontFamily="Consolas"
                   FontSize="22"
                   FontWeight="SemiBold"
                   Foreground="#0F172A"
                   Text="{Binding DisplayTime}" />
        <Border Grid.Column="2"
                Margin="6,0,0,0"
                Padding="5,2"
                Background="#DBEAFE"
                CornerRadius="5">
            <TextBlock VerticalAlignment="Center"
                       FontSize="11"
                       FontWeight="SemiBold"
                       Foreground="#1E40AF"
                       Text="{Binding StatusText}" />
        </Border>
    </Grid>
</DataTemplate>
```

Replace the existing `CompactView` grid with this single-row border. Its timer columns share available width; the fixed controls leave enough room for `999:59:59` plus `일시정지` in both timer columns:

```xml
<Border x:Name="CompactView"
        Background="White"
        BorderBrush="#CBD5E1"
        BorderThickness="1"
        MouseLeftButtonDown="OnCompactViewMouseLeftButtonDown"
        IsVisibleChanged="OnCompactViewIsVisibleChanged">
    <Border.Style>
        <Style TargetType="{x:Type Border}">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsCompactView}" Value="True">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <Grid Margin="8,0,6,0">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <ContentControl Grid.Column="0"
                        HorizontalAlignment="Center"
                        VerticalAlignment="Center"
                        Content="{Binding Timer1}"
                        ContentTemplate="{StaticResource CompactTimerTemplate}" />
        <Border Grid.Column="1"
                Width="1"
                Height="28"
                Margin="6,0"
                VerticalAlignment="Center"
                Background="#CBD5E1" />
        <ContentControl Grid.Column="2"
                        HorizontalAlignment="Center"
                        VerticalAlignment="Center"
                        Content="{Binding Timer2}"
                        ContentTemplate="{StaticResource CompactTimerTemplate}" />
        <Button Grid.Column="3"
                MinWidth="46"
                Style="{StaticResource CompactButton}"
                Command="{Binding ToggleCompactViewCommand}"
                Content="상세" />
        <Button Grid.Column="4"
                Width="32"
                Padding="0"
                Background="Transparent"
                BorderThickness="0"
                FontSize="18"
                Foreground="#64748B"
                Style="{StaticResource CompactButton}"
                ToolTip="트레이로 숨기기"
                Click="OnCompactCloseClick"
                Content="×" />
    </Grid>
</Border>
```

- [ ] **Step 4: Add the three window interaction handlers**

Add the input namespace to `MainWindow.xaml.cs`:

```csharp
using System.Windows.Input;
```

Add these handlers before `OnClosing`:

```csharp
private void OnCompactViewMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
{
    if (eventArgs.ChangedButton == MouseButton.Left
        && eventArgs.LeftButton == MouseButtonState.Pressed)
    {
        DragMove();
    }
}

private void OnCompactViewIsVisibleChanged(
    object sender,
    DependencyPropertyChangedEventArgs eventArgs)
{
    if (eventArgs.NewValue is true && WindowState != WindowState.Normal)
    {
        WindowState = WindowState.Normal;
    }
}

private void OnCompactCloseClick(object sender, RoutedEventArgs eventArgs)
{
    Close();
}
```

`Close()` intentionally reaches the existing `OnClosing` handler, which cancels normal closure and hides the window. Do not call `Hide()` directly and do not change `AllowClose`.

- [ ] **Step 5: Run the focused test and WPF build and verify GREEN**

Run:

```powershell
dotnet test TalesAlarm.sln -c Release --filter "FullyQualifiedName~MainWindowXamlTests"
dotnet build src/TalesAlarm/TalesAlarm.csproj -c Release --no-restore
```

Expected: one focused test passes, and the WPF build succeeds with no XAML parse, event-handler, or C# compilation errors.

- [ ] **Step 6: Update the user-facing compact-view description**

Replace README usage step 6 with:

```markdown
6. **간단 보기**를 누르면 작업표시줄 높이의 한 줄 창에서 두 타이머의 남은 시간과 상태만 확인할 수 있습니다. 버튼이 아닌 영역을 끌어 이동하거나 **상세**로 돌아갈 수 있으며, **×**는 창을 트레이로 숨깁니다. 마지막 보기 모드는 다음 실행에도 유지됩니다.
```

- [ ] **Step 7: Run the full release and publish verification**

Run:

```powershell
dotnet test TalesAlarm.sln -c Release
dotnet publish src/TalesAlarm/TalesAlarm.csproj -p:PublishProfile=win-x64
powershell -ExecutionPolicy Bypass -File tests/Verify-PublishArtifact.ps1 -PublishDirectory artifacts/TalesAlarm-win-x64
```

Expected: the full test suite passes, publish succeeds, and artifact verification confirms a working single-file `artifacts/TalesAlarm-win-x64/TalesAlarm.exe` with no loose runtime or asset files.

- [ ] **Step 8: Inspect the published window behavior**

Run `artifacts/TalesAlarm-win-x64/TalesAlarm.exe` and verify all of the following before closing it from the tray menu:

1. `간단 보기` changes the window to a single `520×56` row with no native title bar.
2. Timer numbers, times, and every status (`대기`, `실행 중`, `일시정지`, `완료`) remain readable without overlap; also inspect `999:59:59` by configuring a timer to 999 hours.
3. Dragging either timer-information area moves the window; clicking `상세` does not start a drag.
4. `×` hides the window and the tray icon can show it again.
5. Maximizing detailed mode before switching still yields a normal `520×56` compact window.
6. Returning to detailed mode restores the existing title bar, resizable layout, and all detailed controls.

- [ ] **Step 9: Commit the tested feature**

```powershell
git add tests/TalesAlarm.Tests/Helpers/ProjectFiles.cs tests/TalesAlarm.Tests/Views/MainWindowXamlTests.cs src/TalesAlarm/MainWindow.xaml src/TalesAlarm/MainWindow.xaml.cs README.md
git commit -m "feat: shrink compact view to timer strip"
```
