using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFNet.Gallery.Infrastructure;

/// <summary>
/// The gallery's saved credential and transport settings, in the shape they are written to disk.
/// </summary>
/// <remarks>
/// Strings everywhere on purpose: a <see cref="Uri"/> or <see cref="TimeSpan"/> round-trips more
/// predictably as text than as a JSON token, and the file stays hand-editable.
/// </remarks>
public sealed class StoredSettings
{
    /// <summary>API token (<c>Authorization: Bearer</c>).</summary>
    public string? ApiToken { get; set; }

    /// <summary>Legacy global API key (<c>X-Auth-Key</c>).</summary>
    public string? GlobalApiKey { get; set; }

    /// <summary>Account email paired with the legacy global API key (<c>X-Auth-Email</c>).</summary>
    public string? ApiEmail { get; set; }

    /// <summary>Default account id.</summary>
    public string? AccountId { get; set; }

    /// <summary>Default zone id.</summary>
    public string? ZoneId { get; set; }

    /// <summary>API base address, empty for the public endpoint.</summary>
    public string? BaseAddress { get; set; }

    /// <summary>Per-request timeout in seconds.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Retries after the first attempt.</summary>
    public int? MaxRetries { get; set; }

    /// <summary>Whether API failures throw.</summary>
    public bool? ThrowOnApiError { get; set; }
}

/// <summary>
/// Reads and writes <see cref="StoredSettings"/> to a single JSON file under the user's application
/// data folder.
/// </summary>
/// <remarks>
/// The file is plain text and holds a live credential. That is why saving is opt-in: the gallery
/// starts with the fields empty, reads the <c>CLOUDFLARE_*</c> environment variables instead, and
/// only writes to disk when the user ticks "Remember on this machine".
/// </remarks>
public static class CredentialStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Absolute path of the settings file.</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CF.Net.Gallery",
        "credentials.json");

    /// <summary>True when a settings file exists.</summary>
    public static bool Exists => File.Exists(FilePath);

    /// <summary>
    /// Loads the settings, falling back to the environment and then to a blank set.
    /// </summary>
    /// <returns>The loaded settings; never <c>null</c>.</returns>
    public static StoredSettings Load()
    {
        if (Exists)
        {
            try
            {
                var json = File.ReadAllText(FilePath);
                var stored = JsonSerializer.Deserialize<StoredSettings>(json, Options);
                if (stored is not null)
                {
                    return stored;
                }
            }
            catch (Exception)
            {
                // A corrupt or partially written file must not stop the gallery from opening.
            }
        }

        return FromEnvironment();
    }

    /// <summary>Writes the settings, creating the folder when needed.</summary>
    /// <param name="settings">The settings to persist.</param>
    public static void Save(StoredSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
    }

    /// <summary>Deletes the settings file if present.</summary>
    public static void Clear()
    {
        if (Exists)
        {
            File.Delete(FilePath);
        }
    }

    /// <summary>Builds settings from the same variables the CI workflow uses.</summary>
    /// <returns>Settings populated from the process environment.</returns>
    public static StoredSettings FromEnvironment() => new()
    {
        ApiToken = Read("CLOUDFLARE_API_TOKEN"),
        GlobalApiKey = Read("CLOUDFLARE_GLOBAL_API_KEY"),
        ApiEmail = Read("CLOUDFLARE_API_EMAIL"),
        AccountId = Read("CLOUDFLARE_ACCOUNT_ID"),
        ZoneId = Read("CLOUDFLARE_ZONE_ID"),
        BaseAddress = Read("CLOUDFLARE_BASE_ADDRESS"),
    };

    private static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
