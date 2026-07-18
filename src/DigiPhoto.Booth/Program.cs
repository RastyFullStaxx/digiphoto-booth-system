using DigiPhoto.Booth.Bundles;
using DigiPhoto.Booth.Configuration;
using DigiPhoto.Booth.Data;
using DigiPhoto.Booth.Hardware;
using DigiPhoto.Booth.Sessions;
using DigiPhoto.Booth.Storage;
using DigiPhoto.Contracts.Events;
using DigiPhoto.Contracts.Sessions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var databasePath = Path.GetFullPath(
    builder.Configuration["Booth:DatabasePath"] ?? "data/booth.db",
    builder.Environment.ContentRootPath);
var storageRoot = Path.GetFullPath(
    builder.Configuration["Booth:StorageRoot"] ?? "data/media",
    builder.Environment.ContentRootPath);
var bundleRoot = Path.GetFullPath(
    builder.Configuration["Booth:BundleRoot"] ?? "data/bundles",
    builder.Environment.ContentRootPath);

Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

builder.Services.AddOpenApi();
builder.Services.AddSingleton(new BoothStorageOptions(storageRoot));
builder.Services.AddSingleton(new BoothIdentityOptions(
    Guid.TryParse(builder.Configuration["Booth:Identity:TenantId"], out var tenantId)
        ? tenantId
        : Guid.Empty,
    Guid.TryParse(builder.Configuration["Booth:Identity:DeviceId"], out var deviceId)
        ? deviceId
        : Guid.Empty));
builder.Services.AddSingleton(new BoothBundleOptions(
    bundleRoot,
    new PinnedBundleKey(
        builder.Configuration["Booth:BundleTrust:Algorithm"] ?? "ES256",
        builder.Configuration["Booth:BundleTrust:KeyId"] ?? string.Empty,
        builder.Configuration["Booth:BundleTrust:SubjectPublicKeyInfoBase64"] ?? string.Empty)));
builder.Services.AddSingleton<BoothFileStore>();
builder.Services.AddDbContextFactory<BoothDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddSingleton<ICameraAdapter, SimulatedCameraAdapter>();
builder.Services.AddSingleton<IPrinterAdapter, SimulatedPrinterAdapter>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<VerifiedEventBundleStore>();
builder.Services.AddSingleton<BoothWorkflow>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var (status, title) = exception switch
    {
        KeyNotFoundException => (StatusCodes.Status404NotFound, "Session not found"),
        ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
        EventBundleVerificationException => (StatusCodes.Status400BadRequest, "Event bundle rejected"),
        BoothWorkflowException => (StatusCodes.Status409Conflict, "Session cannot advance"),
        _ => (StatusCodes.Status500InternalServerError, "The booth could not complete the request"),
    };

    await Results.Problem(
        statusCode: status,
        title: title,
        detail: status == StatusCodes.Status500InternalServerError ? null : exception?.Message)
        .ExecuteAsync(context);
}));
app.UseHttpsRedirection();

var booth = app.MapGroup("/api/v1/booth");

booth.MapPost("/bundles", async (
    SignedEventBundle bundle,
    VerifiedEventBundleStore store,
    CancellationToken cancellationToken) =>
    Results.Ok(await store.LoadAsync(bundle, cancellationToken)));

booth.MapGet("/sessions/active", async (BoothWorkflow workflow, CancellationToken cancellationToken) =>
    await workflow.GetActiveAsync(cancellationToken));

booth.MapGet("/sessions/{sessionId:guid}", async (
    Guid sessionId,
    BoothWorkflow workflow,
    CancellationToken cancellationToken) =>
    await workflow.GetAsync(sessionId, cancellationToken));

booth.MapPost("/sessions", async (
    StartSessionRequest request,
    BoothWorkflow workflow,
    CancellationToken cancellationToken) =>
    Results.Created(
        $"/api/v1/booth/sessions/{request.SessionId}",
        await workflow.StartAsync(request, cancellationToken)));

booth.MapPost("/sessions/{sessionId:guid}/begin", async (
    Guid sessionId,
    BoothWorkflow workflow,
    CancellationToken cancellationToken) =>
    await workflow.BeginPackageSelectionAsync(sessionId, cancellationToken));

booth.MapPost("/sessions/{sessionId:guid}/package", async (
    Guid sessionId,
    SelectPackageRequest request,
    BoothWorkflow workflow,
    CancellationToken cancellationToken) =>
    await workflow.SelectPackageAsync(sessionId, request.PackageVersionId, cancellationToken));

booth.MapPost("/sessions/{sessionId:guid}/privacy", async (
    Guid sessionId,
    AcceptPrivacyRequest request,
    BoothWorkflow workflow,
    CancellationToken cancellationToken) =>
    await workflow.AcceptPrivacyAsync(sessionId, request, cancellationToken));

booth.MapPost("/sessions/{sessionId:guid}/countdown", async (
    Guid sessionId,
    BoothWorkflow workflow,
    CancellationToken cancellationToken) =>
    await workflow.StartCountdownAsync(sessionId, cancellationToken));

booth.MapPost("/sessions/{sessionId:guid}/capture", async (
    Guid sessionId,
    BoothWorkflow workflow,
    CancellationToken cancellationToken) =>
    await workflow.CaptureAsync(sessionId, cancellationToken));

booth.MapPost("/sessions/{sessionId:guid}/review", async (
    Guid sessionId,
    BoothWorkflow workflow,
    CancellationToken cancellationToken) =>
    await workflow.AcceptReviewAsync(sessionId, cancellationToken));

booth.MapPost("/sessions/{sessionId:guid}/render", async (
    Guid sessionId,
    HttpRequest request,
    BoothWorkflow workflow,
    CancellationToken cancellationToken) =>
{
    if (!request.ContentType?.StartsWith("image/png", StringComparison.OrdinalIgnoreCase) ?? true)
    {
        throw new ArgumentException("The rendered output must be sent as image/png bytes.");
    }

    return await workflow.PersistRenderAsync(sessionId, request.Body, cancellationToken);
});

booth.MapPost("/sessions/{sessionId:guid}/print", async (
    Guid sessionId,
    BoothWorkflow workflow,
    CancellationToken cancellationToken) =>
    await workflow.SubmitPrintAsync(sessionId, cancellationToken));

booth.MapPost("/sessions/{sessionId:guid}/resolve-print", async (
    Guid sessionId,
    ResolvePrintRequest request,
    BoothWorkflow workflow,
    CancellationToken cancellationToken) =>
    await workflow.ResolvePrintAsync(sessionId, request.Resolution, cancellationToken));

booth.MapPost("/sessions/{sessionId:guid}/reset", async (
    Guid sessionId,
    BoothWorkflow workflow,
    CancellationToken cancellationToken) =>
    await workflow.ResetAsync(sessionId, cancellationToken));

booth.MapPost("/sessions/{sessionId:guid}/cancel-recovery", async (
    Guid sessionId,
    CancelRecoveryRequest request,
    BoothWorkflow workflow,
    CancellationToken cancellationToken) =>
    await workflow.CancelRecoveryAsync(sessionId, request.Reason, cancellationToken));

booth.MapPost("/sessions/{sessionId:guid}/payment", () => Results.Problem(
    statusCode: StatusCodes.Status501NotImplemented,
    title: "Guest payment is not enabled",
    detail: "This simulator slice cannot verify payment and never unlocks a session from this endpoint."));

await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BoothDbContext>>();
    await BoothDatabaseInitializer.InitializeAsync(factory);
}

await app.Services.GetRequiredService<BoothWorkflow>().RecoverActiveAsync();

app.Run();

public partial class Program;
