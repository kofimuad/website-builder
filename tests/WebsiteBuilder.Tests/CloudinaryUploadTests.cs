using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebsiteBuilder.Web.Images;

namespace WebsiteBuilder.Tests;

/// <summary>
/// Pins the shape of the upload request. Cloudinary decides whether an upload is signed purely
/// from the fields it can find in the multipart body, and its answer when it cannot find them is
/// "Upload preset must be specified when using unsigned upload" — which names the wrong problem
/// and cost an afternoon in production.
/// </summary>
public class CloudinaryUploadTests
{
    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RawBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RawBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private const string Success =
        """{"secure_url":"https://res.cloudinary.com/demo/image/upload/v1/sites/a/photo.jpg","width":1200,"height":800}""";

    private static (CloudinaryImageStore Store, CapturingHandler Handler) Build(string body = Success)
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, body);
        var options = new ImageOptions
        {
            CloudName = "unmfwxkr",
            ApiKey = "123456789012345",
            ApiSecret = "test-secret",
        };

        return (
            new CloudinaryImageStore(new HttpClient(handler), Options.Create(options),
                NullLogger<CloudinaryImageStore>.Instance),
            handler);
    }

    private static Task<StoredImage> Upload(CloudinaryImageStore store) =>
        store.UploadAsync(
            new MemoryStream([1, 2, 3, 4]), "shopfront.jpg", "image/jpeg", Guid.NewGuid());

    [Fact]
    public async Task The_request_carries_the_fields_that_make_it_a_signed_upload()
    {
        var (store, handler) = Build();

        await Upload(store);

        var body = handler.RawBody!;

        // Without all three Cloudinary treats the request as unsigned and demands an upload preset.
        // Quoted names, as RFC 7578 requires — .NET writes them bare unless told otherwise.
        Assert.Contains("name=\"api_key\"", body);
        Assert.Contains("name=\"timestamp\"", body);
        Assert.Contains("name=\"signature\"", body);
        Assert.Contains("name=\"folder\"", body);
        Assert.Contains("123456789012345", body);
    }

    [Fact]
    public async Task Form_fields_are_sent_without_a_content_type_of_their_own()
    {
        // A part carrying "Content-Type: text/plain; charset=utf-8" is read by Cloudinary as a
        // file rather than a form field, so the signature never registers as one.
        var (store, handler) = Build();

        await Upload(store);

        Assert.DoesNotContain("text/plain", handler.RawBody);
    }

    [Fact]
    public async Task A_successful_upload_returns_the_delivery_url_and_size()
    {
        var (store, _) = Build();

        var stored = await Upload(store);

        Assert.Equal("https://res.cloudinary.com/demo/image/upload/v1/sites/a/photo.jpg", stored.Url);
        Assert.Equal(1200, stored.Width);
        Assert.Equal(800, stored.Height);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused_before_it_reaches_the_provider()
    {
        var (store, handler) = Build();

        await Assert.ThrowsAsync<ImageUploadException>(() =>
            store.UploadAsync(new MemoryStream([1]), "invoice.pdf", "application/pdf", Guid.NewGuid()));

        Assert.Null(handler.RawBody);
    }
}
