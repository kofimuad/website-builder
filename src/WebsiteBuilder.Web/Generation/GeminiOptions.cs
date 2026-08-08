namespace WebsiteBuilder.Web.Generation;

/// <summary>Everything about the model provider that might need to change without a deploy.</summary>
public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>
    /// From Google AI Studio. Read from <c>GEMINI_API_KEY</c> as well, because that is the name
    /// every Google example uses and the one someone will reach for on Railway.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Configurable because Google retires model ids on a schedule — <c>gemini-2.0-flash</c> is
    /// already shut down — and being able to move to the next one from a Railway variable is the
    /// difference between an outage and a restart.
    /// </summary>
    public string Model { get; set; } = "gemini-3.6-flash";

    /// <summary>Small, self-contained copy; well under the point where a non-streaming call is at risk.</summary>
    public int MaxOutputTokens { get; set; } = 8000;

    /// <summary>Paid-tier list price, US dollars per million tokens. Free tier reports as ~$0 anyway.</summary>
    public decimal InputPricePerMillion { get; set; } = 1.50m;

    public decimal OutputPricePerMillion { get; set; } = 7.50m;

    /// <summary>Overridable so tests can point the client at a stub without touching the code.</summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
}
