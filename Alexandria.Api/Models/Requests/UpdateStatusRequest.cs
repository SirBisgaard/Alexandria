using Alexandria.Business.Models;

namespace Alexandria.Api.Models.Requests;

public class UpdateStatusRequest
{
    public ReadingStatus Status { get; set; }
}
