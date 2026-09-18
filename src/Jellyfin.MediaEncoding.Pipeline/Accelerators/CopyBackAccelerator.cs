using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Accelerators;

/// <summary>
/// The chain for a source decoded in system memory but encoded on a device.
/// </summary>
/// <remarks>
/// Filtering a frame on the device would cost an upload and a download around every pass, so the
/// whole chain stays in memory and the device only takes the finished frame, if it takes it at all.
/// </remarks>
/// <param name="MemoryFormat">The format the finished frame is left in.</param>
/// <param name="EncoderSurface">The memory the encoder reads from.</param>
/// <param name="EncoderSuffix">The suffix the device's encoders are named with.</param>
/// <param name="ScaleFlags">The scaler the device's chain trades quality for speed with.</param>
public sealed record CopyBackAccelerator(
    PixelFormat MemoryFormat,
    FrameSurface EncoderSurface,
    string? EncoderSuffix,
    string? ScaleFlags = null) : IHardwareAccelerator
{
    /// <inheritdoc />
    public FrameSurface Surface => FrameSurface.System;

    /// <inheritdoc />
    public FrameSurface DecodeSurface => FrameSurface.System;

    /// <inheritdoc />
    public PixelFormat GetDeviceFormat(FrameState state) => MemoryFormat;

    /// <inheritdoc />
    public PixelFormat GetEncoderFormat(int bitDepth) => MemoryFormat;

    /// <inheritdoc />
    public IVideoFilter? SelectFilter(IVideoFilter filter, FrameState state, IPipelineCapabilities capabilities)
        => filter is ScaleFilter scale && !string.IsNullOrEmpty(ScaleFlags)
            ? scale with { Flags = ScaleFlags }
            : filter;

    /// <inheritdoc />
    public IVideoFilter CreateFormatFilter(PixelFormat format) => new FormatFilter(format);

    /// <inheritdoc />
    public IVideoFilter? TryFuse(IVideoFilter first, IVideoFilter second) => null;
}
