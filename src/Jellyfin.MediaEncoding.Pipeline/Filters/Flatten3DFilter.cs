using Jellyfin.MediaEncoding.Pipeline.Frames;
using MediaBrowser.Model.Entities;

namespace Jellyfin.MediaEncoding.Pipeline.Filters;

/// <summary>
/// Drops the second view of a frame packed 3D frame, leaving a plain 2D one.
/// </summary>
/// <remarks>
/// Cropping only rewrites frame metadata, so it runs on a hardware frame too. The half formats then
/// need the remaining view stretched back out: here in system memory, by the scaler on a device.
/// </remarks>
/// <param name="Format">The packing of the source.</param>
/// <param name="OnDevice">Whether the crop runs on a hardware frame.</param>
public sealed record Flatten3DFilter(Video3DFormat? Format, bool OnDevice) : IVideoFilter
{
    /// <summary>
    /// Gets the surface the crop has to run on, or <c>null</c> to leave it wherever the frame is.
    /// </summary>
    /// <remarks>
    /// A device that filters somewhere other than where it decodes wants the crop on the filtering
    /// side, so that the scaler behind it sees the cropped rectangle.
    /// </remarks>
    public FrameSurface? DeviceSurface { get; init; }

    /// <inheritdoc />
    public FrameSurface? RequiredSurface => OnDevice ? DeviceSurface : FrameSurface.System;

    /// <inheritdoc />
    public bool PinsPixelFormat => false;

    /// <summary>
    /// Gets the size a source has once its packed views have been flattened down to one.
    /// </summary>
    /// <param name="format">The packing of the source.</param>
    /// <param name="size">The size of the source frame.</param>
    /// <returns>The flattened size.</returns>
    public static FrameSize Flatten(Video3DFormat? format, FrameSize size) => format switch
    {
        Video3DFormat.FullSideBySide => size with { Width = size.Width / 4 * 2 },
        Video3DFormat.FullTopAndBottom => size with { Height = size.Height / 4 * 2 },
        _ => size
    };

    /// <inheritdoc />
    public IVideoFilter? Evaluate(FrameState state, IPipelineCapabilities capabilities)
        => ToFilterArgument() is null ? null : this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state with
    {
        Size = Flatten(Format, state.Size),
        RequiresResize = OnDevice
    };

    /// <inheritdoc />
    public string? ToFilterArgument()
    {
        // The base view of an MVC stream already is a plain 2D frame, the decoder drops the other one.
        var crop = Format switch
        {
            Video3DFormat.FullSideBySide or Video3DFormat.HalfSideBySide => "crop=trunc(iw/4)*2:ih:0:0",
            Video3DFormat.FullTopAndBottom or Video3DFormat.HalfTopAndBottom => "crop=iw:trunc(ih/4)*2:0:0",
            _ => null
        };

        if (crop is null)
        {
            return crop;
        }

        return Format switch
        {
            Video3DFormat.HalfSideBySide => OnDevice ? crop + ",setsar=sar=1" : crop + ",scale=iw*2:ih,setsar=sar=1",
            Video3DFormat.HalfTopAndBottom => OnDevice ? crop + ",setsar=sar=1" : crop + ",scale=iw:ih*2,setsar=sar=1",
            _ => crop
        };
    }
}
