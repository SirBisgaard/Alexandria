using System.IO.Compression;
using System.Text.Json.Serialization;
using Alexandria.Api.Authentication;
using Alexandria.Api.Filters;
using Alexandria.Api.Middleware;
using Alexandria.Api.Sync;
using Alexandria.Business.Authentication;
using Alexandria.Business.Authentication.Interfaces;
using Alexandria.Business.Integrations;
using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Services;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.Business.Library.Services;
using Alexandria.Business.Logging;
using Alexandria.DataAccess.Database;
using Alexandria.DataAccess.Database.Interfaces;
using Alexandria.DataAccess.Database.Repositories;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files;
using Alexandria.DataAccess.Integrations;
using Alexandria.DataAccess.Files.Books.Audiobooks;
using Alexandria.DataAccess.Files.Books.Ebooks;
using Alexandria.DataAccess.Files.Interfaces;
using Alexandria.DataAccess.Files.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);

// App settings load.
var alexandriaSettings =  builder.Configuration
    .GetSection("AlexandriaSettings")
    .Get<AlexandriaSettings>()!;

builder.Services.AddSingleton(alexandriaSettings);

// Exceptions log: Error-only append file under DataFolder, closed on host shutdown.
Directory.CreateDirectory(alexandriaSettings.DataFolder);
builder.Logging.AddProvider(new ExceptionFileLoggerProvider(Path.Combine(alexandriaSettings.DataFolder, "exceptions.log")));

