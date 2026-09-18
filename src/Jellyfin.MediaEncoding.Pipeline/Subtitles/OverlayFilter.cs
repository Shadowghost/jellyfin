using System.Globalization;
using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Subtitles;

/// <summary>
/// Draws the subtitle branch over the picture.
/// </summary>
/// <param name="Name">The filter name, such as <c>overlay_cuda</c>.</param>
/// <param name="Surface">The surface both inputs have to be on.</param>
/// <param name="Size">The frame size, for the devices whose overlay has to be told it.</param>
public sealed record OverlayFilter(string Name, FrameSurface Surface, FrameSize? Size = null) : IVideoFilter
{
    /// <summary>
    /// Gets the options the device needs on top of the shared ones.
    /// </summary>
    public string? ExtraOptions { get; init; }

    /// <inheritdoc />
    public FrameSurface? RequiredSurface => Surface;

    /// <inheritdoc />
    public bool PinsPixelFormat => false;

    /// <inheritdoc />
    public IVideoFilter Evaluate(FrameState state, IPipelineCapabilities capabilities) => this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state;

    /// <inheritdoc />
    public string ToFilterArgument()
    {
        var argument = Name + "=eof_action=pass:repeatlast=0";

        if (!string.IsNullOrEmpty(ExtraOptions))
        {
            argument += ":" + ExtraOptions;
        }

        return Size is { } size
            ? string.Create(CultureInfo.InvariantCulture, $"{argument}:w={size.Width}:h={size.Height}")
            : argument;
    }
}
