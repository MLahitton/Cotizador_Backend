using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlIntegrationCollection
    : ICollectionFixture<PostgreSqlIntegrationFixture>
{
    public const string Name = "PostgreSQL integration";
}

public sealed class PostgreSqlIntegrationFixture : IAsyncLifetime
{
    private const string EnvironmentVariable =
        "COTIZADOR_TEST_POSTGRES_ADMIN_CONNECTION_STRING";
    private const string DatabasePrefix = "cotizador_backend_test_";
    private string? _adminConnectionString;

    public string? UnavailableReason { get; private set; }
    public string? ConnectionString { get; private set; }
    public string? DatabaseName { get; private set; }
    public bool IsAvailable => ConnectionString is not null;

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(
            EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            UnavailableReason = $"Configure {EnvironmentVariable} para ejecutar PostgreSQL.";
            return;
        }

        NpgsqlConnectionStringBuilder admin;
        try
        {
            admin = new NpgsqlConnectionStringBuilder(configured);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariable} no es una connection string valida.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(admin.Host)
            || string.IsNullOrWhiteSpace(admin.Username)
            || !string.Equals(admin.Database, "postgres",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariable} debe apuntar explicitamente a la base administrativa postgres.");
        }

        DatabaseName = DatabasePrefix
            + Guid.NewGuid().ToString("N");
        EnsureTemporaryDatabaseName(DatabaseName);
        _adminConnectionString = admin.ConnectionString;

        await using (var connection = new NpgsqlConnection(
            _adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE {DatabaseName}";
            await command.ExecuteNonQueryAsync();
        }

        admin.Database = DatabaseName;
        ConnectionString = admin.ConnectionString;
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    public ApplicationDbContext CreateDbContext()
    {
        if (ConnectionString is null)
        {
            throw new InvalidOperationException(UnavailableReason);
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    public void RequireAvailable()
    {
        if (!IsAvailable)
        {
            Assert.Skip(UnavailableReason ?? "PostgreSQL no disponible.");
        }
    }

    public async Task ResetAsync()
    {
        RequireAvailable();
        await using var context = CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            DO $reset$
            DECLARE table_row record;
            BEGIN
              FOR table_row IN
                SELECT schemaname, tablename
                FROM pg_tables
                WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
                  AND tablename <> '__EFMigrationsHistory'
              LOOP
                EXECUTE format('TRUNCATE TABLE %I.%I RESTART IDENTITY CASCADE',
                  table_row.schemaname, table_row.tablename);
              END LOOP;
            END $reset$;
            """);
    }

    public async ValueTask DisposeAsync()
    {
        if (_adminConnectionString is null || DatabaseName is null)
        {
            return;
        }

        EnsureTemporaryDatabaseName(DatabaseName);
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(
            _adminConnectionString);
        await connection.OpenAsync();
        await using (var terminate = connection.CreateCommand())
        {
            terminate.CommandText =
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity "
                + "WHERE datname = @database AND pid <> pg_backend_pid()";
            terminate.Parameters.AddWithValue("database", DatabaseName);
            await terminate.ExecuteNonQueryAsync();
        }

        EnsureTemporaryDatabaseName(DatabaseName);
        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE {DatabaseName}";
        await drop.ExecuteNonQueryAsync();
    }

    private static void EnsureTemporaryDatabaseName(string databaseName)
    {
        if (!databaseName.StartsWith(DatabasePrefix,
                StringComparison.Ordinal)
            || databaseName.Length != DatabasePrefix.Length + 32
            || databaseName[DatabasePrefix.Length..].Any(character =>
                !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException(
                "Se rechazo una operacion sobre una base no temporal.");
        }
    }
}
