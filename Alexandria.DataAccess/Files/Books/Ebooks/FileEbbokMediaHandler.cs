using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Ebooks;

public class FileEbbokMediaHandler(IEpubFileHandler epubFileHandler, IFileRepository fileRepository) : IFileContentMediaHandler
{
    public string[] SupportedExtensions => [".epub"];

    public Task<byte[]?> ReadContent(Book book) => fileRepository.ReadKepub(book);

    public Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null) => fileRepository.ReadCover(book, width, height, quality);

    public Book ExtractMetadata(string filePath) => epubFileHandler.ExtractMetadata(filePath);

    public bool MetadataEquals(Book desired, string filePath)
    {
        var current = epubFileHandler.ExtractMetadata(filePath);
        return desired.MetadataEquals(current);
    }

    public void EmbedMetadata(Book book) => epubFileHandler.EmbedMetadata(book.File.SourcePath, book);

    public void EmbedCover(Book book, byte[] coverData, string mediaType) => epubFileHandler.EmbedCover(book.File.SourcePath, coverData, mediaType);

    public void RemoveCover(Book book) => epubFileHandler.RemoveCover(book.File.SourcePath);
}
