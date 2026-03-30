using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;

namespace Alexandria.DataAccess.Services;

public class IndexFileService(AlexandriaSettings settings) : IIndexFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public List<Book> Books { get; set; } = [];

    public async Task LoadAsync()
    {
        if (!File.Exists(settings.IndexFile))
        {
            Books = [];
            return;
        }

        var json = await File.ReadAllTextAsync(settings.IndexFile);
        if (string.IsNullOrWhiteSpace(json))
        {
            Books = [];
            return;
        }

        var index = JsonSerializer.Deserialize<IndexFile>(json, JsonOptions);
        Books = index?.Books ?? [];
    }

    public async Task SaveAsync()
    {
        var index = new IndexFile { Books = Books };
        var json = JsonSerializer.Serialize(index, JsonOptions);
        var directory = Path.GetDirectoryName(settings.IndexFile);
        if (directory is not null)
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(settings.IndexFile, json);
    }

    public async Task<string> ComputeFileHashAsync(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream);
        return $"sha256:{Convert.ToHexStringLower(hash)}";
    }

    public Book? FindByHash(string fileHash)
    {
        return Books.FirstOrDefault(b => b.FileHash == fileHash);
    }

    public Book? FindByUuid(Guid uuid)
    {
        return Books.FirstOrDefault(b => b.Uuid == uuid);
    }

    private class IndexFile
    {
        public List<Book> Books { get; set; } = [];
    }
}
