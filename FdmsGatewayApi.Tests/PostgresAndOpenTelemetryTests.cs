using FdmsGatewayApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class PostgresAndOpenTelemetryTests
{
    [Theory]
    [InlineData("PostgreSQL", "Host=localhost;Database=fdms_test;Username=postgres;Password=postgres")]
    [InlineData("Postgres", "Host=localhost;Database=fdms_test;Username=postgres;Password=postgres")]
    public void DbContext_ConfiguresNpgsql_WhenProviderIsPostgreSQL(string providerName, string connectionString)
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DatabaseOptions:Provider", providerName },
                { "ConnectionStrings:PostgresDatabase", connectionString }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<ImmutabilityInterceptor>();

        string dbProvider = config["DatabaseOptions:Provider"] ?? "SqlServer";
        string connStr = string.Equals(dbProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase) || string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase)
            ? (config.GetConnectionString("PostgresDatabase") ?? connectionString)
            : connectionString;

        // Act
        services.AddDbContext<FdmsDbContext>((sp, options) =>
        {
            var interceptor = sp.GetRequiredService<ImmutabilityInterceptor>();
            if (string.Equals(dbProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase) || string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
            {
                options.UseNpgsql(connStr);
            }
            options.AddInterceptors(interceptor);
        });

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();

        // Assert
        Assert.NotNull(db);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
    }

    [Fact]
    public void OpenTelemetry_Configuration_ParsesCorrectly()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "OpenTelemetry:ServiceName", "FdmsGatewayApi" },
                { "OpenTelemetry:ServiceVersion", "1.0.0" },
                { "OpenTelemetry:EnablePrometheus", "true" },
                { "OpenTelemetry:OtlpEndpoint", "http://localhost:4317" }
            })
            .Build();

        // Act
        string serviceName = config["OpenTelemetry:ServiceName"] ?? "";
        bool enablePrometheus = config.GetValue<bool>("OpenTelemetry:EnablePrometheus", false);
        string otlpEndpoint = config["OpenTelemetry:OtlpEndpoint"] ?? "";

        // Assert
        Assert.Equal("FdmsGatewayApi", serviceName);
        Assert.True(enablePrometheus);
        Assert.Equal("http://localhost:4317", otlpEndpoint);
    }
}
