using Alexandria.Api.Middleware;
using Alexandria.Business.Models.Settings;
using Alexandria.Business.Services;

var builder = WebApplication.CreateBuilder(args);

// App settings load.
// builder.Services.Configure<KoboRetailSettings>(builder.Configuration.GetSection("KoboRetailSettings")); for reloading with IOptions<KoboRetailSettings> 
var settings = builder.Configuration
    .GetSection("KoboRetailSettings")
    .Get<KoboRetailSettings>()!;

builder.Services.AddSingleton(settings);

// Http client for KoboStoreApi
builder.Services.AddHttpClient<KoboProxyService>(_ => { }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Add services to the container.
builder.Services.AddTransient<IKoboProxyService, KoboProxyService>();
builder.Services.AddTransient<IKoboProxyLogger, KoboProxyLogger>();

builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.Run();