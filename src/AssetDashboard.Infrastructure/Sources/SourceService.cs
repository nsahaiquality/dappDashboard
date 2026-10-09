using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetDashboard.Infrastructure.Sources;

public enum Freshness
{
    /// <summary>Last successful load within 1.5 × the expected interval.</summary>
    Fresh,
    /// <summary>Within 3 × the expected interval.</summary>
    Late,
    Stale,
}

/// <summary>An upstream source system with its latest load, freshness and reliability.</summary>
public sealed record SourceStatus(
    int Id, string Name, SourceKind Kind, string Owner, string Description, int ExpectedIntervalMinutes,
    DateTime? LastRunAt, LoadStatus? LastStatus, string? LastMessage, DateTime? LastSuccessAt, double? MinutesSinceSuccess,
    Freshness Freshness, int Runs7Days, decimal SuccessRate7Days, long RowsRead24Hours, double AverageDurationSeconds7Days);

/// <summary>One load from a source system.</summary>
public sealed record LoadRunItem(long Id, DateTime StartedAt, DateTime FinishedAt, double DurationSeconds, LoadStatus Status,
    int RowsRead, int RowsRejected, string? Message);

public sealed class SourceService(AssetDbContext db)
{
    public async Task<IReadOnlyList<SourceStatus>> GetSourcesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);
        var dayAgo = now.AddDays(-1);

        var rows = await db.SourceSystems.AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new
            {
                s.Id, s.Name, s.Kind, s.Owner, s.Description, s.ExpectedIntervalMinutes,
                Last = s.Runs.OrderByDescending(r => r.StartedAt).Select(r => new { r.StartedAt, r.Status, r.Message }).FirstOrDefault(),
                LastSuccess = s.Runs.Where(r => r.Status != LoadStatus.Failed).Max(r => (DateTime?)r.FinishedAt),
                Runs7 = s.Runs.Count(r => r.StartedAt >= weekAgo),
                Ok7 = s.Runs.Count(r => r.StartedAt >= weekAgo && r.Status != LoadStatus.Failed),
                Rows24 = s.Runs.Where(r => r.StartedAt >= dayAgo).Sum(r => (long)r.RowsRead),
                Duration7 = s.Runs.Where(r => r.StartedAt >= weekAgo)
                    .Average(r => (double?)(r.FinishedAt - r.StartedAt).TotalSeconds),
            })
            .ToListAsync(ct);

        return rows.Select(s =>
        {
            double? since = s.LastSuccess is { } t ? Math.Round((now - t).TotalMinutes, 1) : null;
            var freshness = since is null ? Freshness.Stale
                : since <= s.ExpectedIntervalMinutes * 1.5 ? Freshness.Fresh
                : since <= s.ExpectedIntervalMinutes * 3 ? Freshness.Late
                : Freshness.Stale;
            return new SourceStatus(s.Id, s.Name, s.Kind, s.Owner, s.Description, s.ExpectedIntervalMinutes,
                s.Last?.StartedAt, s.Last?.Status, s.Last?.Message, s.LastSuccess, since, freshness,
                s.Runs7, s.Runs7 == 0 ? 0 : Math.Round((decimal)s.Ok7 / s.Runs7, 4), s.Rows24, Math.Round(s.Duration7 ?? 0, 1));
        }).ToList();
    }

    public async Task<PagedResult<LoadRunItem>?> GetRunsAsync(int sourceId, LoadStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        if (!await db.SourceSystems.AnyAsync(s => s.Id == sourceId, ct)) return null;
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        var query = db.LoadRuns.AsNoTracking().Where(r => r.SourceSystemId == sourceId);
        if (status is not null) query = query.Where(r => r.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.StartedAt).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r.Id, r.StartedAt, r.FinishedAt, r.Status, r.RowsRead, r.RowsRejected, r.Message })
            .ToListAsync(ct);
        return new PagedResult<LoadRunItem>(items.Select(r => new LoadRunItem(r.Id, r.StartedAt, r.FinishedAt,
            Math.Round((r.FinishedAt - r.StartedAt).TotalSeconds, 1), r.Status, r.RowsRead, r.RowsRejected, r.Message)).ToList(),
            total, page, pageSize);
    }
}
