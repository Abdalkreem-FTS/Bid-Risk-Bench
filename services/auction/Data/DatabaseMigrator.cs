using EvolveDb;
using Npgsql;

namespace BidRisk.Auction.Data;

// Runs before the port opens, so a healthy service always has the current schema.
public static class DatabaseMigrator
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public static async Task RunAsync(string connectionString, string migrationsPath, ILogger logger)
    {
        if (!Directory.Exists(migrationsPath))
        {
            throw new DirectoryNotFoundException(
                $"no migrations directory at '{Path.GetFullPath(migrationsPath)}'. " +
                "The schema lives in db/migrations and must be present next to the service.");
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);

                var evolve = new Evolve(connection, message => logger.LogInformation("evolve: {Message}", message))
                {
                    Locations = [migrationsPath],
                    MetadataTableName = "schema_history",
                    IsEraseDisabled = true,
                };

                evolve.Migrate();
                logger.LogInformation("schema is up to date");

                return;
            }
            catch (Exception exception) when (attempt < MaxAttempts && IsTransient(exception))
            {
                logger.LogWarning(
                    "database not ready (attempt {Attempt}/{MaxAttempts}): {Reason}",
                    attempt,
                    MaxAttempts,
                    exception.Message);

                await Task.Delay(RetryDelay);
            }
        }
    }

    private static bool IsTransient(Exception exception) =>
        exception is NpgsqlException or TimeoutException
        || exception.InnerException is NpgsqlException or TimeoutException;
}
