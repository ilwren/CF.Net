using CFNet.Models.R2;

namespace CFNet.Gallery.ViewModels;

/// <summary>
/// R2 buckets. Jurisdiction-restricted buckets are reached by sending the <c>cf-r2-jurisdiction</c>
/// header, which the library does for you when the enum is not <c>Unknown</c>.
/// </summary>
public sealed class R2ViewModel : ModuleViewModelBase
{
    private string _listPerPage = "100";
    private string _listNameContains = string.Empty;
    private string _listStartAfter = string.Empty;
    private string _listCursor = string.Empty;
    private string _bucketName = string.Empty;
    private R2LocationHint _locationHint = R2LocationHint.Unknown;
    private R2StorageClass _storageClass = R2StorageClass.Standard;
    private R2Jurisdiction _jurisdiction = R2Jurisdiction.Unknown;

    /// <summary>Creates the page.</summary>
    /// <param name="gallery">Owning shell.</param>
    public R2ViewModel(GalleryViewModel gallery)
        : base(gallery)
    {
        ListCommand = new RelayCommand(ListAsync);
        ListAllCommand = new RelayCommand(ListAllAsync);
        GetCommand = new RelayCommand(GetAsync);
        CreateCommand = new RelayCommand(CreateAsync);
        UpdateCommand = new RelayCommand(UpdateAsync);
        DeleteCommand = new RelayCommand(DeleteAsync);
    }

    /// <inheritdoc />
    public override string Title => "R2";

    /// <inheritdoc />
    public override string Description =>
        "Object storage buckets. The listing is cursor based, so use the cursor from the previous page.";

    /// <summary>Items per page (1 to 1000).</summary>
    public string ListPerPage { get => _listPerPage; set => SetProperty(ref _listPerPage, value); }

    /// <summary>Name filter (substring).</summary>
    public string ListNameContains { get => _listNameContains; set => SetProperty(ref _listNameContains, value); }

    /// <summary>Start the listing after this bucket name.</summary>
    public string ListStartAfter { get => _listStartAfter; set => SetProperty(ref _listStartAfter, value); }

    /// <summary>Cursor from a previous page.</summary>
    public string ListCursor { get => _listCursor; set => SetProperty(ref _listCursor, value); }

    /// <summary>Bucket every single-bucket call operates on.</summary>
    public string BucketName { get => _bucketName; set => SetProperty(ref _bucketName, value); }

    /// <summary>Where a new bucket is placed. <c>Unknown</c> means Cloudflare decides.</summary>
    public R2LocationHint LocationHint { get => _locationHint; set => SetProperty(ref _locationHint, value); }

    /// <summary>Storage class of a new bucket, or the new class when updating.</summary>
    public R2StorageClass StorageClass { get => _storageClass; set => SetProperty(ref _storageClass, value); }

    /// <summary>Jurisdiction restriction. <c>Unknown</c> sends no header.</summary>
    public R2Jurisdiction Jurisdiction { get => _jurisdiction; set => SetProperty(ref _jurisdiction, value); }

    /// <summary>Location hints offered in the picker.</summary>
    public IReadOnlyList<R2LocationHint> LocationHints { get; } =
        Enum.GetValues<R2LocationHint>();

    /// <summary>Storage classes offered in the picker.</summary>
    public IReadOnlyList<R2StorageClass> StorageClasses { get; } =
        Enum.GetValues<R2StorageClass>().Where(value => value != R2StorageClass.Unknown).ToArray();

    /// <summary>Jurisdictions offered in the picker.</summary>
    public IReadOnlyList<R2Jurisdiction> Jurisdictions { get; } =
        Enum.GetValues<R2Jurisdiction>();

    /// <summary>Lists buckets.</summary>
    public RelayCommand ListCommand { get; }

    /// <summary>Walks every bucket page.</summary>
    public RelayCommand ListAllCommand { get; }

    /// <summary>Gets one bucket.</summary>
    public RelayCommand GetCommand { get; }

    /// <summary>Creates a bucket.</summary>
    public RelayCommand CreateCommand { get; }

    /// <summary>Changes a bucket's storage class.</summary>
    public RelayCommand UpdateCommand { get; }

    /// <summary>Deletes a bucket.</summary>
    public RelayCommand DeleteCommand { get; }

    private Task ListAsync() => RunAsync("List R2 buckets", async () =>
        await Client.R2.ListBucketsAsync(new R2BucketListParameters
        {
            PerPage = OptionalInt(ListPerPage),
            Cursor = Blank(ListCursor),
            NameContains = Blank(ListNameContains),
            StartAfter = Blank(ListStartAfter),
            Order = "name",
            Jurisdiction = Jurisdiction == R2Jurisdiction.Unknown ? null : Jurisdiction,
        }));

    private Task ListAllAsync() => RunAsync("List every R2 bucket", async () =>
    {
        var buckets = new List<R2Bucket>();
        await foreach (var bucket in Client.R2.ListAllBucketsAsync(new R2BucketListParameters
        {
            PerPage = OptionalInt(ListPerPage),
            NameContains = Blank(ListNameContains),
            Jurisdiction = Jurisdiction == R2Jurisdiction.Unknown ? null : Jurisdiction,
        }))
        {
            buckets.Add(bucket);

            if (buckets.Count >= 1000)
            {
                break;
            }
        }

        return buckets;
    });

    private Task GetAsync() => RunAsync("Get R2 bucket", async () =>
        await Client.R2.GetBucketAsync(Bucket(), NullableJurisdiction()));

    private Task CreateAsync() => RunAsync("Create R2 bucket", async () =>
        await Client.R2.CreateBucketAsync(
            Bucket(),
            LocationHint == R2LocationHint.Unknown ? null : LocationHint,
            StorageClass == R2StorageClass.Unknown ? null : StorageClass,
            NullableJurisdiction()));

    private Task UpdateAsync() => RunAsync("Update R2 bucket", () =>
        Client.R2.UpdateBucketAsync(Bucket(), StorageClass, NullableJurisdiction()));

    private Task DeleteAsync() => RunVoidAsync("Delete R2 bucket", () =>
        Client.R2.DeleteBucketAsync(Bucket(), NullableJurisdiction()));

    private R2Jurisdiction? NullableJurisdiction() =>
        Jurisdiction == R2Jurisdiction.Unknown ? null : Jurisdiction;

    private string Bucket() =>
        Blank(BucketName) ?? throw new ArgumentException("Enter a bucket name.");
}
