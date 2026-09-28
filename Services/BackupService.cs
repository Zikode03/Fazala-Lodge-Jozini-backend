using Microsoft.Data.Sqlite;

namespace FalazaLodge.Api.Services;

public sealed class BackupService(IConfiguration configuration, ILogger<BackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CreateBackupAsync(stoppingToken);
                CleanupOldBackups();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falaza database backup failed.");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    public Task<string?> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=falaza.db";
        var builder = new SqliteConnectionStringBuilder(connectionString);

        if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:")
        {
            logger.LogWarning("Backup skipped because the database is not file-based SQLite.");
            return Task.FromResult<string?>(null);
        }

        var sourcePath = Path.GetFullPath(builder.DataSource);
        if (!File.Exists(sourcePath))
        {
            logger.LogWarning("Backup skipped because database file {DatabasePath} does not exist.", sourcePath);
            return Task.FromResult<string?>(null);
        }

        var backupDirectory = Path.GetFullPath(configuration["Backups:Directory"] ?? "Backups");
        Directory.CreateDirectory(backupDirectory);

        var backupPath = Path.Combine(backupDirectory, $"falaza-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");

        using var source = new SqliteConnection(connectionString);
        using var destination = new SqliteConnection($"Data Source={backupPath}");
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);

        logger.LogInformation("Database backup created at {BackupPath}", backupPath);
        return Task.FromResult<string?>(backupPath);
    }

    private void CleanupOldBackups()
    {
        var backupDirectory = Path.GetFullPath(configuration["Backups:Directory"] ?? "Backups");
        if (!Directory.Exists(backupDirectory)) return;

        var retentionDays = Math.Max(1, configuration.GetValue("Backups:RetentionDays", 14));
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        foreach (var file in Directory.EnumerateFiles(backupDirectory, "falaza-*.db"))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                File.Delete(file);
                logger.LogInformation("Deleted expired database backup {BackupPath}", file);
            }
        }
    }
}
