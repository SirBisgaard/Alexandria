namespace Alexandria.Business.Library.Models.Others;

public record CoverUpdate(byte[]? CoverData = null, string? MediaType = null, bool RemoveCover = false) { }
