using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WebsiteBuilder.Core.Entities;
using WebsiteBuilder.Core.Onboarding;
using WebsiteBuilder.Core.SiteModel;
using WebsiteBuilder.Core.Tenancy;

namespace WebsiteBuilder.Data;

public class WebsiteBuilderDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public WebsiteBuilderDbContext(DbContextOptions<WebsiteBuilderDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<BusinessProfile> BusinessProfiles => Set<BusinessProfile>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<SignInToken> SignInTokens => Set<SignInToken>();

    private static readonly ValueConverter<SiteDefinition, string> DefinitionConverter = new(
        definition => SiteDefinitionSerializer.Serialize(definition),
        json => SiteDefinitionSerializer.Deserialize(json));

    private static readonly ValueConverter<SiteDefinition?, string?> NullableDefinitionConverter = new(
        definition => definition == null ? null : SiteDefinitionSerializer.Serialize(definition),
        json => json == null ? null : SiteDefinitionSerializer.Deserialize(json));

    // Site definitions are mutable object graphs. Without a comparer EF compares them by
    // reference, so editing a section in place would never be detected as a change and the
    // update would be silently dropped.
    private static readonly ValueComparer<SiteDefinition> DefinitionComparer = new(
        (left, right) => SiteDefinitionSerializer.Serialize(left!) == SiteDefinitionSerializer.Serialize(right!),
        definition => SiteDefinitionSerializer.Serialize(definition).GetHashCode(),
        definition => definition.DeepClone());

    private static readonly ValueComparer<SiteDefinition?> NullableDefinitionComparer = new(
        (left, right) =>
            left == null ? right == null
            : right != null && SiteDefinitionSerializer.Serialize(left) == SiteDefinitionSerializer.Serialize(right),
        definition => definition == null ? 0 : SiteDefinitionSerializer.Serialize(definition).GetHashCode(),
        definition => definition == null ? null : definition.DeepClone());

    // Opening hours are a small structured list; stored as jsonb rather than a side table.
    private static readonly ValueConverter<List<OpeningHours>, string> HoursConverter = new(
        hours => JsonSerializer.Serialize(hours, (JsonSerializerOptions?)null),
        json => JsonSerializer.Deserialize<List<OpeningHours>>(json, (JsonSerializerOptions?)null) ?? new());

    private static readonly ValueComparer<List<OpeningHours>> HoursComparer = new(
        (left, right) => JsonSerializer.Serialize(left, (JsonSerializerOptions?)null)
            == JsonSerializer.Serialize(right, (JsonSerializerOptions?)null),
        hours => JsonSerializer.Serialize(hours, (JsonSerializerOptions?)null).GetHashCode(),
        hours => JsonSerializer.Deserialize<List<OpeningHours>>(
            JsonSerializer.Serialize(hours, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null) ?? new());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Subdomain).HasMaxLength(63).IsRequired();
            e.Property(t => t.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(t => t.Subdomain).IsUnique();

            // The dashboard's only query is "tenants for this owner", so the FK is indexed.
            // Restrict, not Cascade: deleting an owner must never silently take live customer
            // websites down with it. Detaching or transferring them is a deliberate act.
            e.HasIndex(t => t.OwnerId);
            e.HasOne<Owner>().WithMany().HasForeignKey(t => t.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Owner>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Email).HasMaxLength(320).IsRequired();
            e.Property(o => o.Name).HasMaxLength(200);
            e.Property(o => o.GoogleSubject).HasMaxLength(64);
            // Email is the identity: Google and magic link for one address must resolve to one row.
            e.HasIndex(o => o.Email).IsUnique();
        });

        modelBuilder.Entity<SignInToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Email).HasMaxLength(320).IsRequired();
            e.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(t => t.ReturnUrl).HasMaxLength(2048);
            // Redemption looks a token up by its hash and nothing else; it must be unique so a
            // hash collision cannot resolve to two rows, and indexed because it is the hot path.
            e.HasIndex(t => t.TokenHash).IsUnique();
            // Rate limiting counts recent tokens per address.
            e.HasIndex(t => new { t.Email, t.CreatedUtc });
        });

        modelBuilder.Entity<Site>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(s => s.TenantId);
            e.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Cascade);

            e.Property(s => s.Draft)
                .HasColumnType("jsonb")
                .HasConversion(DefinitionConverter, DefinitionComparer)
                .IsRequired();

            e.Property(s => s.Published)
                .HasColumnType("jsonb")
                .HasConversion(NullableDefinitionConverter, NullableDefinitionComparer);
        });

        modelBuilder.Entity<BusinessProfile>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.BusinessName).HasMaxLength(200).IsRequired();
            e.Property(p => p.Category).HasMaxLength(200).IsRequired();
            e.Property(p => p.Tone).HasConversion<string>().HasMaxLength(32);
            e.Property(p => p.PrimaryAction).HasConversion<string>().HasMaxLength(32);
            e.Property(p => p.OpeningHours).HasColumnType("jsonb").HasConversion(HoursConverter, HoursComparer);
            // One profile per tenant: onboarding fills it, WB-18 edits the same row.
            e.HasIndex(p => p.TenantId).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Slug).HasMaxLength(120).IsRequired();
            e.Property(p => p.Description).HasMaxLength(4000);
            e.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            // The slug is the product's public address, so it has to be unique per tenant — and
            // only per tenant: two businesses may both sell "jollof".
            e.HasIndex(p => new { p.TenantId, p.Slug }).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Lead>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Name).HasMaxLength(200).IsRequired();
            e.Property(l => l.PhoneNumber).HasMaxLength(40);
            e.Property(l => l.Email).HasMaxLength(200);
            e.Property(l => l.Message).HasMaxLength(4000).IsRequired();
            e.Property(l => l.Channel).HasConversion<string>().HasMaxLength(32);
            // The inbox reads a tenant's leads newest-first; index the sort it always uses.
            e.HasIndex(l => new { l.TenantId, l.CreatedUtc });
            e.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Site>().WithMany().HasForeignKey(l => l.SiteId).OnDelete(DeleteBehavior.Cascade);
        });

        // Tenant-owned entities are filtered on every query. A null TenantId matches nothing
        // rather than everything, so a missing tenant scope fails closed.
        modelBuilder.Entity<Site>()
            .HasQueryFilter(s => _tenantContext.TenantId != null && s.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<BusinessProfile>()
            .HasQueryFilter(p => _tenantContext.TenantId != null && p.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Lead>()
            .HasQueryFilter(l => _tenantContext.TenantId != null && l.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Product>()
            .HasQueryFilter(p => _tenantContext.TenantId != null && p.TenantId == _tenantContext.TenantId);

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges()
    {
        ApplyTenantId();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTenantId();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Stamps new tenant-owned rows with the ambient tenant and blocks writes to any other tenant.</summary>
    private void ApplyTenantId()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var ambient = _tenantContext.TenantId
                ?? throw new InvalidOperationException(
                    $"Cannot save {entry.Entity.GetType().Name}: no tenant is in scope.");

            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.TenantId == Guid.Empty)
                {
                    entry.Entity.TenantId = ambient;
                }
                else if (entry.Entity.TenantId != ambient)
                {
                    throw new InvalidOperationException(
                        $"Cannot create {entry.Entity.GetType().Name} for tenant {entry.Entity.TenantId} while tenant {ambient} is in scope.");
                }
            }
            else if (entry.Entity.TenantId != ambient)
            {
                throw new InvalidOperationException(
                    $"Cannot modify {entry.Entity.GetType().Name} owned by tenant {entry.Entity.TenantId} while tenant {ambient} is in scope.");
            }
        }
    }
}
