using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CFNet.Gallery.ViewModels;
using CFNet.Gallery.Views;

namespace CFNet.Gallery;

/// <summary>
/// Application shell. Creates the single window and its view model.
/// </summary>
public partial class App : Application
{
    private GalleryViewModel? _viewModel;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _viewModel = new GalleryViewModel();
            desktop.MainWindow = new MainWindow(_viewModel);
            desktop.Exit += OnExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _viewModel?.Dispose();
        _viewModel = null;
    }
}
