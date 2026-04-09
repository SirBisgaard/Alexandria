using Alexandria.Business.Library.Models;

namespace Alexandria.Api.Models.Requests;

public class UpdateStatusRequest
{
    public ReadingStatus Status { get; set; }
}
