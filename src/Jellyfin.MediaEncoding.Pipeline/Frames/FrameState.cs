namespace Jellyfin.MediaEncoding.Pipeline.Frames;

/// <summary>
/// What a video frame looks like at one point in the filter chain.
/// </summary>
/// <remarks>
/// Every filter reports the state it leaves behind, so the builder always knows where the frame
/// lives and what it holds without the vendor chains having to track it by hand.
/// </remarks>
public sealed record FrameState
{
    /// <summary>
    /// Gets the memory the frame lives in.
    /// </summary>
    public FrameSurface Surface { get; init; } = FrameSurface.System;

    /// <summary>
    /// Gets the pixel format of the frame.
    /// </summary>
    public PixelFormat PixelFormat { get; init; } = PixelFormat.NONE;

    /// <summary>
    /// Gets the dimensions of the frame.
    /// </summary>
    public FrameSize Size { get; init; }

    /// <summary>
    /// Gets a value indicating whether the frame still carries both fields.
    /// </summary>
    public bool IsInterlaced { get; init; }

    /// <summary>
    /// Gets a value indicating whether a scaler still has to resize the frame even though its size
    /// already matches, because an earlier filter only rewrote the size in the frame metadata.
    /// </summary>
    public bool RequiresResize { get; init; }

    /// <summary>
    /// Gets the high dynamic range format of the frame.
    /// </summary>
    public HdrFormat HdrFormat { get; init; } = HdrFormat.None;
}
