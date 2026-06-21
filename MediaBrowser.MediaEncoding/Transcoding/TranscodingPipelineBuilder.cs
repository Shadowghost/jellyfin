using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Session;

namespace MediaBrowser.MediaEncoding.Transcoding;

/// <summary>
/// Builds a <see cref="TranscodingPipelineInfo"/> from the ffmpeg command line that was generated
/// for a transcode. The ffmpeg arguments are the most faithful description of the real pipeline:
/// every decoder, filter and encoder is present and its name encodes the hardware framework it
/// runs on (for example <c>_qsv</c>, <c>_vaapi</c>, <c>_opencl</c>).
/// </summary>
public static partial class TranscodingPipelineBuilder
{
    /// <summary>
    /// Builds the transcoding pipeline for a video transcode.
    /// </summary>
    /// <param name="state">The encoding job.</param>
    /// <param name="commandLineArguments">The full ffmpeg command line arguments.</param>
    /// <param name="filterGraphJson">Optional ffmpeg <c>-print_graphs</c> JSON output. When
    /// supplied and parseable, its filters are used in place of the command-line-derived filters.</param>
    /// <returns>The pipeline, or <c>null</c> when there is nothing meaningful to describe (audio
    /// only, direct play or video remux).</returns>
    public static TranscodingPipelineInfo? Build(EncodingJobInfo state, string? commandLineArguments, string? filterGraphJson = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (string.IsNullOrWhiteSpace(commandLineArguments))
        {
            return null;
        }

        var tokens = Tokenize(commandLineArguments);
        if (tokens.Count == 0)
        {
            return null;
        }

        var firstInput = tokens.FindIndex(t => string.Equals(t, "-i", StringComparison.Ordinal));
        var lastInput = tokens.FindLastIndex(t => string.Equals(t, "-i", StringComparison.Ordinal));

        // Prefer the authoritative ffmpeg -print_graphs dump when available: it lists the actual
        // decoders, encoders and the negotiated filter graph (with per-filter output dimensions).
        if (!string.IsNullOrWhiteSpace(filterGraphJson))
        {
            try
            {
                var graphStages = BuildFromGraph(filterGraphJson, state, tokens, firstInput);
                if (graphStages is { Count: > 0 })
                {
                    return new TranscodingPipelineInfo { Stages = graphStages };
                }
            }
            catch (JsonException)
            {
                // Fall back to the command-line parser below.
            }
        }

        var stages = new List<TranscodingPipelineStage>();

        var videoTranscoded = state.VideoStream is not null && !EncodingHelper.IsCopyCodec(state.OutputVideoCodec);
        if (videoTranscoded)
        {
            AddDecodeStage(stages, tokens, firstInput, state);
            AddFilterStages(stages, tokens, lastInput, state);
            AddEncodeStage(stages, tokens, lastInput, state);
        }

        // Everything added so far is the video chain.
        TagMediaType(stages, "Video");

        // Audio may be transcoded alongside (or instead of) video. Append its decode -> encode
        // chain so the pipeline reflects every stream that is actually being transcoded.
        AddAudioStages(stages, tokens, lastInput, state);

        if (stages.Count == 0)
        {
            return null;
        }

        return new TranscodingPipelineInfo
        {
            Stages = stages
        };
    }

    /// <summary>
    /// Derives a short-lived "graph probe" command from a transcode command line. ffmpeg's
    /// <c>-print_graphs</c> only flushes the filter graph when ffmpeg exits, so the long-running
    /// transcode cannot be used. Instead this builds an equivalent command that initializes the
    /// graph and exits immediately (<c>-t 0</c>).
    /// </summary>
    /// <param name="commandLineArguments">The real transcode command line.</param>
    /// <param name="outputPath">The real transcode output path (its directory is redirected).</param>
    /// <param name="probeDirectory">A dedicated throwaway directory for the probe's output.</param>
    /// <param name="graphFilePath">The path the graph JSON should be written to.</param>
    /// <returns>The probe command line, or <c>null</c> if it cannot be derived safely.</returns>
    public static string? BuildGraphProbeArguments(string commandLineArguments, string outputPath, string probeDirectory, string graphFilePath)
    {
        if (string.IsNullOrWhiteSpace(commandLineArguments) || string.IsNullOrEmpty(outputPath))
        {
            return null;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(outputDirectory))
        {
            return null;
        }

        var probeCommand = commandLineArguments.Replace(outputDirectory, probeDirectory, StringComparison.Ordinal);
        var probeOutput = Path.Combine(probeDirectory, Path.GetFileName(outputPath));
        var outputIndex = probeCommand.LastIndexOf(probeOutput, StringComparison.Ordinal);
        if (outputIndex < 0)
        {
            return null;
        }

        var insertIndex = outputIndex > 0 && probeCommand[outputIndex - 1] == '"' ? outputIndex - 1 : outputIndex;
        probeCommand = probeCommand.Insert(insertIndex, "-t 0 ");

        return $"-print_graphs -print_graphs_format json -print_graphs_file \"{graphFilePath}\" {probeCommand}";
    }

