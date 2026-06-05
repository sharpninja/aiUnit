using Avalonia;
using System;
using System.Linq;

namespace SharpNinja.AiUnit.Desktop;

/// <summary>
/// Entry point for the aiunit-review Avalonia 12 dotnet tool.
/// Supports --probe-exit for CI smoke test (per plan/FR).
/// NOTE: Full hosting of Avalonia.RemoteControl services (Add/Start/Attach + custom IRemoteControlRootProvider for exposing the comparison UI)
/// is prepared per user revision and plan slice 8, but commented here to avoid package version conflicts during pack (sibling Server is Avalonia 12 based).
/// To enable: add the Server project ref (or its nupkg), uncomment the DI/host code, implement a custom root provider that surfaces the wireframe scenarios, images, terminal state etc.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--probe-exit", StringComparison.OrdinalIgnoreCase)))
        {
            // Per plan TEST-013 / FR-AIUNITDESKTOP-014: return 0 immediately without opening window/GUI.
            Console.WriteLine("aiunit-review probe-exit: OK (no GUI started)");
            return 0;
        }

        // TODO (per revision/plan): Setup DI for RemoteControl hosting here when version aligned.
        // Example (requires the Server ref):
        // var services = new ServiceCollection();
        // services.AddAvaloniaRemoteControl();
        // var sp = services.BuildServiceProvider();
        // var host = sp.GetRequiredService<AvaloniaRemoteControlServerHost>();
        // host.StartAsync()...
        // Then in App/MainWindow lifetime: sp.Attach... or use the extensions.

        Console.WriteLine("[aiunit-review] Starting Avalonia UI (L:wireframe / M:pwsh-chat via sibling terminal control / R:screenshot). Full RemoteControl hosting to be enabled in later slice.");

        // Build and run the Avalonia app (shows the MainWindow with 3-col layout + basic "finds tests" via loader on load)
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
