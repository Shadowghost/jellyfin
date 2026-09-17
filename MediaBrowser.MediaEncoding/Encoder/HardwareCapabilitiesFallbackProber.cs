using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaEncoding.Hardware;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.MediaEncoding.Encoder;

/// <summary>
/// Probes hardware capabilities by running ffmpeg, for builds whose ffprobe cannot report them.
/// </summary>
/// <remarks>
/// This is a last resort: it only covers the presets Jellyfin actually offers and deliberately
/// reports nothing at all for legacy codecs, which every supported CPU can handle in software.
/// </remarks>
public class HardwareCapabilitiesFallbackProber
{
    private const int ProcessTimeoutMs = 5000;
    private const int ProbeBudgetMs = 60000;

    // Tonemapping filters reject anything that is not tagged as 10 bit HDR.
    private const string HdrSource = "format=p010le,setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc";
    private const string SdrSource = "format=nv12";

    private static readonly (int Width, int Height, int BitDepth)[] _presets =
    [
        (1920, 1080, 8),
        (1920, 1080, 10),
        (3840, 2160, 8),
        (3840, 2160, 10)
    ];

    private static readonly Dictionary<string, string> _softwareEncoders = new(StringComparer.OrdinalIgnoreCase)
    {
        { "h264", "libx264" },
        { "hevc", "libx265" },
        { "vp9", "libvpx-vp9" },
        { "av1", "libsvtav1" }
    };

    private readonly ILogger _logger;
    private readonly string _ffmpegPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="HardwareCapabilitiesFallbackProber"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="ffmpegPath">The ffmpeg path.</param>
    public HardwareCapabilitiesFallbackProber(ILogger logger, string ffmpegPath)
    {
        _logger = logger;
        _ffmpegPath = ffmpegPath;
    }

    /// <summary>
    /// Probes the capabilities of the configured hardware acceleration type.
    /// </summary>
    /// <param name="options">The encoding options.</param>
    /// <param name="encoderVersion">The ffmpeg version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The probed capabilities, or <c>null</c> when nothing could be probed.</returns>
    public HwAccelCapabilities? Probe(EncodingOptions options, string encoderVersion, CancellationToken cancellationToken)
    {
        var type = options.HardwareAccelerationType;
        var deviceArgs = GetDeviceArgs(type, options);
        if (deviceArgs is null)
        {
            return null;
        }

        var deadline = Stopwatch.StartNew();
        var workDir = Directory.CreateTempSubdirectory("jellyfin-hwprobe");
        try
        {
            var decoders = ProbeDecoders(type, deviceArgs, workDir.FullName, deadline, cancellationToken);
            var encoders = ProbeEncoders(type, deadline, cancellationToken);
            var filters = ProbeFilters(type, deviceArgs, deadline, cancellationToken);

            if (decoders.Count == 0 && encoders.Count == 0 && filters.Count == 0)
            {
                return null;
            }

            _logger.LogInformation(
                "Probed {Type} hardware capabilities in {Elapsed} ms: {DecoderCount} decoders, {EncoderCount} encoders, {FilterCount} filters",
                type,
                deadline.ElapsedMilliseconds,
                decoders.Count,
                encoders.Count,
                filters.Count);

            return new HwAccelCapabilities
            {
                AccelerationType = type,
                EncoderVersion = encoderVersion,
                IsReported = false,
                Devices = new[]
                {
                    new HwDeviceCapabilities
                    {
                        Decoders = decoders,
                        Encoders = encoders,
                        Filters = filters
                    }
                }
            };
        }
        finally
        {
            try
            {
                workDir.Delete(true);
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Could not clean up the hardware probe directory");
            }
        }
    }

