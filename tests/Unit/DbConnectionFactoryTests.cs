using FluentAssertions;
using Infrastructure;
using Npgsql;
using Xunit;

namespace Unit;

public sealed class DbConnectionFactoryTests
{
    [Fact]
    public void NormalizePostgresConnectionString_ConvertsUrlToNpgsqlFormat()
    {
        var input = "postgresql://demo-user:demo-pass@db.example.com:25060/demo_db?sslmode=verify-full";

        var normalized = DbConnectionFactory.NormalizePostgresConnectionString(input);
        var builder = new NpgsqlConnectionStringBuilder(normalized);

        builder.Host.Should().Be("db.example.com");
        builder.Port.Should().Be(25060);
        builder.Database.Should().Be("demo_db");
        builder.Username.Should().Be("demo-user");
        builder.Password.Should().Be("demo-pass");
        builder.SslMode.Should().Be(SslMode.VerifyFull);
        builder.TrustServerCertificate.Should().BeTrue();
    }

    [Fact]
    public void NormalizePostgresConnectionString_KeepsKeyValueFormat()
    {
        var input = "Host=localhost;Port=5432;Database=ingestion;Username=app;Password=secret;";

        var normalized = DbConnectionFactory.NormalizePostgresConnectionString(input);

        normalized.Should().Be(input.Trim());
    }
}
