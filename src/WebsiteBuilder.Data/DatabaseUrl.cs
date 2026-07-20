using Npgsql;

namespace WebsiteBuilder.Data;

public static class DatabaseUrl
{
    /// <summary>
    /// Railway (and Heroku-style providers) expose Postgres as a postgres:// URL, which Npgsql
    /// does not accept. Converts such a URL to an Npgsql connection string; already-key-value
    /// strings are returned unchanged.
    /// </summary>
    public static string ToNpgsqlConnectionString(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            SslMode = SslMode.Prefer,
        };

        return builder.ConnectionString;
    }
}
