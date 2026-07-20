using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace WebsiteBuilder.Tests;

public class StartupConfigurationTests
{
    private sealed class ConfiguredAppFactory(Dictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(settings));
            return base.CreateHost(builder);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_database_url_is_reported_as_missing_rather_than_passed_on(string value)
    {
        // Railway resolves a reference to a service that does not exist as an empty string.
        using var factory = new ConfiguredAppFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = value,
            ["DATABASE_URL"] = value,
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("DATABASE_URL", exception.Message);
        Assert.Contains("did not resolve", exception.Message);
    }

    [Fact]
    public void DATABASE_URL_is_used_when_no_connection_string_is_configured()
    {
        // Not a valid server, but it must get far enough to prove the value was accepted.
        using var factory = new ConfiguredAppFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "",
            ["DATABASE_URL"] = "postgresql://u:p@127.0.0.1:1/db",
            ["RunMigrationsOnStartup"] = "false",
        });

        // Startup gets past configuration; any later failure is not an InvalidOperationException
        // about the missing connection string.
        var exception = Record.Exception(() => factory.CreateClient());

        Assert.True(
            exception is null || !exception.Message.Contains("No database connection string"),
            $"Startup rejected a valid DATABASE_URL: {exception?.Message}");
    }
}
