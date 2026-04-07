using System.ComponentModel.DataAnnotations;

namespace Alexandria.Api.Models.Requests;

public class UpdateStatusRequest
{
    [AllowedValues("ReadyToRead", "Reading", "Finished", ErrorMessage = "Status must be one of: ReadyToRead, Reading, Finished.")]
    public string Status { get; set; } = string.Empty;
}
