using DigiPhoto.Contracts.Events;
using DigiPhoto.Contracts.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigiPhoto.Cloud.Data;

public sealed class TenantContext
{
    public Guid? TenantId { get; private set; }

    public void Set(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
        }

        if (TenantId is not null && TenantId != tenantId)
        {
            throw new InvalidOperationException("Tenant context cannot change during a scope.");
        }

        TenantId = tenantId;
    }
}

public sealed class TenantRecord
{
    public Guid Id { get; set; }

    public required string Name { get; set; }
}

public abstract class TenantOwnedRecord
{
    public Guid TenantId { get; set; }
}

public sealed class EventRecord : TenantOwnedRecord
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public Guid BundleId { get; set; }

    public long BundleSequence { get; set; }

    public DateTimeOffset IssuedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public bool PaymentEnabled { get; set; }

    public int RetentionDays { get; set; }

    public required string PrimaryColor { get; set; }

    public required string AccentColor { get; set; }

    public Guid? LogoAssetId { get; set; }

    public Guid NoticeId { get; set; }

    public int NoticeVersion { get; set; }

    public required string NoticeLocale { get; set; }

    public required string NoticeSha256 { get; set; }

    public required string ControllerName { get; set; }

    public required string PrivacyContact { get; set; }

    public required string AdultNotice { get; set; }

    public required string ChildNotice { get; set; }
}

public sealed class PackageRecord : TenantOwnedRecord
{
    public Guid Id { get; set; }

    public Guid VersionId { get; set; }

    public Guid EventId { get; set; }

    public required string Name { get; set; }

    public MediaMode MediaMode { get; set; }

    public long PriceMinor { get; set; }

    public required string Currency { get; set; }

    public int RequiredShots { get; set; }

    public int PrintCopies { get; set; }

    public int RetakeLimitPerShot { get; set; }

    public int CountdownSeconds { get; set; }

    public PrintLayout PrintLayout { get; set; }

    public Guid TemplateVersionId { get; set; }

    public required string GuestFiltersJson { get; set; }
}

public sealed class TemplateVersionRecord : TenantOwnedRecord
{
    public Guid Id { get; set; }

    public Guid TemplateId { get; set; }

    public Guid EventId { get; set; }

    public required string Name { get; set; }

    public required string ContentSha256 { get; set; }

    public int SchemaVersion { get; set; }

    public int FabricMajorVersion { get; set; }

    public int WidthPx { get; set; }

    public int HeightPx { get; set; }

    public int Dpi { get; set; }

    public required string CanvasJson { get; set; }

    public required string AssetIdsJson { get; set; }
}

public sealed class AssetRecord : TenantOwnedRecord
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public required string RelativePath { get; set; }

    public required string MediaType { get; set; }

    public required byte[] Content { get; set; }

    public required string Sha256 { get; set; }
}

public sealed class DeviceRecord : TenantOwnedRecord
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Status { get; set; }
}

public sealed class CloudDbContext(
    DbContextOptions<CloudDbContext> options,
    TenantContext tenantContext) : DbContext(options)
{
    public DbSet<TenantRecord> Tenants => Set<TenantRecord>();

    public DbSet<EventRecord> Events => Set<EventRecord>();

    public DbSet<PackageRecord> Packages => Set<PackageRecord>();

    public DbSet<TemplateVersionRecord> TemplateVersions => Set<TemplateVersionRecord>();

    public DbSet<AssetRecord> Assets => Set<AssetRecord>();

    public DbSet<DeviceRecord> Devices => Set<DeviceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantRecord>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Name).HasMaxLength(160);
        });

        modelBuilder.Entity<EventRecord>(entity =>
        {
            ConfigureTenantOwned(entity, "events");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.Id }).IsUnique();
            entity.HasIndex(row => new { row.TenantId, row.BundleSequence }).IsUnique();
            entity.Property(row => row.Name).HasMaxLength(160);
            entity.Property(row => row.PrimaryColor).HasMaxLength(32);
            entity.Property(row => row.AccentColor).HasMaxLength(32);
            entity.Property(row => row.NoticeLocale).HasMaxLength(16);
            entity.Property(row => row.NoticeSha256).HasMaxLength(64);
            entity.Property(row => row.ControllerName).HasMaxLength(160);
            entity.Property(row => row.PrivacyContact).HasMaxLength(320);
        });

        modelBuilder.Entity<PackageRecord>(entity =>
        {
            ConfigureTenantOwned(entity, "packages");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.VersionId }).IsUnique();
            entity.Property(row => row.Name).HasMaxLength(160);
            entity.Property(row => row.Currency).HasMaxLength(3);
        });

        modelBuilder.Entity<TemplateVersionRecord>(entity =>
        {
            ConfigureTenantOwned(entity, "template_versions");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.Id }).IsUnique();
            entity.Property(row => row.Name).HasMaxLength(160);
            entity.Property(row => row.ContentSha256).HasMaxLength(64);
        });

        modelBuilder.Entity<AssetRecord>(entity =>
        {
            ConfigureTenantOwned(entity, "assets");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.Id }).IsUnique();
            entity.Property(row => row.RelativePath).HasMaxLength(512);
            entity.Property(row => row.MediaType).HasMaxLength(128);
            entity.Property(row => row.Sha256).HasMaxLength(64);
        });

        modelBuilder.Entity<DeviceRecord>(entity =>
        {
            ConfigureTenantOwned(entity, "devices");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.Id }).IsUnique();
            entity.Property(row => row.Name).HasMaxLength(160);
            entity.Property(row => row.Status).HasMaxLength(32);
        });
    }

    public override int SaveChanges()
    {
        ValidateTenantWrites();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ValidateTenantWrites();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ConfigureTenantOwned<TEntity>(EntityTypeBuilder<TEntity> entity, string tableName)
        where TEntity : TenantOwnedRecord
    {
        entity.ToTable(tableName);
        entity.Property(row => row.TenantId).HasColumnName("tenant_id").IsRequired();
        entity.HasOne<TenantRecord>()
            .WithMany()
            .HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(row =>
            tenantContext.TenantId.HasValue && row.TenantId == tenantContext.TenantId.Value);
    }

    private void ValidateTenantWrites()
    {
        var currentTenantId = tenantContext.TenantId;
        foreach (var entry in ChangeTracker.Entries<TenantOwnedRecord>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (!currentTenantId.HasValue || currentTenantId.Value == Guid.Empty)
            {
                throw new InvalidOperationException("A valid tenant context is required for tenant-owned writes.");
            }

            if (entry.Entity.TenantId == Guid.Empty || entry.Entity.TenantId != currentTenantId.Value)
            {
                throw new InvalidOperationException("Tenant-owned writes must match the current tenant context.");
            }
        }
    }
}
