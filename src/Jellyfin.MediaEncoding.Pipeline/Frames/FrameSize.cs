namespace Jellyfin.MediaEncoding.Pipeline.Frames;

/// <summary>
/// The pixel dimensions of a video frame.
/// </summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
public readonly record struct FrameSize(int Width, int Height)
{
    /// <summary>
    /// Gets a size with both dimensions rounded down to an even number.
    /// </summary>
    /// <returns>The rounded size.</returns>
    public FrameSize RoundToEven() => new(Width - (Width % 2), Height - (Height % 2));
}
