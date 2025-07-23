using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using NDAProcesses.Services;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Extend server timeouts so the Blazor Server app does not disconnect
builder.WebHost.ConfigureKestrel(opt =>
{
    opt.Limits.KeepAliveTimeout = TimeSpan.FromDays(7);
    opt.Limits.RequestHeadersTimeout = TimeSpan.FromDays(7);
});

// 1) Load configuration first so SettingsService picks up the correct values
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

// 2) Register your SettingsService and the background scheduler
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddHostedService<ScheduledEmailService>();

// 3) Register HttpClient and your export logic
builder.Services.AddHttpClient();
builder.Services.AddScoped<ExportService>();

// 4) Blazor Server setup
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(opts =>
    {
        // Hold disconnected circuits for a long time so the app never times out
        opts.DisconnectedCircuitRetentionPeriod = TimeSpan.FromDays(7);
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();

// expose the "Exports" folder at "/exports"
var exportDir = Path.Combine(app.Environment.ContentRootPath, "Exports");
Directory.CreateDirectory(exportDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(exportDir),
    RequestPath  = "/exports"
});

// expose the "Logs" folder at "/logs"
var logsDir = Path.Combine(app.Environment.ContentRootPath, "Logs");
Directory.CreateDirectory(logsDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(logsDir),
    RequestPath  = "/logs"
});

app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
