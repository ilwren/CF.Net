namespace CFNet.Gallery.ViewModels;

/// <summary>
/// Anything the sidebar can select: a Cloudflare module page or the credentials page.
/// </summary>
public abstract class PageViewModelBase : ObservableObject
{
    /// <summary>Sidebar label.</summary>
    public abstract string Title { get; }

    /// <summary>One-line explanation shown at the top of the page.</summary>
    public abstract string Description { get; }

    /// <inheritdoc />
    public override string ToString() => Title;
}
