namespace Alexandria.Business.Interfaces;

public interface ILibraryIndexerService
{
    Task<int> Index(IProgress<(int current, int total)>? progress = null);
    Task ReindexBook(Guid uuid);
}