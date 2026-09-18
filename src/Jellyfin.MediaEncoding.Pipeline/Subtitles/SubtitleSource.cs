using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Subtitles;

/// <summary>
/// The subtitle a job burns into the picture, and where it comes from.
/// </summary>
/// <param name="IsText">Whether a filter renders the subtitle, rather than it arriving as a stream.</param>
public sealed record SubtitleSource(bool IsText)
{
    /// <summary>
    /// Gets the size of the bitmap subtitle, which decides whether it has to be padded to the frame.
    /// </summary>
    public FrameSize? Size { get; init; }

    /// <summary>
    /// Gets the file a text subtitle is rendered from.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Gets the directory the fonts of a text subtitle are loaded from.
    /// </summary>
    public string? FontsDirectory { get; init; }

    /// <summary>
    /// Gets the character set a text subtitle is read as.
    /// </summary>
    public string? CharacterEncoding { get; init; }

    /// <summary>
    /// Gets how many frames a second a rendered text subtitle is drawn at.
    /// </summary>
    public double FrameRate { get; init; } = 25;

    /// <summary>
    /// Gets the seconds the output is seeked by, which the rendered subtitle has to be shifted by.
    /// </summary>
    public double SeekSeconds { get; init; }

    /// <summary>
    /// Gets a value indicating whether the timestamps are copied through, which leaves the shift out.
    /// </summary>
    public bool CopyTimestamps { get; init; }
}
