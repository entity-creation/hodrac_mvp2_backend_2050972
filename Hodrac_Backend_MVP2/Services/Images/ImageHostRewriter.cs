using System.Data.Common;
using Hodrac_Backend_MVP2.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Hodrac_Backend_MVP2.Services.Images;

/// <summary>One image column's result: rows whose value starts with the old base.</summary>
public record ImageColumnRewrite(string Table, string Column, int Matched, int Updated, IReadOnlyList<string> Samples);

/// <summary>What <see cref="ImageHostRewriter.RunAsync"/> found (and changed, when applied).</summary>
public record RewriteReport(string FromBase, string ToBase, bool Applied, IReadOnlyList<ImageColumnRewrite> Columns)
{
    public int TotalMatched => Columns.Sum(c => c.Matched);
    public int TotalUpdated => Columns.Sum(c => c.Updated);
}

/// <summary>
/// Moves stored image URLs from one host to another (<c>dotnet run -- --rewrite-image-host</c>).
/// Only values that START with the old base are touched; the base is swapped for the new one and
/// the rest of the URL (the file name) is kept. Dry run by default; with apply, every column is
/// updated in one transaction. Running it again changes nothing, and swapping the bases reverses it.
/// </summary>
public class ImageHostRewriter
{
    /// <summary>Every Postgres column that stores an absolute image URL (MongoDB holds none).</summary>
    public static readonly IReadOnlyList<(string Table, string Column)> ImageColumns = new[]
    {
        ("DestinationImages", "ImageUrl"),
        ("DestinationImages", "ThumbnailUrl"),
        ("Countries", "GlobalHeroImage"),
        ("Wishlists", "WishlistHeroImage"),
        ("ItineraryItems", "ImageUrl"),
        ("Creators", "AvatarUrl"),
        ("AspNetUsers", "AvatarUrl"),
        ("Partners", "LogoUrl"),
        ("HostProfiles", "LogoUrl"),
        ("Events", "ImageUrl"),
        ("BlogPosts", "CoverImageUrl"),
        ("TripPosts", "CoverImageUrl"),
    };

    private const int SampleCount = 3;

    private readonly HodracDbContext _db;
    private readonly ILogger<ImageHostRewriter> _logger;

    public ImageHostRewriter(HodracDbContext db, ILogger<ImageHostRewriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<RewriteReport> RunAsync(string fromBase, string toBase, bool apply, CancellationToken ct = default)
    {
        var from = ImagesOptions.NormalizeBaseUrl(fromBase);
        var to = ImagesOptions.NormalizeBaseUrl(toBase);
        if (string.Equals(from, to, StringComparison.Ordinal))
            throw new ArgumentException($"The old and new image bases are the same ({from}); nothing to rewrite.");
        // One base inside the other would make a second run rewrite the URLs again (not idempotent).
        if (to.StartsWith(from, StringComparison.Ordinal) || from.StartsWith(to, StringComparison.Ordinal))
            throw new ArgumentException($"One image base contains the other ({from} / {to}); refusing to rewrite.");

        var likePattern = EscapeLike(from) + "%";
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere) await _db.Database.OpenConnectionAsync(ct);

        IDbContextTransaction? transaction = null;
        try
        {
            if (apply) transaction = await _db.Database.BeginTransactionAsync(ct);
            var dbTransaction = transaction?.GetDbTransaction();

            var results = new List<ImageColumnRewrite>();
            foreach (var (table, column) in ImageColumns)
            {
                var where = $"\"{column}\" LIKE @pattern ESCAPE '\\'";

                var matched = Convert.ToInt32(await ScalarAsync(connection, dbTransaction,
                    $"SELECT count(*) FROM \"{table}\" WHERE {where}", likePattern, ct));

                var samples = new List<string>();
                if (matched > 0)
                {
                    await using var sampleCmd = Command(connection, dbTransaction,
                        $"SELECT \"{column}\" FROM \"{table}\" WHERE {where} ORDER BY \"{column}\" LIMIT {SampleCount}", likePattern);
                    await using var reader = await sampleCmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        var value = reader.GetString(0);
                        samples.Add($"{value} -> {to}{value[from.Length..]}");
                    }
                }

                var updated = 0;
                if (apply && matched > 0)
                {
                    await using var update = Command(connection, dbTransaction,
                        $"UPDATE \"{table}\" SET \"{column}\" = @to || substr(\"{column}\", char_length(@from) + 1) WHERE {where}",
                        likePattern);
                    update.Parameters.Add(new NpgsqlParameter("to", to));
                    update.Parameters.Add(new NpgsqlParameter("from", from));
                    updated = await update.ExecuteNonQueryAsync(ct);
                }

                _logger.LogInformation("Image host rewrite {Mode}: {Table}.{Column}: {Matched} matched, {Updated} updated",
                    apply ? "apply" : "dry run", table, column, matched, updated);
                results.Add(new ImageColumnRewrite(table, column, matched, updated, samples));
            }

            if (transaction is not null) await transaction.CommitAsync(ct);

            var report = new RewriteReport(from, to, apply, results);
            _logger.LogInformation("Image host rewrite {Mode} {From} -> {To}: {Matched} matched, {Updated} updated",
                apply ? "apply" : "dry run", from, to, report.TotalMatched, report.TotalUpdated);
            return report;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
            if (openedHere) await _db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Escapes LIKE wildcards (and the escape character) so the base matches literally.</summary>
    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql, string likePattern)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction;
        cmd.Parameters.Add(new NpgsqlParameter("pattern", likePattern));
        return cmd;
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, DbTransaction? transaction, string sql, string likePattern, CancellationToken ct)
    {
        await using var cmd = Command(connection, transaction, sql, likePattern);
        return await cmd.ExecuteScalarAsync(ct);
    }
}
