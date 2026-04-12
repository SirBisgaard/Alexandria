namespace Alexandria.Business.Interfaces;

public interface IDatabasePersistence
{
    Task<bool> Flush();
}
