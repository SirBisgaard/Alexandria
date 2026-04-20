using System.Text.Json;

namespace Alexandria.Tests.KoboProtocol.Shape;

/// <summary>
/// Tests-for-the-tester. <see cref="JsonShape"/> is the structural assertion used by every Kobo
/// protocol test — its behavior is load-bearing, so its rules (permissive on values, strict on
/// keys and kinds, element-wise on arrays, opt-in strict values via <c>strictPaths</c>) have
/// their own tripwire tests here.
/// </summary>
[TestClass]
public sealed class JsonShapeTests
{
    [TestMethod]
    public void Matches_SameShape_DifferentValues_Passes()
    {
        var expected = Parse("""{"id":"abc","count":1}""");
        var actual = Parse("""{"id":"xyz","count":999}""");

        JsonShape.AssertMatches(expected, actual);
    }

    [TestMethod]
    public void Matches_MissingKey_Fails()
    {
        var expected = Parse("""{"id":"abc","count":1}""");
        var actual = Parse("""{"id":"abc"}""");

        Assert.ThrowsExactly<AssertFailedException>(() => JsonShape.AssertMatches(expected, actual));
    }

    [TestMethod]
    public void Matches_TypeChange_Fails()
    {
        var expected = Parse("""{"count":1}""");
        var actual = Parse("""{"count":"1"}""");

        Assert.ThrowsExactly<AssertFailedException>(() => JsonShape.AssertMatches(expected, actual));
    }

    [TestMethod]
    public void Matches_ExtraKeyInActual_Passes()
    {
        var expected = Parse("""{"id":"abc"}""");
        var actual = Parse("""{"id":"abc","extra":42}""");

        JsonShape.AssertMatches(expected, actual);
    }

    [TestMethod]
    public void Matches_NestedObject_ComparesRecursively()
    {
        var expected = Parse("""{"outer":{"inner":"a"}}""");
        var actual = Parse("""{"outer":{"inner":"b"}}""");

        JsonShape.AssertMatches(expected, actual);
    }

    [TestMethod]
    public void Matches_NestedMissingKey_Fails()
    {
        var expected = Parse("""{"outer":{"inner":"a"}}""");
        var actual = Parse("""{"outer":{}}""");

        Assert.ThrowsExactly<AssertFailedException>(() => JsonShape.AssertMatches(expected, actual));
    }

    [TestMethod]
    public void Matches_Arrays_ChecksEachActualAgainstFirstExpected()
    {
        var expected = Parse("""[{"id":"a","kind":"x"}]""");
        var actual = Parse("""[{"id":"a","kind":"x"},{"id":"b","kind":"y"}]""");

        JsonShape.AssertMatches(expected, actual);
    }

    [TestMethod]
    public void Matches_Arrays_ElementMissingKey_Fails()
    {
        var expected = Parse("""[{"id":"a","kind":"x"}]""");
        var actual = Parse("""[{"id":"a","kind":"x"},{"id":"b"}]""");

        Assert.ThrowsExactly<AssertFailedException>(() => JsonShape.AssertMatches(expected, actual));
    }

    [TestMethod]
    public void Matches_EmptyExpectedArray_IsPermissive()
    {
        var expected = Parse("""[]""");
        var actual = Parse("""[{"anything":true}]""");

        JsonShape.AssertMatches(expected, actual);
    }

    [TestMethod]
    public void Matches_NullExpected_IsPermissive()
    {
        var expected = Parse("""{"maybe":null}""");
        var actual = Parse("""{"maybe":"something"}""");

        JsonShape.AssertMatches(expected, actual);
    }

    [TestMethod]
    public void Matches_ActualNull_IsPermissive_ForNullableFields()
    {
        var expected = Parse("""{"maybe":"a"}""");
        var actual = Parse("""{"maybe":null}""");

        JsonShape.AssertMatches(expected, actual);
    }

    [TestMethod]
    public void Matches_StrictPath_ValueDiffers_Fails()
    {
        var expected = Parse("""{"OriginCategory":"Purchase"}""");
        var actual = Parse("""{"OriginCategory":"Imported"}""");

        Assert.ThrowsExactly<AssertFailedException>(() =>
            JsonShape.AssertMatches(expected, actual, "OriginCategory"));
    }

    [TestMethod]
    public void Matches_StrictPath_ValueMatches_Passes()
    {
        var expected = Parse("""{"OriginCategory":"Purchase"}""");
        var actual = Parse("""{"OriginCategory":"Purchase"}""");

        JsonShape.AssertMatches(expected, actual, "OriginCategory");
    }

    [TestMethod]
    public void Matches_StrictPath_InArrayElement()
    {
        var expected = Parse("""{"DownloadUrls":[{"Format":"KEPUB"}]}""");
        var actualBad = Parse("""{"DownloadUrls":[{"Format":"EPUB"}]}""");
        var actualGood = Parse("""{"DownloadUrls":[{"Format":"KEPUB"}]}""");

        Assert.ThrowsExactly<AssertFailedException>(() =>
            JsonShape.AssertMatches(expected, actualBad, "DownloadUrls[].Format"));
        JsonShape.AssertMatches(expected, actualGood, "DownloadUrls[].Format");
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
