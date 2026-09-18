using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Accelerators;

/// <summary>
/// A device that encodes but does no filtering of its own, so the picture is prepared in system
/// memory and handed to it.
/// </summary>
/// <param name="EncoderSuffix">The suffix the device's encoders are named with.</param>
public sealed record EncoderOnlyAccelerator(string? EncoderSuffix) : IHardwareAccelerator
{
    /// <inheritdoc />
    public FrameSurface Surface => FrameSurface.System;

    /// <inheritdoc />
    public PixelFormat GetDeviceFormat(FrameState state) => PixelFormat.Unknown;

    /// <inheritdoc />
    public PixelFormat GetEncoderFormat(int bitDepth)
        => bitDepth >= 10 ? PixelFormat.Yuv420p10le : PixelFormat.Yuv420p;

    /// <inheritdoc />
    public IVideoFilter? SelectFilter(IVideoFilter filter, FrameState state, IPipelineCapabilities capabilities) => filter;

    /// <inheritdoc />
    public IVideoFilter? CreateFormatFilter(PixelFormat format) => new Filters.FormatFilter(format);

    /// <inheritdoc />
    public IVideoFilter? TryFuse(IVideoFilter first, IVideoFilter second) => null;
}
