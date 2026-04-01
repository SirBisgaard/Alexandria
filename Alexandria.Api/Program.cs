using Alexandria.Business.Interfaces;
using Alexandria.Business.Middleware;
using Alexandria.Business.Models.Settings;
using Alexandria.Business.Services;
using Alexandria.DataAccess.Repositories;
using Alexandria.DataAccess.Services;

var builder = WebApplication.CreateBuilder(args);

// App settings load.
// builder.Services.Configure<KoboRetailSettings>(builder.Configuration.GetSection("KoboRetailSettings")); for reloading with IOptions<KoboRetailSettings> 
var koboRetailSettings = builder.Configuration
    .GetSection("KoboRetailSettings")
    .Get<KoboRetailSettings>()!;
var alexandriaSettings =  builder.Configuration
    .GetSection("AlexandriaSettings")
    .Get<AlexandriaSettings>()!;

builder.Services.AddSingleton(koboRetailSettings);
builder.Services.AddSingleton(alexandriaSettings);

// Http client for KoboStoreApi
builder.Services.AddHttpClient<KoboProxyService>(_ => { }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Add repositories to the container.
builder.Services.AddSingleton<IKoboTrafficLogRepository, KoboTrafficLogRepository>();
builder.Services.AddSingleton<IIndexFileService, IndexFileService>();
builder.Services.AddSingleton<IEpubMetadataExtractor, EpubMetadataExtractor>();
builder.Services.AddSingleton<IKepubifyConverter, KepubifyConverter>();

// Add services to the container.
builder.Services.AddTransient<IKoboProxyService, KoboProxyService>();
builder.Services.AddTransient<ILibraryIndexerService, LibraryIndexerService>();
builder.Services.AddTransient<IAlexandriaLibraryService, AlexandriaLibraryService>();

builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseMiddleware<KoboProxyLoggingMiddleware>();

// Index library at boot.
var libraryIndexer = app.Services.GetRequiredService<ILibraryIndexerService>();
await libraryIndexer.Index();

app.UseAuthorization();
app.MapControllers();

app.Run();