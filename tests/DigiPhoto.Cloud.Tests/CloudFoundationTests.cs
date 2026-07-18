using System.Net;
using System.Net.Http.Json;
using DigiPhoto.Cloud.Data;
using DigiPhoto.Cloud.Development;
using DigiPhoto.Cloud.Events;
using DigiPhoto.Contracts.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigiPhoto.Cloud.Tests;

public sealed class CloudFoundationTests
{
    [Fact]
    public async Task TenantQueriesAndWritesFailClosed()
    {
        await using var harness = await CloudHarness.CreateAsync();

        await using (var missingTenantScope = harness.Services.CreateAsyncScope())
        {
            var database = missingTenantScope.ServiceProvider.GetRequiredService<CloudDbContext>();
            Assert.Empty(await database.Events.ToListAsync(TestContext.Current.CancellationToken));

            database.Events.Add(CreateEvent(Guid.NewGuid(), DevelopmentFixtureIds.Tenant));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await using (var firstTenantScope = harness.Services.CreateAsyncScope())
        {
            firstTenantScope.ServiceProvider.GetRequiredService<TenantContext>()
                .Set(DevelopmentFixtureIds.Tenant);
            var database = firstTenantScope.ServiceProvider.GetRequiredService<CloudDbContext>();
            var visibleEvents = await database.Events
                .Select(row => row.Id)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal([DevelopmentFixtureIds.Event], visibleEvents);
            database.Events.Add(CreateEvent(Guid.NewGuid(), DevelopmentFixtureIds.OtherTenant));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await using (var otherTenantScope = harness.Services.CreateAsyncScope())
        {
            otherTenantScope.ServiceProvider.GetRequiredService<TenantContext>()
                .Set(DevelopmentFixtureIds.OtherTenant);
            var database = otherTenantScope.ServiceProvider.GetRequiredService<CloudDbContext>();
            var visibleEvents = await database.Events
                .Select(row => row.Id)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal([DevelopmentFixtureIds.OtherEvent], visibleEvents);
        }
    }

    [Fact]
    public async Task BundleManifestIsStableHashedAndSigned()
    {
        await using var harness = await CloudHarness.CreateAsync();
        await using var scope = harness.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(DevelopmentFixtureIds.Tenant);
        var publisher = scope.ServiceProvider.GetRequiredService<EventBundlePublisher>();
        var signer = scope.ServiceProvider.GetRequiredService<DevelopmentBundleSigner>();

        var first = await publisher.GetAsync(
            DevelopmentFixtureIds.Event,
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("Primary development bundle was not seeded.");
        var second = await publisher.GetAsync(
            DevelopmentFixtureIds.Event,
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("Primary development bundle was not stable.");

        Assert.Equal(1, first.Manifest.Sequence);
        Assert.Equal(
            CanonicalJson.Serialize(first.Manifest),
            CanonicalJson.Serialize(second.Manifest));
        Assert.All(first.Manifest.Assets, asset =>
        {
            Assert.Equal(64, asset.Sha256.Length);
            Assert.Equal(asset.Sha256, asset.Sha256.ToLowerInvariant());
        });

        var publicKey = signer.DescribePublicKey();
        Assert.True(DevelopmentBundleSigner.Verify(first.Manifest, first.Signature, publicKey));
        Assert.True(DevelopmentBundleSigner.Verify(second.Manifest, second.Signature, publicKey));
        Assert.False(DevelopmentBundleSigner.Verify(
            first.Manifest with { Sequence = first.Manifest.Sequence + 1 },
            first.Signature,
            publicKey));

        Assert.Null(await publisher.GetAsync(
            DevelopmentFixtureIds.OtherEvent,
            TestContext.Current.CancellationToken));
    }

    private static EventRecord CreateEvent(Guid id, Guid tenantId) => new()
    {
        Id = id,
        TenantId = tenantId,
        Name = "Write guard test",
        BundleId = Guid.NewGuid(),
        BundleSequence = 99,
        IssuedAtUtc = DateTimeOffset.UtcNow,
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
        PaymentEnabled = false,
        RetentionDays = 7,
        PrimaryColor = "#ffffff",
        AccentColor = "#000000",
        NoticeId = Guid.NewGuid(),
        NoticeVersion = 1,
        NoticeLocale = "en-PH",
        NoticeSha256 = new('0', 64),
        ControllerName = "Test",
        PrivacyContact = "test@example.invalid",
        AdultNotice = "Test",
        ChildNotice = "Test",
    };
}

public sealed class DevelopmentApiTests
{
    [Fact]
    public async Task TenantHeaderIsRequiredAndCrossTenantEventIsHidden()
    {
        using var factory = new CloudApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var missing = await client.GetAsync(
            "/api/v1/development/fixture",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/development/fixture");
        invalidRequest.Headers.Add("X-DigiPhoto-Tenant-Id", "not-a-uuid");
        var invalid = await client.SendAsync(invalidRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var fixtureRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/development/fixture");
        fixtureRequest.Headers.Add("X-DigiPhoto-Tenant-Id", DevelopmentFixtureIds.Tenant.ToString());
        var fixture = await client.SendAsync(fixtureRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, fixture.StatusCode);

        using var crossTenantRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/development/events/{DevelopmentFixtureIds.OtherEvent}/bundle");
        crossTenantRequest.Headers.Add("X-DigiPhoto-Tenant-Id", DevelopmentFixtureIds.Tenant.ToString());
        var crossTenant = await client.SendAsync(crossTenantRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        var health = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}

internal sealed class CloudApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"digiphoto-cloud-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CloudDatabase"] = $"Data Source={databasePath}",
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}

internal sealed class CloudHarness : IAsyncDisposable
{
    private CloudHarness(SqliteConnection connection, ServiceProvider services)
    {
        Connection = connection;
        Services = services;
    }

    private SqliteConnection Connection { get; }

    public ServiceProvider Services { get; }

    public static async Task<CloudHarness> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddScoped<TenantContext>();
        serviceCollection.AddDbContext<CloudDbContext>(options => options.UseSqlite(connection));
        serviceCollection.AddSingleton<DevelopmentBundleSigner>();
        serviceCollection.AddScoped<EventBundlePublisher>();
        var services = serviceCollection.BuildServiceProvider();
        await DevelopmentFixtureSeeder.SeedAsync(services, TestContext.Current.CancellationToken);
        return new CloudHarness(connection, services);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
