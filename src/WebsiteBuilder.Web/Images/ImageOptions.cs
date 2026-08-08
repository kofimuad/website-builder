namespace WebsiteBuilder.Web.Images;

public sealed class ImageOptions
{
    public const string SectionName = "Images";

    /// <summary>Cloudinary cloud name — the account the images live in.</summary>
    public string? CloudName { get; set; }

    public string? ApiKey { get; set; }

    public string? ApiSecret { get; set; }

    /// <summary>
    /// Root folder uploads land in. Each tenant gets a folder beneath it, so one business's photos
    /// can be found — or removed — without touching another's.
    /// </summary>
    public string Folder { get; set; } = "sites";

    /// <summary>
    /// Largest file accepted. Phone photos routinely reach 8–10 MB, and rejecting the picture
    /// someone just took of their own shop is a bad first experience, so the ceiling is generous.
    /// </summary>
    public long MaxBytes { get; set; } = 12 * 1024 * 1024;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudName) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret);

    /// <summary>True when some but not all credentials are present — always a mistake.</summary>
    public bool IsPartiallyConfigured =>
        !IsConfigured &&
        (!string.IsNullOrWhiteSpace(CloudName) ||
         !string.IsNullOrWhiteSpace(ApiKey) ||
         !string.IsNullOrWhiteSpace(ApiSecret));
}
