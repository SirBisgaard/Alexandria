using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Database.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByUsername(string username);
    Task<User?> GetByUuid(Guid userUuid);
    Task<List<User>> GetAll();
    Task<int> Count();
    Task Add(User user);
    Task Update(User user);
    Task Delete(Guid userUuid);
    Task UpdateLastLogin(Guid userUuid);
}
