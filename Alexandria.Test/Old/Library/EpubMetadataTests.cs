using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;

namespace Alexandria.Tests.Library;

[TestClass]
public sealed class EpubMetadataTests
{
    private static Book CreateBook() => new()
    {
        Title = "Life and Fate",
        Language = "en",
        Authors = [new Author { Name = "Vasily Grossman" }],
        PublicationDate = "1980-01-01",
        Description = "A novel",
        Publisher = "Harvill Press",
        Series = new Series { Name = "Test Series", Index = 1.0m }
    };

    [TestMethod]
    public void MetadataEquals_IdenticalMetadata_ReturnsTrue()
    {
        var a = CreateBook();
        var b = CreateBook();

        Assert.IsTrue(a.MetadataEquals(b));
    }

    [TestMethod]
    public void MetadataEquals_NullVsEmptyString_ReturnsTrue()
    {
        var a = CreateBook();
        a.PublicationDate = null;
        a.Description = null;
        a.Publisher = null;
        a.Series = null;

        var b = CreateBook();
        b.PublicationDate = "";
        b.Description = "";
        b.Publisher = "";
        b.Series = new Series { Name = "" };

        Assert.IsTrue(a.MetadataEquals(b));
    }

    [TestMethod]
    public void MetadataEquals_DifferentTitle_ReturnsFalse()
    {
        var a = CreateBook();
        var b = CreateBook();
        b.Title = "Different Title";

        Assert.IsFalse(a.MetadataEquals(b));
    }

    [TestMethod]
    public void MetadataEquals_DifferentAuthorOrder_ReturnsTrue()
    {
        var a = CreateBook();
        a.Authors = [new Author { Name = "Author A" }, new Author { Name = "Author B" }];

        var b = CreateBook();
        b.Authors = [new Author { Name = "Author B" }, new Author { Name = "Author A" }];

        Assert.IsTrue(a.MetadataEquals(b));
    }

    [TestMethod]
    public void MetadataEquals_DifferentSeriesIndex_ReturnsFalse()
    {
        var a = CreateBook();
        var b = CreateBook();
        b.Series = new Series { Name = "Test Series", Index = 2.0m };

        Assert.IsFalse(a.MetadataEquals(b));
    }

}
