using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.MediaEncoding.Probing.HwCaps;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaEncoding.Hardware;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.MediaEncoding.Encoder;

/// <summary>
/// Collects and caches the capabilities of the configured hardware acceleration type.
/// </summary>
public class HardwareCapabilitiesProvider : IHardwareCapabilitiesProvider
{
    private const int CacheVersion = 1;
    private const string ProbeFlags = "dev+dec+enc+vpp+ocl+vk";

    // Every supported CPU decodes the legacy codecs comfortably, so they are not worth pointing out.
    private static readonly string[] _codecsWorthAccelerating = ["h264", "hevc", "vp9", "av1"];

    private readonly ILogger<HardwareCapabilitiesProvider> _logger;
    private readonly IApplicationPaths _appPaths;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonDefaults.Options) { WriteIndented = false };
    private readonly ConcurrentDictionary<string, HwAccelCapabilities> _capabilities = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _knownBadDecodes = new(StringComparer.Ordinal);

    private volatile bool _isPopulated;
    private CancellationTokenSource? _detectionCancellation;

    /// <summary>
    /// Initializes a new instance of the <see cref="HardwareCapabilitiesProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="appPaths">The application paths.</param>
    public HardwareCapabilitiesProvider(ILogger<HardwareCapabilitiesProvider> logger, IApplicationPaths appPaths)
    {
        _logger = logger;
        _appPaths = appPaths;
    }

    /// <inheritdoc />
    public bool IsPopulated => _isPopulated;

    /// <inheritdoc />
    public Task DetectionTask { get; private set; } = Task.CompletedTask;

    /// <inheritdoc />
    public void Refresh(string ffmpegPath, string ffprobePath, string encoderVersion, bool canReportHwCaps, EncodingOptions options)
    {
        _capabilities.Clear();
        _knownBadDecodes.Clear();
        _isPopulated = false;

        if (options.HardwareCapabilityDetection == HardwareCapabilityDetectionMode.Disabled)
        {
            _logger.LogDebug("Hardware capability detection is disabled");
            return;
        }

        var probeTypes = GetProbeTypes(options).ToList();
        if (probeTypes.Count == 0)
        {
            return;
        }

        var deviceKey = GetDeviceKey(options);
        var useReport = canReportHwCaps && options.HardwareCapabilityDetection == HardwareCapabilityDetectionMode.Auto;
        var probed = false;

        foreach (var probeType in probeTypes)
        {
            var caps = LoadFromCache(probeType, encoderVersion, deviceKey)
                ?? (useReport ? Report(ffprobePath, probeType, options, encoderVersion) : null);

            if (caps is null)
            {
                continue;
            }

            probed = true;
            _capabilities[probeType] = caps;
            Save(probeType, encoderVersion, deviceKey, caps);
            LogSummary(probeType, caps);
        }

        _isPopulated = !_capabilities.IsEmpty;
        LogUnusedCapabilities(options);

        if (!probed && !useReport)
        {
            StartFallbackProbe(ffmpegPath, probeTypes[0], encoderVersion, deviceKey, options);
        }
    }

    /// <summary>
    /// Points out what a manually tuned server is leaving on the table, once, at startup.
    /// </summary>
    /// <param name="options">The encoding options.</param>
    private void LogUnusedCapabilities(EncodingOptions options)
    {
        if (!_isPopulated || options.HardwareTuning != HardwareTuningMode.Manual)
        {
            return;
        }

        var device = GetActiveDevice(options.HardwareAccelerationType, options);
        if (device is null || device.Decoders.Count == 0)
        {
            return;
        }

        var unused = device.Decoders
            .Select(d => d.CodecName)
            .Where(c => _codecsWorthAccelerating.Contains(c, StringComparer.OrdinalIgnoreCase)
                && !options.HardwareDecodingCodecs.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (unused.Length > 0)
        {
            _logger.LogInformation(
                "Hardware decoding is set manually; the detected device also reports {Codecs}",
                string.Join(", ", unused));
        }
    }

    private void StartFallbackProbe(string ffmpegPath, string probeType, string encoderVersion, string deviceKey, EncodingOptions options)
    {
        // Running ffmpeg for every preset is far too slow to hold up startup, so it happens in the background.
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _detectionCancellation, cancellation);
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        DetectionTask = Task.Run(
            () =>
            {
                var prober = new HardwareCapabilitiesFallbackProber(_logger, ffmpegPath);
                var caps = prober.Probe(options, encoderVersion, cancellation.Token);
                if (caps is null)
                {
                    return;
                }

                _capabilities[probeType] = caps;
                Save(probeType, encoderVersion, deviceKey, caps);
                _isPopulated = true;
            },
            cancellation.Token).ContinueWith(
            _ =>
            {
                if (Interlocked.CompareExchange(ref _detectionCancellation, null, cancellation) == cancellation)
                {
                    cancellation.Dispose();
                }
            },
            TaskScheduler.Default);
    }

    /// <inheritdoc />
    public HwAccelCapabilities? GetCapabilities(HardwareAccelerationType type)
    {
        foreach (var probeType in GetProbeTypesForQuery(type))
        {
            if (_capabilities.TryGetValue(probeType, out var caps) && caps.Devices.Count > 0)
            {
                return caps;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public HwDeviceCapabilities? GetActiveDevice(HardwareAccelerationType type, EncodingOptions options)
    {
        var caps = GetCapabilities(type);
        if (caps is null || caps.Devices.Count == 0)
        {
            return null;
        }

        var selected = GetDeviceKey(options);
        if (!string.IsNullOrEmpty(selected))
        {
            var match = caps.Devices.FirstOrDefault(d => string.Equals(d.DrmPath, selected, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        return caps.Devices[0];
    }

    /// <inheritdoc />
    public bool CanDecode(HardwareAccelerationType type, EncodingOptions options, string? codec, int width, int height, string? pixelFormat)
    {
        if (IsDecodeKnownBad(type, codec, width, height))
        {
            return false;
        }

        // No report means no opinion, so the caller keeps its static behaviour.
        var device = GetActiveDevice(type, options);
        if (device is null || string.IsNullOrEmpty(codec) || device.Decoders.Count == 0)
        {
            return true;
        }

        var decoder = device.GetDecoder(codec);
        if (decoder is null)
        {
            return false;
        }

        if (!decoder.SupportsSize(width, height))
        {
            return false;
        }

        return MatchesPixelFormat(decoder.PixelFormats, pixelFormat);
    }

    /// <inheritdoc />
    public bool CanEncode(HardwareAccelerationType type, EncodingOptions options, string? codec, int width, int height)
    {
        // Only trust a negative answer when the device actually reported encoders.
        var device = GetActiveDevice(type, options);
        if (device is null || string.IsNullOrEmpty(codec) || device.Encoders.Count == 0)
        {
            return true;
        }

        var encoder = device.GetEncoder(codec);
        return encoder is not null && encoder.SupportsSize(width, height);
    }

    /// <inheritdoc />
    public bool CanFilter(HardwareAccelerationType type, EncodingOptions options, HwVppKind kind, int width, int height)
    {
        var device = GetActiveDevice(type, options);

        // cuda, amf and videotoolbox have no fixed function video processing to report, so this cannot be decided here.
        if (device is null || device.Filters.Count == 0)
        {
            return true;
        }

        var filter = device.GetFilter(kind);
        return filter is not null && filter.SupportsSize(width, height);
    }

    /// <inheritdoc />
    public void RecordDecodeFailure(HardwareAccelerationType type, string? codec, int width, int height)
    {
        var key = GetFailureKey(type, codec, width, height);
        if (_knownBadDecodes.TryAdd(key, 0))
        {
            _logger.LogWarning("Disabling hardware decoding for {Key} after a transcoding failure", key);
        }
    }

    /// <inheritdoc />
    public bool IsDecodeKnownBad(HardwareAccelerationType type, string? codec, int width, int height)
        => _knownBadDecodes.ContainsKey(GetFailureKey(type, codec, width, height));

    private static bool MatchesPixelFormat(IReadOnlyList<string> supported, string? pixelFormat)
    {
        if (supported.Count == 0 || string.IsNullOrEmpty(pixelFormat))
        {
            return true;
        }

        return supported.Contains(pixelFormat, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetFailureKey(HardwareAccelerationType type, string? codec, int width, int height)
    {
        // Bucket by resolution tier so one 4K failure does not disable 1080p.
        var tier = height > 2160 || width > 3840 ? "uhd+" : (height > 1080 || width > 1920 ? "uhd" : "hd");
        return string.Create(CultureInfo.InvariantCulture, $"{type}-{codec ?? "unknown"}-{tier}");
    }

    private static IEnumerable<string> GetProbeTypesForQuery(HardwareAccelerationType type)
    {
        switch (type)
        {
            case HardwareAccelerationType.qsv:
                yield return "qsv";
                yield return "vaapi";
                yield return "d3d11va";
                break;
            case HardwareAccelerationType.amf:
                yield return "amf";
                yield return "d3d11va";
                break;
            case HardwareAccelerationType.nvenc:
                yield return "cuda";
                break;
            case HardwareAccelerationType.vaapi:
                yield return "vaapi";
                break;
            case HardwareAccelerationType.videotoolbox:
                yield return "videotoolbox";
                break;
            case HardwareAccelerationType.rkmpp:
                yield return "rkmpp";
                break;
        }
    }

    private static IEnumerable<string> GetProbeTypes(EncodingOptions options)
    {
        foreach (var probeType in GetProbeTypesForQuery(options.HardwareAccelerationType))
        {
            // The system native decoder path hands vaapi or d3d11va surfaces to the qsv and amf pipelines.
            if (string.Equals(probeType, "vaapi", StringComparison.Ordinal)
                && options.HardwareAccelerationType == HardwareAccelerationType.qsv
                && !(OperatingSystem.IsLinux() && options.PreferSystemNativeHwDecoder))
            {
                continue;
            }

            if (string.Equals(probeType, "d3d11va", StringComparison.Ordinal) && !OperatingSystem.IsWindows())
            {
                continue;
            }

            yield return probeType;
        }
    }

    private static string GetDeviceKey(EncodingOptions options) => options.HardwareAccelerationType switch
    {
        HardwareAccelerationType.qsv => string.IsNullOrEmpty(options.QsvDevice) ? options.VaapiDevice ?? string.Empty : options.QsvDevice,
        HardwareAccelerationType.vaapi => options.VaapiDevice ?? string.Empty,
        _ => string.Empty
    };

    private HwAccelCapabilities? Report(string ffprobePath, string probeType, EncodingOptions options, string encoderVersion)
    {
        var device = GetDeviceKey(options);
        var arguments = new StringBuilder("-v quiet -hide_banner -print_format json -show_hwaccel ")
            .Append(probeType)
            .Append(" -hwaccel_flags ")
            .Append(ProbeFlags);

        if (!string.IsNullOrEmpty(device) && !string.Equals(probeType, "cuda", StringComparison.Ordinal))
        {
            arguments.Append(" -hwaccel_device ").Append(device);
        }

        string output;
        try
        {
            output = RunProber(ffprobePath, arguments.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting {ProbeType} hardware capabilities", probeType);
            return null;
        }

        var caps = HwCapsResultNormalizer.Parse(output, options.HardwareAccelerationType, encoderVersion);
        if (caps is null)
        {
            _logger.LogWarning("Could not read the {ProbeType} hardware capability report", probeType);
        }

        return caps;
    }

    private string RunProber(string ffprobePath, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(ffprobePath, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                ErrorDialog = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        _logger.LogDebug("Running {Path} {Arguments}", ffprobePath, arguments);
        process.Start();

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill(true);
            throw new TimeoutException("ffprobe did not report hardware capabilities in time");
        }

        Task.WaitAll(standardOutput, standardError);

        return process.ExitCode == 0 ? standardOutput.GetAwaiter().GetResult() : string.Empty;
    }

    private void LogSummary(string probeType, HwAccelCapabilities caps)
    {
        if (caps.Devices.Count == 0)
        {
            _logger.LogInformation("No {ProbeType} device reported any capabilities", probeType);
            return;
        }

        foreach (var device in caps.Devices)
        {
            _logger.LogInformation(
                "{ProbeType} device {Device}: {DecoderCount} decoders, {EncoderCount} encoders, {FilterCount} filters",
                probeType,
                device.Name ?? device.DrmPath ?? device.Index?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
                device.Decoders.Count,
                device.Encoders.Count,
                device.Filters.Count);
        }
    }

    private string GetCachePath(string probeType)
        => Path.Join(_appPaths.CachePath, "hwcaps", probeType + ".json");

    private HwAccelCapabilities? LoadFromCache(string probeType, string encoderVersion, string deviceKey)
    {
        var path = GetCachePath(probeType);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var entry = JsonSerializer.Deserialize<HwCapsCacheEntry>(File.ReadAllText(path), _jsonOptions);
            if (entry is null
                || entry.CacheVersion != CacheVersion
                || !string.Equals(entry.EncoderVersion, encoderVersion, StringComparison.Ordinal)
                || !string.Equals(entry.DeviceKey, deviceKey, StringComparison.Ordinal))
            {
                return null;
            }

            _logger.LogDebug("Using cached {ProbeType} hardware capabilities", probeType);

            return entry.Capabilities;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the cached {ProbeType} hardware capabilities", probeType);
            return null;
        }
    }

    private void Save(string probeType, string encoderVersion, string deviceKey, HwAccelCapabilities caps)
    {
        var path = GetCachePath(probeType);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var entry = new HwCapsCacheEntry
            {
                CacheVersion = CacheVersion,
                EncoderVersion = encoderVersion,
                DeviceKey = deviceKey,
                Capabilities = caps
            };

            File.WriteAllText(path, JsonSerializer.Serialize(entry, _jsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not cache the {ProbeType} hardware capabilities", probeType);
        }
    }

    private sealed class HwCapsCacheEntry
    {
        public int CacheVersion { get; set; }

        public string? EncoderVersion { get; set; }

        public string? DeviceKey { get; set; }

        public HwAccelCapabilities? Capabilities { get; set; }
    }
}
