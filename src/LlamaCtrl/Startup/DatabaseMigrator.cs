using LlamaCtrl.Data;
using Microsoft.EntityFrameworkCore;

namespace LlamaCtrl.Startup;

static class DatabaseMigrator
{
    internal static async Task MigrateAsync(IServiceProvider services, string modelsDir, string? binary)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        await MigrateBenchmarkResultsAsync(db);
        await MigrateProfilesAsync(db);
        await CreateBinaryAndDirectoryTablesAsync(db);
        await AddSelectedBinaryColumnAsync(db);
        await SeedDefaultsAsync(db, modelsDir, binary);
    }

    private static async Task MigrateBenchmarkResultsAsync(AppDbContext db)
    {
        var existingColumns = db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('BenchmarkResults')")
            .AsEnumerable().ToHashSet();

        if (!existingColumns.Contains("ChartDataJson"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE BenchmarkResults ADD COLUMN ChartDataJson TEXT");

        if (!existingColumns.Contains("BenchmarkType"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE BenchmarkResults ADD COLUMN BenchmarkType TEXT NOT NULL DEFAULT 'token-generation'");

        if (!existingColumns.Contains("RoundsJson"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE BenchmarkResults ADD COLUMN RoundsJson TEXT NULL");
    }

    private static async Task MigrateProfilesAsync(AppDbContext db)
    {
        var profileColumns = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Profiles')")
            .ToListAsync();

        if (!profileColumns.Contains("ParametersJson"))
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Profiles ADD COLUMN ParametersJson TEXT");

        if (!profileColumns.Contains("CustomArgsJson"))
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Profiles ADD COLUMN CustomArgsJson TEXT");

        if (!profileColumns.Contains("ContextSize"))
            return;

        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Profiles
            SET ParametersJson = json_object(
                '-c',      CAST(ContextSize AS TEXT),
                '-b',      CAST(BatchSize AS TEXT),
                '-ngl',    CAST(GpuLayers AS TEXT),
                '-t',      CAST(Threads AS TEXT),
                '-fa',     CASE WHEN UseFlashAttn = 1 THEN 'on' ELSE 'off' END,
                '--temp',  CAST(Temperature AS TEXT),
                '--top-p', CAST(TopP AS TEXT),
                '--top-k', CAST(TopK AS TEXT)
            )
            WHERE ParametersJson IS NULL
            """);

        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Profiles
            SET ParametersJson = json_insert(ParametersJson, '$."--no-mmap"', '')
            WHERE UseMmap = 0
            """);

        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Profiles
            SET ParametersJson = json_insert(ParametersJson, '$."--mlock"', '')
            WHERE UseMlock = 1
            """);

        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Profiles
            SET ParametersJson = json_insert(ParametersJson, '$."-sp"', SystemPrompt)
            WHERE SystemPrompt IS NOT NULL AND SystemPrompt != ''
            """);

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE Profiles SET SystemPrompt = NULL WHERE SystemPrompt IS NOT NULL");
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE Profiles SET UseMmap = 1 WHERE UseMmap = 0");
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE Profiles SET UseMlock = 0 WHERE UseMlock = 1");
    }

    private static async Task CreateBinaryAndDirectoryTablesAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS LlamaServerBinaries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Path TEXT NOT NULL,
                IsDefault INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )
            """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS ModelDirectories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Path TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )
            """);
    }

    private static async Task AddSelectedBinaryColumnAsync(AppDbContext db)
    {
        var profileCols = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Profiles')")
            .ToListAsync();

        if (!profileCols.Contains("SelectedBinaryId"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Profiles ADD COLUMN SelectedBinaryId INTEGER");
    }

    private static async Task SeedDefaultsAsync(AppDbContext db, string modelsDir, string? binary)
    {
        var binaryCount = await db.LlamaServerBinaries.CountAsync();
        if (binaryCount == 0)
        {
            var seedBinary = binary ?? "llama-server";
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO LlamaServerBinaries (Name, Path, IsDefault, CreatedAt, UpdatedAt) VALUES ('Default', {0}, 1, {1}, {2})",
                seedBinary, DateTime.UtcNow.ToString("o"), DateTime.UtcNow.ToString("o"));
        }

        var dirCount = await db.ModelDirectories.CountAsync();
        if (dirCount == 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO ModelDirectories (Name, Path, CreatedAt, UpdatedAt) VALUES ('Models', {0}, {1}, {2})",
                modelsDir, DateTime.UtcNow.ToString("o"), DateTime.UtcNow.ToString("o"));
        }
    }
}
