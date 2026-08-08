using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Tests;

public class ImageDeliveryTests
{
    private const string Uploaded =
        "https://res.cloudinary.com/demo-cloud/image/upload/v1712345678/sites/abc123/photo.jpg";

    [Fact]
    public void An_uploaded_image_is_resized_on_delivery()
    {
        var url = ImageDelivery.Sized(Uploaded, 1200);

        Assert.Equal(
            "https://res.cloudinary.com/demo-cloud/image/upload/f_auto,q_auto,c_limit,w_1200/v1712345678/sites/abc123/photo.jpg",
            url);
    }

    [Fact]
    public void Cropping_asks_for_an_exact_box_and_lets_the_provider_choose_the_subject()
    {
        var url = ImageDelivery.Cropped(Uploaded, 800, 600);

        Assert.Contains("c_fill,g_auto,w_800,h_600", url!);
        Assert.Contains("/v1712345678/sites/abc123/photo.jpg", url!);
    }

    [Fact]
    public void Every_transform_asks_for_automatic_format_and_quality()
    {
        // The resize matters less than these two on a phone connection.
        Assert.Contains("f_auto,q_auto", ImageDelivery.Sized(Uploaded, 400)!);
        Assert.Contains("f_auto,q_auto", ImageDelivery.Cropped(Uploaded, 400, 300)!);
    }

    [Theory]
    [InlineData("https://example.com/photos/shopfront.jpg")]
    [InlineData("/local/asset.png")]
    [InlineData("https://images.unsplash.com/photo-123")]
    public void A_url_from_anywhere_else_is_left_exactly_as_it_is(string original)
    {
        // Sites built before uploads existed, and hand-entered URLs, must keep rendering.
        Assert.Equal(original, ImageDelivery.Sized(original, 800));
        Assert.Equal(original, ImageDelivery.Cropped(original, 800, 600));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_url_stays_missing(string? original)
    {
        Assert.Equal(original, ImageDelivery.Sized(original, 800));
        Assert.Equal(original, ImageDelivery.Cropped(original, 800, 600));
    }

    [Fact]
    public void A_url_that_already_carries_a_transformation_is_not_chained_onto()
    {
        // Prepending a second transform chains them, which silently means something different
        // from what either one asked for.
        const string transformed =
            "https://res.cloudinary.com/demo-cloud/image/upload/w_400,c_scale/v1712345678/sites/abc/photo.jpg";

        Assert.Equal(transformed, ImageDelivery.Sized(transformed, 1200));
    }

    [Fact]
    public void A_transformed_url_is_still_a_working_url_for_the_same_image()
    {
        var url = ImageDelivery.Sized(Uploaded, 600)!;

        // The public id and version must survive: they are what identifies the image.
        Assert.EndsWith("/v1712345678/sites/abc123/photo.jpg", url);
        Assert.StartsWith("https://res.cloudinary.com/demo-cloud/image/upload/", url);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(320)]
    [InlineData(1600)]
    [InlineData(4000)]
    public void Any_requested_width_produces_one_upload_marker_and_that_width(int width)
    {
        var url = ImageDelivery.Sized(Uploaded, width)!;

        Assert.Contains($"w_{width}", url);
        Assert.Equal(2, url.Split("/image/upload/").Length);
    }
}
