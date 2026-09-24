using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LibraryBookManager.Api.Data;

public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Title> Titles => Set<Title>();
    public DbSet<Copy> Copies => Set<Copy>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SignInThrottle> SignInThrottles => Set<SignInThrottle>();
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder conventions)
    {
        // SQLite cannot compare DateTimeOffset values in queries; store them as sortable integers.
        conventions.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        conventions.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.HasIndex(u => u.PublicId).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Role).HasConversion<string>();
        });
        model.Entity<Title>(e =>
        {
            e.HasIndex(t => t.Isbn).IsUnique();
            e.HasMany(t => t.Copies).WithOne(c => c.Title).HasForeignKey(c => c.TitleId);
        });
        model.Entity<Copy>(e =>
        {
            // Unique across the library, removed copies included.
            e.HasIndex(c => c.Barcode).IsUnique();
            e.HasMany(c => c.Loans).WithOne(l => l.Copy).HasForeignKey(l => l.CopyId);
        });
        model.Entity<Session>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });
        model.Entity<SignInThrottle>(e => e.HasKey(x => x.Email));
        model.Entity<AuditRecord>(e =>
        {
            e.Property(a => a.Event).HasConversion<string>();
            e.Property(a => a.OldRole).HasConversion<string>();
            e.Property(a => a.NewRole).HasConversion<string>();
            e.HasIndex(a => a.OccurredAt);
            // Audit rows must outlive nothing they point at: users are never deleted, and the
            // foreign keys stop that changing silently.
            e.HasOne<User>().WithMany().HasForeignKey(a => a.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(a => a.AffectedUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Loan>(e =>
        {
            e.HasOne(l => l.Member).WithMany().HasForeignKey(l => l.MemberId);
            // At most one open loan per copy, whatever the application does (Q-LEND-06).
            e.HasIndex(l => l.CopyId).IsUnique().HasFilter("ReturnedOn IS NULL").HasDatabaseName("IX_Loans_OneOpenPerCopy");
            e.HasIndex(l => new { l.MemberId, l.ReturnedOn });
        });
    }
}
