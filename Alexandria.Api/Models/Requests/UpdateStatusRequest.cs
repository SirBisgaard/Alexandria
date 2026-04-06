using System.ComponentModel.DataAnnotations;

namespace Alexandria.Api.Models.Requests;

public record UpdateStatusRequest([property: AllowedValues("ReadyToRead", "Reading", "Finished", ErrorMessage = "Status must be one of: ReadyToRead, Reading, Finished.")] string Status);