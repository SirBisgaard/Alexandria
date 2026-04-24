using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Services;

namespace Alexandria.Tests.Business;

[TestClass]
public sealed class FileNameTemplateTests
{
    [TestMethod]
    public void Render_PlainTokens_Expand()
    {
        var book = BuildBook(title: "Sample Title", authors: ["Sample Author"]);

        var result = FileNameTemplate.Render(book, "<Authors> - <Title>");

        Assert.AreEqual("Sample Author - Sample Title", result);
    }

    [TestMethod]
    public void Render_MultipleAuthors_CommaJoined()
    {
        var book = BuildBook(title: "Sample Title", authors: ["First Author", "Second Author"]);

        var result = FileNameTemplate.Render(book, "<Authors> - <Title>");

        Assert.AreEqual("First Author, Second Author - Sample Title", result);
    }

    [TestMethod]
    public void Render_OptionalGroupWithAllTokensPresent_EmitsGroup()
    {
        var book = BuildBook(title: "Sample Title", authors: ["Sample Author"], seriesName: "Sample Series", seriesIndex: 13m);

        var result = FileNameTemplate.Render(book, "<Authors> - (<Serie> #<SerieIndex> - )<Title>");

        Assert.AreEqual("Sample Author - Sample Series #13 - Sample Title", result);
    }

    [TestMethod]
    public void Render_OptionalGroupWithEmptyToken_GroupIsDropped()
    {
        var book = BuildBook(title: "Sample Title", authors: ["Sample Author"]);

        var result = FileNameTemplate.Render(book, "<Authors> - (<Serie> #<SerieIndex> - )<Title>");

        Assert.AreEqual("Sample Author - Sample Title", result);
    }

    [TestMethod]
    public void Render_OptionalGroupWithPartialEmpty_GroupIsDropped()
    {
        var book = BuildBook(title: "Sample Title", authors: ["Sample Author"], seriesName: "Sample Series", seriesIndex: null);

        var result = FileNameTemplate.Render(book, "(<Serie> #<SerieIndex> - )<Title>");

        Assert.AreEqual("Sample Title", result);
    }

    [TestMethod]
    public void Render_SerieIndexWithDecimal_TrimsTrailingZeros()
    {
        var book = BuildBook(title: "Sample Title", authors: ["Sample Author"], seriesName: "Sample Series", seriesIndex: 17.5m);

        var result = FileNameTemplate.Render(book, "<Serie> #<SerieIndex> - <Title>");

        Assert.AreEqual("Sample Series #17.5 - Sample Title", result);
    }

    [TestMethod]
    public void Render_SerieIndexWholeNumber_NoDecimalPoint()
    {
        var book = BuildBook(title: "x", authors: ["a"], seriesName: "S", seriesIndex: 1m);

        var result = FileNameTemplate.Render(book, "<SerieIndex>");

        Assert.AreEqual("1", result);
    }

    [TestMethod]
    public void Render_InvalidFilenameCharsInTitle_AreStripped()
    {
        var book = BuildBook(title: "What?: A/Story\\Of <Things>", authors: ["A"]);

        var result = FileNameTemplate.Render(book, "<Authors> - <Title>");

        Assert.IsFalse(result.Contains('?'));
        Assert.IsFalse(result.Contains(':'));
        Assert.IsFalse(result.Contains('/'));
        Assert.IsFalse(result.Contains('\\'));
        Assert.IsFalse(result.Contains('<'));
        Assert.IsFalse(result.Contains('>'));
    }

    [TestMethod]
    public void Render_WhitespaceIsCollapsed()
    {
        var book = BuildBook(title: "Hello   World", authors: ["A"]);

        var result = FileNameTemplate.Render(book, "<Title>");

        Assert.AreEqual("Hello World", result);
    }

    [TestMethod]
    public void Render_LiteralWhitespace_IsPreserved()
    {
        var book = BuildBook(title: "The Great Gatsby", authors: ["F. Scott Fitzgerald"], seriesName: "Modern Classics", seriesIndex: 1m);

        var result = FileNameTemplate.Render(book, "<Authors> - (<Serie> #<SerieIndex> - )       <Title>");

        Assert.AreEqual("F. Scott Fitzgerald - Modern Classics #1 -        The Great Gatsby", result);
    }

