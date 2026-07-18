using DigiPhoto.Booth.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DigiPhoto.Booth.Tests;

public sealed class BoothDatabaseInitializerTests
{
    [Fact]
    public async Task FreshDatabaseGetsTheCompleteVersionOneSchema()
    {
        await using var database = new TemporaryBoothDatabase();

        await BoothDatabaseInitializer.InitializeAsync(database.Factory);

        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(BoothDatabaseInitializer.CurrentSchemaVersion, await ScalarIntAsync(
            connection,
            "PRAGMA user_version;"));
        Assert.Equal(1, await ScalarIntAsync(
            connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'event_bundles';"));
        Assert.Equal(1, await ScalarIntAsync(
            connection,
            "SELECT COUNT(*) FROM pragma_table_info('booth_sessions') WHERE name = 'RecoveryReason';"));
        Assert.Equal(1, await ScalarIntAsync(
            connection,
            "SELECT COUNT(*) FROM pragma_table_info('session_media') WHERE name = 'TenantId';"));
        Assert.Equal(1, await ScalarIntAsync(
            connection,
            "SELECT COUNT(*) FROM pragma_table_info('print_jobs') WHERE name = 'TenantId';"));
    }

    [Fact]
    public async Task IncompatibleVersionOneDatabaseIsRejectedWithoutDeletingState()
    {
        await using var database = new TemporaryBoothDatabase();
        await using (var connection = new SqliteConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE booth_sessions (Id TEXT NOT NULL PRIMARY KEY, Marker TEXT); " +
                "INSERT INTO booth_sessions (Id, Marker) VALUES ('kept-v1', 'preserve-v1'); " +
                "PRAGMA user_version = 1;";
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<BoothSchemaVersionException>(() =>
            BoothDatabaseInitializer.InitializeAsync(database.Factory));

        Assert.Contains("left unchanged", exception.Message, StringComparison.Ordinal);
        await using var verification = new SqliteConnection(database.ConnectionString);
        await verification.OpenAsync();
        Assert.Equal(1, await ScalarIntAsync(
            verification,
            "SELECT COUNT(*) FROM booth_sessions WHERE Marker = 'preserve-v1';"));
        Assert.Equal(1, await ScalarIntAsync(verification, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task IncompleteUnversionedDatabaseIsRejectedWithoutDeletingState()
    {
        await using var database = new TemporaryBoothDatabase();
        await using (var connection = new SqliteConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE booth_sessions (Id TEXT NOT NULL PRIMARY KEY, Marker TEXT); " +
                "INSERT INTO booth_sessions (Id, Marker) VALUES ('kept', 'preserve-me');";
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<BoothSchemaVersionException>(() =>
            BoothDatabaseInitializer.InitializeAsync(database.Factory));

        Assert.Contains("left unchanged", exception.Message, StringComparison.Ordinal);
        await using var verification = new SqliteConnection(database.ConnectionString);
        await verification.OpenAsync();
        Assert.Equal(1, await ScalarIntAsync(
            verification,
            "SELECT COUNT(*) FROM booth_sessions WHERE Marker = 'preserve-me';"));
        Assert.Equal(0, await ScalarIntAsync(verification, "PRAGMA user_version;"));
    }

    private static async Task<int> ScalarIntAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class TemporaryBoothDatabase : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "digiphoto-booth-schema-tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryBoothDatabase()
        {
            Directory.CreateDirectory(_root);
            ConnectionString = $"Data Source={Path.Combine(_root, "booth.db")}";
            var options = new DbContextOptionsBuilder<BoothDbContext>()
                .UseSqlite(ConnectionString)
                .Options;
            Factory = new TestContextFactory(options);
        }

        public string ConnectionString { get; }

        public IDbContextFactory<BoothDbContext> Factory { get; }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
