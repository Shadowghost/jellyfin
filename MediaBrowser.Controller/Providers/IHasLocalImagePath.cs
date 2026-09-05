using System.Diagnostics.CodeAnalysis;

namespace MediaBrowser.Controller.Providers;

/// <summary>
/// Implemented by remote image providers whose images are already files on this server.
/// </summary>
public interface IHasLocalImagePath
{
    /// <summary>
    /// Resolves one of this provider's image URLs to the file backing it.
    /// </summary>
    /// <param name="url">A URL previously returned by this provider.</param>
    /// <param name="path">The absolute path on disk, when the URL is backed by a local file.</param>
    /// <returns><c>true</c> if the URL resolved to an existing local file, otherwise <c>false</c>.</returns>
    bool TryGetLocalImagePath(string url, [NotNullWhen(true)] out string? path);
}
