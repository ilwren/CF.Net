using Avalonia;

namespace CFNet.Gallery;

/// <summary>
/// Avalonia entry point. Kept deliberately tiny: everything interesting lives in the view models.
/// </summary>
internal static class Program
{
    /// <summary>Starts the gallery.</summary>
    /// <param name="args">Command line arguments forwarded to Avalonia.</param>
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Configures the Avalonia application builder. Avalonia's previewer uses this too.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
