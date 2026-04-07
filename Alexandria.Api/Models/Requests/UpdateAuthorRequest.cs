using System.ComponentModel.DataAnnotations;

namespace Alexandria.Api.Models.Requests;

public class UpdateAuthorRequest : IValidatableObject
{
    [Required, MinLength(1)]
    public string Name { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(Name) && string.IsNullOrWhiteSpace(Name))
            yield return new ValidationResult("Name cannot be only whitespace.", [nameof(Name)]);
    }
}