    private List<HwCodecCapabilities> ProbeDecoders(
        HardwareAccelerationType type,
        string deviceArgs,
        string workDir,
        Stopwatch deadline,
        CancellationToken cancellationToken)
    {
        var results = new List<HwCodecCapabilities>();
        var hwaccel = GetHwaccelName(type);
        var outputFormat = GetHwaccelOutputFormat(type);
        if (hwaccel is null)
        {
            return results;
        }

        foreach (var (codec, softwareEncoder) in _softwareEncoders)
        {
            var sizes = new List<(int Width, int Height)>();
            var formats = new List<string>();

            foreach (var preset in _presets)
            {
                if (IsOutOfBudget(deadline, cancellationToken))
                {
                    break;
                }

                var pixelFormat = preset.BitDepth == 10 ? "yuv420p10le" : "yuv420p";
                var clip = Path.Join(workDir, string.Create(CultureInfo.InvariantCulture, $"{codec}-{preset.Width}-{preset.BitDepth}.mkv"));

                // No software encoder means no way to build a sample, so hardware decoding stays off.
                var encodeArgs = string.Create(
                    CultureInfo.InvariantCulture,
                    $"-loglevel error -f lavfi -i testsrc=s={preset.Width}x{preset.Height}:d=1:r=10 -pix_fmt {pixelFormat} -c:v {softwareEncoder} -frames:v 10 -y \"{clip}\"");
                if (!Run(encodeArgs))
                {
                    continue;
                }

                var decodeArgs = string.Create(
                    CultureInfo.InvariantCulture,
                    $"-loglevel error {deviceArgs} -hwaccel {hwaccel} -hwaccel_output_format {outputFormat} -i \"{clip}\" -f null -");
                if (Run(decodeArgs))
                {
                    sizes.Add((preset.Width, preset.Height));
                    formats.Add(preset.BitDepth == 10 ? "p010le" : "nv12");
                }
            }

            if (sizes.Count > 0)
            {
                results.Add(BuildCodec(codec, sizes, formats));
            }
        }

        return results;
    }

    private List<HwCodecCapabilities> ProbeEncoders(HardwareAccelerationType type, Stopwatch deadline, CancellationToken cancellationToken)
    {
        var results = new List<HwCodecCapabilities>();
        var suffix = GetEncoderSuffix(type);
        if (suffix is null)
        {
            return results;
        }

        foreach (var codec in _softwareEncoders.Keys)
        {
            var sizes = new List<(int Width, int Height)>();
            var formats = new List<string>();

            foreach (var preset in _presets)
            {
                if (IsOutOfBudget(deadline, cancellationToken))
                {
                    break;
                }

                var pixelFormat = preset.BitDepth == 10 ? "p010le" : "yuv420p";
                var args = string.Create(
                    CultureInfo.InvariantCulture,
                    $"-loglevel error -f lavfi -i nullsrc=s={preset.Width}x{preset.Height}:d=1 -pix_fmt {pixelFormat} -c:v {codec}_{suffix} -f null -");
                if (Run(args))
                {
                    sizes.Add((preset.Width, preset.Height));
                    formats.Add(pixelFormat);
                }
            }

            if (sizes.Count > 0)
            {
                results.Add(BuildCodec(codec, sizes, formats));
            }
        }

        return results;
    }

    private List<HwFilterCapabilities> ProbeFilters(
        HardwareAccelerationType type,
        string deviceArgs,
        Stopwatch deadline,
        CancellationToken cancellationToken)
    {
        var results = new List<HwFilterCapabilities>();

        foreach (var (kind, filter) in GetProbeFilters(type))
        {
            if (IsOutOfBudget(deadline, cancellationToken))
            {
                break;
            }

            var source = kind == HwVppKind.Tonemap ? HdrSource : SdrSource;
            var args = kind == HwVppKind.Overlay
                ? $"-loglevel error {deviceArgs} -f lavfi -i nullsrc=s=1280x720:d=1 -f lavfi -i nullsrc=s=320x240:d=1 "
                    + $"-filter_complex \"[0:v]{source},hwupload[b];[1:v]{source},hwupload[o];[b][o]{filter}\" -f null -"
                : $"-loglevel error {deviceArgs} -f lavfi -i nullsrc=s=1280x720:d=1 -vf \"{source},hwupload,{filter}\" -f null -";

            if (Run(args))
            {
                results.Add(new HwFilterCapabilities { Kind = kind, FilterDescription = filter });
            }
        }

        return results;
    }

