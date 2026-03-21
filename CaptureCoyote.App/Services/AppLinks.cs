namespace CaptureCoyote.App.Services;

public static class AppLinks
{
    // Set this to your public support page when you're ready.
    // Recommended first choices:
    // - GitHub Sponsors: https://github.com/sponsors/<your-account>
    // - Ko-fi: https://ko-fi.com/<your-page>
    public const string SupportUrl = "https://ko-fi.com/capturecoyote";

    // Optional public download/distribution URL for direct downloads outside the Store.
    // Example:
    // https://github.com/<you>/<repo>/releases
    public const string DownloadUrl = "";

    public static bool HasSupportUrl => Uri.IsWellFormedUriString(SupportUrl, UriKind.Absolute);

    public static bool HasDownloadUrl => Uri.IsWellFormedUriString(DownloadUrl, UriKind.Absolute);
}
