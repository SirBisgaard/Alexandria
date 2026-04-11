using System.ComponentModel.DataAnnotations;

namespace Alexandria.Api.Models.Requests;

public class UpdateBookMetadataRequest : IValidatableObject
{
    [Required, MinLength(1)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "At least one author is required."), MinLength(1)]
    public List<string> Authors { get; set; } = [];

    public string? Language { get; set; }

    [RegularExpression(@"^\d{4}(-\d{2}(-\d{2})?)?$", ErrorMessage = "PublicationDate must be in YYYY, YYYY-MM, or YYYY-MM-DD format.")]
    public string? PublicationDate { get; set; }

    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? SeriesName { get; set; }

    [Range(0, (double)decimal.MaxValue, ErrorMessage = "Index must be zero or positive.")]
    public decimal? Index { get; set; }

    public bool? IsAbridged { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Authors.Count > 0 && Authors.All(string.IsNullOrWhiteSpace))
            yield return new ValidationResult("At least one author is required.", [nameof(Authors)]);
    }
}
