using Avalonia.Controls;
using Avalonia.Threading;
using Iciclecreek.Terminal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Desktop.Services;
using SharpNinja.AiUnit.Scenarios;

namespace SharpNinja.AiUnit.Desktop;

public partial class MainWindow : Window
{
    private bool _terminalRunning;

    // For multi-scenario navigation + verdict save (per FR-AIUNITDESKTOP-009)
    private IReadOnlyList<WireframeScenario> _scenarios = [];
    private int _currentIndex = -1;
    private WireframeScenarioLoader? _loader;
    private string _scenariosFolder = string.Empty;

    // Tracks the last MarkupImageViewer the user interacted with (via mouse).
    // Zoom/pan view-state changes are mirrored to the peer viewer for comparison alignment.
    // Mode changes (Pan, Highlighter, etc.) are still applied to both for convenience.
    private Controls.MarkupImageViewer? _activeViewer;
    private bool _syncingImageViewState;

    private static readonly (string Label, Avalonia.Media.Stretch Stretch)[] _scaleOptions = new[]
    {
        ("None (1:1)", Avalonia.Media.Stretch.None),
        ("Stretch (distort)", Avalonia.Media.Stretch.Fill),
        ("Fit (uniform)", Avalonia.Media.Stretch.Uniform),
        ("Fill (cover)", Avalonia.Media.Stretch.UniformToFill),
    };

    // Internal for Byrd TDD tests (AC_UI_*). Allows tests to validate mapping without making public surface.
    internal static Avalonia.Media.Stretch MapScaleLabelToStretchForTest(string label)
    {
        var match = _scaleOptions.FirstOrDefault(o => o.Label == label);
        return match != default ? match.Stretch : Avalonia.Media.Stretch.Uniform;
    }

