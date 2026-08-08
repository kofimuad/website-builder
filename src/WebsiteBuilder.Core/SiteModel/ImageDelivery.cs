namespace WebsiteBuilder.Core.SiteModel;

/// <summary>
/// Builds sized delivery URLs for the images on a site.
///
/// Photos uploaded through the builder are resized on <em>delivery</em>, not on upload: the
/// original is stored once and each slot asks for the size it needs by rewriting the URL. A phone
/// photo therefore does not have to be re-processed when a section is moved, a theme changes, or a
/// new breakpoint is added — and the owner's original is never destroyed by a crop they cannot undo.
///
/// The schema stores a plain URL rather than a provider id so that nothing here is <em>required</em>
/// for an image to render. A URL from anywhere else — a hand-typed one, or an older site built
/// before uploads existed — is passed through untouched.
/// </summary>
public static class ImageDelivery
{
    private const string UploadMarker = "/image/upload/";

    /// <summary>Fits inside <paramref name="width"/> without cropping, and never upscales.</summary>
    public static string? Sized(string? url, int width) =>
        Transform(url, $"c_limit,w_{width}");

    /// <summary>
    /// Fills an exact box, choosing the crop automatically so faces and subjects survive it. Used
    /// where the layout needs every image the same shape — a hero band, a gallery grid.
    /// </summary>
    public static string? Cropped(string? url, int width, int height) =>
        Transform(url, $"c_fill,g_auto,w_{width},h_{height}");

    private static string? Transform(string? url, string sizing)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var marker = url.IndexOf(UploadMarker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return url;
        }

        var start = marker + UploadMarker.Length;
        var end = url.IndexOf('/', start);
        if (end < 0)
        {
            return url;
        }

        // Only insert ahead of the version segment. Anything else already carries a transformation,
        // and prepending a second one chains them — which silently means something different from
        // what either asked for.
        if (!IsVersionSegment(url.AsSpan(start, end - start)))
        {
            return url;
        }

        // f_auto lets the CDN serve WebP or AVIF to browsers that take it; q_auto picks a quality
        // per image rather than a flat number that is wasteful on photos and visibly poor on flat
        // graphics. Both matter more than the resize on a phone connection.
        return $"{url[..start]}f_auto,q_auto,{sizing}/{url[start..]}";
    }

    private static bool IsVersionSegment(ReadOnlySpan<char> segment)
    {
        if (segment.Length < 2 || segment[0] != 'v')
        {
            return false;
        }

        foreach (var c in segment[1..])
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
