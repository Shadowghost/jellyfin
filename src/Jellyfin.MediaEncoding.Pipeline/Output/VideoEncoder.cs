using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Output;

/// <summary>
/// The encoder a job ends on, and what it needs the filter chain to hand it.
/// </summary>
/// <param name="Name">The encoder ffmpeg knows.</param>
/// <param name="Surface">The memory the encoder reads frames from.</param>
/// <param name="Format">The pixel format the encoder reads.</param>
public sealed record VideoEncoder(string Name, FrameSurface Surface, PixelFormat Format)
{
    /// <summary>
    /// Gets a value indicating whether the stream is copied rather than encoded.
    /// </summary>
    public bool IsCopy => string.Equals(Name, "copy", System.StringComparison.OrdinalIgnoreCase);
}
