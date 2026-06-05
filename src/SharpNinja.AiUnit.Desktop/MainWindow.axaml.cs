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
        Loaded += async (_, __) => await TryLoadScenariosAsync();
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
            }

            // Default launch pwsh in CWD (as per requirement)
            // The control is now in XAML; launch on button or auto.
            // For demo, auto-launch shell on load (like sibling "Shell" button).
            if (!_terminalRunning)
            {
                LaunchShell();
            }
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
        ApplyScaleToImages(ScaleComboBox.SelectedIndex);
    }

    private void OnScaleChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedIndex >= 0)
        {
            ApplyScaleToImages(cb.SelectedIndex);
        }
    }

    private void ApplyScaleToImages(int index)
    {
        if (index < 0 || index >= _scaleOptions.Length) return;
        var stretch = _scaleOptions[index].Stretch;
        bool updated = false;
        if (WireframeImage != null && WireframeImage.Stretch != stretch)
        {
            WireframeImage.Stretch = stretch;
            updated = true;
        }
        if (ScreenshotImage != null && ScreenshotImage.Stretch != stretch)
        {
            ScreenshotImage.Stretch = stretch;
            updated = true;
        }
        if (updated)
        {
            // Ensure the change (especially None vs Fit/Uniform) takes effect immediately,
            // including re-computing sizes inside the ScrollViewers and updating scrollbars.
            WireframeImage?.InvalidateMeasure();
            ScreenshotImage?.InvalidateMeasure();
            if (WireframeImage?.Parent is Avalonia.Layout.Layoutable wp) wp.InvalidateMeasure();
            if (ScreenshotImage?.Parent is Avalonia.Layout.Layoutable sp) sp.InvalidateMeasure();
        }
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

        // Images (same resolution rules as before)
        WireframeImage.Source = File.Exists(s.WireframeScreenshotFullPath)
            ? new Avalonia.Media.Imaging.Bitmap(s.WireframeScreenshotFullPath)
            : null;
        WireframePathText.Text = $"Wireframe: {Path.GetFileName(s.WireframeScreenshotPath ?? "")}";

        ScreenshotImage.Source = File.Exists(s.ActualScreenshotFullPath)
            ? new Avalonia.Media.Imaging.Bitmap(s.ActualScreenshotFullPath)
            : null;
        ScreenshotPathText.Text = $"Screenshot: {Path.GetFileName(s.ActualScreenshotPath ?? "")}";

        // Status bar panels (same widths as main 3-col body): place filenames in corresponding columns
        if (StatusWireframeText != null)
            StatusWireframeText.Text = $"Wireframe: {Path.GetFileName(s.WireframeScreenshotPath ?? "")}";
        if (StatusScreenshotText != null)
            StatusScreenshotText.Text = $"Screenshot: {Path.GetFileName(s.ActualScreenshotPath ?? "")}";

        // keep current scale
        if (ScaleComboBox?.SelectedIndex >= 0)
            ApplyScaleToImages(ScaleComboBox.SelectedIndex);

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
