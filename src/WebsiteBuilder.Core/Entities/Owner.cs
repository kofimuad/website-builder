namespace WebsiteBuilder.Core.Entities;

/// <summary>
/// A person who signs in and owns tenants (WB-15). Deliberately *not* <c>ITenantOwned</c>: an owner
/// is what grants tenant scope in the first place, so it cannot itself be read through a tenant
/// filter — the lookup that resolves a sign-in happens before any tenant is known.
/// </summary>
public class Owner
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Lowercased at the boundary and unique. Email is the identity: signing in with Google and
    /// with a magic link for the same address must land on the same owner, not create a second one.
    /// </summary>
    public required string Email { get; set; }

    /// <summary>Display name, from Google or the business name at onboarding. Blank until we learn one.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Google's stable subject claim, set the first time they use Google. Kept because an address
    /// can be reassigned inside a Workspace domain; the subject is the durable identifier.
    /// </summary>
    public string? GoogleSubject { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSignedInUtc { get; set; }

    /// <summary>Normalises an address for storage and lookup. Both must use this or they will disagree.</summary>
    public static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();
}
