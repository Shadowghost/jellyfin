using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;

/// <summary>
/// Moves a frame from a hardware surface back into system memory.
/// </summary>
/// <param name="TargetFormat">The format the frame is left in.</param>
/// <param name="MapArgument">
/// The filter that maps the frame into system memory instead of copying it, where the device and
/// driver allow it.
/// </param>
public sealed record DownloadFilter(PixelFormat TargetFormat, string? MapArgument = null) : IVideoFilter
{
    /// <inheritdoc />
    public FrameSurface? RequiredSurface => null;

    /// <inheritdoc />
    public bool PinsPixelFormat => true;

    /// <inheritdoc />
    public IVideoFilter Evaluate(FrameState state, IPipelineCapabilities capabilities) => this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state with
    {
        Surface = FrameSurface.System,
        PixelFormat = TargetFormat
    };

    /// <inheritdoc />
    public string ToFilterArgument() => $"{MapArgument ?? "hwdownload"},format={TargetFormat.Name}";
}
