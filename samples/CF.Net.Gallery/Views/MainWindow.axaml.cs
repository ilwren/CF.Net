using Avalonia.Controls;
using CFNet.Gallery.ViewModels;

namespace CFNet.Gallery.Views;

/// <summary>
/// The single window of the gallery: sidebar, page host and the request/response inspector.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Creates the window. Required by the Avalonia designer.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Creates the window around its view model.</summary>
    /// <param name="viewModel">Shell view model.</param>
    public MainWindow(GalleryViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
