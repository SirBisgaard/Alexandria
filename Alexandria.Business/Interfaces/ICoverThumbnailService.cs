namespace Alexandria.Business.Interfaces;

public interface ICoverThumbnailService
{
    Task<byte[]?> GetOrCreateThumbnail(string? coverFilePath, Guid uuid, int width, int height, int quality);
    void DeleteThumbnails(Guid uuid);
    byte[] ConvertToJpeg(byte[] imageData);
}
