using System.IO;

namespace CaptureCoyote.Infrastructure.Helpers;

internal static class AppStoragePaths
{
    private static string BaseDirectory => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CaptureCoyote"));

    public static string RecentProjectsDirectory => Ensure(Path.Combine(BaseDirectory, "RecentProjects"));

    public static string RecentThumbnailsDirectory => Ensure(Path.Combine(BaseDirectory, "RecentThumbnails"));

    public static string LibraryThumbnailsDirectory => Ensure(Path.Combine(BaseDirectory, "LibraryThumbnails"));

    public static string RecoveryDirectory => Ensure(Path.Combine(BaseDirectory, "Recovery"));

    public static string RecoveryThumbnailsDirectory => Ensure(Path.Combine(BaseDirectory, "RecoveryThumbnails"));

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
