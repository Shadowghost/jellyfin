using System;
using System.Globalization;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters;

/// <summary>
/// Weaves both fields of an interlaced frame into one progressive frame.
/// </summary>
/// <param name="Method">The ffmpeg filter to use.</param>
/// <param name="DoubleRate">Whether one frame is emitted per field.</param>
public sealed record DeinterlaceFilter(string Method, bool DoubleRate) : IVideoFilter
{
    private static readonly string[] _fallbacks = ["yadif", "bwdif", "w3fdif"];

    /// <inheritdoc />
    public FrameSurface? RequiredSurface => FrameSurface.System;

    /// <inheritdoc />
    public bool PinsPixelFormat => false;

    /// <inheritdoc />
    public IVideoFilter? Evaluate(FrameState state, IPipelineCapabilities capabilities)
    {
        if (!state.IsInterlaced)
        {
            return null;
        }

        if (capabilities.SupportsFilter(Method))
        {
            return this;
        }

        foreach (var fallback in _fallbacks)
        {
            if (capabilities.SupportsFilter(fallback))
            {
                return this with { Method = fallback };
            }
        }

        return null;
    }

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state with { IsInterlaced = false };

    /// <inheritdoc />
    public string ToFilterArgument()
    {
        var rate = DoubleRate ? 1 : 0;
        return string.Equals(Method, "w3fdif", StringComparison.Ordinal)
            ? string.Create(CultureInfo.InvariantCulture, $"w3fdif=mode={rate}")
            : string.Create(CultureInfo.InvariantCulture, $"{Method}={rate}:-1:0");
    }
}
