using Alexandria.Business.Models.Files;

namespace Alexandria.Tests.Library;

[TestClass]
public sealed class EpubMetadataTests
{
    private static EpubMetadata CreateMetadata() => new()
    {
        Title = "Life and Fate",
        Language = "en",
        Authors = ["Vasily Grossman"],
        PublicationDate = "1980-01-01",
        Description = "A novel",
        Publisher = "Harvill Press",
        SeriesName = "Test Series",
        SeriesIndex = 1.0m
    };

    [TestMethod]
    public void MetadataEquals_IdenticalMetadata_ReturnsTrue()
    {
        var a = CreateMetadata();
        var b = CreateMetadata();

        Assert.IsTrue(a.MetadataEquals(b));
    }

    [TestMethod]
    public void MetadataEquals_NullVsEmptyString_ReturnsTrue()
    {
        var a = CreateMetadata();
        a.PublicationDate = null;
        a.Description = null;
        a.Publisher = null;
        a.SeriesName = null;

        var b = CreateMetadata();
        b.PublicationDate = "";
        b.Description = "";
        b.Publisher = "";
        b.SeriesName = "";

        Assert.IsTrue(a.MetadataEquals(b));
    }

    [TestMethod]
    public void MetadataEquals_DifferentTitle_ReturnsFalse()
    {
        var a = CreateMetadata();
        var b = CreateMetadata();
        b.Title = "Different Title";

        Assert.IsFalse(a.MetadataEquals(b));
    }

    [TestMethod]
    public void MetadataEquals_DifferentAuthorOrder_ReturnsFalse()
    {
        var a = CreateMetadata();
        a.Authors = ["Author A", "Author B"];

        var b = CreateMetadata();
        b.Authors = ["Author B", "Author A"];

        Assert.IsFalse(a.MetadataEquals(b));
    }

    [TestMethod]
    public void MetadataEquals_DifferentSeriesIndex_ReturnsFalse()
    {
        var a = CreateMetadata();
        var b = CreateMetadata();
        b.SeriesIndex = 2.0m;

        Assert.IsFalse(a.MetadataEquals(b));
    }

    [TestMethod]
    public void MetadataEquals_DifferentCoverFilePath_ReturnsTrue()
    {
        var a = CreateMetadata();
        a.CoverFilePath = "/covers/a.jpg";

        var b = CreateMetadata();
        b.CoverFilePath = "/covers/b.jpg";

        Assert.IsTrue(a.MetadataEquals(b));
    }

    [TestMethod]
    public void MetadataEquals_DifferentAlexandriaUuid_ReturnsTrue()
    {
        var a = CreateMetadata();
        a.AlexandriaUuid = Guid.NewGuid();

        var b = CreateMetadata();
        b.AlexandriaUuid = Guid.NewGuid();

        Assert.IsTrue(a.MetadataEquals(b));
    }
}
