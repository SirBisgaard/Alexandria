using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Database.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByUsername(string username);
    Task<List<User>> GetAll();
    Task Add(User user);
    Task Update(User user);
    Task UpdateLastLogin(Guid userUuid);
}