// Http client for KoboStoreApi
builder.Services.AddHttpClient<KoboStoreProxyService>(_ => { }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<IAudioBookShelfSyncIntegration, AudioBookShelfSyncIntegration>();

// File-backed SQLite in WAL mode; every commit is durable on its own.
builder.Services.AddSingleton<SqliteConnectionProvider>();
builder.Services.AddSingleton<ISqliteConnectionProvider>(sp => sp.GetRequiredService<SqliteConnectionProvider>());

// Add repositories to the container.
builder.Services.AddTransient<IDatabaseInitializer, DatabaseInitializer>();

builder.Services.AddTransient<ILibraryVersionRepository, LibraryVersionRepository>();
builder.Services.AddTransient<LibraryETagFilter>();
builder.Services.AddTransient<IKoboTrafficLogRepository, KoboTrafficLogRepository>();
builder.Services.AddTransient<IKoboDeviceSyncRepository, KoboDeviceSyncRepository>();
builder.Services.AddTransient<IBookActivityEventRepository, BookActivityEventRepository>();
builder.Services.AddTransient<IKoboDeviceRepository, KoboDeviceRepository>();
builder.Services.AddTransient<IUserRepository, UserRepository>();
builder.Services.AddTransient<IAudioBookShelfSyncIntegrationRepository, AudioBookShelfSyncIntegrationRepository>();
builder.Services.AddTransient<ISettingsRepository, SettingsRepository>();
builder.Services.AddTransient<IFileRepository, FileRepository>();
builder.Services.AddSingleton<IBookRepository, BookRepository>();
builder.Services.AddSingleton<IReadingStateRepository, ReadingStateRepository>();
builder.Services.AddSingleton<ICollectionRepository, CollectionRepository>();
builder.Services.AddSingleton<IAuthorRepository, AuthorRepository>();
builder.Services.AddSingleton<ISeriesRepository, SeriesRepository>();

builder.Services.AddTransient<IEpubFileHandler, EpubFileHandler>();
builder.Services.AddTransient<IKepubifyWrapper, KepubifyWrapper>();
builder.Services.AddTransient<ICoverThumbnailService, CoverThumbnailService>();
builder.Services.AddTransient<IFileContentIndexer, FileEbookIndexer>();
builder.Services.AddTransient<IFfmpegWrapper, FfmpegWrapper>();
builder.Services.AddTransient<IM4BFileHandler, M4BFileHandler>();
builder.Services.AddTransient<FileM4BAudiobookMediaHandler>();
builder.Services.AddTransient<IFileContentIndexer, FileM4BAudiobookIndexer>();
builder.Services.AddTransient<IFileContentMediaHandler, FileEbbokMediaHandler>();
builder.Services.AddTransient<IFileContentMediaHandler, FileM4BAudiobookMediaHandler>();
builder.Services.AddTransient<IMp3FileHandler, Mp3FileHandler>();
builder.Services.AddTransient<FileMp3AudiobookMediaHandler>();
builder.Services.AddTransient<IFileContentIndexer, FileMp3AudiobookIndexer>();
builder.Services.AddTransient<IFileContentMediaHandler, FileMp3AudiobookMediaHandler>();
builder.Services.AddTransient<IFileConverter, FileMp3AudiobookConverter>();
builder.Services.AddTransient<IFileContentManager, FileContentManager>();

builder.Services.AddTransient<IFileRenamer, EbookFileRenamer>();
builder.Services.AddTransient<IFileRenamer, M4BFileRenamer>();
builder.Services.AddTransient<IFileRenamer, Mp3FolderFileRenamer>();

// Add services to the container.
builder.Services.AddSingleton<IKoboDeviceSyncTokenService, KoboDeviceSyncTokenService>();
builder.Services.AddSingleton<ILibraryOperationLock, LibraryOperationLock>();
builder.Services.AddSingleton<ILibraryTaskBroker, LibraryTaskBroker>();
builder.Services.AddHostedService<LibraryTaskBackgroundService>();
builder.Services.AddTransient<SyncTask>();
builder.Services.AddTransient<FileManagementTask>();
builder.Services.AddTransient<IKoboProxyService, KoboStoreProxyService>();
builder.Services.AddTransient<IKoboLibrarySyncService, KoboLibrarySyncService>();
builder.Services.AddTransient<IKoboContentDeliveryService, KoboContentDeliveryService>();
builder.Services.AddTransient<IKoboReadingStateService, KoboReadingStateService>();
builder.Services.AddTransient<IKoboAnalyticsService, KoboAnalyticsService>();
builder.Services.AddTransient<IKoboProductService, KoboProductService>();
builder.Services.AddScoped<IProgressService, ProgressService>();
builder.Services.AddTransient<IIndexerService, IndexerService>();
builder.Services.AddTransient<IBookService, BookService>();
builder.Services.AddTransient<IAuthorService, AuthorService>();
builder.Services.AddTransient<ISeriesService, SeriesService>();
builder.Services.AddTransient<ICollectionService, CollectionService>();
builder.Services.AddTransient<ISettingsService, SettingsService>();
builder.Services.AddTransient<IFileManagementService, FileManagementService>();
builder.Services.AddTransient<IIntegrationService, IntegrationService>();
builder.Services.AddHostedService<IntegrationRunnerService>();

// Authentication
var authSecrets = new AuthSecretProvider();
builder.Services.AddSingleton(authSecrets);
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = AlexandriaAuthHandler.SchemeName;
})
.AddScheme<AuthenticationSchemeOptions, AlexandriaAuthHandler>(AlexandriaAuthHandler.SchemeName, null)
.AddScheme<AuthenticationSchemeOptions, KoboDeviceAuthHandler>(KoboDeviceAuthHandler.SchemeName, null);

builder.Services.AddAuthorization();

// EnableForHttps is safe here: response bodies never echo attacker-controlled input alongside secrets (BREACH).
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

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
app.UseResponseCompression();
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

// Enqueue boot-time library index. The hosted LibraryTaskBackgroundService picks it up after
// app.Run() and streams progress through ILibraryTaskBroker — so a user opening the sync popover
// during a boot index sees the live run just like a manually triggered one.
if (appSettings.SyncLibraryAtBoot)
{
    var broker = app.Services.GetRequiredService<ILibraryTaskBroker>();
    broker.TryEnqueue(typeof(SyncTask));
}

app.UseAuthentication();
app.UseAuthorization();

app.UseFileServer();
app.MapControllers();

app.Run();