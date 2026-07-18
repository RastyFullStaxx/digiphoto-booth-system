using System.Text;
using System.Text.Json;
using DigiPhoto.Cloud.Data;
using DigiPhoto.Cloud.Events;
using DigiPhoto.Contracts;
using DigiPhoto.Contracts.Events;
using DigiPhoto.Contracts.Templates;
using Microsoft.EntityFrameworkCore;

namespace DigiPhoto.Cloud.Development;

public static class DevelopmentFixtureIds
{
    public static readonly Guid Tenant = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid Event = Guid.Parse("11111111-1111-4111-8111-111111111112");
    public static readonly Guid Package = Guid.Parse("11111111-1111-4111-8111-111111111113");
    public static readonly Guid PackageVersion = Guid.Parse("11111111-1111-4111-8111-111111111114");
    public static readonly Guid Device = Guid.Parse("11111111-1111-4111-8111-111111111115");
    public static readonly Guid Template = Guid.Parse("11111111-1111-4111-8111-111111111116");
    public static readonly Guid TemplateVersion = Guid.Parse("11111111-1111-4111-8111-111111111117");
    public static readonly Guid LogoAsset = Guid.Parse("11111111-1111-4111-8111-111111111118");
    public static readonly Guid Notice = Guid.Parse("11111111-1111-4111-8111-111111111119");
    public static readonly Guid Bundle = Guid.Parse("11111111-1111-4111-8111-111111111120");

    public static readonly Guid OtherTenant = Guid.Parse("22222222-2222-4222-8222-222222222221");
    public static readonly Guid OtherEvent = Guid.Parse("22222222-2222-4222-8222-222222222222");
}

