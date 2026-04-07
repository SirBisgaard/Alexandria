using System.Text.Json.Serialization;
using Alexandria.Business.Extensions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Middleware;
using Alexandria.Business.Models.Settings;
using Alexandria.Business.Services;
using Alexandria.DataAccess;
using Alexandria.DataAccess.Repositories;
using Alexandria.DataAccess.Services;

var builder = WebApplication.CreateBuilder(args);

// App settings load.
// builder.Services.Configure<KoboRetailSettings>(builder.Configuration.GetSection("KoboRetailSettings")); for reloading with IOptions<KoboRetailSettings>
var koboRetailSettings = builder.Configuration
    .GetSection("KoboProxySettings")
    .Get<KoboProxySettings>()!;
var alexandriaSettings =  builder.Configuration
    .GetSection("AlexandriaSettings")
    .Get<AlexandriaSettings>()!;

builder.Services.AddSingleton(koboRetailSettings);
builder.Services.AddSingleton(alexandriaSettings);

// Http client for KoboStoreApi
builder.Services.AddHttpClient<KoboStoreProxyService>(_ => { }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Add repositories to the container.
builder.Services.AddTransient<IDatabaseInitializer, DatabaseInitializer>();
builder.Services.AddTransient<IBookRepository, BookRepository>();
builder.Services.AddTransient<IFileRepository, FileRepository>();
builder.Services.AddTransient<IReadingStateRepository, ReadingStateRepository>();
builder.Services.AddTransient<IKoboTrafficLogRepository, KoboTrafficLogRepository>();
builder.Services.AddTransient<ICollectionRepository, CollectionRepository>();
builder.Services.AddTransient<IAuthorRepository, AuthorRepository>();
builder.Services.AddTransient<ISeriesRepository, SeriesRepository>();
builder.Services.AddTransient<ISettingsRepository, SettingsRepository>();
builder.Services.AddTransient<IKoboDeviceSyncRepository, KoboDeviceSyncRepository>();
builder.Services.AddTransient<IEpubMetadataHandler, EpubMetadataHandler>();
builder.Services.AddTransient<IKepubifyConverter, KepubifyConverter>();
builder.Services.AddTransient<ICoverThumbnailService, CoverThumbnailService>();

// Add services to the container.
builder.Services.AddSingleton<ISyncSessionService, SyncSessionService>();
builder.Services.AddTransient<ISyncTokenService, SyncTokenService>();
builder.Services.AddTransient<IKoboProxyService, KoboStoreProxyService>();
builder.Services.AddTransient<ILibraryIndexerService, LibraryIndexerService>();
builder.Services.AddTransient<IAlexandriaLibraryService, AlexandriaLibraryService>();
builder.Services.AddTransient<IAlexandriaSettingsService, AlexandriaSettingsService>();

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseMiddleware<KoboProxyLoggingMiddleware>();

// Initialize database schema before indexing.
var dbInitializer = app.Services.GetRequiredService<IDatabaseInitializer>();
await dbInitializer.Initialize();

// Load UUID seed from settings.
var settingsRepo = app.Services.GetRequiredService<ISettingsRepository>();
var appSettings = await settingsRepo.Get();
GuidExtensions.SetNamespace(appSettings.UuidSeed);

// Index library at boot.
var libraryIndexer = app.Services.GetRequiredService<ILibraryIndexerService>();
await libraryIndexer.Index();

app.UseAuthorization();

app.UseFileServer();   
app.MapControllers();

app.Run();
