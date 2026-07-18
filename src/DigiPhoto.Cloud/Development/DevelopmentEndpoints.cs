using DigiPhoto.Cloud.Data;
using DigiPhoto.Cloud.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace DigiPhoto.Cloud.Development;

public static class DevelopmentEndpoints
{
    public static void MapDevelopmentEndpoints(this WebApplication app)
    {
        var development = app.MapGroup("/api/v1/development")
            .WithTags("Development fixtures");

        development.MapGet("/signing-key", (DevelopmentBundleSigner signer) =>
                TypedResults.Ok(signer.DescribePublicKey()))
            .WithName("GetDevelopmentBundleSigningKey")
            .WithSummary("Returns the ephemeral development event-bundle public key.");

        var tenant = development.MapGroup(string.Empty)
            .AddEndpointFilter<DevelopmentTenantContextFilter>();

        tenant.MapGet("/fixture", GetFixtureAsync)
            .WithName("GetDevelopmentFixture")
            .WithSummary("Returns IDs for the simulated tenant fixture selected by the tenant header.");
        tenant.MapGet("/events/{eventId:guid}/bundle", GetEventBundleAsync)
            .WithName("GetDevelopmentEventBundle")
            .WithSummary("Returns the immutable signed development event bundle.");
        tenant.MapGet("/assets/{assetId:guid}", GetAssetAsync)
            .WithName("GetDevelopmentAsset")
            .WithSummary("Returns a hash-addressed asset from the selected development tenant.");
    }

    private static async Task<IResult> GetFixtureAsync(
        TenantContext tenantContext,
        CloudDbContext database,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        var tenant = await database.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == tenantId, cancellationToken);
        var cloudEvent = await database.Events
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var device = await database.Devices
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return tenant is null || cloudEvent is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new
            {
                schemaVersion = 1,
                warning = "Development fixture only. Do not use real guest data.",
                tenant = new { tenant.Id, tenant.Name },
                eventId = cloudEvent.Id,
                deviceId = device?.Id,
            });
    }

    private static async Task<IResult> GetEventBundleAsync(
        Guid eventId,
        EventBundlePublisher publisher,
        CancellationToken cancellationToken)
    {
        var bundle = await publisher.GetAsync(eventId, cancellationToken);
        return bundle is null ? TypedResults.NotFound() : TypedResults.Ok(bundle);
    }

    private static async Task<IResult> GetAssetAsync(
        Guid assetId,
        CloudDbContext database,
        CancellationToken cancellationToken)
    {
        var asset = await database.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == assetId, cancellationToken);
        return asset is null
            ? TypedResults.NotFound()
            : TypedResults.File(asset.Content, asset.MediaType);
    }
}

public sealed class DevelopmentTenantContextFilter(TenantContext tenantContext) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var header = context.HttpContext.Request.Headers["X-DigiPhoto-Tenant-Id"];
        if (!TryReadTenantId(header, out var tenantId))
        {
            return TypedResults.Problem(
                title: "Invalid tenant context",
                detail: "Development fixture requests require exactly one non-empty X-DigiPhoto-Tenant-Id UUID header.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://digiphoto.invalid/problems/invalid-tenant-context");
        }

        tenantContext.Set(tenantId);
        return await next(context);
    }

    private static bool TryReadTenantId(StringValues header, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        return header.Count == 1 && Guid.TryParse(header[0], out tenantId) && tenantId != Guid.Empty;
    }
}
