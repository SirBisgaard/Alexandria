using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Ebooks;

public class FileEbbokMediaHandler(IEpubMetadataHandler epubMetadataHandler, IFileRepository fileRepository) : IFileContentMediaHandler
{
    public string[] SupportedExtensions => [".epub"];

    public Task<byte[]?> ReadContent(Book book) => fileRepository.ReadKepub(book);

    public Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null) => fileRepository.ReadCover(book, width, height, quality);

    public Book ExtractMetadata(string filePath) => epubMetadataHandler.ExtractMetadata(filePath);

    public bool MetadataEquals(Book desired, string filePath)
    {
        var current = epubMetadataHandler.ExtractMetadata(filePath);
        return desired.MetadataEquals(current);
    }

    public void EmbedMetadata(string filePath, Book book) => epubMetadataHandler.EmbedMetadata(filePath, book);

    public void EmbedCover(string filePath, byte[] coverData, string mediaType) => epubMetadataHandler.EmbedCover(filePath, coverData, mediaType);

    public void RemoveCover(string filePath) => epubMetadataHandler.RemoveCover(filePath);
}
