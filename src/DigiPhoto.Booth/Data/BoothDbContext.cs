using DigiPhoto.Contracts.Sessions;
using Microsoft.EntityFrameworkCore;

namespace DigiPhoto.Booth.Data;

public sealed class BoothDbContext(DbContextOptions<BoothDbContext> options) : DbContext(options)
{
    public DbSet<BoothSessionRow> Sessions => Set<BoothSessionRow>();

    public DbSet<SessionMediaRow> Media => Set<SessionMediaRow>();

    public DbSet<PrintJobRow> PrintJobs => Set<PrintJobRow>();

    public DbSet<EventBundleRow> EventBundles => Set<EventBundleRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var session = modelBuilder.Entity<BoothSessionRow>();
        session.ToTable("booth_sessions");
        session.HasKey(row => row.Id);
        session.HasAlternateKey(row => new { row.TenantId, row.Id });
        session.Property(row => row.Id).ValueGeneratedNever();
        session.Property(row => row.State).HasConversion<string>().HasMaxLength(32);
        session.HasIndex(row => row.ActiveSlot).IsUnique();
        session.HasMany(row => row.Media)
            .WithOne()
            .HasForeignKey(row => new { row.TenantId, row.SessionId })
            .HasPrincipalKey(row => new { row.TenantId, row.Id })
            .OnDelete(DeleteBehavior.Cascade);
        session.HasOne(row => row.PrintJob)
            .WithOne()
            .HasForeignKey<PrintJobRow>(row => new { row.TenantId, row.SessionId })
            .HasPrincipalKey<BoothSessionRow>(row => new { row.TenantId, row.Id })
            .OnDelete(DeleteBehavior.Cascade);

        var media = modelBuilder.Entity<SessionMediaRow>();
        media.ToTable("session_media");
        media.HasKey(row => row.Id);
        media.Property(row => row.Id).ValueGeneratedNever();
        media.Property(row => row.Kind).HasConversion<string>().HasMaxLength(32);
        media.HasIndex(row => new { row.TenantId, row.SessionId, row.Kind });

        var printJob = modelBuilder.Entity<PrintJobRow>();
        printJob.ToTable("print_jobs");
        printJob.HasKey(row => row.Id);
        printJob.Property(row => row.Id).ValueGeneratedNever();
        printJob.Property(row => row.State).HasConversion<string>().HasMaxLength(32);
        printJob.HasIndex(row => new { row.TenantId, row.SessionId }).IsUnique();
        printJob.HasIndex(row => row.IdempotencyKey).IsUnique();

        var eventBundle = modelBuilder.Entity<EventBundleRow>();
        eventBundle.ToTable("event_bundles");
        eventBundle.HasKey(row => row.BundleId);
        eventBundle.Property(row => row.BundleId).ValueGeneratedNever();
        eventBundle.HasIndex(row => new { row.EventId, row.Sequence }).IsUnique();
        eventBundle.Property(row => row.ManifestSha256).HasMaxLength(64);
        eventBundle.Property(row => row.SigningKeyId).HasMaxLength(128);
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
    public string? RecoveryReason { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public List<SessionMediaRow> Media { get; } = [];
    public PrintJobRow? PrintJob { get; set; }
}

public sealed class SessionMediaRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
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
    public Guid TenantId { get; set; }
    public Guid SessionId { get; set; }
    public PrintJobState State { get; set; }
    public int RequestedCopies { get; set; }
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class EventBundleRow
{
    public Guid BundleId { get; set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; set; }
    public long Sequence { get; set; }
    public required string SignedBundleJson { get; set; }
    public required string ManifestSha256 { get; set; }
    public required string SigningKeyId { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset LoadedAtUtc { get; set; }
}
