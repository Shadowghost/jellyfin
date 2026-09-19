using System.Globalization;
using System.Text;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters.Devices;

/// <summary>
/// The Vulkan shader filter, which resizes, converts and tone maps in a single pass.
/// </summary>
public sealed record LibplaceboFilter : IVideoFilter
{
    /// <summary>
    /// Gets the size the filter scales to, or <c>null</c> when it does not scale.
    /// </summary>
    public FrameSize? Size { get; init; }

    /// <summary>
    /// Gets the format the filter converts to, or <c>null</c> when it does not convert.
    /// </summary>
    public PixelFormat? Format { get; init; }

    /// <summary>
    /// Gets a value indicating whether the filter tone maps.
    /// </summary>
    public bool ToneMap { get; init; }

    /// <summary>
    /// Gets the tone mapping algorithm, in the spelling libplacebo uses.
    /// </summary>
    public string Algorithm { get; init; } = "clip";

    /// <summary>
    /// Gets the colour range to declare, or <c>null</c> to leave it alone.
    /// </summary>
    public string? Range { get; init; }

    /// <inheritdoc />
    public FrameSurface? RequiredSurface => FrameSurface.Vulkan;

    /// <inheritdoc />
    public bool PinsPixelFormat => Format.HasValue;

    /// <summary>
    /// Merges two passes into one, which is the point of the filter.
    /// </summary>
    /// <param name="first">The earlier filter.</param>
    /// <param name="second">The later filter.</param>
    /// <returns>The merged filter, or <c>null</c> when both claim the same operation.</returns>
    public static LibplaceboFilter? Fuse(LibplaceboFilter first, LibplaceboFilter second)
    {
        if ((first.Size.HasValue && second.Size.HasValue) || (first.ToneMap && second.ToneMap))
        {
            return null;
        }

        return first with
        {
            Size = first.Size ?? second.Size,
            Format = second.Format ?? first.Format,
            ToneMap = first.ToneMap || second.ToneMap,
            Algorithm = second.ToneMap ? second.Algorithm : first.Algorithm,
            Range = second.Range ?? first.Range
        };
    }

    /// <inheritdoc />
    public IVideoFilter Evaluate(FrameState state, IPipelineCapabilities capabilities) => this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state)
    {
        var next = state with { Surface = FrameSurface.Vulkan };

        if (Size.HasValue)
        {
            next = next with { Size = Size.Value, RequiresResize = false };
        }

        if (Format.HasValue)
        {
            next = next with { PixelFormat = Format.Value };
        }

        return ToneMap ? next with { HdrFormat = HdrFormat.None } : next;
    }

    /// <inheritdoc />
    public string ToFilterArgument()
    {
        var argument = new StringBuilder("libplacebo=upscaler=none:downscaler=none");

        if (Size.HasValue)
        {
            argument.Append(CultureInfo.InvariantCulture, $":w={Size.Value.Width}:h={Size.Value.Height}");
        }

        if (Format.HasValue)
        {
            argument.Append(":format=").Append(Format.Value.Name);
        }

        if (ToneMap)
        {
            argument.Append(CultureInfo.InvariantCulture, $":tonemapping={Algorithm}:peak_detect=0")
                .Append(":color_primaries=bt709:color_trc=bt709:colorspace=bt709");
        }

        if (!string.IsNullOrEmpty(Range))
        {
            argument.Append(":range=").Append(Range);
        }

        return argument.ToString();
    }
}
