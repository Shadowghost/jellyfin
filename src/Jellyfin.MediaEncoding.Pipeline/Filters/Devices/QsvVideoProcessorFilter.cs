using System.Collections.Generic;
using System.Globalization;
using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters.Devices;

/// <summary>
/// The Intel video processing filter, which performs several operations in a single invocation.
/// </summary>
/// <remarks>
/// Every operation is optional, so adjacent instances fuse into one pass over the frame.
/// </remarks>
public sealed record QsvVideoProcessorFilter : IVideoFilter
{
    /// <summary>
    /// Gets the deinterlacing mode, or <c>null</c> when the filter does not deinterlace.
    /// </summary>
    public int? Deinterlace { get; init; }

    /// <summary>
    /// Gets a value indicating whether the filter tone maps.
    /// </summary>
    public bool TonesMap { get; init; }

    /// <summary>
    /// Gets the size the filter scales to, or <c>null</c> when it does not scale.
    /// </summary>
    public FrameSize? Size { get; init; }

    /// <summary>
    /// Gets the format the filter converts to, or <c>null</c> when it does not convert.
    /// </summary>
    public PixelFormat? Format { get; init; }

    /// <summary>
    /// Gets the direction the filter turns the picture, or <c>null</c> when it does not turn it.
    /// </summary>
    public string? Transpose { get; init; }

    /// <summary>
    /// Gets the options appended after the rest, such as the range the encoder wants.
    /// </summary>
    public string? ExtraOptions { get; init; }

    /// <inheritdoc />
    public FrameSurface? RequiredSurface => FrameSurface.Qsv;

    /// <inheritdoc />
    public bool PinsPixelFormat => Format.HasValue;

    /// <summary>
    /// Creates a filter that scales.
    /// </summary>
    /// <param name="size">The target size.</param>
    /// <returns>The filter.</returns>
    public static QsvVideoProcessorFilter Scale(FrameSize size) => new() { Size = size };

    /// <summary>
    /// Creates a filter that converts the pixel format.
    /// </summary>
    /// <param name="format">The target format.</param>
    /// <returns>The filter.</returns>
    public static QsvVideoProcessorFilter ToFormat(PixelFormat format) => new() { Format = format };

    /// <summary>
    /// Creates a filter that turns the picture.
    /// </summary>
    /// <param name="direction">The direction to turn it.</param>
    /// <returns>The filter.</returns>
    public static QsvVideoProcessorFilter ToTranspose(string direction) => new() { Transpose = direction };

    /// <summary>
    /// Creates a filter that tone maps.
    /// </summary>
    /// <param name="outputFormat">The format the frame is left in.</param>
    /// <returns>The filter.</returns>
    public static QsvVideoProcessorFilter ToneMap(PixelFormat outputFormat) => new() { TonesMap = true, Format = outputFormat };

    /// <summary>
    /// Merges this filter with the one that follows it.
    /// </summary>
    /// <param name="next">The following filter.</param>
    /// <returns>The merged filter, or <c>null</c> when both claim the same operation.</returns>
    public QsvVideoProcessorFilter? Fuse(QsvVideoProcessorFilter next)
    {
        if ((Deinterlace.HasValue && next.Deinterlace.HasValue)
            || (Size.HasValue && next.Size.HasValue)
            || (Transpose is not null && next.Transpose is not null)
            || (TonesMap && next.TonesMap))
        {
            return null;
        }

        return new QsvVideoProcessorFilter
        {
            Deinterlace = Deinterlace ?? next.Deinterlace,
            TonesMap = TonesMap || next.TonesMap,
            Size = Size ?? next.Size,
            Format = next.Format ?? Format,
            Transpose = Transpose ?? next.Transpose,
            ExtraOptions = ExtraOptions ?? next.ExtraOptions
        };
    }

    /// <inheritdoc />
    public IVideoFilter Evaluate(FrameState state, IPipelineCapabilities capabilities) => this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state)
    {
        var next = state with { Surface = FrameSurface.Qsv };

        if (Deinterlace.HasValue)
        {
            next = next with { IsInterlaced = false };
        }

        if (TonesMap)
        {
            next = next with { HdrFormat = HdrFormat.None };
        }

        if (Size.HasValue)
        {
            next = next with { Size = Size.Value, RequiresResize = false };
        }

        if (Format.HasValue)
        {
            next = next with { PixelFormat = Format.Value };
        }

        if (!Size.HasValue && Transpose is "cclock" or "clock")
        {
            next = next with { Size = new FrameSize(next.Size.Height, next.Size.Width) };
        }

        return next;
    }

    /// <inheritdoc />
    public string? ToFilterArgument()
    {
        var options = new List<string>();

        if (Deinterlace.HasValue)
        {
            options.Add(string.Create(CultureInfo.InvariantCulture, $"deinterlace={Deinterlace.Value}"));
        }

        if (TonesMap)
        {
            options.Add("tonemap=1");
        }

        if (Size.HasValue)
        {
            // The turn happens after the resize, so this is the size produced before turning.
            var swap = Transpose is "cclock" or "clock";
            var width = swap ? Size.Value.Height : Size.Value.Width;
            var height = swap ? Size.Value.Width : Size.Value.Height;
            options.Add(string.Create(CultureInfo.InvariantCulture, $"w={width}:h={height}"));
        }

        if (Format.HasValue)
        {
            options.Add($"format={Format.Value.Name}");
        }

        if (Transpose is not null)
        {
            options.Add($"transpose={Transpose}");
        }

        if (!string.IsNullOrEmpty(ExtraOptions))
        {
            options.Add(ExtraOptions);
        }

        if (options.Count == 0)
        {
            return null;
        }

        return "vpp_qsv=" + string.Join(':', options);
    }
}