    private static void AddAudioStages(List<TranscodingPipelineStage> stages, IReadOnlyList<string> tokens, int lastInput, EncodingJobInfo state)
    {
        if (state.AudioStream is null || EncodingHelper.IsCopyCodec(state.OutputAudioCodec))
        {
            return;
        }

        // Audio decode is implicit (ffmpeg picks the decoder), so name the decode stage after the
        // source codec. The framework is derived from the name.
        var sourceCodec = state.AudioStream.Codec;
        if (!string.IsNullOrEmpty(sourceCodec))
        {
            stages.Add(MakeAudioStage(TranscodeStageType.Decode, sourceCodec, GetCodecDisplayName(sourceCodec)));
        }

        var start = lastInput < 0 ? 0 : lastInput;
        var encoder = FindAudioCodecValue(tokens, start, tokens.Count);
        if (!string.IsNullOrEmpty(encoder) && !EncodingHelper.IsCopyCodec(encoder))
        {
            stages.Add(MakeAudioStage(TranscodeStageType.Encode, encoder, GetCodecDisplayName(state.ActualOutputAudioCodec)));
        }
    }

    private static TranscodingPipelineStage MakeAudioStage(TranscodeStageType type, string name, string? detail)
    {
        var framework = FrameworkFromName(name);
        return new TranscodingPipelineStage
        {
            Type = type,
            Framework = framework,
            Name = name,
            Detail = detail,
            IsHardware = framework != HardwareFramework.Software,
            MediaType = "Audio"
        };
    }

    private static void TagMediaType(List<TranscodingPipelineStage> stages, string mediaType)
    {
        foreach (var stage in stages)
        {
            stage.MediaType ??= mediaType;
        }
    }

    private static void AddDecodeStage(List<TranscodingPipelineStage> stages, IReadOnlyList<string> tokens, int firstInput, EncodingJobInfo state)
    {
        // An explicit input decoder is given as "-c:v <decoder>" before the first "-i".
        var inputEnd = firstInput < 0 ? tokens.Count : firstInput;
        var decoder = FindCodecValue(tokens, 0, inputEnd);
        var hwaccel = FindOptionValue(tokens, 0, inputEnd, "-hwaccel");

        var sourceCodec = state.VideoStream?.Codec;
        var name = decoder ?? sourceCodec;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        // When there is no explicit decoder the framework is inferred from -hwaccel, otherwise from the decoder name suffix.
        var framework = decoder is null
            ? FrameworkFromHwaccel(hwaccel)
            : FrameworkFromName(decoder);

        stages.Add(new TranscodingPipelineStage
        {
            Type = TranscodeStageType.Decode,
            Framework = framework,
            Name = name,
            Detail = GetCodecDisplayName(sourceCodec),
            IsHardware = framework != HardwareFramework.Software,
            VideoBitDepth = state.VideoStream?.BitDepth,
            VideoRange = state.VideoStream?.VideoRangeType
        });
    }

