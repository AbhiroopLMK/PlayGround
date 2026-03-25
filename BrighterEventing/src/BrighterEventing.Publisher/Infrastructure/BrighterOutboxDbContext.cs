using Microsoft.EntityFrameworkCore;

namespace BrighterEventing.Publisher.Infrastructure;

/// <summary>
/// Minimal DbContext for Brighter Postgres Outbox. Used by PostgreSqlEntityFrameworkTransactionProvider
/// so DepositPost runs in the same transaction as any business data (HLD: Reliability via Outbox).
/// </summary>
public class BrighterOutboxDbContext : DbContext
{
    public BrighterOutboxDbContext(DbContextOptions<BrighterOutboxDbContext> options)
        : base(options)
    {
    }

    public DbSet<DemoOrderRecord> DemoOrders { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DemoOrderRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OrderKey).HasMaxLength(128);
        });
    }
}
