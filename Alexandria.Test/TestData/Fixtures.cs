namespace Alexandria.Tests.TestData;

/// <summary>
/// Copies copyright-free fixture files from the test assembly's output directory into
/// per-test temp folders. Fixtures under <c>TestData/</c> are read-only inputs — never modified in place.
/// </summary>
public static class Fixtures
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "TestData");

    public const string EpubFileName = "J.M. Barrie - Peter Pan.epub";
    public const string M4BFileName = "test.m4b";
    public const string Mp3FileName = "test.mp3";
    public const string CoverFileName = "cover.jpg";

    public static string CopyEpubTo(string destinationFolder)
    {
        return CopyFile(EpubFileName, destinationFolder);
    }

    public static string CopyM4BTo(string destinationFolder)
    {
        return CopyFile(M4BFileName, destinationFolder);
    }

    public static string CopyMp3To(string destinationFolder)
    {
        return CopyFile(Mp3FileName, destinationFolder);
    }

    public static string CopyMp3AudiobookTo(string destinationAudiobookFolder, string subfolderName)
    {
        var target = Path.Combine(destinationAudiobookFolder, subfolderName);
        Directory.CreateDirectory(target);
        for (var i = 1; i <= 4; i++)
        {
            CopyFile($"{i}.mp3", target);
        }
        return target;
    }

    public static string CopyCoverTo(string destinationFolder)
    {
        return CopyFile(CoverFileName, destinationFolder);
    }

    private static string CopyFile(string fileName, string destinationFolder)
    {
        Directory.CreateDirectory(destinationFolder);
        var source = Path.Combine(FixturesRoot, fileName);
        var destination = Path.Combine(destinationFolder, fileName);
        File.Copy(source, destination, overwrite: false);
        return destination;
    }
}
