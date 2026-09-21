using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.BitStreams;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using MediaBrowser.Model.MediaEncoding.Hardware;

namespace Jellyfin.Controller.Tests.MediaEncoding.FilterChains;

/// <summary>
/// An ffmpeg with every filter and a device that can do everything, matching the permissive
/// encoder mock the legacy chains are driven with.
/// </summary>
internal sealed class PermissiveCapabilities : IPipelineCapabilities
{
    public static readonly PermissiveCapabilities Instance = new(true);

    /// <summary>
    /// An ffmpeg built without the metadata bitstream filter options.
    /// </summary>
    public static readonly PermissiveCapabilities WithoutBitStreamFilterOptions = new(false);

    private readonly bool _bitStreamFilterOptions;

    private PermissiveCapabilities(bool bitStreamFilterOptions)
    {
        _bitStreamFilterOptions = bitStreamFilterOptions;
    }

    public bool SupportsFilter(string filterName) => true;

    public bool SupportsEncoder(string encoderName) => true;

    public bool SupportsBitStreamFilterOption(BitStreamFilterOption option) => _bitStreamFilterOptions;

    public bool CanPerform(HwVppKind kind, FrameSize size, PixelFormat format) => true;

    public bool SupportsSurfaceFormat(PixelFormat format) => true;
}