    // Internal for Byrd TDD tests (AC_PROMPT_*). Produces the exact single-line (no \r\n) text that will be pushed to terminal stdio/pty on Send prompt.
    // The real SendReviewPromptClicked uses this so tests can assert the prompt shape without UI.
    internal static string BuildReviewPromptTextForTest(WireframeScenario s)
    {
        if (s == null) return string.Empty;
        string Clean(string? v) => (v ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        var id = Clean(s.ScreenId);
        var title = Clean(s.Title);
        var yamlPath = Clean(s.SourcePath);
        return $"# Navigated to new wireframe-screenshot pair: {id} ({title}). Review the scenario in: {yamlPath}";
    }

    // Internal for Byrd TDD tests (AC-FOCUS-*). Requests focus on the terminal control BEFORE pasting the prompt.
    // (Updated for "go back to pasting the prompt, but set focus to the terminal before pasting".)
    // Direct Focus() (no defer post) so that focus is active when the subsequent paste/send writes chars to the pty/XTerm input.
    // Calling Focus() on TerminalControl delegates to the inner TerminalView (the actual focusable pty view with caret).
    internal void RequestTerminalFocusForTest()
    {
        if (TerminalControl == null) return;

        // Direct (synchronous in the click handler) so focus precedes the paste of the prompt text.
        TerminalControl.Focus();
    }

    public MainWindow()
    {
        InitializeComponent();

        // Set a default layout immediately (horizontal wireframe-screenshot-terminal) so the
        // UI is never completely blank while the async scenario loader runs.
        ReconfigureMainLayout(false);

        SetApplicationVersion();

        Loaded += async (_, __) => await TryLoadScenariosAsync();
    }

    private void SetApplicationVersion()
    {
        if (VersionText == null) return;

        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            string ver;
            if (!string.IsNullOrWhiteSpace(info))
            {
                // Prefer the InformationalVersion (set by GitVersion during Nuke build via
                // /p:InformationalVersion + GitVersion.MsBuild). We format it as
                // "0.11.1-beta.3+1c5bb33" so the UI label visibly changes on every rebuild/redeploy.
                var plus = info.IndexOf('+');
                if (plus > 0)
                {
                    var baseVer = info.Substring(0, plus);
                    var meta = info.Substring(plus + 1);
                    var shortSha = meta;
                    var shaIdx = meta.LastIndexOf('.');
                    if (shaIdx > 0 && shaIdx < meta.Length - 1)
                        shortSha = meta.Substring(shaIdx + 1);
                    if (shortSha.Length > 8) shortSha = shortSha.Substring(0, 8);
                    ver = string.IsNullOrWhiteSpace(shortSha) ? baseVer : $"{baseVer}+{shortSha}";
                }
                else
                {
                    ver = info;
                }
            }
            else
            {
                // Fallbacks (should rarely be hit now that Nuke always passes the GitVersion values)
                var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location);
                ver = fvi.ProductVersion ?? fvi.FileVersion ?? asm.GetName().Version?.ToString() ?? "0.0.0";
            }

            VersionText.Text = ver;
        }
        catch
        {
            VersionText.Text = "0.0.0";
        }
    }

    private async Task TryLoadScenariosAsync()
    {
        try
        {
            // "finds aiUnit tests" - use loader with conventional aiunit-repl folders.
            var root = FindRepoRoot();
            var scenariosFolder = Path.Combine(root, "tests", "SharpNinja.AiUnit.Tests", "AiUnitReplWireframeComparisons");
            var wireframesFolder = Path.Combine(root, "docs", "wireframes", "aiunit-repl");
            var screenshotsFolder = Path.Combine(root, "docs", "screenshots", "aiunit-repl");

            var loader = new WireframeScenarioLoader();
            var scenarios = await loader.LoadAsync(scenariosFolder, wireframesFolder, screenshotsFolder);

            // Store for navigation + per-scenario verdict save
            _scenarios = scenarios;
            _scenariosFolder = scenariosFolder;
            _loader = loader;

            Title = $"aiunit-review - Found {scenarios.Count} aiUnit wireframe tests (Avalonia 12)";

            // Init dropdown early (always, even if no scenarios yet) so UI ready
            InitializeScaleDropdown();
            InitializeVerdictCombo();

            if (PrevButton != null) PrevButton.IsEnabled = false;
            if (NextButton != null) NextButton.IsEnabled = false;
            if (SendPromptButton != null) SendPromptButton.IsEnabled = false;

            if (scenarios.Count > 0)
            {
                ShowCurrentScenario(0);
            }
            else
            {
                // no scenarios: still allow terminal etc.
                if (ScenarioInfoText != null) ScenarioInfoText.Text = "(no aiUnit tests found)";
                if (StatusWireframeText != null) StatusWireframeText.Text = "Wireframe: (none)";
                if (StatusScreenshotText != null) StatusScreenshotText.Text = "Screenshot: (none)";
                if (StatusText != null) StatusText.Text = "Status: No aiUnit tests found";

                // Default horizontal layout only when there are no scenarios to drive aspect-based layout.
                ReconfigureMainLayout(false);
            }

            // Wire activation tracking and synchronized image view-state (wheel zoom, drag pan,
            // box zoom, and toolbar zoom/fits).
            if (WireframeViewer != null)
            {
                WireframeViewer.Activated += SetActiveImageViewer;
                WireframeViewer.ViewChanged += OnImageViewerViewChanged;
                WireframeViewer.MarkupHistoryChanged += OnMarkupHistoryChanged;
            }
            if (ScreenshotViewer != null)
            {
                ScreenshotViewer.Activated += SetActiveImageViewer;
                ScreenshotViewer.ViewChanged += OnImageViewerViewChanged;
                ScreenshotViewer.MarkupHistoryChanged += OnMarkupHistoryChanged;
            }
            UpdateMarkupHistoryButtons();

            // Default launch pwsh in CWD (as per requirement)
            // The control is now in XAML; launch on button or auto.
            // For demo, auto-launch shell on load (like sibling "Shell" button).
            if (!_terminalRunning)
            {
                LaunchShell();
            }

            // Final safety net for the very first ("default") view.
            // After async scenario load, first Show (which sets Sources + Reconfigure + multiple Applies),
            // shell launch, and all initial layout passes have settled, force the selected scale one
            // more time at Loaded priority. This catches any case where an early layout pass "won"
            // before the images had Sources or the final parent structure (leftStack etc.) was built.
            var initialScaleIdx = ScaleComboBox?.SelectedIndex ?? -1;
            Dispatcher.UIThread.Post(() =>
            {
                if (initialScaleIdx >= 0)
                    ApplyScaleToImages(initialScaleIdx);
            }, DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            Title = "aiunit-review (loader error: " + ex.Message + ")";
        }
    }

    private string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SharpNinja.aiUnit.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Environment.CurrentDirectory;
    }

    // Terminal launch logic copied/adapted from sibling ToolPanels/TerminalPanel.axaml.cs and ViewModel for "opens pwsh in the CWD"
    private void LaunchShellClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => LaunchShell();

    private void LaunchShell()
    {
        if (_terminalRunning) return;

        var process = GetDefaultShellProcess();
        var args = new[] { "-NoLogo", "-NoProfile" };  // non-interactive for stability; remove for full interactive if wanted
        var cwd = Environment.CurrentDirectory;  // CWD as required

        try
        {
            TerminalControl.LaunchProcess(cwd, process, args);
            _terminalRunning = true;
            TerminalStatusText.Text = $"Running: {process} in {cwd}";
            LaunchShellButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            if (SendPromptButton != null) SendPromptButton.IsEnabled = _currentIndex >= 0;
        }
        catch (Exception ex)
        {
            TerminalStatusText.Text = "Launch failed: " + ex.Message;
        }
    }

    private void StopClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_terminalRunning) return;
        try
        {
            TerminalControl.Kill();
            TerminalStatusText.Text = "Stop requested.";
            if (SendPromptButton != null) SendPromptButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            TerminalStatusText.Text = "Stop failed: " + ex.Message;
        }
    }

    private void TerminalProcessExited(object? sender, ProcessExitedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _terminalRunning = false;
            TerminalStatusText.Text = $"Exited with code {e.ExitCode}.";
            LaunchShellButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            if (SendPromptButton != null) SendPromptButton.IsEnabled = false;
        });
    }

    private static string GetDefaultShellProcess()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pwsh.exe" : "pwsh";
    }

    private void InitializeScaleDropdown()
    {
        if (ScaleComboBox == null) return;
        ScaleComboBox.ItemsSource = _scaleOptions.Select(o => o.Label).ToList();
        ScaleComboBox.SelectedIndex = 2; // Fit (uniform) default per AC-UI-005, matches old hardcoded Uniform
        // IMPORTANT: Do *not* call Apply here. At this point in load the Images have no Source yet
        // and the first ShowCurrentScenario will set Sources + Reconfigure the tree + apply scale.
        // Calling early can lose the effect on the very first layout pass ("default view").
    }

    private void OnScaleChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedIndex >= 0)
        {
            ApplyScaleToImages(cb.SelectedIndex);
        }
    }

    // Legacy scale path for the old dropdown. Now delegates to the viewer's SetBaseScale
    // which uses the exact same target size + Stretch logic as the original working direct
    // Image + Stretch inside ScrollViewer. This guarantees the base "Fit (uniform)" etc.
    // render the images correctly in the panels. The interactive zoom/pan/markups then
    // layer on top.
    private void ApplyScaleToImages(int index)
    {
        if (index < 0 || index >= _scaleOptions.Length) return;

        var stretch = _scaleOptions[index].Stretch;
        WireframeViewer?.SetBaseScale(stretch);
        ScreenshotViewer?.SetBaseScale(stretch);
    }

    private void OnImageViewerViewChanged(
        Controls.MarkupImageViewer source,
        Controls.MarkupImageViewer.ViewState state)
    {
        if (_syncingImageViewState) return;

        SetActiveImageViewer(source);
        _syncingImageViewState = true;
        try
        {
            foreach (var viewer in GetImageViewers())
            {
                if (!ReferenceEquals(viewer, source))
                    viewer.ApplyViewState(state);
            }
        }
        finally
        {
            _syncingImageViewState = false;
        }
    }

    private void SetActiveImageViewer(Controls.MarkupImageViewer viewer)
    {
        _activeViewer = viewer;
        UpdateMarkupHistoryButtons();
    }

    private void OnMarkupHistoryChanged(Controls.MarkupImageViewer viewer)
    {
        if (_activeViewer == null)
            _activeViewer = viewer;

        UpdateMarkupHistoryButtons();
    }

    private void UpdateMarkupHistoryButtons()
    {
        var target = _activeViewer ?? WireframeViewer;
        if (UndoMarkupButton != null)
            UndoMarkupButton.IsEnabled = target?.CanUndoMarkup == true;
        if (RedoMarkupButton != null)
            RedoMarkupButton.IsEnabled = target?.CanRedoMarkup == true;
    }

    private IEnumerable<Controls.MarkupImageViewer> GetImageViewers()
    {
        if (WireframeViewer != null)
            yield return WireframeViewer;
        if (ScreenshotViewer != null)
            yield return ScreenshotViewer;
    }

    private static void InvalidateLayoutTarget(Avalonia.Layout.Layoutable? target)
    {
        if (target == null) return;
        target.InvalidateMeasure();
        target.InvalidateArrange();
        target.InvalidateVisual();

        // Walk up a few levels (ScrollViewer -> inner Grid -> Border -> leftStack/ContentGrid)
        var current = target.Parent as Avalonia.Layout.Layoutable;
        int guard = 0;
        while (current != null && guard++ < 6)
        {
            current.InvalidateMeasure();
            current.InvalidateArrange();
            current.InvalidateVisual();
            current = current.Parent as Avalonia.Layout.Layoutable;
        }
    }

    private GridSplitter CreateVerticalSplitter() => new GridSplitter
    {
        Width = 5,
        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.DimGray),
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        ResizeDirection = GridResizeDirection.Columns,
    };

    private GridSplitter CreateHorizontalSplitter() => new GridSplitter
    {
        Height = 5,
        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.DimGray),
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        ResizeDirection = GridResizeDirection.Rows,
    };

    /// <summary>
    /// Reconfigures the main three panels (wireframe, screenshot, terminal) inside ContentGrid.
    /// Base desired horizontal order: wireframe -> screenshot -> terminal.
    /// - If the wireframe is landscape or square: place screenshot *under* the wireframe (stacked left area);
    ///   terminal takes the full right side.
    /// - If the wireframe is portrait: wireframe | screenshot | terminal (horizontal columns).
    /// </summary>
    private void ReconfigureMainLayout(bool wireframeIsLandscapeOrSquare)
    {
        if (ContentGrid == null) return;

        DetachFromParent(WireframeBorder);
        DetachFromParent(ScreenshotBorder);
        DetachFromParent(TerminalBorder);

        ContentGrid.Children.Clear();
        ContentGrid.RowDefinitions.Clear();
        ContentGrid.ColumnDefinitions.Clear();

        if (wireframeIsLandscapeOrSquare)
        {
            // Left stacked area (wireframe on top of screenshot) + right terminal
            ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftStack = new Grid();
            leftStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(WireframeBorder, 0);
            leftStack.Children.Add(WireframeBorder);

            var hSplitter = CreateHorizontalSplitter();
            Grid.SetRow(hSplitter, 1);
            leftStack.Children.Add(hSplitter);

            Grid.SetRow(ScreenshotBorder, 2);
            leftStack.Children.Add(ScreenshotBorder);

            Grid.SetColumn(leftStack, 0);
            ContentGrid.Children.Add(leftStack);

            var vSplitter = CreateVerticalSplitter();
            Grid.SetColumn(vSplitter, 1);
            ContentGrid.Children.Add(vSplitter);

            Grid.SetColumn(TerminalBorder, 2);
            ContentGrid.Children.Add(TerminalBorder);
        }
        else
        {
            // Horizontal: wireframe | screenshot | terminal
            ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(WireframeBorder, 0);
            ContentGrid.Children.Add(WireframeBorder);

            var vs1 = CreateVerticalSplitter();
            Grid.SetColumn(vs1, 1);
            ContentGrid.Children.Add(vs1);

            Grid.SetColumn(ScreenshotBorder, 2);
            ContentGrid.Children.Add(ScreenshotBorder);

            var vs2 = CreateVerticalSplitter();
            Grid.SetColumn(vs2, 3);
            ContentGrid.Children.Add(vs2);

            Grid.SetColumn(TerminalBorder, 4);
            ContentGrid.Children.Add(TerminalBorder);
        }

        // After any structural reconfigure (including the initial placeholder one in the ctor and
        // the aspect-driven one in ShowCurrentScenario), re-enforce the current scale if the
        // dropdown is ready and the image controls exist. This guarantees the Stretch is set
        // on the Images while they live in their final parents (ContentGrid cols or the dynamic
        // leftStack for landscape/square wireframes). Callers like Show will still do a full
        // Apply + UpdateLayout immediately after, but this makes the "default view" correct
        // even if a layout pass sneaks in between.
        if (ScaleComboBox?.SelectedIndex >= 0 && WireframeViewer != null && ScreenshotViewer != null)
        {
            ApplyScaleToImages(ScaleComboBox.SelectedIndex);
        }
    }

    private static void DetachFromParent(Control? control)
    {
        if (control?.Parent is Panel panel)
            panel.Children.Remove(control);
    }

    // --- Verdict + navigation support (user: "There's now way to specify a verdict") ---

    private void ShowCurrentScenario(int index)
    {
        if (_scenarios.Count == 0 || index < 0 || index >= _scenarios.Count)
            return;

        _currentIndex = index;
        var s = _scenarios[index];

        if (ScenarioInfoText != null)
        {
            var yamlName = Path.GetFileName(s.SourcePath);
            ScenarioInfoText.Text = $"{s.ScreenId} ({index + 1}/{_scenarios.Count}) | YAML: {yamlName}";
        }

        Title = $"aiunit-review - {s.ScreenId} ({index + 1}/{_scenarios.Count}) - Wireframe Comparison (Avalonia 12)";

        // Enable/disable nav icon buttons based on position
        if (PrevButton != null) PrevButton.IsEnabled = index > 0;
        if (NextButton != null) NextButton.IsEnabled = index < _scenarios.Count - 1;

        // Images (now hosted inside MarkupImageViewer which handles zoom/pan/markup)
        WireframeViewer.Source = File.Exists(s.WireframeScreenshotFullPath)
            ? new Avalonia.Media.Imaging.Bitmap(s.WireframeScreenshotFullPath)
            : null;
        WireframePathText.Text = $"Wireframe: {Path.GetFileName(s.WireframeScreenshotPath ?? "")}";

        ScreenshotViewer.Source = File.Exists(s.ActualScreenshotFullPath)
            ? new Avalonia.Media.Imaging.Bitmap(s.ActualScreenshotFullPath)
            : null;
        ScreenshotPathText.Text = $"Screenshot: {Path.GetFileName(s.ActualScreenshotPath ?? "")}";

        // Decide layout based on wireframe aspect ratio (per user request)
        bool wireframeIsLandscapeOrSquare = true; // default: screenshot under wireframe + terminal on right
        if (WireframeViewer?.Source is Avalonia.Media.Imaging.Bitmap wBmp &&
            wBmp.PixelSize.Width > 0 && wBmp.PixelSize.Height > 0)
        {
            wireframeIsLandscapeOrSquare = wBmp.PixelSize.Height <= wBmp.PixelSize.Width;
        }
        ReconfigureMainLayout(wireframeIsLandscapeOrSquare);

        // Settle the layout after reconfiguring the panels.
        this.UpdateLayout();

        // Defer the base scale apply (Fit/Uniform etc from dropdown or default).
        // After ReconfigureMainLayout the Borders are reparented into ContentGrid or the
        // dynamic leftStack (for landscape/square wireframes). Even with UpdateLayout the
        // inner ScrollViewer.Viewport in the MarkupImageViewer can still be 0 or stale on
        // this pass (star rows / arrange timing). SetBaseScale now has its own fallback +
        // Post retry, but wrapping the call here at Background gives the layout a chance
        // to produce a real positive viewport for the first paint. This (with the sizing
        // fixes inside the viewer) makes the images visible instead of solid black panels.
        Dispatcher.UIThread.Post(() =>
        {
            if (ScaleComboBox?.SelectedIndex >= 0 && ScaleComboBox.SelectedIndex < _scaleOptions.Length)
            {
                var stretch = _scaleOptions[ScaleComboBox.SelectedIndex].Stretch;
                WireframeViewer?.SetBaseScale(stretch);
                ScreenshotViewer?.SetBaseScale(stretch);
            }
            else
            {
                WireframeViewer?.SetBaseScale(Avalonia.Media.Stretch.Uniform);
                ScreenshotViewer?.SetBaseScale(Avalonia.Media.Stretch.Uniform);
            }
        }, DispatcherPriority.Background);

        // populate verdict editor from persisted sidecar (if any)
        PopulateVerdictControls(s.HumanReview);

        UpdateStatusBar(s);
    }

    private void PopulateVerdictControls(HumanReview? hr)
    {
        if (VerdictCombo == null) return;

        var choice = hr?.Verdict;
        if (!string.IsNullOrWhiteSpace(choice) && VerdictCombo.ItemsSource is IEnumerable<string> items && items.Contains(choice))
        {
            VerdictCombo.SelectedItem = choice;
        }
        else if (VerdictCombo.Items.Count > 0)
        {
            VerdictCombo.SelectedIndex = 0; // default approved
        }

        if (VerdictNotes != null)
            VerdictNotes.Text = hr?.Notes ?? string.Empty;
    }

    private void UpdateStatusBar(WireframeScenario s)
    {
        // Short status for the middle panel of the 3-col status bar (filenames live in left/right panels; no need to repeat scenario/yaml/verdict here).
        if (StatusText != null)
        {
            StatusText.Text = "Ready | Agent: pwsh | CWD: (aiUnit root)";
        }
    }

    private void PrevScenarioClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_currentIndex > 0)
            ShowCurrentScenario(_currentIndex - 1);
    }

    private void NextScenarioClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_currentIndex >= 0 && _currentIndex < _scenarios.Count - 1)
            ShowCurrentScenario(_currentIndex + 1);
    }

    private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (IsTextInputEventSource(e.Source))
            return;

        bool ctrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);
        if (ctrl && e.Key == Avalonia.Input.Key.Z && !shift)
        {
            UndoActiveMarkup();
            e.Handled = true;
            return;
        }

        if (ctrl && (e.Key == Avalonia.Input.Key.Y || (e.Key == Avalonia.Input.Key.Z && shift)))
        {
            RedoActiveMarkup();
            e.Handled = true;
            return;
        }

        if (e.Key == Avalonia.Input.Key.Left || e.Key == Avalonia.Input.Key.PageUp)
        {
            PrevScenarioClicked(sender, null!);
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Right || e.Key == Avalonia.Input.Key.PageDown)
        {
            NextScenarioClicked(sender, null!);
            e.Handled = true;
        }
    }

    private static bool IsTextInputEventSource(object? source)
    {
        var current = source as Control;
        while (current != null)
        {
            if (current is TextBox)
                return true;

            current = current.Parent as Control;
        }

        return false;
    }

    private void SaveVerdictClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_currentIndex < 0 || _loader == null || string.IsNullOrWhiteSpace(_scenariosFolder) || _scenarios.Count == 0)
            return;

        var s = _scenarios[_currentIndex];
        var ver = (VerdictCombo?.SelectedItem as string) ?? "approved";
        var notes = VerdictNotes?.Text?.Trim();
        if (string.IsNullOrEmpty(notes)) notes = null;

        var review = new HumanReview(ver, Environment.UserName, DateTimeOffset.UtcNow, notes);

        try
        {
            _loader.SaveReview(_scenariosFolder, s.ScreenId, review);
            s.HumanReview = review; // update in-memory so switch/reload sees it
            PopulateVerdictControls(review);
            UpdateStatusBar(s);
            if (TerminalStatusText != null)
                TerminalStatusText.Text = $"Verdict saved for {s.ScreenId} ({ver})";
        }
        catch (Exception ex)
        {
            if (TerminalStatusText != null)
                TerminalStatusText.Text = "Save verdict failed: " + ex.Message;
        }
    }

    private void InitializeVerdictCombo()
    {
        if (VerdictCombo == null) return;
        VerdictCombo.ItemsSource = new[] { "approved", "rejected", "needs-changes" };
        VerdictCombo.SelectedIndex = 0;
    }

    // === Toolbar handlers for new zoom/pan + markup requirements ===

    private void ToolbarPanClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WireframeViewer?.SetTool(Controls.MarkupImageViewer.Tool.Pan);
        ScreenshotViewer?.SetTool(Controls.MarkupImageViewer.Tool.Pan);
    }

    private void ToolbarZoomInClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var target = _activeViewer ?? WireframeViewer;
        target?.ZoomIn();
    }

    private void ToolbarZoomOutClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var target = _activeViewer ?? WireframeViewer;
        target?.ZoomOut();
    }

    private void ToolbarFitClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Fit is commonly wanted on both for comparison reset, but we respect active if set.
        // User can always use mouse wheel on the specific image for independent control.
        var target = _activeViewer;
        if (target != null)
            target.FitToView();
        else
        {
            WireframeViewer?.FitToView();
            ScreenshotViewer?.FitToView();
        }
    }

    private void ToolbarBoxZoomClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WireframeViewer?.SetTool(Controls.MarkupImageViewer.Tool.BoxZoom);
        ScreenshotViewer?.SetTool(Controls.MarkupImageViewer.Tool.BoxZoom);
    }

    private void ToolbarHighlighterClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WireframeViewer?.SetTool(Controls.MarkupImageViewer.Tool.Highlighter);
        ScreenshotViewer?.SetTool(Controls.MarkupImageViewer.Tool.Highlighter);
    }

    private void ToolbarTextAreaClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WireframeViewer?.SetTool(Controls.MarkupImageViewer.Tool.TextArea);
        ScreenshotViewer?.SetTool(Controls.MarkupImageViewer.Tool.TextArea);
    }

    private void ToolbarArrowClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WireframeViewer?.SetTool(Controls.MarkupImageViewer.Tool.Arrow);
        ScreenshotViewer?.SetTool(Controls.MarkupImageViewer.Tool.Arrow);
    }

    private void ToolbarUndoMarkupClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        UndoActiveMarkup();
    }

    private void ToolbarRedoMarkupClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RedoActiveMarkup();
    }

    private void ToolbarClearMarkupsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WireframeViewer?.ClearMarkups();
        ScreenshotViewer?.ClearMarkups();
    }

    private void UndoActiveMarkup()
    {
        var target = _activeViewer ?? WireframeViewer;
        if (target?.UndoMarkup() == true && TerminalStatusText != null)
            TerminalStatusText.Text = "Markup undo";
        UpdateMarkupHistoryButtons();
    }

    private void RedoActiveMarkup()
    {
        var target = _activeViewer ?? WireframeViewer;
        if (target?.RedoMarkup() == true && TerminalStatusText != null)
            TerminalStatusText.Text = "Markup redo";
        UpdateMarkupHistoryButtons();
    }

    // Paste the (single-line) prompt into the terminal (via XTerm send or pty writer paste path).
    // "Go back to pasting the prompt" (using the paste-oriented send), but set focus to the terminal BEFORE pasting.
    private void SendReviewPromptClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_currentIndex < 0 || _scenarios.Count == 0 || !_terminalRunning)
        {
            if (TerminalStatusText != null) TerminalStatusText.Text = "Send prompt: terminal not running or no scenario selected.";
            return;
        }

        var s = _scenarios[_currentIndex];

        if (string.IsNullOrWhiteSpace(s.SourcePath))
        {
            if (TerminalStatusText != null) TerminalStatusText.Text = "No YAML path defined for current scenario.";
            return;
        }

        // Use the testable builder (ensures single line, no breaks). Path-only per prior change.
        var textToSend = BuildReviewPromptTextForTest(s);

        try
        {
            // Set focus BEFORE pasting (per "go back to pasting the prompt, but set focus to the terminal before pasting").
            RequestTerminalFocusForTest();

            SendTextToTerminal(textToSend);  // the actual "paste" of the prompt text (XTerm or pty writer)
            if (TerminalStatusText != null) TerminalStatusText.Text = $"Sent review prompt path for {s.ScreenId}.";
        }
        catch (Exception ex)
        {
            if (TerminalStatusText != null) TerminalStatusText.Text = "Send prompt failed: " + ex.Message;
        }
    }

    private void SendTextToTerminal(string text)
    {
        if (string.IsNullOrEmpty(text) || TerminalControl == null) return;

        // "Pasting" the prompt: prefer the XTerm.NET terminal's input method if available
        // (this is the high-level "send/paste" path on the emulator; falls back to direct pty writer for the input stream).
        // This is the "pasting the prompt" mechanism (go back to prior paste-oriented send, before stdio preference).
        try
        {
            var termProp = TerminalControl.GetType().GetProperty("Terminal", BindingFlags.Public | BindingFlags.Instance);
            var term = termProp?.GetValue(TerminalControl);
            if (term != null)
            {
                var send = term.GetType().GetMethod("Send", new[] { typeof(string) }) ??
                           term.GetType().GetMethod("Write", new[] { typeof(string) }) ??
                           term.GetType().GetMethod("Input", new[] { typeof(string) }) ??
                           term.GetType().GetMethod("Feed", new[] { typeof(string) });
                if (send != null)
                {
                    send.Invoke(term, new object[] { text });
                    return;
                }
            }
        }
        catch { /* fall through to pty stream paste */ }

        // Fallback: direct to the pty writer stream (this writes the prompt chars as if pasted/typed into the shell's controlling tty).
        // Matches the internal paste path used by TerminalView.PasteAsync / SendToPtyAsync (via WriterStream).
        try
        {
            var viewField = TerminalControl.GetType().GetField("_terminalView", BindingFlags.NonPublic | BindingFlags.Instance);
            var view = viewField?.GetValue(TerminalControl);
            if (view != null)
            {
                var connField = view.GetType().GetField("_ptyConnection", BindingFlags.NonPublic | BindingFlags.Instance);
                var conn = connField?.GetValue(view);
                if (conn != null)
                {
                    var wsProp = conn.GetType().GetProperty("WriterStream", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var stream = wsProp?.GetValue(conn) as System.IO.Stream;
                    if (stream != null && stream.CanWrite)
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush();
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to access terminal paste writer stream via reflection: " + ex.Message, ex);
        }

        throw new InvalidOperationException("No writable input stream found for terminal (shell may not be running or internal API changed).");
    }
}
