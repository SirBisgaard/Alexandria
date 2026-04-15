using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Interfaces;

public interface IIndexerService
{
    Task<int> Index(IProgress<(int current, int total)>? progress = null);
    Task ReindexBook(Guid uuid, Book? desiredMetadata = null, CoverUpdate? coverUpdate = null);
}