public static class DevelopmentFixtureSeeder
{
    private static readonly byte[] SimulatedLogo = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZfVwAAAAASUVORK5CYII=");

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(services, cancellationToken);
        await SeedPrimaryTenantAsync(services, cancellationToken);
        await SeedOtherTenantAsync(services, cancellationToken);
    }

    private static async Task EnsureDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CloudDbContext>();
        await database.Database.EnsureCreatedAsync(cancellationToken);
    }

    private static async Task SeedPrimaryTenantAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(DevelopmentFixtureIds.Tenant);
        var database = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        if (await database.Events.AnyAsync(row => row.Id == DevelopmentFixtureIds.Event, cancellationToken))
        {
            return;
        }

        database.Tenants.Add(new TenantRecord
        {
            Id = DevelopmentFixtureIds.Tenant,
            Name = "DigiPhoto Studio (Development Fixture)",
        });

        const string adultNotice = "Development fixture only. No real guest media or personal data may be used.";
        const string childNotice = "Ask a guardian before using this development-only simulated booth.";
        const string canvasJson = """
            {"version":"7.4.0","objects":[{"type":"rect","left":0,"top":0,"width":1200,"height":1800,"fill":"#f5f5ef"},{"type":"textbox","left":120,"top":120,"text":"SIMULATED OUTPUT","fontFamily":"Manrope","fontSize":64,"fill":"#26281f"}]}
            """;

        var templateDocument = new TemplateDocument(
            ContractVersions.TemplateDocument,
            new FabricEngine("fabric", 7),
            new PixelDocument(1200, 1800, 300),
            JsonDocument.Parse(canvasJson).RootElement.Clone(),
            [DevelopmentFixtureIds.LogoAsset]);

        database.Events.Add(new EventRecord
        {
            Id = DevelopmentFixtureIds.Event,
            TenantId = DevelopmentFixtureIds.Tenant,
            Name = "Internal Studio Test Event",
            BundleId = DevelopmentFixtureIds.Bundle,
            BundleSequence = 1,
            IssuedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ExpiresAtUtc = new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero),
            PaymentEnabled = false,
            RetentionDays = 30,
            PrimaryColor = "#c4ef45",
            AccentColor = "#ee654f",
            LogoAssetId = DevelopmentFixtureIds.LogoAsset,
            NoticeId = DevelopmentFixtureIds.Notice,
            NoticeVersion = 1,
            NoticeLocale = "en-PH",
            NoticeSha256 = CanonicalJson.Sha256Hex(Encoding.UTF8.GetBytes($"{adultNotice}\n{childNotice}")),
            ControllerName = "DigiPhoto Studio Development Fixture",
            PrivacyContact = "privacy@example.invalid",
            AdultNotice = adultNotice,
            ChildNotice = childNotice,
        });
        database.Packages.Add(new PackageRecord
        {
            Id = DevelopmentFixtureIds.Package,
            VersionId = DevelopmentFixtureIds.PackageVersion,
            EventId = DevelopmentFixtureIds.Event,
            TenantId = DevelopmentFixtureIds.Tenant,
            Name = "Free 4x6 Photo",
            MediaMode = MediaMode.Photo,
            PriceMinor = 0,
            Currency = "PHP",
            RequiredShots = 1,
            PrintCopies = 1,
            RetakeLimitPerShot = 1,
            CountdownSeconds = 3,
            PrintLayout = PrintLayout.FourBySix,
            TemplateVersionId = DevelopmentFixtureIds.TemplateVersion,
            GuestFiltersJson = JsonSerializer.Serialize(new[] { GuestFilter.Original, GuestFilter.BlackAndWhite }),
        });
        database.TemplateVersions.Add(new TemplateVersionRecord
        {
            Id = DevelopmentFixtureIds.TemplateVersion,
            TemplateId = DevelopmentFixtureIds.Template,
            EventId = DevelopmentFixtureIds.Event,
            TenantId = DevelopmentFixtureIds.Tenant,
            Name = "Shutter Rail 4x6 Fixture",
            ContentSha256 = CanonicalJson.Sha256Hex(templateDocument),
            SchemaVersion = ContractVersions.TemplateDocument,
            FabricMajorVersion = 7,
            WidthPx = 1200,
            HeightPx = 1800,
            Dpi = 300,
            CanvasJson = canvasJson,
            AssetIdsJson = JsonSerializer.Serialize(new[] { DevelopmentFixtureIds.LogoAsset }),
        });
        database.Assets.Add(new AssetRecord
        {
            Id = DevelopmentFixtureIds.LogoAsset,
            EventId = DevelopmentFixtureIds.Event,
            TenantId = DevelopmentFixtureIds.Tenant,
            RelativePath = "assets/simulated-logo.png",
            MediaType = "image/png",
            Content = SimulatedLogo,
            Sha256 = CanonicalJson.Sha256Hex(SimulatedLogo),
        });
        database.Devices.Add(new DeviceRecord
        {
            Id = DevelopmentFixtureIds.Device,
            TenantId = DevelopmentFixtureIds.Tenant,
            Name = "Simulated Booth 01",
            Status = "simulated",
        });

        await database.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedOtherTenantAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(DevelopmentFixtureIds.OtherTenant);
        var database = scope.ServiceProvider.GetRequiredService<CloudDbContext>();

        if (await database.Events.AnyAsync(row => row.Id == DevelopmentFixtureIds.OtherEvent, cancellationToken))
        {
            return;
        }

        database.Tenants.Add(new TenantRecord
        {
            Id = DevelopmentFixtureIds.OtherTenant,
            Name = "Isolation Test Tenant (Development Fixture)",
        });
        database.Events.Add(new EventRecord
        {
            Id = DevelopmentFixtureIds.OtherEvent,
            TenantId = DevelopmentFixtureIds.OtherTenant,
            Name = "Cross-Tenant Isolation Fixture",
            BundleId = Guid.Parse("22222222-2222-4222-8222-222222222223"),
            BundleSequence = 1,
            IssuedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ExpiresAtUtc = new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero),
            PaymentEnabled = false,
            RetentionDays = 7,
            PrimaryColor = "#c4ef45",
            AccentColor = "#ee654f",
            NoticeId = Guid.Parse("22222222-2222-4222-8222-222222222224"),
            NoticeVersion = 1,
            NoticeLocale = "en-PH",
            NoticeSha256 = CanonicalJson.Sha256Hex(Encoding.UTF8.GetBytes("development isolation fixture")),
            ControllerName = "Isolation Test Tenant",
            PrivacyContact = "privacy@example.invalid",
            AdultNotice = "Development isolation fixture only.",
            ChildNotice = "Development isolation fixture only.",
        });

        await database.SaveChangesAsync(cancellationToken);
    }
}
