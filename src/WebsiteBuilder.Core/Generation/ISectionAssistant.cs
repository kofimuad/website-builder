using WebsiteBuilder.Core.SiteModel;

namespace WebsiteBuilder.Core.Generation;

/// <summary>
/// Revises the text of a single section from a plain-language instruction. Only that section is
/// changed; the returned section keeps the same type and id. Available only when Claude is
/// configured — the editor hides the assistant otherwise.
/// </summary>
public interface ISectionAssistant
{
    Task<SiteSection> ReviseAsync(SiteSection section, string instruction, CancellationToken cancellationToken = default);
}

public sealed class SectionAssistantException : Exception
{
    public SectionAssistantException(string message) : base(message) { }
    public SectionAssistantException(string message, Exception inner) : base(message, inner) { }
}
