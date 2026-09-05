using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Controller.Providers;

namespace MediaBrowser.Providers.Plugins.StudioImages;

/// <summary>
/// DI-visible front for <see cref="StudioArtworkManager"/>, so the API layer can serve bundle files
/// without taking a dependency on the bundled plugin or on its on-disk layout.
/// </summary>
public class StudioArtworkResolver : IStudioArtworkResolver
{
    /// <inheritdoc />
    public bool TryResolveArtworkFile(string relativePath, [NotNullWhen(true)] out string? fullPath)
        => StudioArtworkManager.TryResolveArtworkFile(relativePath, out fullPath);
}
