using Alexandria.Business.Interfaces;
using Alexandria.Business.Middleware;
using Alexandria.Business.Models.Settings;
using Alexandria.Business.Services;
using Alexandria.DataAccess;
using Alexandria.DataAccess.Repositories;

var builder = WebApplication.CreateBuilder(args);

// App settings load.
// builder.Services.Configure<KoboRetailSettings>(builder.Configuration.GetSection("KoboRetailSettings")); for reloading with IOptions<KoboRetailSettings> 
var settings = builder.Configuration
    .GetSection("KoboRetailSettings")
    .Get<KoboRetailSettings>()!;

builder.Services.AddSingleton(settings);

// Http client for KoboStoreApi
builder.Services.AddHttpClient<KoboProxyService>(_ => { }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Add repositories to the container.
builder.Services.AddSingleton<IKoboTrafficLogRepository, KoboTrafficLogRepository>();

// Add services to the container.
builder.Services.AddTransient<IKoboProxyService, KoboProxyService>();

builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseMiddleware<KoboProxyRequestLoggingMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.Run();