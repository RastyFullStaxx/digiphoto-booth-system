using DigiPhoto.Cloud.Data;
using DigiPhoto.Cloud.Development;
using DigiPhoto.Cloud.Events;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddSingleton<DevelopmentBundleSigner>();
builder.Services.AddScoped<EventBundlePublisher>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

var connectionString = builder.Configuration.GetConnectionString("CloudDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("ConnectionStrings:CloudDatabase is required outside Development.");
    }

    var dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DigiPhoto",
        "Development");
    Directory.CreateDirectory(dataDirectory);
    connectionString = $"Data Source={Path.Combine(dataDirectory, "digiphoto-cloud.db")}";
}

builder.Services.AddDbContext<CloudDbContext>(options => options.UseSqlite(connectionString));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await DevelopmentFixtureSeeder.SeedAsync(app.Services);
    app.MapDevelopmentEndpoints();
}

app.Run();

public partial class Program;
