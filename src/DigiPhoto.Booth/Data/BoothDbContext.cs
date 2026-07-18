using DigiPhoto.Contracts.Sessions;
using Microsoft.EntityFrameworkCore;

namespace DigiPhoto.Booth.Data;

public sealed class BoothDbContext(DbContextOptions<BoothDbContext> options) : DbContext(options)
{
    public DbSet<BoothSessionRow> Sessions => Set<BoothSessionRow>();

    public DbSet<SessionMediaRow> Media => Set<SessionMediaRow>();

    public DbSet<PrintJobRow> PrintJobs => Set<PrintJobRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var session = modelBuilder.Entity<BoothSessionRow>();
        session.ToTable("booth_sessions");
        session.HasKey(row => row.Id);
        session.Property(row => row.State).HasConversion<string>().HasMaxLength(32);
        session.HasIndex(row => row.ActiveSlot).IsUnique();
        session.HasMany(row => row.Media)
            .WithOne()
            .HasForeignKey(row => row.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
        session.HasOne(row => row.PrintJob)
            .WithOne()
            .HasForeignKey<PrintJobRow>(row => row.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        var media = modelBuilder.Entity<SessionMediaRow>();
        media.ToTable("session_media");
        media.HasKey(row => row.Id);
        media.Property(row => row.Kind).HasConversion<string>().HasMaxLength(32);
        media.HasIndex(row => new { row.SessionId, row.Kind });

        var printJob = modelBuilder.Entity<PrintJobRow>();
        printJob.ToTable("print_jobs");
        printJob.HasKey(row => row.Id);
        printJob.Property(row => row.State).HasConversion<string>().HasMaxLength(32);
        printJob.HasIndex(row => row.SessionId).IsUnique();
        printJob.HasIndex(row => row.IdempotencyKey).IsUnique();
    }
}

public sealed class BoothSessionRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid EventId { get; set; }
    public Guid? PackageVersionId { get; set; }
    public Guid? TemplateVersionId { get; set; }
    public long EventBundleSequence { get; set; }
    public SessionState State { get; set; }
    public int? ActiveSlot { get; set; }
    public int RequiredShots { get; set; }
    public int PrintCopies { get; set; }
    public int RetentionDays { get; set; }
    public string? PrivacyJson { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public List<SessionMediaRow> Media { get; } = [];
    public PrintJobRow? PrintJob { get; set; }
}

public sealed class SessionMediaRow
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public MediaKind Kind { get; set; }
    public required string RelativePath { get; set; }
    public required string Sha256 { get; set; }
    public long ByteLength { get; set; }
    public int? WidthPx { get; set; }
    public int? HeightPx { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class PrintJobRow
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public PrintJobState State { get; set; }
    public int RequestedCopies { get; set; }
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
