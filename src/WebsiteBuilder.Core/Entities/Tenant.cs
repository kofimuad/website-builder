namespace WebsiteBuilder.Core.Entities;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Subdomain { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The person who may manage this tenant (WB-15). Nullable because tenants created before
    /// sign-in existed have no owner, and because the demo seeder makes one without a person
    /// behind it. An unowned tenant still serves its published site; it simply cannot be edited.
    /// </summary>
    public Guid? OwnerId { get; set; }
}
