using System;
using System.Collections.Generic;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Output;

/// <summary>
/// Picks the encoder a job ends on, preferring the one on the device and falling back to software
/// when the device has no encoder for the codec.
/// </summary>
public static class VideoEncoderSelector
{
    private static readonly Dictionary<string, string> _softwareEncoders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["h264"] = "libx264",
        ["h265"] = "libx265",
        ["hevc"] = "libx265",
        ["av1"] = "libsvtav1"
    };

    private static readonly Dictionary<string, string> _hardwareCodecNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["h264"] = "h264",
        ["h265"] = "hevc",
        ["hevc"] = "hevc",
        ["av1"] = "av1"
    };

    /// <summary>
    /// Selects the encoder.
    /// </summary>
    /// <param name="outputCodec">The codec the job is asked to produce.</param>
    /// <param name="accelerator">The accelerator the job runs on.</param>
    /// <param name="request">What the job is allowed to use.</param>
    /// <param name="capabilities">What the installed ffmpeg was built with.</param>
    /// <returns>The encoder.</returns>
    public static VideoEncoder Select(
        string? outputCodec,
        IHardwareAccelerator accelerator,
        VideoEncoderRequest request,
        IPipelineCapabilities capabilities)
    {
        if (string.IsNullOrEmpty(outputCodec))
        {
            return Copy();
        }

        if (!_softwareEncoders.TryGetValue(outputCodec, out var softwareEncoder))
        {
            return new VideoEncoder(outputCodec.ToLowerInvariant(), FrameSurface.System, SoftwareFormat(request.BitDepth));
        }

        if (request.HardwareEncoding
            && !request.HardwareDisabled
            && accelerator.EncoderSuffix is { } suffix
            && _hardwareCodecNames.TryGetValue(outputCodec, out var hardwareCodec))
        {
            var name = hardwareCodec + "_" + suffix;
            if (capabilities.SupportsEncoder(name))
            {
                return new VideoEncoder(name, accelerator.Surface, accelerator.GetEncoderFormat(request.BitDepth));
            }
        }

        return new VideoEncoder(softwareEncoder, FrameSurface.System, SoftwareFormat(request.BitDepth));
    }

    /// <summary>
    /// Gets the encoder that copies the stream through untouched.
    /// </summary>
    /// <returns>The encoder.</returns>
    public static VideoEncoder Copy() => new("copy", FrameSurface.System, PixelFormat.NONE);

    private static PixelFormat SoftwareFormat(int bitDepth)
        => bitDepth >= 10 ? PixelFormat.YUV420P10LE : PixelFormat.YUV420P;
}