    private static HwCodecCapabilities BuildCodec(string codec, List<(int Width, int Height)> sizes, List<string> formats)
        => new()
        {
            CodecName = codec,
            MaxWidth = sizes.Max(s => s.Width),
            MaxHeight = sizes.Max(s => s.Height),
            PixelFormats = formats.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

    private static IEnumerable<(HwVppKind Kind, string Filter)> GetProbeFilters(HardwareAccelerationType type)
    {
        switch (type)
        {
            case HardwareAccelerationType.vaapi:
                yield return (HwVppKind.Scale, "scale_vaapi=w=640:h=360");
                yield return (HwVppKind.Deinterlace, "deinterlace_vaapi");
                yield return (HwVppKind.Procamp, "procamp_vaapi");
                yield return (HwVppKind.Tonemap, "tonemap_vaapi=format=nv12");
                yield return (HwVppKind.Rotate, "transpose_vaapi=dir=1");
                yield return (HwVppKind.Overlay, "overlay_vaapi");
                break;
            case HardwareAccelerationType.qsv:
                yield return (HwVppKind.Scale, "vpp_qsv=w=640:h=360");
                yield return (HwVppKind.Deinterlace, "deinterlace_qsv");
                yield return (HwVppKind.Overlay, "overlay_qsv");
                break;
            case HardwareAccelerationType.nvenc:
                yield return (HwVppKind.Scale, "scale_cuda=640:360");
                yield return (HwVppKind.Deinterlace, "yadif_cuda");
                yield return (HwVppKind.Tonemap, "tonemap_cuda=format=nv12");
                yield return (HwVppKind.Overlay, "overlay_cuda");
                break;
            case HardwareAccelerationType.videotoolbox:
                yield return (HwVppKind.Scale, "scale_vt=640:360");
                yield return (HwVppKind.Deinterlace, "yadif_videotoolbox");
                yield return (HwVppKind.Tonemap, "tonemap_videotoolbox");
                yield return (HwVppKind.Overlay, "overlay_videotoolbox");
                break;
            case HardwareAccelerationType.rkmpp:
                yield return (HwVppKind.Scale, "scale_rkrga=w=640:h=360");
                yield return (HwVppKind.Overlay, "overlay_rkrga");
                break;
        }
    }

    private static string? GetDeviceArgs(HardwareAccelerationType type, EncodingOptions options) => type switch
    {
        HardwareAccelerationType.vaapi => "-init_hw_device vaapi=hw:" + options.VaapiDevice + " -filter_hw_device hw",
        HardwareAccelerationType.qsv => "-init_hw_device qsv=hw -filter_hw_device hw",
        HardwareAccelerationType.nvenc => "-init_hw_device cuda=hw -filter_hw_device hw",
        HardwareAccelerationType.amf => "-init_hw_device d3d11va=hw -filter_hw_device hw",
        HardwareAccelerationType.videotoolbox => "-init_hw_device videotoolbox=hw -filter_hw_device hw",
        HardwareAccelerationType.rkmpp => "-init_hw_device rkmpp=hw -filter_hw_device hw",
        _ => null
    };

    private static string? GetHwaccelName(HardwareAccelerationType type) => type switch
    {
        HardwareAccelerationType.vaapi => "vaapi",
        HardwareAccelerationType.qsv => "qsv",
        HardwareAccelerationType.nvenc => "cuda",
        HardwareAccelerationType.amf => "d3d11va",
        HardwareAccelerationType.videotoolbox => "videotoolbox",
        HardwareAccelerationType.rkmpp => "rkmpp",
        _ => null
    };

    private static string GetHwaccelOutputFormat(HardwareAccelerationType type) => type switch
    {
        HardwareAccelerationType.nvenc => "cuda",
        HardwareAccelerationType.amf => "d3d11",
        HardwareAccelerationType.videotoolbox => "videotoolbox_vld",
        HardwareAccelerationType.rkmpp => "drm_prime",
        _ => type.ToString()
    };

    private static string? GetEncoderSuffix(HardwareAccelerationType type) => type switch
    {
        HardwareAccelerationType.vaapi => "vaapi",
        HardwareAccelerationType.qsv => "qsv",
        HardwareAccelerationType.nvenc => "nvenc",
        HardwareAccelerationType.amf => "amf",
        HardwareAccelerationType.videotoolbox => "videotoolbox",
        HardwareAccelerationType.rkmpp => "rkmpp",
        _ => null
    };

    private bool IsOutOfBudget(Stopwatch deadline, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        if (deadline.ElapsedMilliseconds > ProbeBudgetMs)
        {
            _logger.LogWarning("Hardware capability probing exceeded its budget and was cut short");
            return true;
        }

        return false;
    }

    private bool Run(string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(_ffmpegPath, arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    ErrorDialog = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            _logger.LogTrace("Probing with {Path} {Arguments}", _ffmpegPath, arguments);
            process.Start();
            process.StandardOutput.ReadToEndAsync();
            process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(ProcessTimeoutMs))
            {
                process.Kill(true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Hardware capability probe failed to run");
            return false;
        }
    }
}
