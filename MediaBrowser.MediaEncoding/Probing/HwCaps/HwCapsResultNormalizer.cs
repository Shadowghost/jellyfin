using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaEncoding.Hardware;

namespace MediaBrowser.MediaEncoding.Probing.HwCaps;

/// <summary>
/// Normalizes the JSON emitted by <c>ffprobe -show_hwaccel</c> into <see cref="HwAccelCapabilities"/>.
/// </summary>
public static class HwCapsResultNormalizer
{
    private const string DevicesProperty = "hwaccel_devices";

    /// <summary>
    /// Parses an <c>ffprobe -show_hwaccel ... -print_format json</c> report.
    /// </summary>
    /// <param name="json">The ffprobe output.</param>
    /// <param name="accelerationType">The hardware acceleration type that was probed.</param>
    /// <param name="encoderVersion">The ffmpeg version the report was produced with.</param>
    /// <returns>The parsed capabilities, or <c>null</c> if the report could not be read.</returns>
    public static HwAccelCapabilities? Parse(string? json, HardwareAccelerationType accelerationType, string? encoderVersion)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty(DevicesProperty, out var devices)
                || devices.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var parsed = new List<HwDeviceCapabilities>();
            foreach (var device in devices.EnumerateArray())
            {
                if (device.ValueKind == JsonValueKind.Object)
                {
                    parsed.Add(ParseDevice(device));
                }
            }

            return new HwAccelCapabilities
            {
                AccelerationType = accelerationType,
                EncoderVersion = encoderVersion,
                IsReported = true,
                Devices = parsed
            };
        }
    }

    private static HwDeviceCapabilities ParseDevice(JsonElement device)
    {
        var decoders = new List<HwCodecCapabilities>();
        var encoders = new List<HwCodecCapabilities>();
        var filters = new List<HwFilterCapabilities>();
        var result = new HwDeviceCapabilities
        {
            Decoders = decoders,
            Encoders = encoders,
            Filters = filters
        };

        // Every section carries the hwaccel name as a suffix, e.g. device_decoders_cuda, so match on the prefix.
        foreach (var property in device.EnumerateObject())
        {
            var name = property.Name;
            if (name.StartsWith("device_index_", StringComparison.Ordinal) && property.Value.TryGetInt32(out var index))
            {
                result.Index = index;
            }
            else if (name.StartsWith("device_info_", StringComparison.Ordinal) || string.Equals(name, "device_path_drm", StringComparison.Ordinal))
            {
                ReadDeviceInfo(property, result);
            }
            else if (name.StartsWith("device_decoders_", StringComparison.Ordinal))
            {
                ReadCodecs(property.Value, decoders);
            }
            else if (name.StartsWith("device_encoders_", StringComparison.Ordinal))
            {
                ReadCodecs(property.Value, encoders);
            }
            else if (name.StartsWith("device_filters_", StringComparison.Ordinal))
            {
                ReadFilters(property.Value, filters);
            }
            else if (name.StartsWith("device_opencl", StringComparison.Ordinal))
            {
                result.SupportsOpenCl = true;
            }
            else if (name.StartsWith("device_vulkan", StringComparison.Ordinal))
            {
                result.SupportsVulkan = true;
            }
        }

        return result;
    }

    private static void ReadDeviceInfo(JsonProperty property, HwDeviceCapabilities result)
    {
        if (property.Value.ValueKind == JsonValueKind.String)
        {
            if (string.Equals(property.Name, "device_path_drm", StringComparison.Ordinal))
            {
                result.DrmPath = property.Value.GetString();
            }

            return;
        }

        if (property.Value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var entry in property.Value.EnumerateObject())
        {
            switch (entry.Name)
            {
                case "device_name":
                    result.Name ??= entry.Value.GetString();
                    break;
                case "device_path_drm":
                    result.DrmPath ??= entry.Value.GetString();
                    break;
                case "device_driver_name":
                    result.DriverName ??= entry.Value.GetString();
                    break;
                case "device_driver_version":
                    result.DriverVersion ??= GetScalarString(entry.Value);
                    break;
            }
        }
    }

    private static void ReadCodecs(JsonElement array, List<HwCodecCapabilities> target)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var codec in array.EnumerateArray())
        {
            if (codec.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = GetString(codec, "codec_name");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            target.Add(new HwCodecCapabilities
            {
                CodecName = name,
                CodecDescription = GetString(codec, "codec_desc"),
                MinWidth = GetInt(codec, "min_width"),
                MinHeight = GetInt(codec, "min_height"),
                MaxWidth = GetInt(codec, "max_width"),
                MaxHeight = GetInt(codec, "max_height"),
                Profiles = ReadNames(codec, "profiles", "profile_name"),
                PixelFormats = ReadNames(codec, "pix_fmts", "format_name")
            });
        }
    }

    private static void ReadFilters(JsonElement array, List<HwFilterCapabilities> target)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var filter in array.EnumerateArray())
        {
            if (filter.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var kind = MapVppKind(GetString(filter, "filter_name"));
            if (kind == HwVppKind.Unknown)
            {
                continue;
            }

            var modes = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var entry in filter.EnumerateObject())
            {
                if (entry.Name.EndsWith("_mode", StringComparison.Ordinal)
                    || entry.Name.Contains("_mode_", StringComparison.Ordinal))
                {
                    modes[entry.Name] = entry.Value.ValueKind == JsonValueKind.Number
                        ? entry.Value.GetInt32() != 0
                        : entry.Value.ValueKind == JsonValueKind.True;
                }
            }

            target.Add(new HwFilterCapabilities
            {
                Kind = kind,
                FilterDescription = GetString(filter, "filter_desc"),
                MinWidth = GetInt(filter, "min_width"),
                MinHeight = GetInt(filter, "min_height"),
                MaxWidth = GetInt(filter, "max_width"),
                MaxHeight = GetInt(filter, "max_height"),
                PixelFormats = ReadNames(filter, "pix_fmts", "format_name"),
                Modes = modes
            });
        }
    }

    private static HwVppKind MapVppKind(string? name) => name switch
    {
        "scale" => HwVppKind.Scale,
        "deinterlace" => HwVppKind.Deinterlace,
        "overlay" => HwVppKind.Overlay,
        "rotate" => HwVppKind.Rotate,
        "flip" => HwVppKind.Flip,
        "denoise" => HwVppKind.Denoise,
        "detail" => HwVppKind.Detail,
        "procamp" => HwVppKind.Procamp,
        "tonemap" => HwVppKind.Tonemap,
        "framerate" => HwVppKind.Framerate,
        _ => HwVppKind.Unknown
    };

    private static IReadOnlyList<string> ReadNames(JsonElement parent, string arrayName, string valueName)
    {
        if (!parent.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        foreach (var entry in array.EnumerateArray())
        {
            var value = GetString(entry, valueName);
            if (!string.IsNullOrEmpty(value))
            {
                names.Add(value);
            }
        }

        return names;
    }

    private static string? GetString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? GetScalarString(value)
            : null;

    private static string? GetScalarString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };

    private static int? GetInt(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