    [TestMethod]
    public void Render_TrailingDashAfterDroppedGroup_IsTrimmed()
    {
        var book = BuildBook(title: "Solo", authors: ["A"]);

        var result = FileNameTemplate.Render(book, "<Title> - (<Serie>)");

        Assert.AreEqual("Solo", result);
    }

    [TestMethod]
    public void Render_LeadingDashAfterDroppedGroup_IsTrimmed()
    {
        var book = BuildBook(title: "Solo", authors: ["A"]);

        var result = FileNameTemplate.Render(book, "(<Serie> - )<Title>");

        Assert.AreEqual("Solo", result);
    }

    [TestMethod]
    public void Render_EscapedParens_TreatedAsLiterals()
    {
        var book = BuildBook(title: "T", authors: ["A"]);

        var result = FileNameTemplate.Render(book, "\\(literal\\) <Title>");

        Assert.AreEqual("(literal) T", result);
    }

    [TestMethod]
    public void Render_UnknownToken_Throws()
    {
        var book = BuildBook();

        Assert.ThrowsExactly<TemplateException>(() => FileNameTemplate.Render(book, "<Nope>"));
    }

    [TestMethod]
    public void Render_UnclosedToken_Throws()
    {
        var book = BuildBook();

        Assert.ThrowsExactly<TemplateException>(() => FileNameTemplate.Render(book, "<Title"));
    }

    [TestMethod]
    public void Render_UnclosedGroup_Throws()
    {
        var book = BuildBook();

        Assert.ThrowsExactly<TemplateException>(() => FileNameTemplate.Render(book, "(<Title>"));
    }

    [TestMethod]
    public void Render_StrayCloseParen_Throws()
    {
        var book = BuildBook();

        Assert.ThrowsExactly<TemplateException>(() => FileNameTemplate.Render(book, "abc)"));
    }

    [TestMethod]
    public void Render_NestedGroups_Throws()
    {
        var book = BuildBook(title: "T", authors: ["A"]);

        Assert.ThrowsExactly<TemplateException>(() => FileNameTemplate.Render(book, "(<Title>(<Serie>))"));
    }

    [TestMethod]
    public void Render_LongTitleWithoutFixedContext_TruncatesToBudget()
    {
        var longTitle = new string('a', 500);
        var book = BuildBook(title: longTitle, authors: ["A"]);

        var result = FileNameTemplate.Render(book, "<Title>");

        Assert.IsLessThanOrEqualTo(FileNameTemplate.MaxBaseNameLength, result.Length);
    }

    [TestMethod]
    public void Render_LongCombined_TitleIsTruncatedFirst()
    {
        var author = "A";
        var longTitle = new string('t', 500);
        var book = BuildBook(title: longTitle, authors: [author]);

        var result = FileNameTemplate.Render(book, "<Authors> - <Title>");

        Assert.IsLessThanOrEqualTo(FileNameTemplate.MaxBaseNameLength, result.Length);
        Assert.StartsWith("A - ", result);
    }

    [TestMethod]
    public void Validate_WellFormedTemplate_Ok()
    {
        var result = FileNameTemplate.Validate("<Authors> - (<Serie> #<SerieIndex> - )<Title>");

        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Validate_Empty_Invalid()
    {
        var result = FileNameTemplate.Validate("");

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void Validate_UnknownToken_InvalidWithMessage()
    {
        var result = FileNameTemplate.Validate("<Foo>");

        Assert.IsFalse(result.IsValid);
        Assert.Contains("Foo", result.Error!);
    }

    [TestMethod]
    public void Validate_UnclosedGroup_Invalid()
    {
        var result = FileNameTemplate.Validate("(<Title>");

        Assert.IsFalse(result.IsValid);
    }

    private static Book BuildBook(string title = "Title", List<string>? authors = null, string? seriesName = null, decimal? seriesIndex = null)
    {
        return new Book
        {
            Title = title,
            Authors = (authors ?? ["Author"]).Select(n => new Author { Name = n }).ToList(),
            Series = seriesName is null ? null : new Series { Name = seriesName, Index = seriesIndex }
        };
    }
}
