using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using NightInjection.Core.Models;
using NightInjection.Infrastructure.Storage;

namespace NightInjection.Tests;

public sealed class HistoryRepositoryTests
{
    [Fact]
    public async Task MigratesLegacyLightningHistoryExactlyOnce()
    {
        using var environment = new TestEnvironment();
        environment.Paths.EnsureApplicationDirectories();
        await using (var connection = new SqliteConnection($"Data Source={environment.Paths.DatabasePath}"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE lightning_history (
                    app_id TEXT PRIMARY KEY, game_name TEXT, cover_path TEXT, lua_path TEXT,
                    manifest_path TEXT, repo TEXT, applied_at INTEGER, status TEXT NOT NULL, error TEXT);
                INSERT INTO lightning_history VALUES
                    ('220', 'Half-Life 2', NULL, '220.lua', '220.manifest', 'legacy-repo', 1767225600000, 'ok', NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = new HistoryRepository(environment.Paths, NullLogger<HistoryRepository>.Instance);
        await repository.InitializeAsync();
        await repository.InitializeAsync();
        var records = await repository.ListAsync();

        var record = Assert.Single(records);
        Assert.Equal("LegacySnapshot", record.Operation);
        Assert.Equal("220", record.AppId);
        Assert.Equal("legacy-repo", record.Repository);
    }

    [Fact]
    public async Task InitializesVersionedSchemaAndSupportsSearchFiltersAndClear()
    {
        using var environment = new TestEnvironment();
        var repository = new HistoryRepository(environment.Paths, NullLogger<HistoryRepository>.Instance);
        await repository.InitializeAsync();
        await repository.AddAsync(new NewHistoryRecord(
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), "Inject", "220", "220.lua", "Success", "done", "repo"));
        await repository.AddAsync(new NewHistoryRecord(
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"), "Remove", "440", null, "Error", "locked", null));

        var all = await repository.ListAsync();
        var search = await repository.ListAsync(new HistoryQuery(Search: "locked"));
        var filtered = await repository.ListAsync(new HistoryQuery(Result: "Success"));

        Assert.Equal(2, all.Count);
        Assert.Single(search);
        Assert.Equal("440", search[0].AppId);
        Assert.Single(filtered);
        Assert.Equal("220", filtered[0].AppId);

        await repository.ClearAsync();
        Assert.Empty(await repository.ListAsync());
        Assert.True(File.Exists(environment.Paths.DatabasePath));
    }

    [Fact]
    public async Task LikeSearchTreatsPercentAsLiteral()
    {
        using var environment = new TestEnvironment();
        var repository = new HistoryRepository(environment.Paths, NullLogger<HistoryRepository>.Instance);
        await repository.AddAsync(new NewHistoryRecord(DateTimeOffset.UtcNow, "Inject", "1", null, "Success", "100% complete", null));
        await repository.AddAsync(new NewHistoryRecord(DateTimeOffset.UtcNow, "Inject", "2", null, "Success", "ordinary", null));

        var records = await repository.ListAsync(new HistoryQuery(Search: "%"));

        Assert.Single(records);
        Assert.Equal("1", records[0].AppId);
    }
}
