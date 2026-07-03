using BendIt.Api.Discovery;
using BendIt.Api.Runner;
using BendIt.Api.Storage;
using BendIt.Api.TestBattery;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IProjectStore>(_ =>
{
    var resultsDir = builder.Configuration["BENDIT_RESULTS_DIR"] ?? "bend-results";
    return new JsonProjectStore(resultsDir);
});
builder.Services.AddSingleton<ActiveRunRegistry>();
builder.Services.AddSingleton<DiscoveryOrchestrator>();
builder.Services.AddSingleton<TestBatteryRunner>();
builder.Services.AddSingleton<RunExecutionService>();
builder.Services.AddSingleton<RunCoordinator>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.MapControllers();

var frontendDist = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "frontend", "dist"));
var frontendRoot = Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "..", "frontend", "dist"))
    ? frontendDist
    : Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "frontend"));

if (Directory.Exists(frontendRoot))
{
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(frontendRoot) });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(frontendRoot) });
    app.MapFallback(async context =>
    {
        var index = Path.Combine(frontendRoot, "index.html");
        if (File.Exists(index))
        {
            context.Response.ContentType = "text/html";
            await context.Response.SendFileAsync(index);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    });
}

app.Run();
