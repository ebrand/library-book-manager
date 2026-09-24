using LibraryBookManager.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LibraryBookManager.Api.Identity;

/// <summary>
/// REQ-AUTH-006: audit records are kept for at least two years. Whether they are deleted
/// after that is not specified (QUESTIONS.md Q-AUTH-05), so deletion happens only when
/// Audit:PurgeAfterRetention is true, and never for anything younger than two years.
/// </summary>
public sealed class AuditRetention(IServiceScopeFactory scopes, TimeProvider clock, IConfiguration configuration, ILogger<AuditRetention> log)
{
    public const int RetentionYears = 2;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Audit:PurgeAfterRetention", false))
        {
            log.LogInformation("Audit retention: purging is off; all audit records are kept.");
            return;
        }
        var cutoff = clock.GetUtcNow().AddYears(-RetentionYears);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var deleted = await db.AuditRecords.Where(a => a.OccurredAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        log.LogInformation("Audit retention: deleted {Count} records older than {Cutoff:o}.", deleted, cutoff);
    }
}

/// <summary>Runs <see cref="AuditRetention"/> at start-up and then daily.</summary>
public sealed class AuditRetentionHostedService(AuditRetention retention, TimeProvider clock, ILogger<AuditRetentionHostedService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), clock);
        do
        {
            try
            {
                await retention.RunAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Audit retention run failed; it will be retried in a day.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
