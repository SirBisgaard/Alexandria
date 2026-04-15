using System.Text.Json.Serialization;
using Alexandria.Api.Authentication;
using Alexandria.Api.Middleware;
using Alexandria.Business.Authentication;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.Library.Services;
using Alexandria.Business.KoboStoreProxy.Services;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Database;
using Alexandria.DataAccess.Database.Interfaces;
using Alexandria.DataAccess.Database.Repositories;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files;
using Alexandria.DataAccess.Files.Books.Audiobooks;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files.Books.Ebooks;
using Alexandria.DataAccess.Files.Interfaces;
using Alexandria.DataAccess.Files.Services;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// App settings load.
var alexandriaSettings =  builder.Configuration
    .GetSection("AlexandriaSettings")
    .Get<AlexandriaSettings>()!;

builder.Services.AddSingleton(alexandriaSettings);

// Http client for KoboStoreApi
builder.Services.AddHttpClient<KoboStoreProxyService>(_ => { }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// In-memory SQLite with periodic flush to disk.
builder.Services.AddSingleton<SqliteConnectionProvider>();
builder.Services.AddSingleton<ISqliteConnectionProvider>(sp => sp.GetRequiredService<SqliteConnectionProvider>());
builder.Services.AddSingleton<IDatabasePersistence>(sp => sp.GetRequiredService<SqliteConnectionProvider>());
builder.Services.AddHostedService<DatabaseFlushService>();

// Add repositories to the container.
builder.Services.AddTransient<IDatabaseInitializer, DatabaseInitializer>();
builder.Services.AddTransient<IKoboTrafficLogRepository, KoboTrafficLogRepository>();
builder.Services.AddTransient<IKoboDeviceSyncRepository, KoboDeviceSyncRepository>();
builder.Services.AddTransient<IKoboDeviceRepository, KoboDeviceRepository>();
builder.Services.AddTransient<IUserRepository, UserRepository>();
builder.Services.AddTransient<ISettingsRepository, SettingsRepository>();
builder.Services.AddTransient<IFileRepository, FileRepository>();

builder.Services.AddSingleton<IBookRepository, BookRepository>();
builder.Services.AddSingleton<IReadingStateRepository, ReadingStateRepository>();
builder.Services.AddSingleton<ICollectionRepository, CollectionRepository>();
builder.Services.AddSingleton<IAuthorRepository, AuthorRepository>();
builder.Services.AddSingleton<ISeriesRepository, SeriesRepository>();

builder.Services.AddTransient<IEpubMetadataHandler, EpubMetadataHandler>();
builder.Services.AddTransient<IKepubifyConverter, KepubifyConverter>();
builder.Services.AddTransient<ICoverThumbnailService, CoverThumbnailService>();
builder.Services.AddTransient<IFileContentIndexer, FileEbookIndexer>();
builder.Services.AddTransient<IFfmpegHandler, FfmpegHandler>();
builder.Services.AddTransient<IM4BMetadataHandler, M4BMetadataHandler>();
builder.Services.AddTransient<FileAudiobookMediaHandler>();
builder.Services.AddTransient<IFileContentIndexer, FileAudiobookIndexer>();
builder.Services.AddTransient<IFileContentMediaHandler, FileEbbokMediaHandler>();
builder.Services.AddTransient<IFileContentMediaHandler, FileAudiobookMediaHandler>();
builder.Services.AddTransient<IFileContentManager, FileContentManager>();

// Add services to the container.
builder.Services.AddSingleton<IKoboDeviceSyncTokenService, KoboDeviceSyncTokenService>();
builder.Services.AddSingleton<LibrarySyncLock>();
builder.Services.AddTransient<IKoboProxyService, KoboStoreProxyService>();
builder.Services.AddTransient<IKoboLibrarySyncService, KoboLibrarySyncService>();
builder.Services.AddTransient<IKoboContentDeliveryService, KoboContentDeliveryService>();
builder.Services.AddTransient<IKoboReadingStateService, KoboReadingStateService>();
builder.Services.AddTransient<IKoboAnalyticsService, KoboAnalyticsService>();
builder.Services.AddTransient<IKoboProductService, KoboProductService>();
builder.Services.AddTransient<IIndexerService, IndexerService>();
builder.Services.AddTransient<IBookService, BookService>();
builder.Services.AddTransient<IAuthorService, AuthorService>();
builder.Services.AddTransient<ISeriesService, SeriesService>();
builder.Services.AddTransient<ICollectionService, CollectionService>();
builder.Services.AddTransient<ISettingsService, SettingsService>();

// Authentication
var authSecrets = new AuthSecretProvider();
builder.Services.AddSingleton(authSecrets);

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = AlexandriaAuthHandler.SchemeName;
})
.AddScheme<AuthenticationSchemeOptions, AlexandriaAuthHandler>(AlexandriaAuthHandler.SchemeName, null)
.AddScheme<AuthenticationSchemeOptions, KoboDeviceAuthHandler>(KoboDeviceAuthHandler.SchemeName, null);

builder.Services.AddAuthorization();

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// Normalize double slashes in request paths so trailing-slash api_endpoint values work.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path is not null && path.Contains("//"))
    {
        context.Request.Path = path.Replace("//", "/");
    }
    await next();
});

// Configure the HTTP request pipeline.
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<KoboStoreProxyLoggingMiddleware>();

// Initialize database schema before indexing.
var dbInitializer = app.Services.GetRequiredService<IDatabaseInitializer>();
await dbInitializer.Initialize();

// Load UUID seed from settings and initialize auth secrets.
var settingsRepo = app.Services.GetRequiredService<ISettingsRepository>();
var appSettings = await settingsRepo.Get();
GuidExtensions.SetNamespace(appSettings.UuidSeed);
authSecrets.Initialize(appSettings.UuidSeed);

// Index library at boot.
if (appSettings.SyncLibraryAtBoot)
{
    var libraryIndexer = app.Services.GetRequiredService<IIndexerService>();
    await libraryIndexer.Index();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseFileServer();   
app.MapControllers();

app.Run();
