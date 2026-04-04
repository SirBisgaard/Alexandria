namespace Alexandria.Business.Interfaces;

public interface ILibraryIndexerService
{
    Task Index();
    Task ReindexBook(Guid uuid);
}