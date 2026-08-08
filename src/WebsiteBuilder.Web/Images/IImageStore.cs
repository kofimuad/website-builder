namespace WebsiteBuilder.Web.Images;

/// <summary>An image that has been stored and is ready to reference from a site definition.</summary>
public sealed record StoredImage(string Url, int Width, int Height);

/// <summary>
/// Stores an uploaded photo and returns a URL the renderer can size on delivery (WB-23).
///
/// Registered only when a provider is configured. Like the per-section assistant, uploads are a
/// capability the app can be missing: without credentials the editor simply does not offer them,
/// rather than offering a button that fails.
/// </summary>
public interface IImageStore
{
    /// <summary>Largest accepted file. Enforced here, before any byte reaches the provider.</summary>
    long MaxBytes { get; }

    IReadOnlyCollection<string> AcceptedContentTypes { get; }

    Task<StoredImage> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An upload that failed for a reason worth showing the owner. The message is written for someone
/// who was trying to put a photo of their shopfront on their website, not for a log reader.
/// </summary>
public sealed class ImageUploadException(string message, Exception? inner = null)
    : Exception(message, inner);
