using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Bhoomisetu.Api.Persistence;

/// <summary>
/// Applies db/migrations/*.sql in file-name order, each in its own transaction, recording a checksum.
/// A PostgreSQL advisory lock stops two instances migrating at once. An already-applied file whose content
/// changed is an error: migrations are append-only, fix forward with a new file.
/// </summary>
public sealed class MigrationRunner(string connectionString, string migrationsDirectory, ILogger<MigrationRunner> logger)
{
    private const long AdvisoryLockKey = 0x42_48_4F_4F_4D_49; // "BHOOMI"

    public async Task<IReadOnlyList<string>> ApplyAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(migrationsDirectory))
        {
            throw new DirectoryNotFoundException($"Migrations directory not found: {migrationsDirectory}");
        }

        var files = Directory.GetFiles(migrationsDirectory, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList();
        var applied = new List<string>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection))
        {
            lockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using (var create = new NpgsqlCommand(
                "CREATE TABLE IF NOT EXISTS schema_migrations (version text PRIMARY KEY, checksum text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now())",
                connection))
            {
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            var existing = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var read = new NpgsqlCommand("SELECT version, checksum FROM schema_migrations", connection))
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    existing[reader.GetString(0)] = reader.GetString(1);
                }
            }

            foreach (var file in files)
            {
                var version = Path.GetFileNameWithoutExtension(file);
                var sql = await File.ReadAllTextAsync(file, cancellationToken);
                // Line endings are normalised first, so a Windows (CRLF) checkout gives the same checksum.
                var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql.ReplaceLineEndings("\n"))));

                if (existing.TryGetValue(version, out var appliedChecksum))
                {
                    if (!string.Equals(appliedChecksum, checksum, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Migration {version} was changed after it was applied. Add a new migration instead of editing an applied one.");
                    }

                    continue;
                }

                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await using (var apply = new NpgsqlCommand(sql, connection, transaction))
                {
                    apply.CommandTimeout = 300;
                    await apply.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var record = new NpgsqlCommand("INSERT INTO schema_migrations (version, checksum) VALUES (@v, @c)", connection, transaction))
                {
                    record.Parameters.AddWithValue("v", version);
                    record.Parameters.AddWithValue("c", checksum);
                    await record.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Applied migration {Version}", version);
                applied.Add(version);
            }
        }
        finally
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
            unlock.Parameters.AddWithValue("key", AdvisoryLockKey);
            await unlock.ExecuteNonQueryAsync(CancellationToken.None);
        }

        return applied;
    }
}
