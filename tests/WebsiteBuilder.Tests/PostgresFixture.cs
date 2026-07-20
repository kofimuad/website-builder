using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using WebsiteBuilder.Core.Tenancy;
using WebsiteBuilder.Data;

namespace WebsiteBuilder.Tests;

/// <summary>Spins up a real Postgres once for the whole collection; query filters must be exercised against the real provider.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        using var db = CreateContext(new TenantContext());
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public WebsiteBuilderDbContext CreateContext(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<WebsiteBuilderDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new WebsiteBuilderDbContext(options, tenantContext);
    }

    public WebsiteBuilderDbContext CreateContext(Guid tenantId) =>
        CreateContext(new TenantContext { TenantId = tenantId });
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
