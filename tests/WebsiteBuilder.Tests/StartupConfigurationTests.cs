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

    [Fact]
    public void A_mail_provider_without_a_from_address_stops_the_app()
    {
        // Otherwise the symptom is silence: links reported as sent that never arrive anywhere.
        using var factory = new ConfiguredAppFactory(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://u:p@127.0.0.1:1/db",
            ["RunMigrationsOnStartup"] = "false",
            ["Email:SmtpHost"] = "smtp.resend.com",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Email:FromAddress", exception.Message);
    }

    [Fact]
    public void An_implicit_tls_smtp_port_stops_the_app()
    {
        // System.Net.Mail cannot speak implicit TLS, so a send on 465 hangs until it times out
        // rather than failing — which reads as the provider being at fault.
        using var factory = new ConfiguredAppFactory(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://u:p@127.0.0.1:1/db",
            ["RunMigrationsOnStartup"] = "false",
            ["Email:SmtpHost"] = "smtp.resend.com",
            ["Email:FromAddress"] = "no-reply@csbuild.app",
            ["Email:SmtpPort"] = "465",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("587", exception.Message);
    }

    [Fact]
    public void The_starttls_submission_port_is_accepted()
    {
        using var factory = new ConfiguredAppFactory(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://u:p@127.0.0.1:1/db",
            ["RunMigrationsOnStartup"] = "false",
            ["Email:SmtpHost"] = "smtp.resend.com",
            ["Email:FromAddress"] = "no-reply@csbuild.app",
            ["Email:SmtpPort"] = "587",
        });

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.True(
            exception is null || !exception.Message.Contains("SmtpPort"),
            $"Startup rejected the standard submission port: {exception?.Message}");
    }

    [Theory]
    [InlineData("Images:CloudName")]
    [InlineData("Images:ApiKey")]
    [InlineData("Images:ApiSecret")]
    public void Half_configured_image_credentials_stop_the_app_rather_than_silently_disabling_uploads(string key)
    {
        // The symptom of a silently-disabled image store is an editor that never shows the upload
        // button, which is a long way from the cause.
        using var factory = new ConfiguredAppFactory(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://u:p@127.0.0.1:1/db",
            ["RunMigrationsOnStartup"] = "false",
            [key] = "set-but-alone",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("CloudName, ApiKey and ApiSecret", exception.Message);
    }

    [Fact]
    public void No_image_credentials_at_all_is_a_supported_configuration()
    {
        using var factory = new ConfiguredAppFactory(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://u:p@127.0.0.1:1/db",
            ["RunMigrationsOnStartup"] = "false",
        });

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.True(
            exception is null || !exception.Message.Contains("CloudName"),
            $"Startup objected to having no image provider: {exception?.Message}");
    }
}
