using System.ComponentModel.DataAnnotations;
using Alexandria.Api.Models.Requests;
using Alexandria.Business.Library.Models;

namespace Alexandria.Tests.Old.Api;

[TestClass]
public sealed class RequestValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    private static UpdateBookMetadataRequest ValidMetadataRequest() => new()
    {
        Title = "The Last Kingdom",
        Authors = ["Bernard Cornwell"],
        Language = "en",
        PublicationDate = "2004-10-01",
        Description = "A novel about Uhtred of Bebbanburg.",
        Publisher = "HarperCollins",
        SeriesName = "The Last Kingdom",
        Index = 1
    };

    [TestMethod]
    public void UpdateBookMetadata_ValidRequest_PassesValidation()
    {
        var results = Validate(ValidMetadataRequest());
        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void UpdateBookMetadata_EmptyTitle_FailsValidation()
    {
        var request = ValidMetadataRequest();
        request.Title = "";
        var results = Validate(request);
        Assert.IsNotEmpty(results);
    }

    [TestMethod]
    public void UpdateBookMetadata_EmptyAuthorsList_FailsValidation()
    {
        var request = ValidMetadataRequest();
        request.Authors = [];
        var results = Validate(request);
        Assert.IsNotEmpty(results);
    }

    [TestMethod]
    public void UpdateBookMetadata_AllWhitespaceAuthors_FailsValidation()
    {
        var request = ValidMetadataRequest();
        request.Authors = [" ", "  "];
        var results = Validate(request);
        Assert.IsNotEmpty(results);
    }

    [TestMethod]
    public void UpdateBookMetadata_EmptyLanguage_PassesValidation()
    {
        var request = ValidMetadataRequest();
        request.Language = "";
        var results = Validate(request);
        Assert.IsEmpty(results);
    }

    [TestMethod]
    [DataRow("2004")]
    [DataRow("2004-10")]
    [DataRow("2004-10-01")]
    public void UpdateBookMetadata_ValidPublicationDate_PassesValidation(string date)
    {
        var request = ValidMetadataRequest();
        request.PublicationDate = date;
        var results = Validate(request);
        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void UpdateBookMetadata_NullPublicationDate_PassesValidation()
    {
        var request = ValidMetadataRequest();
        request.PublicationDate = null;
        var results = Validate(request);
        Assert.IsEmpty(results);
    }

    [TestMethod]
    [DataRow("not-a-date")]
    [DataRow("01-10-2004")]
    [DataRow("2004/10/01")]
    public void UpdateBookMetadata_InvalidPublicationDate_FailsValidation(string date)
    {
        var request = ValidMetadataRequest();
        request.PublicationDate = date;
        var results = Validate(request);
        Assert.IsNotEmpty(results);
    }

    [TestMethod]
    public void UpdateBookMetadata_NegativeIndex_FailsValidation()
    {
        var request = ValidMetadataRequest();
        request.Index = -1;
        var results = Validate(request);
        Assert.IsNotEmpty(results);
    }

    [TestMethod]
    public void UpdateBookMetadata_ZeroIndex_PassesValidation()
    {
        var request = ValidMetadataRequest();
        request.Index = 0;
        var results = Validate(request);
        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void UpdateBookMetadata_NullOptionalFields_PassesValidation()
    {
        var request = new UpdateBookMetadataRequest
        {
            Title = "Title",
            Authors = ["Author"],
            Language = "en"
        };
        var results = Validate(request);
        Assert.IsEmpty(results);
    }

    [TestMethod]
    [DataRow(ReadingStatus.ReadyToRead)]
    [DataRow(ReadingStatus.Reading)]
    [DataRow(ReadingStatus.Finished)]
    public void UpdateStatus_ValidStatus_PassesValidation(ReadingStatus status)
    {
        var results = Validate(new UpdateStatusRequest { Status = status });
        Assert.IsEmpty(results);
    }
}
