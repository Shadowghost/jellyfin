using System;
using System.Collections.Generic;

namespace Jellyfin.MediaEncoding.Pipeline.Frames;

/// <summary>
/// A pixel format, with the properties the filter chain has to reason about.
/// </summary>
/// <param name="Name">The name ffmpeg knows the format by.</param>
/// <param name="BitDepth">The bits per colour component.</param>
public readonly record struct PixelFormat(string Name, int BitDepth)
{
    /// <summary>
    /// A format that could not be determined, named after <c>AV_PIX_FMT_NONE</c>.
    /// </summary>
    public static readonly PixelFormat NONE = new(string.Empty, 0);

    /// <summary>
    /// Planar 8 bit YUV 4:2:0.
    /// </summary>
    public static readonly PixelFormat YUV420P = new("yuv420p", 8);

    /// <summary>
    /// Planar 10 bit YUV 4:2:0.
    /// </summary>
    public static readonly PixelFormat YUV420P10LE = new("yuv420p10le", 10);

    /// <summary>
    /// Planar 8 bit YUV 4:4:4.
    /// </summary>
    public static readonly PixelFormat YUV444P = new("yuv444p", 8);

    /// <summary>
    /// Planar 8 bit YUV 4:2:0 with an alpha channel.
    /// </summary>
    public static readonly PixelFormat YUVA420P = new("yuva420p", 8);

    /// <summary>
    /// Semi planar 8 bit YUV 4:2:0.
    /// </summary>
    public static readonly PixelFormat NV12 = new("nv12", 8);

    /// <summary>
    /// Semi planar 10 bit YUV 4:2:0.
    /// </summary>
    public static readonly PixelFormat P010LE = new("p010le", 10);

    /// <summary>
    /// Packed 8 bit BGRA.
    /// </summary>
    public static readonly PixelFormat BGRA = new("bgra", 8);

    private static readonly Dictionary<string, PixelFormat> _known = new(StringComparer.OrdinalIgnoreCase)
    {
        [YUV420P.Name] = YUV420P,
        [YUV420P10LE.Name] = YUV420P10LE,
        [YUV444P.Name] = YUV444P,
        [YUVA420P.Name] = YUVA420P,
        [NV12.Name] = NV12,
        [P010LE.Name] = P010LE,
        [BGRA.Name] = BGRA,
        ["yuvj420p"] = YUV420P,
        ["yuv420p10"] = YUV420P10LE,

        // p010 is ffmpeg's alias for p010le, not a format of its own.
        ["p010"] = P010LE
    };

    /// <summary>
    /// Gets a value indicating whether the format was determined.
    /// </summary>
    public bool IsKnown => BitDepth > 0;

    /// <summary>
    /// Resolves a pixel format reported by ffprobe.
    /// </summary>
    /// <param name="name">The format name.</param>
    /// <returns>The format, or <see cref="NONE"/> when it is not one the chain knows.</returns>
    public static PixelFormat Parse(string? name)
        => !string.IsNullOrEmpty(name) && _known.TryGetValue(name, out var format) ? format : NONE;
}