    private static void AddFilterStages(List<TranscodingPipelineStage> stages, IReadOnlyList<string> tokens, int lastInput, EncodingJobInfo state)
    {
        var start = lastInput < 0 ? 0 : lastInput;
        var filterGraph = FindOptionValue(tokens, start, tokens.Count, "-vf")
            ?? FindOptionValue(tokens, start, tokens.Count, "-filter:v")
            ?? FindOptionValue(tokens, start, tokens.Count, "-filter_complex");

        if (string.IsNullOrWhiteSpace(filterGraph))
        {
            return;
        }

        // A -filter_complex is split into sub-chains by ';'. The graphical-subtitle pre-processing
        // chain (scale/pad/crop/hwupload of the subtitle image) outputs a "[sub]" pad that is later
        // consumed by the overlay. That chain is plumbing for the burn-in, not part of the main
        // video processing path, so skip it - the overlay stage alone denotes the burn-in.
        foreach (var rawChain in filterGraph.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsSubtitlePrepChain(rawChain))
            {
                continue;
            }

            var cleaned = StreamLabelRegex().Replace(rawChain, string.Empty);
            foreach (var rawFilter in cleaned.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var filter = rawFilter.Trim();
                if (filter.Length == 0)
                {
                    continue;
                }

                var eq = filter.IndexOf('=', StringComparison.Ordinal);
                var filterName = (eq < 0 ? filter : filter[..eq]).Trim();
                var filterArgs = eq < 0 ? string.Empty : filter[(eq + 1)..];

                var stage = ClassifyFilter(filterName, filterArgs, state);
                if (stage is not null)
                {
                    stages.Add(stage);
                }
            }
        }
    }

    // A subtitle pre-processing chain is identified by its trailing output pad label ("[sub]").
    private static bool IsSubtitlePrepChain(string chain)
    {
        var match = TrailingLabelRegex().Match(chain.Trim());
        return match.Success && match.Groups[1].Value.Equals("sub", StringComparison.OrdinalIgnoreCase);
    }

    // Builds the full pipeline from the ffmpeg 8.0+ -print_graphs JSON dump, which lists the actual
    // decoders, encoders and the negotiated filter graph. Decoders/encoders are taken from the
    // top-level "decoders"/"encoders" arrays; filters from "graphs[].filters". To keep the video
    // lane clean we only include filters reachable forward from the video decoder, which naturally
    // drops subtitle-prep tributaries that merely feed an overlay's secondary input.
    private static List<TranscodingPipelineStage>? BuildFromGraph(string filterGraphJson, EncodingJobInfo state, IReadOnlyList<string> tokens, int firstInput)
    {
        using var document = JsonDocument.Parse(filterGraphJson);
        var root = document.RootElement;

        // Index filters by id and keep graph order (which mirrors chain order).
        var filtersById = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var orderedFilters = new List<JsonElement>();
        if (root.TryGetProperty("graphs", out var graphs) && graphs.ValueKind == JsonValueKind.Array)
        {
            foreach (var graph in graphs.EnumerateArray())
            {
                if (graph.TryGetProperty("filters", out var filterList) && filterList.ValueKind == JsonValueKind.Array)
                {
                    foreach (var filter in filterList.EnumerateArray())
                    {
                        orderedFilters.Add(filter);
                        var id = GetFilterId(filter);
                        if (!string.IsNullOrEmpty(id))
                        {
                            filtersById[id] = filter;
                        }
                    }
                }
            }
        }

        var stages = new List<TranscodingPipelineStage>();

        // Video chain: decode -> filters -> encode.
        var (videoDecoder, videoDecoderId) = FindCodec(root, "decoders", "video");
        var (videoEncoder, _) = FindCodec(root, "encoders", "video");
        if (!string.IsNullOrEmpty(videoEncoder))
        {
            if (!string.IsNullOrEmpty(videoDecoder))
            {
                // The decoder name rarely encodes the hwaccel, so fall back to the -hwaccel flag.
                var framework = FrameworkFromName(videoDecoder);
                if (framework == HardwareFramework.Software)
                {
                    var inputEnd = firstInput < 0 ? tokens.Count : firstInput;
                    framework = FrameworkFromHwaccel(FindOptionValue(tokens, 0, inputEnd, "-hwaccel"));
                }

                stages.Add(new TranscodingPipelineStage
                {
                    Type = TranscodeStageType.Decode,
                    Framework = framework,
                    Name = videoDecoder,
                    Detail = GetCodecDisplayName(videoDecoder),
                    IsHardware = framework != HardwareFramework.Software,
                    // The data leaving the decoder is the first main-path filter's input pad.
                    EdgeLabel = GetSeedInputLabel(orderedFilters, videoDecoderId),
                    VideoBitDepth = state.VideoStream?.BitDepth,
                    VideoRange = state.VideoStream?.VideoRangeType
                });
            }

            var mainPath = GetForwardReachableFilters(filtersById, orderedFilters, videoDecoderId);
            foreach (var filter in orderedFilters)
            {
                var id = GetFilterId(filter);
                if (id is null || !mainPath.Contains(id))
                {
                    continue;
                }

                var name = GetStringProperty(filter, "filter_name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var (width, height) = GetFilterOutputSize(filter);
                var args = width > 0 && height > 0
                    ? string.Format(CultureInfo.InvariantCulture, "w={0}:h={1}", width, height)
                    : string.Empty;

                var stage = ClassifyFilter(name, args, state);
                if (stage is not null)
                {
                    // The connector after this filter carries its output pad's format/resolution.
                    stage.EdgeLabel = GetPadLabel(GetFirstPad(filter, "filter_outputs"));
                    stages.Add(stage);
                }
            }

            var encoderFramework = FrameworkFromName(videoEncoder);
            stages.Add(new TranscodingPipelineStage
            {
                Type = TranscodeStageType.Encode,
                Framework = encoderFramework,
                Name = videoEncoder,
                Detail = GetCodecDisplayName(state.ActualOutputVideoCodec) ?? GetCodecDisplayName(videoEncoder),
                IsHardware = encoderFramework != HardwareFramework.Software,
                VideoBitDepth = GetTargetVideoBitDepth(state),
                VideoRange = GetTargetVideoRange(state, stages)
            });
        }

        // Everything added so far is the video chain; the audio stages below tag themselves.
        TagMediaType(stages, "Video");

        // Audio chain: decode -> encode. The framework comes from the codec name.
        var (audioDecoder, _) = FindCodec(root, "decoders", "audio");
        var (audioEncoder, _) = FindCodec(root, "encoders", "audio");
        if (!string.IsNullOrEmpty(audioEncoder))
        {
            if (!string.IsNullOrEmpty(audioDecoder))
            {
                stages.Add(MakeAudioStage(TranscodeStageType.Decode, audioDecoder, GetCodecDisplayName(audioDecoder)));
            }

            stages.Add(MakeAudioStage(TranscodeStageType.Encode, audioEncoder, GetCodecDisplayName(state.ActualOutputAudioCodec) ?? GetCodecDisplayName(audioEncoder)));
        }

        return stages.Count > 0 ? stages : null;
    }

    // Finds the first decoder/encoder of the given media type, returning its (name, id).
    private static (string? Name, string? Id) FindCodec(JsonElement root, string arrayName, string mediaType)
    {
        if (root.TryGetProperty(arrayName, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in array.EnumerateArray())
            {
                if (string.Equals(GetStringProperty(entry, "media_type"), mediaType, StringComparison.OrdinalIgnoreCase))
                {
                    return (GetStringProperty(entry, "name"), GetStringProperty(entry, "id"));
                }
            }
        }

        return (null, null);
    }

    // Collects every filter id reachable by following output links forward from the given source
    // (a decoder id). Filters on a secondary input branch (e.g. subtitle pre-processing feeding an
    // overlay) are not forward-reachable from the video decoder and are therefore excluded.
    private static HashSet<string> GetForwardReachableFilters(
        IReadOnlyDictionary<string, JsonElement> filtersById,
        IReadOnlyList<JsonElement> orderedFilters,
        string? sourceId)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(sourceId))
        {
            return reachable;
        }

        var queue = new Queue<string>();
        // Seed with filters whose input is the decoder itself.
        foreach (var filter in orderedFilters)
        {
            if (FilterHasInputSource(filter, sourceId))
            {
                var id = GetFilterId(filter);
                if (id is not null && reachable.Add(id))
                {
                    queue.Enqueue(id);
                }
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!filtersById.TryGetValue(current, out var filter)
                || !filter.TryGetProperty("filter_outputs", out var outputs)
                || outputs.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var output in outputs.EnumerateArray())
            {
                var dest = GetStringProperty(output, "dest_filter_id");
                if (dest is not null && filtersById.ContainsKey(dest) && reachable.Add(dest))
                {
                    queue.Enqueue(dest);
                }
            }
        }

        return reachable;
    }

    private static bool FilterHasInputSource(JsonElement filter, string sourceId)
    {
        if (filter.TryGetProperty("filter_inputs", out var inputs) && inputs.ValueKind == JsonValueKind.Array)
        {
            foreach (var input in inputs.EnumerateArray())
            {
                if (string.Equals(GetStringProperty(input, "source_filter_id"), sourceId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static (int Width, int Height) GetFilterOutputSize(JsonElement filter)
    {
        if (filter.TryGetProperty("filter_outputs", out var outputs)
            && outputs.ValueKind == JsonValueKind.Array
            && outputs.GetArrayLength() > 0)
        {
            var first = outputs[0];
            if (first.TryGetProperty("width", out var w) && first.TryGetProperty("height", out var h)
                && w.TryGetInt32(out var width) && h.TryGetInt32(out var height))
            {
                return (width, height);
            }
        }

        return (0, 0);
    }

    // The edge leaving the decoder is the input pad of the first main-path filter.
    private static string? GetSeedInputLabel(IReadOnlyList<JsonElement> orderedFilters, string? decoderId)
    {
        if (string.IsNullOrEmpty(decoderId))
        {
            return null;
        }

        foreach (var filter in orderedFilters)
        {
            if (FilterHasInputSource(filter, decoderId))
            {
                return GetPadLabel(GetFirstPad(filter, "filter_inputs"));
            }
        }

        return null;
    }

    private static JsonElement? GetFirstPad(JsonElement filter, string padArray)
    {
        if (filter.TryGetProperty(padArray, out var pads)
            && pads.ValueKind == JsonValueKind.Array
            && pads.GetArrayLength() > 0)
        {
            return pads[0];
        }

        return null;
    }

    // Formats a pad as "<format> <width>x<height>" (omitting any missing part) - the per-edge label shown on the connectors, mirroring ffmpeg's mermaid graph.
    private static string? GetPadLabel(JsonElement? pad)
    {
        if (pad is not { } element)
        {
            return null;
        }

        var format = GetStringProperty(element, "format");
        // Hardware frames are reported as "<hw_surface> | <sw_format>" (e.g. "videotoolbox_vld | p010le"); keep only the meaningful pixel format.
        if (!string.IsNullOrEmpty(format) && format.Contains('|', StringComparison.Ordinal))
        {
            format = format.Split('|')[^1].Trim();
        }

        string? size = null;
        if (element.TryGetProperty("width", out var w) && element.TryGetProperty("height", out var h)
            && w.TryGetInt32(out var width) && h.TryGetInt32(out var height) && width > 0 && height > 0)
        {
            size = string.Format(CultureInfo.InvariantCulture, "{0}x{1}", width, height);
        }

        var label = string.Join(' ', new[] { format, size }.Where(s => !string.IsNullOrEmpty(s)));
        return string.IsNullOrEmpty(label) ? null : label;
    }

    // A filter's own id is not a top-level field; ffmpeg records it as "filter_id" inside each of the filter's input and output pad entries.
    private static string? GetFilterId(JsonElement filter)
    {
        foreach (var padArray in new[] { "filter_outputs", "filter_inputs" })
        {
            if (filter.TryGetProperty(padArray, out var pads)
                && pads.ValueKind == JsonValueKind.Array
                && pads.GetArrayLength() > 0)
            {
                var id = GetStringProperty(pads[0], "filter_id");
                if (!string.IsNullOrEmpty(id))
                {
                    return id;
                }
            }
        }

        return null;
    }

    private static string? GetStringProperty(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static void AddEncodeStage(List<TranscodingPipelineStage> stages, IReadOnlyList<string> tokens, int lastInput, EncodingJobInfo state)
    {
        var start = lastInput < 0 ? 0 : lastInput;
        var encoder = FindCodecValue(tokens, start, tokens.Count);
        if (string.IsNullOrEmpty(encoder) || EncodingHelper.IsCopyCodec(encoder))
        {
            return;
        }

        var framework = FrameworkFromName(encoder);
        stages.Add(new TranscodingPipelineStage
        {
            Type = TranscodeStageType.Encode,
            Framework = framework,
            Name = encoder,
            Detail = GetCodecDisplayName(state.ActualOutputVideoCodec),
            IsHardware = framework != HardwareFramework.Software,
            VideoBitDepth = GetTargetVideoBitDepth(state),
            VideoRange = GetTargetVideoRange(state, stages)
        });
    }

    private static TranscodingPipelineStage? ClassifyFilter(string filterName, string filterArgs, EncodingJobInfo state)
    {
        var lower = filterName.ToLowerInvariant();
        var framework = FrameworkFromName(lower);
        var isHardware = framework != HardwareFramework.Software;

        // vpp_qsv is a multi purpose Intel filter. When it tone maps it carries a "tonemap" parameter, otherwise we treat it as the scaler.
        if (lower.Contains("tonemap", StringComparison.Ordinal)
            || (lower.Equals("vpp_qsv", StringComparison.Ordinal) && filterArgs.Contains("tonemap", StringComparison.OrdinalIgnoreCase)))
        {
            return new TranscodingPipelineStage
            {
                Type = TranscodeStageType.ToneMap,
                Framework = framework,
                Name = filterName,
                Detail = GetToneMapDetail(filterArgs),
                IsHardware = isHardware
            };
        }

        if (lower.StartsWith("scale", StringComparison.Ordinal)
            || lower.Equals("vpp_qsv", StringComparison.Ordinal)
            || lower.Equals("zscale", StringComparison.Ordinal))
        {
            return new TranscodingPipelineStage
            {
                Type = TranscodeStageType.Scale,
                Framework = framework,
                Name = filterName,
                Detail = GetScaleDetail(filterArgs, state),
                IsHardware = isHardware
            };
        }

        if (lower.StartsWith("yadif", StringComparison.Ordinal)
            || lower.StartsWith("bwdif", StringComparison.Ordinal)
            || lower.StartsWith("deinterlace", StringComparison.Ordinal)
            || lower.StartsWith("estdif", StringComparison.Ordinal))
        {
            return new TranscodingPipelineStage
            {
                Type = TranscodeStageType.Deinterlace,
                Framework = framework,
                Name = filterName,
                IsHardware = isHardware
            };
        }

        if (lower.StartsWith("overlay", StringComparison.Ordinal)
            || lower.Equals("subtitles", StringComparison.Ordinal)
            || lower.Equals("ass", StringComparison.Ordinal))
        {
            // overlay/subtitles/ass all burn the subtitle into the video frames.
            return new TranscodingPipelineStage
            {
                Type = TranscodeStageType.Subtitle,
                Framework = framework,
                Name = filterName,
                Detail = "Burn-in",
                IsHardware = isHardware
            };
        }

        // hwupload/hwdownload move frame data across the software/hardware boundary. Unlike the
        // other plumbing filters - pure format conversion, or hwmap which remaps between two
        // hardware contexts (e.g. QSV <-> OpenCL) and is typically zero-copy - these are real
        // memory transfers that switch between software and hardware memory and can noticeably
        // affect performance, so they are surfaced as their own stages.
        if (lower.StartsWith("hwupload", StringComparison.Ordinal))
        {
            // The upload produces hardware frames; the target framework is encoded either in the
            // name suffix (hwupload_vaapi, hwupload_cuda) or in a "derive_device=<framework>" arg.
            return new TranscodingPipelineStage
            {
                Type = TranscodeStageType.HardwareUpload,
                Framework = FrameworkForTransfer(lower, filterArgs),
                Name = filterName,
                Detail = "System -> hardware memory",
                IsHardware = true
            };
        }

        if (lower.StartsWith("hwdownload", StringComparison.Ordinal))
        {
            // The download produces system-memory frames, so the resulting data is in software.
            return new TranscodingPipelineStage
            {
                Type = TranscodeStageType.HardwareDownload,
                Framework = HardwareFramework.Software,
                Name = filterName,
                Detail = "Hardware -> system memory",
                IsHardware = false
            };
        }

        // The remaining plumbing filters (format conversion, hwmap) are omitted to keep the
        // pipeline focused on the user visible processing steps.
        return null;
    }

    // Resolves the hardware framework an hwupload targets: the name suffix wins (hwupload_vaapi,
    // hwupload_cuda), otherwise it is taken from the filter's "derive_device=<framework>" argument.
    private static HardwareFramework FrameworkForTransfer(string lowerName, string filterArgs)
    {
        var fromName = FrameworkFromName(lowerName);
        if (fromName != HardwareFramework.Software)
        {
            return fromName;
        }

        var device = MatchNamed(filterArgs, "derive_device");
        return string.IsNullOrEmpty(device) ? HardwareFramework.Software : FrameworkFromHwaccel(device);
    }

    private static string? GetScaleDetail(string filterArgs, EncodingJobInfo state)
    {
        var width = MatchDimension(filterArgs, 'w') ?? MatchNamed(filterArgs, "width");
        var height = MatchDimension(filterArgs, 'h') ?? MatchNamed(filterArgs, "height");

        if (width is not null && height is not null && !width.Contains("-1", StringComparison.Ordinal) && !height.Contains("-1", StringComparison.Ordinal))
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}x{1}", width, height);
        }

        if (state.OutputWidth.HasValue && state.OutputHeight.HasValue)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}x{1}", state.OutputWidth.Value, state.OutputHeight.Value);
        }

        return null;
    }

    private static int? GetTargetVideoBitDepth(EncodingJobInfo state)
        => (state.BaseRequest is null ? null : state.GetRequestedVideoBitDepth(state.ActualOutputVideoCodec))
            ?? state.VideoStream?.BitDepth;

    // The negotiated output range, sent as-is - the client decides how to present it (Unknown is
    // simply not displayed). Requires a BaseRequest to resolve. When the request doesn't pin an
    // explicit output range (the common case), it is inferred from the pipeline: a tone map collapses
    // HDR to SDR, otherwise the source range is preserved.
    private static VideoRangeType? GetTargetVideoRange(EncodingJobInfo state, IEnumerable<TranscodingPipelineStage> stages)
    {
        if (state.BaseRequest is null)
        {
            return null;
        }

        var requested = state.TargetVideoRangeType;
        if (requested != VideoRangeType.Unknown)
        {
            return requested;
        }

        var tonemapped = stages.Any(s => s.Type == TranscodeStageType.ToneMap);
        return tonemapped ? VideoRangeType.SDR : state.VideoStream?.VideoRangeType;
    }

    private static string? GetToneMapDetail(string filterArgs)
    {
        var algo = MatchNamed(filterArgs, "tonemap");
        if (!string.IsNullOrEmpty(algo))
        {
            return char.ToUpperInvariant(algo[0]) + algo[1..];
        }

        return null;
    }

    private static HardwareFramework FrameworkFromName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return HardwareFramework.Software;
        }

        var lower = name.ToLowerInvariant();
        if (lower.EndsWith("_qsv", StringComparison.Ordinal) || lower.Equals("vpp_qsv", StringComparison.Ordinal))
        {
            return HardwareFramework.Qsv;
        }

        if (lower.EndsWith("_vaapi", StringComparison.Ordinal))
        {
            return HardwareFramework.Vaapi;
        }

        if (lower.EndsWith("_cuda", StringComparison.Ordinal)
            || lower.EndsWith("_npp", StringComparison.Ordinal)
            || lower.EndsWith("_nvenc", StringComparison.Ordinal)
            || lower.EndsWith("_cuvid", StringComparison.Ordinal))
        {
            return HardwareFramework.Cuda;
        }

        if (lower.EndsWith("_opencl", StringComparison.Ordinal))
        {
            return HardwareFramework.OpenCl;
        }

        if (lower.EndsWith("_vulkan", StringComparison.Ordinal))
        {
            return HardwareFramework.Vulkan;
        }

        if (lower.EndsWith("_vt", StringComparison.Ordinal)
            || lower.EndsWith("_videotoolbox", StringComparison.Ordinal))
        {
            return HardwareFramework.VideoToolbox;
        }

        if (lower.EndsWith("_amf", StringComparison.Ordinal))
        {
            return HardwareFramework.Amf;
        }

        if (lower.EndsWith("_rkmpp", StringComparison.Ordinal) || lower.EndsWith("_rkrga", StringComparison.Ordinal))
        {
            return HardwareFramework.Rkmpp;
        }

        if (lower.EndsWith("_v4l2m2m", StringComparison.Ordinal))
        {
            return HardwareFramework.V4l2m2m;
        }

        if (lower.EndsWith("_at", StringComparison.Ordinal))
        {
            return HardwareFramework.AudioToolbox;
        }

        return HardwareFramework.Software;
    }

    private static HardwareFramework FrameworkFromHwaccel(string? hwaccel)
    {
        if (string.IsNullOrEmpty(hwaccel))
        {
            return HardwareFramework.Software;
        }

        return hwaccel.ToLowerInvariant() switch
        {
            "qsv" => HardwareFramework.Qsv,
            "cuda" or "cuvid" or "nvdec" => HardwareFramework.Cuda,
            "vaapi" or "drm" => HardwareFramework.Vaapi,
            "d3d11va" or "dxva2" => HardwareFramework.D3D11Va,
            "videotoolbox" => HardwareFramework.VideoToolbox,
            "opencl" => HardwareFramework.OpenCl,
            "vulkan" => HardwareFramework.Vulkan,
            "rkmpp" => HardwareFramework.Rkmpp,
            "v4l2m2m" or "v4l2" => HardwareFramework.V4l2m2m,
            _ => HardwareFramework.Software
        };
    }

    private static string? GetCodecDisplayName(string? codec)
    {
        if (string.IsNullOrEmpty(codec))
        {
            return null;
        }

        return codec.ToLowerInvariant() switch
        {
            "hevc" or "h265" => "H.265 (HEVC)",
            "h264" or "avc" => "H.264 (AVC)",
            "av1" => "AV1",
            "vp9" => "VP9",
            "vp8" => "VP8",
            "mpeg2video" or "mpeg2" => "MPEG-2",
            "mpeg4" => "MPEG-4",
            "vc1" => "VC-1",
            "aac" => "AAC",
            "ac3" => "Dolby Digital (AC-3)",
            "eac3" => "Dolby Digital Plus (E-AC-3)",
            "dts" or "dca" => "DTS",
            "truehd" => "Dolby TrueHD",
            "flac" => "FLAC",
            "opus" => "Opus",
            "mp3" => "MP3",
            "vorbis" => "Vorbis",
            _ => codec.ToUpperInvariant()
        };
    }

    // Finds the value of a video codec option (handles indexed forms such as "-codec:v:0").
    private static string? FindCodecValue(IReadOnlyList<string> tokens, int start, int end)
    {
        return FindCodecValueForType(tokens, start, end, 'v');
    }

    // Finds the value of an audio codec option (handles indexed forms such as "-codec:a:0").
    private static string? FindAudioCodecValue(IReadOnlyList<string> tokens, int start, int end)
    {
        return FindCodecValueForType(tokens, start, end, 'a');
    }

    // Matches "-c:<t>", "-codec:<t>", "-<t>codec" and their stream-indexed variants ("-c:<t>:0").
    private static string? FindCodecValueForType(IReadOnlyList<string> tokens, int start, int end, char type)
    {
        end = Math.Min(end, tokens.Count);
        var cShort = "-c:" + type;
        var cLong = "-codec:" + type;
        var cAlt = "-" + (type == 'v' ? "vcodec" : "acodec");

        for (var i = Math.Max(0, start); i < end - 1; i++)
        {
            var t = tokens[i];
            if (string.Equals(t, cShort, StringComparison.Ordinal)
                || string.Equals(t, cLong, StringComparison.Ordinal)
                || string.Equals(t, cAlt, StringComparison.Ordinal)
                || t.StartsWith(cShort + ":", StringComparison.Ordinal)
                || t.StartsWith(cLong + ":", StringComparison.Ordinal))
            {
                return tokens[i + 1].Trim('"');
            }
        }

        return null;
    }

    private static string? FindOptionValue(IReadOnlyList<string> tokens, int start, int end, string option)
    {
        end = Math.Min(end, tokens.Count);
        for (var i = Math.Max(0, start); i < end - 1; i++)
        {
            if (string.Equals(tokens[i], option, StringComparison.Ordinal))
            {
                return tokens[i + 1].Trim('"');
            }
        }

        return null;
    }

    private static string? MatchDimension(string input, char dim)
    {
        var match = Regex.Match(input, dim + @"=([^:]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? MatchNamed(string input, string key)
    {
        var match = Regex.Match(input, key + @"=([^:]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    // Splits a command line into tokens, treating double quoted spans as part of the value that follows an option (so the filter graph after -vf "..." stays a single token).
    private static List<string> Tokenize(string commandLine)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex StreamLabelRegex();

    [GeneratedRegex(@"\[([^\]]*)\]\s*$")]
    private static partial Regex TrailingLabelRegex();
}
