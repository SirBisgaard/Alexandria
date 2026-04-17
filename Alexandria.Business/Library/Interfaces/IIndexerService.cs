using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Interfaces;

public interface IIndexerService
{
    Task<int> Index();
    Task ReindexBook(Guid uuid, Book? desiredMetadata = null, CoverUpdate? coverUpdate = null);
}
