using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IKoboTrafficLogRepository
{
    Task Log(KoboTrafficLog entry);
}