using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters.Devices;

/// <summary>
/// A hardware scaler, which resizes and converts the pixel format in one pass.
/// </summary>
/// <remarks>
/// Both operations are optional, so a resize and a later format conversion fuse into one pass.
/// </remarks>
/// <param name="Name">The filter name, such as <c>scale_cuda</c>.</param>
/// <param name="Surface">The surface the filter runs on.</param>
public sealed record DeviceScaleFilter(string Name, FrameSurface Surface) : IVideoFilter
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
    /// Gets the options appended after the scaling and format ones, such as a frame pool size.
    /// </summary>
    public string? ExtraOptions { get; init; }

    /// <summary>
    /// Gets a value indicating whether the filter may be merged with the one next to it.
    /// </summary>
    public bool Fusable { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the filter also turns the picture onto its side.
    /// </summary>
    /// <remarks>
    /// The turn happens after the resize within the same pass, so the size the filter is given is
    /// the one it produces before turning, which is the output size the other way round.
    /// </remarks>
    public bool SwapsOutputDimensions { get; init; }

    /// <inheritdoc />
    public FrameSurface? RequiredSurface => Surface;

    /// <inheritdoc />
    public bool PinsPixelFormat => Format.HasValue;

    /// <summary>
    /// Merges two scalers that run on the same device into one invocation.
    /// </summary>
    /// <param name="first">The earlier filter.</param>
    /// <param name="second">The later filter.</param>
    /// <returns>The merged filter, or <c>null</c> when they cannot be merged.</returns>
    public static DeviceScaleFilter? Fuse(DeviceScaleFilter first, DeviceScaleFilter second)
    {
        if (!first.Fusable
            || !second.Fusable
            || !string.Equals(first.Name, second.Name, StringComparison.Ordinal)
            || first.Surface != second.Surface
            || (first.Size.HasValue && second.Size.HasValue))
        {
            return null;
        }

        return first with
        {
            SwapsOutputDimensions = first.SwapsOutputDimensions || second.SwapsOutputDimensions,
            Size = first.Size ?? second.Size,
            Format = second.Format ?? first.Format,
            ExtraOptions = JoinOptions(first.ExtraOptions, second.ExtraOptions)
        };
    }

    private static string? JoinOptions(string? first, string? second)
    {
        if (string.IsNullOrEmpty(first))
        {
            return second;
        }

        if (string.IsNullOrEmpty(second))
        {
            return first;
        }

        // Both sides may carry the same option, such as the frame pool size every pass asks for.
        var options = new List<string>(first.Split(':'));
        foreach (var option in second.Split(':'))
        {
            if (!options.Contains(option, StringComparer.Ordinal))
            {
                options.Add(option);
            }
        }

        return string.Join(':', options);
    }

    /// <inheritdoc />
    public IVideoFilter Evaluate(FrameState state, IPipelineCapabilities capabilities) => this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state)
    {
        var next = state with { Surface = Surface };

        if (Size.HasValue)
        {
            next = next with { Size = Size.Value, RequiresResize = false };
        }
        else if (SwapsOutputDimensions)
        {
            next = next with { Size = new FrameSize(next.Size.Height, next.Size.Width) };
        }

        return Format.HasValue ? next with { PixelFormat = Format.Value } : next;
    }

    /// <inheritdoc />
    public string? ToFilterArgument()
    {
        if (!Size.HasValue && !Format.HasValue && string.IsNullOrEmpty(ExtraOptions))
        {
            return null;
        }

        var argument = new StringBuilder(Name);
        var separator = '=';

        if (Size.HasValue)
        {
            var width = SwapsOutputDimensions ? Size.Value.Height : Size.Value.Width;
            var height = SwapsOutputDimensions ? Size.Value.Width : Size.Value.Height;
            argument.Append(CultureInfo.InvariantCulture, $"=w={width}:h={height}");
            separator = ':';
        }

        if (Format.HasValue)
        {
            argument.Append(separator).Append("format=").Append(Format.Value.Name);
            separator = ':';
        }

        if (!string.IsNullOrEmpty(ExtraOptions))
        {
            argument.Append(separator).Append(ExtraOptions);
        }

        return argument.ToString();
    }
}
