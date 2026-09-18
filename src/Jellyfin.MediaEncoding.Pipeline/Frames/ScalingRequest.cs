using System;
using System.Globalization;

namespace Jellyfin.MediaEncoding.Pipeline.Frames;

/// <summary>
/// The size a client asked for, which may fix one or both dimensions or only cap them.
/// </summary>
/// <param name="Width">The requested width.</param>
/// <param name="Height">The requested height.</param>
/// <param name="MaxWidth">The requested maximum width.</param>
/// <param name="MaxHeight">The requested maximum height.</param>
public readonly record struct ScalingRequest(int? Width, int? Height, int? MaxWidth, int? MaxHeight)
{
    /// <summary>
    /// The largest frame a transcode will produce, whatever was asked for.
    /// </summary>
    private const int UpperBound = 4096;

    /// <summary>
    /// Gets a value indicating whether any size was asked for at all.
    /// </summary>
    public bool IsRequested => Width.HasValue || Height.HasValue || MaxWidth.HasValue || MaxHeight.HasValue;

    /// <summary>
    /// Works out the frame size the request comes to for a given source, which is what a hardware
    /// scaler needs since it takes numbers rather than expressions.
    /// </summary>
    /// <param name="input">The size of the source frame.</param>
    /// <returns>The output size, with both dimensions rounded down to an even number.</returns>
    public FrameSize Resolve(FrameSize input)
    {
        var outputWidth = Width ?? input.Width;
        var outputHeight = Height ?? input.Height;

        var maximumWidth = Math.Min(MaxWidth ?? outputWidth, UpperBound);
        var maximumHeight = Math.Min(MaxHeight ?? outputHeight, UpperBound);

        if (outputWidth > maximumWidth || outputHeight > maximumHeight)
        {
            var scale = Math.Min((double)maximumWidth / outputWidth, (double)maximumHeight / outputHeight);
            outputWidth = Math.Min(maximumWidth, Convert.ToInt32(outputWidth * scale));
            outputHeight = Math.Min(maximumHeight, Convert.ToInt32(outputHeight * scale));
        }

        return new FrameSize(outputWidth, outputHeight).RoundToEven();
    }

    /// <summary>
    /// Renders the request as a software scale filter, which works the size out from the frame it
    /// is handed rather than from the probed one.
    /// </summary>
    /// <param name="aspectRatio">The expression the filter reads the display aspect ratio from.</param>
    /// <param name="alignment">The multiple the width has to be a multiple of.</param>
    /// <returns>The filter, or <c>null</c> when no size was asked for.</returns>
    public string? ToSoftwareFilterArgument(string aspectRatio = "a", int alignment = 2)
    {
        var culture = CultureInfo.InvariantCulture;

        if (Width.HasValue && Height.HasValue)
        {
            if (alignment != 2)
            {
                return string.Format(culture, "scale=trunc({0}/{2})*{2}:trunc({1}/2)*2", Width.Value, Height.Value, alignment);
            }

            return Height.Value > 0
                ? string.Format(culture, "scale=trunc({0}/2)*2:trunc({1}/2)*2", Width.Value, Height.Value)
                : string.Format(culture, "scale={0}:trunc({0}/a/2)*2", Width.Value);
        }

        if (MaxWidth.HasValue && MaxHeight.HasValue)
        {
            return string.Format(
                culture,
                @"scale=trunc(min(max(iw\,ih*{3})\,min({0}\,{1}*{3}))/{2})*{2}:trunc(min(max(iw/{3}\,ih)\,min({0}/{3}\,{1}))/2)*2",
                MaxWidth.Value,
                MaxHeight.Value,
                alignment,
                aspectRatio);
        }

        if (Width.HasValue)
        {
            return string.Format(culture, "scale={0}:trunc(ow/{1}/2)*2", Width.Value, aspectRatio);
        }

        if (Height.HasValue)
        {
            return string.Format(culture, "scale=trunc(oh*{2}/{1})*{1}:{0}", Height.Value, alignment, aspectRatio);
        }

        if (MaxWidth.HasValue)
        {
            return string.Format(
                culture,
                @"scale=trunc(min(max(iw\,ih*{2})\,{0})/{1})*{1}:trunc(ow/{2}/2)*2",
                MaxWidth.Value,
                alignment,
                aspectRatio);
        }

        if (MaxHeight.HasValue)
        {
            return string.Format(
                culture,
                @"scale=trunc(oh*{2}/{1})*{1}:min(max(iw/{2}\,ih)\,{0})",
                MaxHeight.Value,
                alignment,
                aspectRatio);
        }

        return null;
    }
}
