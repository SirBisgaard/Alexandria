namespace Alexandria.DataAccess.Database.Cache;

public class CacheInvalidator
{
    internal Action? InvalidateBookCache { get; set; }
    internal Action? InvalidateAuthorCache { get; set; }
    internal Action? InvalidateSeriesCache { get; set; }
    internal Action? InvalidateCollectionCache { get; set; }
    internal Action? InvalidateReadingStateCache { get; set; }

    public void InvalidateBooks()
    {
        InvalidateBookCache?.Invoke();
    }

    public void InvalidateAuthors()
    {
        InvalidateAuthorCache?.Invoke();
        InvalidateBookCache?.Invoke();
    }

    public void InvalidateSeries()
    {
        InvalidateSeriesCache?.Invoke();
        InvalidateBookCache?.Invoke();
    }

    public void InvalidateCollections()
    {
        InvalidateCollectionCache?.Invoke();
    }

    public void InvalidateReadingStates()
    {
        InvalidateReadingStateCache?.Invoke();
    }

    public void InvalidateAll()
    {
        InvalidateBookCache?.Invoke();
        InvalidateAuthorCache?.Invoke();
        InvalidateSeriesCache?.Invoke();
        InvalidateCollectionCache?.Invoke();
        InvalidateReadingStateCache?.Invoke();
    }
}
