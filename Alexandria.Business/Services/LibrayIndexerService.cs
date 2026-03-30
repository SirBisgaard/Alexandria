using Alexandria.Business.Interfaces;

namespace Alexandria.Business.Services;

public class LibraryIndexerService : ILibraryIndexerService
{
    public async Task Index()
    {
        // Load index file.
        
        // Load book files in book folder.
        
        // Convert new or update books. (SHA hash not matched)
        
        // Exstract new or update cover photos (SHA hash not matched)
        
        // Update index file with changes.
    }
}