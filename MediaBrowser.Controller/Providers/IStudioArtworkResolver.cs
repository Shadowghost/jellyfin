using System.Diagnostics.CodeAnalysis;

namespace MediaBrowser.Controller.Providers;

/// <summary>
/// Maps the public request paths handed out by the studio image provider back to files inside the
/// local jellyfin-artwork bundle.
/// </summary>
public interface IStudioArtworkResolver
{
    /// <summary>
    /// Resolves a bundle-relative request path (e.g. <c>studios/2/20th-television/thumb.webp</c>)
    /// to a file inside the artwork bundle.
    /// </summary>
    /// <param name="relativePath">The path below the bundle root, as it appears in the request.</param>
    /// <param name="fullPath">The resolved absolute path on disk, when the file exists.</param>
    /// <returns><c>true</c> if the path resolved to an existing artwork file, otherwise <c>false</c>.</returns>
    bool TryResolveArtworkFile(string relativePath, [NotNullWhen(true)] out string? fullPath);
}
