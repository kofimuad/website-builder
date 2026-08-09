using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace WebsiteBuilder.Web.Images;

/// <summary>
/// Uploads to Cloudinary with a server-signed request.
///
/// Signing happens here rather than in the browser because an unsigned upload preset is effectively
/// world-writable: the preset name is visible in page source, and anyone who reads it can upload
/// into the account. Routing the file through this server costs a hop but means the size and type
/// limits are enforced by us, on our side, before a byte reaches the provider.
/// </summary>
public sealed class CloudinaryImageStore(
    HttpClient http,
    IOptions<ImageOptions> options,
    ILogger<CloudinaryImageStore> logger) : IImageStore
{
    private static readonly string[] Accepted =
        ["image/jpeg", "image/png", "image/webp", "image/gif", "image/avif"];

    private readonly ImageOptions _options = options.Value;

    public long MaxBytes => _options.MaxBytes;

    public IReadOnlyCollection<string> AcceptedContentTypes => Accepted;

    public async Task<StoredImage> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (!Accepted.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new ImageUploadException(
                "That file isn't a photo we can use. Try a JPG, PNG or WebP image.");
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var folder = $"{_options.Folder.Trim('/')}/{tenantId:N}";

        using var form = new MultipartFormDataContent();

        var file = new StreamContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "file", SafeFileName(fileName));

        AddField(form, "api_key", _options.ApiKey!);
        AddField(form, "timestamp", timestamp);
        AddField(form, "folder", folder);

        // Signed parameters are every parameter except the file, the api_key and the signature
        // itself, sorted by name. Getting this wrong returns a 401 that says nothing useful.
        AddField(form, "signature", Sign($"folder={folder}&timestamp={timestamp}"));

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(
                $"https://api.cloudinary.com/v1_1/{_options.CloudName}/image/upload",
                form,
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Cloudinary upload could not be reached for tenant {TenantId}.", tenantId);
            throw new ImageUploadException(
                "We couldn't reach the photo service just now. Please try again in a moment.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The provider's own message is for us, not the owner: it talks about presets and
            // signatures. Log it and show something the owner can act on.
            logger.LogError(
                "Cloudinary rejected an upload for tenant {TenantId}: {Status} {Body}",
                tenantId, (int)response.StatusCode, body);

            throw new ImageUploadException(
                "The photo service wouldn't accept that image. Please try a different photo.");
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        if (!root.TryGetProperty("secure_url", out var url) || url.GetString() is not { } secureUrl)
        {
            logger.LogError("Cloudinary upload succeeded but returned no secure_url: {Body}", body);
            throw new ImageUploadException("That photo uploaded but came back unusable. Please try again.");
        }

        return new StoredImage(
            secureUrl,
            root.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
            root.TryGetProperty("height", out var h) ? h.GetInt32() : 0);
    }

    /// <summary>
    /// Adds one plain form field.
    /// <para>
    /// Two details, both learned from a production 400. <c>StringContent</c> stamps
    /// <c>Content-Type: text/plain; charset=utf-8</c> on the part, and Cloudinary reads any part
    /// with a content type as a *file* rather than a parameter — so api_key, timestamp and
    /// signature all vanish and the upload is treated as unsigned. And the field name has to be
    /// quoted: <c>MultipartFormDataContent.Add(content, name)</c> writes it bare, which RFC 7578
    /// does not allow and not every parser tolerates.
    /// </para>
    /// <para>
    /// The error Cloudinary returns for all of this is "Upload preset must be specified when using
    /// unsigned upload", which describes a different mistake entirely.
    /// </para>
    /// </summary>
    private static void AddField(MultipartFormDataContent form, string name, string value)
    {
        var field = new StringContent(value);
        field.Headers.ContentType = null;
        field.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = $"\"{name}\"",
        };

        form.Add(field);
    }

    private string Sign(string parameters)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(parameters + _options.ApiSecret));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// The browser sends whatever the file was called on the owner's phone. Only the extension is
    /// worth keeping; the rest becomes a provider-side public id we do not want shaped by user input.
    /// </summary>
    private static string SafeFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.IsNullOrWhiteSpace(extension) || extension.Length > 8
            ? "photo"
            : $"photo{extension.ToLowerInvariant()}";
    }
}
