using System.Text.Json.Serialization;
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
using Alexandria.DataAccess.Database.Cache;
using Alexandria.DataAccess.Files.Books.Ebooks;
using Alexandria.DataAccess.Files.Interfaces;
using Alexandria.DataAccess.Files.Services;

var builder = WebApplication.CreateBuilder(args);

// App settings load.
var alexandriaSettings =  builder.Configuration
    .GetSection("AlexandriaSettings")
    .Get<AlexandriaSettings>()!;

builder.Services.AddSingleton(alexandriaSettings);

// Http client for KoboStoreApi
builder.Services.AddHttpClient<KoboStoreProxyService>(_ => { }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Add repositories to the container.
builder.Services.AddTransient<IDatabaseInitializer, DatabaseInitializer>();
builder.Services.AddTransient<IKoboTrafficLogRepository, KoboTrafficLogRepository>();
builder.Services.AddTransient<IKoboDeviceSyncRepository, KoboDeviceSyncRepository>();
builder.Services.AddTransient<ISettingsRepository, SettingsRepository>();
builder.Services.AddTransient<IFileRepository, FileRepository>();

builder.Services.AddSingleton<BookRepository>();
builder.Services.AddSingleton<ReadingStateRepository>();
builder.Services.AddSingleton<CollectionRepository>();
builder.Services.AddSingleton<AuthorRepository>();
builder.Services.AddSingleton<SeriesRepository>();
// Cache layer — decorators wrap the concrete repositories above.
builder.Services.AddSingleton<CacheInvalidator>();
builder.Services.AddSingleton<IAuthorRepository>(sp => new CachingAuthorRepository(sp.GetRequiredService<AuthorRepository>(), sp.GetRequiredService<CacheInvalidator>()).Init());
builder.Services.AddSingleton<ISeriesRepository>(sp => new CachingSeriesRepository(sp.GetRequiredService<SeriesRepository>(), sp.GetRequiredService<CacheInvalidator>()).Init());
builder.Services.AddSingleton<IBookRepository>(sp => new CachingBookRepository(sp.GetRequiredService<BookRepository>(), sp.GetRequiredService<CacheInvalidator>()).Init());
builder.Services.AddSingleton<ICollectionRepository>(sp => new CachingCollectionRepository(sp.GetRequiredService<CollectionRepository>(), sp.GetRequiredService<CacheInvalidator>()).Init());
builder.Services.AddSingleton<IReadingStateRepository>(sp => new CachingReadingStateRepository(sp.GetRequiredService<ReadingStateRepository>(), sp.GetRequiredService<CacheInvalidator>()).Init());

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
builder.Services.AddTransient<IKoboProxyService, KoboStoreProxyService>();
builder.Services.AddTransient<IKoboLibrarySyncService, KoboLibrarySyncService>();
builder.Services.AddTransient<IKoboContentDeliveryService, KoboContentDeliveryService>();
builder.Services.AddTransient<IKoboReadingStateService, KoboReadingStateService>();
builder.Services.AddTransient<IKoboProductService, KoboProductService>();
builder.Services.AddTransient<ILibraryIndexerService, AlexandriaIndexerService>();
builder.Services.AddTransient<IAlexandriaLibraryService, AlexandriaLibraryService>();
builder.Services.AddTransient<IAlexandriaSettingsService, AlexandriaSettingsService>();

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseMiddleware<KoboStoreProxyLoggingMiddleware>();

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
