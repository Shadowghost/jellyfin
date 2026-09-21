using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Accelerators;

/// <summary>
/// The accelerator used when everything runs in system memory.
/// </summary>
public sealed class NoneAccelerator : IHardwareAccelerator
{
    /// <summary>
    /// The single instance.
    /// </summary>
    public static readonly NoneAccelerator Instance = new();

    private NoneAccelerator()
    {
    }

    /// <inheritdoc />
    public FrameSurface Surface => FrameSurface.System;

    /// <inheritdoc />
    public PixelFormat GetDeviceFormat(FrameState state) => PixelFormat.YUV420P;

    /// <inheritdoc />
    public IVideoFilter? SelectFilter(IVideoFilter filter, FrameState state, IPipelineCapabilities capabilities) => filter;

    /// <inheritdoc />
    public IVideoFilter? CreateFormatFilter(PixelFormat format) => new Filters.FormatFilter(format);

    /// <inheritdoc />
    public IVideoFilter? TryFuse(IVideoFilter first, IVideoFilter second) => null;
}
