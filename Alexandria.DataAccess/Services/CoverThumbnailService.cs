using Alexandria.Business.Interfaces;
using Alexandria.Business.Models.Settings;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess.Services;

public class CoverThumbnailService(AlexandriaSettings settings, ILogger<CoverThumbnailService> logger) : ICoverThumbnailService
{
    public async Task<byte[]?> GetOrCreateThumbnail(string? coverFilePath, Guid uuid, int width, int height, int quality)
    {
        if (coverFilePath is null || !File.Exists(coverFilePath))
        {
            return null;
        }

        var subdirectory = $"{width}x{height}q{quality}";
        var thumbnailDir = Path.Combine(settings.CoversFolder, subdirectory);
        var thumbnailPath = Path.Combine(thumbnailDir, $"{uuid}.jpg");

        if (File.Exists(thumbnailPath))
        {
            return await File.ReadAllBytesAsync(thumbnailPath);
        }

        Directory.CreateDirectory(thumbnailDir);

        using var image = new MagickImage(coverFilePath);
        var geometry = new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = false };
        image.Resize(geometry);
        image.Quality = (uint)quality;
        image.Format = MagickFormat.Jpeg;
        await image.WriteAsync(thumbnailPath);

        logger.LogInformation("Generated thumbnail {Size} for {Uuid}", subdirectory, uuid);

        return await File.ReadAllBytesAsync(thumbnailPath);
    }

    public byte[] ConvertToJpeg(byte[] imageData)
    {
        using var image = new MagickImage(imageData);
        image.Format = MagickFormat.Jpeg;
        image.Quality = 100;
        return image.ToByteArray();
    }

    public void DeleteThumbnails(Guid uuid)
    {
        var fileName = $"{uuid}.jpg";
        if (!Directory.Exists(settings.CoversFolder))
        {
            return;
        }

        foreach (var dir in Directory.GetDirectories(settings.CoversFolder, "*x*q*"))
        {
            var thumbnailPath = Path.Combine(dir, fileName);
            if (File.Exists(thumbnailPath))
            {
                File.Delete(thumbnailPath);
            }
        }
    }
}
