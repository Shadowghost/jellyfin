using System.Collections.Generic;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Input;

/// <summary>
/// How the source is decoded, and what that leaves the first filter looking at.
/// </summary>
/// <param name="HwAccel">The hardware acceleration to decode with, or <c>null</c> to decode in software.</param>
/// <param name="OutputSurface">The surface the decoder leaves frames on.</param>
/// <param name="OutputFormatName">The frame type the decoder is told to produce.</param>
public sealed record VideoDecoder(string? HwAccel, FrameSurface OutputSurface, string? OutputFormatName)
{
    /// <summary>
    /// Gets the decoder to use explicitly, instead of letting the hardware acceleration pick.
    /// </summary>
    public string? Codec { get; init; }

    /// <summary>
    /// Gets a value indicating whether the rotation is dropped from the frames the decoder produces.
    /// </summary>
    /// <remarks>
    /// The filter chain turns the picture itself, so the rotation must not travel with the frames or
    /// the player would turn it a second time.
    /// </remarks>
    public bool StripDisplayRotation { get; init; }

    /// <summary>
    /// Gets the options the decoder needs on top of the acceleration ones.
    /// </summary>
    public IReadOnlyList<string> ExtraOptions { get; init; } = [];

    /// <summary>
    /// Renders the input options the decoder needs.
    /// </summary>
    /// <returns>The arguments, in the order ffmpeg takes them.</returns>
    public IEnumerable<string> ToArguments()
    {
        if (!string.IsNullOrEmpty(HwAccel))
        {
            yield return "-hwaccel";
            yield return HwAccel;
        }

        if (!string.IsNullOrEmpty(OutputFormatName))
        {
            yield return "-hwaccel_output_format";
            yield return OutputFormatName;
        }

        if (!string.IsNullOrEmpty(Codec))
        {
            yield return "-c:v";
            yield return Codec;
        }

        yield return "-noautorotate";

        if (StripDisplayRotation)
        {
            yield return "-display_rotation";
            yield return "0";
        }

        foreach (var option in ExtraOptions)
        {
            yield return option;
        }
    }
}
