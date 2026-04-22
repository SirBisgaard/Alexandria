namespace Alexandria.DataAccess.Files.Services;

/// <summary>
/// File/Directory.Move wrappers that handle case-only renames on case-insensitive filesystems
/// (macOS/Windows dev). On those systems a direct <c>File.Move("foo", "Foo")</c> is a no-op or
/// error — an intermediate GUID-suffixed name is required. Linux (case-sensitive) passes straight
/// through.
/// </summary>
internal static class FileSystemSafeMove
{
    public static void File(string oldPath, string newPath)
    {
        if (NeedsTwoStep(oldPath, newPath))
        {
            var intermediate = oldPath + "." + Guid.NewGuid().ToString("N");
            System.IO.File.Move(oldPath, intermediate);
            System.IO.File.Move(intermediate, newPath);
            return;
        }
        System.IO.File.Move(oldPath, newPath);
    }

    public static void Directory(string oldPath, string newPath)
    {
        if (NeedsTwoStep(oldPath, newPath))
        {
            var intermediate = oldPath + "." + Guid.NewGuid().ToString("N");
            System.IO.Directory.Move(oldPath, intermediate);
            System.IO.Directory.Move(intermediate, newPath);
            return;
        }
        System.IO.Directory.Move(oldPath, newPath);
    }

    private static bool NeedsTwoStep(string oldPath, string newPath)
    {
        return !string.Equals(oldPath, newPath, StringComparison.Ordinal)
            && string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase);
    }
}
