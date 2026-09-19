#nullable enable

#pragma warning disable CS1591
// We need lowercase normalized string for ffmpeg
#pragma warning disable CA1308

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Extensions;
using Jellyfin.MediaEncoding.Pipeline.BitStreams;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Extensions;
using MediaBrowser.Controller.IO;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaEncoding.Hardware;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Configuration;
using IConfigurationManager = MediaBrowser.Common.Configuration.IConfigurationManager;

namespace MediaBrowser.Controller.MediaEncoding
{
    /// <summary>
    /// Works out the arguments around the filters: the input, the stream mapping and the output.
    /// </summary>
    public partial class EncodingHelper
    {
        public static string? GetInputFormat(string container)
        {
            if (string.IsNullOrEmpty(container) || !ContainerValidationRegex().IsMatch(container))
            {
                return null;
            }

            container = container.Replace("mkv", "matroska", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(container, "ts", StringComparison.OrdinalIgnoreCase))
            {
                return "mpegts";
            }

            // For these need to find out the ffmpeg names
            if (string.Equals(container, "m2ts", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "wmv", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "mts", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "vob", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "mpg", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "mpeg", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "rec", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "dvr-ms", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "ogm", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "divx", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "tp", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "rmvb", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(container, "rtp", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Seeing reported failures here, not sure yet if this is related to specifying input format
            if (string.Equals(container, "m4v", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // obviously don't do this for strm files
            if (string.Equals(container, "strm", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // ISO files don't have an ffmpeg format
            if (string.Equals(container, "iso", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return container;
        }

        /// <summary>
        /// Gets the input argument.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="segmentContainer">Segment Container.</param>
        /// <returns>Input arguments.</returns>
        public string GetInputArgument(EncodingJobInfo state, EncodingOptions options, string? segmentContainer)
        {
            var arg = new StringBuilder();
            var inputVidHwaccelArgs = GetInputVideoHwaccelArgs(state, options);

            if (!string.IsNullOrEmpty(inputVidHwaccelArgs))
            {
                arg.Append(inputVidHwaccelArgs);
            }

            var canvasArgs = GetGraphicalSubCanvasSize(state);
            if (!string.IsNullOrEmpty(canvasArgs))
            {
                arg.Append(canvasArgs);
            }

            if (state.MediaSource.VideoType == VideoType.Dvd || state.MediaSource.VideoType == VideoType.BluRay)
            {
                var concatFilePath = Path.Join(_configurationManager.CommonApplicationPaths.CachePath, "concat", state.MediaSource.Id + ".concat");
                if (!File.Exists(concatFilePath))
                {
                    _mediaEncoder.GenerateConcatConfig(state.MediaSource, concatFilePath);
                }

                arg.Append(" -f concat -safe 0 -i \"")
                    .Append(concatFilePath)
                    .Append("\" ");
            }
            else
            {
                arg.Append(" -i ")
                    .Append(_mediaEncoder.GetInputPathArgument(state));
            }

            if (NeedsExternalSubtitleMuxing(state))
            {
                var subtitlePath = state.SubtitleStream.Path;
                var isGraphicalBurnIn = ShouldEncodeSubtitle(state) && !state.SubtitleStream.IsTextSubtitleStream;

                // dvdsub/vobsub graphical subtitles use .sub+.idx pairs
                var subtitleExtension = Path.GetExtension(subtitlePath.AsSpan());
                if (subtitleExtension.Equals(".sub", StringComparison.OrdinalIgnoreCase))
                {
                    var idxFile = Path.ChangeExtension(subtitlePath, ".idx");
                    if (File.Exists(idxFile))
                    {
                        subtitlePath = idxFile;
                    }
                }

                // Use analyzeduration also for subtitle streams to improve resolution detection  with streams inside MKS files
                var analyzeDurationArgument = GetFfmpegAnalyzeDurationArg(state);
                if (!string.IsNullOrEmpty(analyzeDurationArgument))
                {
                    arg.Append(' ').Append(analyzeDurationArgument);
                }

                // Apply probesize, too, if configured
                var ffmpegProbeSizeArgument = GetFfmpegProbesizeArg();
                if (!string.IsNullOrEmpty(ffmpegProbeSizeArgument))
                {
                    arg.Append(' ').Append(ffmpegProbeSizeArgument);
                }

                // Also seek the external subtitles stream.
                var seekSubParam = GetFastSeekCommandLineParameter(state, options, segmentContainer);
                if (!string.IsNullOrEmpty(seekSubParam))
                {
                    arg.Append(' ').Append(seekSubParam);
                }

                if (isGraphicalBurnIn && !string.IsNullOrEmpty(canvasArgs))
                {
                    arg.Append(canvasArgs);
                }

                arg.Append(" -i file:\"").Append(subtitlePath.EscapeProcessArgument()).Append('\"');
            }

            if (state.AudioStream is not null && state.AudioStream.IsExternal)
            {
                // Also seek the external audio stream.
                var seekAudioParam = GetFastSeekCommandLineParameter(state, options, segmentContainer);
                if (!string.IsNullOrEmpty(seekAudioParam))
                {
                    arg.Append(' ').Append(seekAudioParam);
                }

                arg.Append(" -i \"").Append(state.AudioStream.Path.EscapeProcessArgument()).Append('"');
            }

            // Disable auto inserted SW scaler for HW decoders in case of changed resolution.
            var isSwDecoder = string.IsNullOrEmpty(GetHardwareVideoDecoder(state, options));
            if (!isSwDecoder)
            {
                arg.Append(" -noautoscale");
            }

            return arg.ToString();
        }

        public static string GetSegmentFileExtension(string? segmentContainer)
        {
            if (!string.IsNullOrWhiteSpace(segmentContainer))
            {
                return "." + segmentContainer;
            }

            return ".ts";
        }

        /// <summary>
        /// Gets the fast seek command line parameter.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="options">The options.</param>
        /// <param name="segmentContainer">Segment Container.</param>
        /// <returns>System.String.</returns>
        /// <value>The fast seek command line parameter.</value>
        public string GetFastSeekCommandLineParameter(EncodingJobInfo state, EncodingOptions options, string? segmentContainer)
        {
            var time = state.BaseRequest.StartTimeTicks ?? 0;
            var maxTime = state.RunTimeTicks ?? 0;
            var seekParam = string.Empty;

            if (time > 0)
            {
                // For direct streaming/remuxing, HLS segments start at keyframes.
                // However, ffmpeg will seek to previous keyframe when the exact frame time is the input
                // Workaround this by adding 0.5s offset to the seeking time to get the exact keyframe on most videos.
                // This will help subtitle syncing.
                var isHlsRemuxing = state.IsVideoRequest && state.TranscodingType is TranscodingJobType.Hls && IsCopyCodec(state.OutputVideoCodec);
                var seekTick = isHlsRemuxing ? time + 5000000L : time;

                // Seeking beyond EOF makes no sense in transcoding. Clamp the seekTick value to
                // [0, RuntimeTicks - 5.0s], so that the muxer gets packets and avoid error codes.
                if (maxTime > 0)
                {
                    seekTick = Math.Clamp(seekTick, 0, Math.Max(maxTime - 50000000L, 0));
                }

                seekParam += string.Format(CultureInfo.InvariantCulture, "-ss {0}", _mediaEncoder.GetTimeParameter(seekTick));
            }

            return seekParam;
        }

        /// <summary>
        /// Gets the map args.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        public string GetMapArgs(EncodingJobInfo state)
        {
            // If we don't have known media info
            // If input is video, use -sn to drop subtitles
            // Otherwise just return empty
            if (state.VideoStream is null && state.AudioStream is null)
            {
                return state.IsInputVideo ? "-sn" : string.Empty;
            }

            // We have media info, but we don't know the stream index
            if (state.VideoStream is not null && state.VideoStream.Index == -1)
            {
                return "-sn";
            }

            // We have media info, but we don't know the stream index
            if (state.AudioStream is not null && state.AudioStream.Index == -1)
            {
                return state.IsInputVideo ? "-sn" : string.Empty;
            }

            var args = string.Empty;

            if (state.VideoStream is not null)
            {
                int videoStreamIndex = FindIndex(state.MediaSource.MediaStreams, state.VideoStream);

                args += string.Format(
                    CultureInfo.InvariantCulture,
                    "-map 0:{0}",
                    videoStreamIndex);
            }
            else
            {
                // No known video stream
                args += "-vn";
            }

            if (state.AudioStream is not null)
            {
                int audioStreamIndex = FindIndex(state.MediaSource.MediaStreams, state.AudioStream);
                if (state.AudioStream.IsExternal)
                {
                    bool hasExternalSubAsInput = NeedsExternalSubtitleMuxing(state);
                    int externalAudioMapIndex = hasExternalSubAsInput ? 2 : 1;

                    args += string.Format(
                        CultureInfo.InvariantCulture,
                        " -map {0}:{1}",
                        externalAudioMapIndex,
                        audioStreamIndex);
                }
                else
                {
                    args += string.Format(
                        CultureInfo.InvariantCulture,
                        " -map 0:{0}",
                        audioStreamIndex);
                }
            }
            else
            {
                args += " -map -0:a";
            }

            var subtitleMethod = state.SubtitleDeliveryMethod;
            if (state.SubtitleStream is null || subtitleMethod == SubtitleDeliveryMethod.Hls)
            {
                args += " -map -0:s";
            }
            else if (subtitleMethod == SubtitleDeliveryMethod.Embed)
            {
                if (state.SubtitleStream.IsExternal)
                {
                    // External subtitle file is added as second FFmpeg input.
                    // For single-stream files (SRT/ASS/VTT) the in-file index is always 0.
                    // For multi-stream containers (MKS) we count how many streams from
                    // the same file appear before the selected one.
                    var inFileIndex = state.MediaSource.MediaStreams
                        .Where(s => string.Equals(s.Path, state.SubtitleStream.Path, StringComparison.Ordinal))
                        .TakeWhile(s => s.Index != state.SubtitleStream.Index)
                        .Count();

                    args += string.Format(
                        CultureInfo.InvariantCulture,
                        " -map 1:{0}",
                        inFileIndex);
                }
                else
                {
                    int subtitleStreamIndex = FindIndex(state.MediaSource.MediaStreams, state.SubtitleStream);

                    args += string.Format(
                        CultureInfo.InvariantCulture,
                        " -map 0:{0}",
                        subtitleStreamIndex);
                }
            }
            else if (state.SubtitleStream.IsExternal && !state.SubtitleStream.IsTextSubtitleStream)
            {
                int externalSubtitleStreamIndex = FindIndex(state.MediaSource.MediaStreams, state.SubtitleStream);

                args += string.Format(
                    CultureInfo.InvariantCulture,
                    " -map 1:{0} -sn",
                    externalSubtitleStreamIndex);
            }

            return args;
        }

        /// <summary>
        /// Gets the negative map args by filters.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="videoProcessFilters">The videoProcessFilters.</param>
        /// <returns>System.String.</returns>
        public string GetNegativeMapArgsByFilters(EncodingJobInfo state, string videoProcessFilters)
        {
            string args = string.Empty;

            // http://ffmpeg.org/ffmpeg-all.html#toc-Complex-filtergraphs-1
            if (state.VideoStream is not null && videoProcessFilters.Contains("-filter_complex", StringComparison.Ordinal))
            {
                int videoStreamIndex = FindIndex(state.MediaSource.MediaStreams, state.VideoStream);

                args += string.Format(
                    CultureInfo.InvariantCulture,
                    "-map -0:{0} ",
                    videoStreamIndex);
            }

            return args;
        }

        private string GetFfmpegAnalyzeDurationArg(EncodingJobInfo state)
        {
            var analyzeDurationArgument = string.Empty;

            // Apply -analyzeduration as per the environment variable,
            // otherwise ffmpeg will break on certain files due to default value is 0.
            var ffmpegAnalyzeDuration = _config.GetFFmpegAnalyzeDuration() ?? string.Empty;

            if (state.MediaSource.AnalyzeDurationMs > 0)
            {
                analyzeDurationArgument = "-analyzeduration " + (state.MediaSource.AnalyzeDurationMs.Value * 1000).ToString(CultureInfo.InvariantCulture);
            }
            else if (!string.IsNullOrEmpty(ffmpegAnalyzeDuration))
            {
                analyzeDurationArgument = "-analyzeduration " + ffmpegAnalyzeDuration;
            }

            return analyzeDurationArgument;
        }

        private string GetFfmpegProbesizeArg()
        {
            var ffmpegProbeSize = _config.GetFFmpegProbeSize();

            if (!string.IsNullOrEmpty(ffmpegProbeSize))
            {
                return $"-probesize {ffmpegProbeSize}";
            }

            return string.Empty;
        }

        public string GetInputModifier(EncodingJobInfo state, EncodingOptions encodingOptions, string? segmentContainer)
        {
            var inputModifier = string.Empty;
            var analyzeDurationArgument = GetFfmpegAnalyzeDurationArg(state);

            if (!string.IsNullOrEmpty(analyzeDurationArgument))
            {
                inputModifier += " " + analyzeDurationArgument;
            }

            inputModifier = inputModifier.Trim();

            // Apply -probesize if configured
            var ffmpegProbeSizeArgument = GetFfmpegProbesizeArg();

            if (!string.IsNullOrEmpty(ffmpegProbeSizeArgument))
            {
                inputModifier += " " + ffmpegProbeSizeArgument;
            }

            var userAgentParam = GetUserAgentParam(state);

            if (!string.IsNullOrEmpty(userAgentParam))
            {
                inputModifier += " " + userAgentParam;
            }

            inputModifier = inputModifier.Trim();

            var refererParam = GetRefererParam(state);

            if (!string.IsNullOrEmpty(refererParam))
            {
                inputModifier += " " + refererParam;
            }

            inputModifier = inputModifier.Trim();

            inputModifier += " " + GetFastSeekCommandLineParameter(state, encodingOptions, segmentContainer);
            inputModifier = inputModifier.Trim();

            if (state.InputProtocol == MediaProtocol.Rtsp)
            {
                inputModifier += " -rtsp_transport tcp+udp -rtsp_flags prefer_tcp";
            }

            if (!string.IsNullOrEmpty(state.InputAudioSync))
            {
                inputModifier += " -async " + state.InputAudioSync;
            }

            // The -fps_mode option cannot be applied to input
            if (!string.IsNullOrEmpty(state.InputVideoSync) && _mediaEncoder.EncoderVersion < new Version(5, 1))
            {
                inputModifier += GetVideoSyncOption(state.InputVideoSync, _mediaEncoder.EncoderVersion);
            }

            int readrate = 0;
            if (state.ReadInputAtNativeFramerate && state.InputProtocol != MediaProtocol.Rtsp)
            {
                readrate = 1;
                inputModifier += " -re";
            }
            else if (encodingOptions.EnableSegmentDeletion
                && state.VideoStream is not null
                && state.TranscodingType == TranscodingJobType.Hls
                && IsCopyCodec(state.OutputVideoCodec)
                && _mediaEncoder.EncoderVersion >= _minFFmpegReadrateOption)
            {
                // Set an input read rate limit 10x for using SegmentDeletion with stream-copy
                // to prevent ffmpeg from exiting prematurely (due to fast drive)
                readrate = 10;
                inputModifier += $" -readrate {readrate}";
            }

            // Set a larger catchup value to revert to the old behavior,
            // otherwise, remuxing might stall due to this new option
            if (readrate > 0 && _mediaEncoder.EncoderVersion >= _minFFmpegReadrateCatchupOption)
            {
                inputModifier += $" -readrate_catchup {readrate * 100}";
            }

            var flags = new List<string>();
            if (state.IgnoreInputDts)
            {
                flags.Add("+igndts");
            }

            if (state.IgnoreInputIndex)
            {
                flags.Add("+ignidx");
            }

            if (state.GenPtsInput || IsCopyCodec(state.OutputVideoCodec))
            {
                flags.Add("+genpts");
            }

            if (state.DiscardCorruptFramesInput)
            {
                flags.Add("+discardcorrupt");
            }

            if (state.EnableFastSeekInput)
            {
                flags.Add("+fastseek");
            }

            if (flags.Count > 0)
            {
                inputModifier += " -fflags " + string.Join(string.Empty, flags);
            }

            if (state.IsVideoRequest)
            {
                if (!string.IsNullOrEmpty(state.InputContainer) && state.VideoType == VideoType.VideoFile && encodingOptions.HardwareAccelerationType != HardwareAccelerationType.none)
                {
                    var inputFormat = GetInputFormat(state.InputContainer);
                    if (!string.IsNullOrEmpty(inputFormat))
                    {
                        inputModifier += " -f " + inputFormat;
                    }
                }
            }

            if (state.MediaSource.RequiresLooping)
            {
                inputModifier += " -stream_loop -1 -reconnect_at_eof 1 -reconnect_streamed 1 -reconnect_delay_max 2";
            }

            return inputModifier;
        }

        private void NormalizeSubtitleEmbed(EncodingJobInfo state)
        {
            if (state.SubtitleStream is null || state.SubtitleDeliveryMethod != SubtitleDeliveryMethod.Embed)
            {
                return;
            }

            // This is tricky to remux in, after converting to dvdsub it's not positioned correctly
            // Therefore, let's just burn it in
            if (string.Equals(state.SubtitleStream.Codec, "DVBSUB", StringComparison.OrdinalIgnoreCase))
            {
                state.SubtitleDeliveryMethod = SubtitleDeliveryMethod.Encode;
            }
        }

        public string GetSubtitleEmbedArguments(EncodingJobInfo state)
        {
            if (state.SubtitleStream is null || state.SubtitleDeliveryMethod != SubtitleDeliveryMethod.Embed)
            {
                return string.Empty;
            }

            var format = state.SupportedSubtitleCodecs.FirstOrDefault();
            string codec;

            if (string.IsNullOrEmpty(format) || string.Equals(format, state.SubtitleStream.Codec, StringComparison.OrdinalIgnoreCase))
            {
                codec = "copy";
            }
            else
            {
                codec = format;
            }

            return " -codec:s:0 " + codec + " -disposition:s:0 default";
        }

        public string GetProgressiveVideoFullCommandLine(EncodingJobInfo state, EncodingOptions encodingOptions, EncoderPreset defaultPreset)
        {
            // Get the output codec name
            var videoCodec = GetVideoEncoder(state, encodingOptions);

            var format = string.Empty;
            var keyFrame = string.Empty;
            var outputPath = state.OutputFilePath;

            if (Path.GetExtension(outputPath.AsSpan()).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                && state.BaseRequest.Context == EncodingContext.Streaming)
            {
                // Comparison: https://github.com/jansmolders86/mediacenterjs/blob/master/lib/transcoding/desktop.js
                format = " -f mp4 -movflags frag_keyframe+empty_moov+delay_moov";
            }

            var threads = GetNumberOfThreads(state, encodingOptions, videoCodec);

            var inputModifier = GetInputModifier(state, encodingOptions, null);

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}{2} {3} {4} -map_metadata -1 -map_chapters -1 -threads {5} {6}{7}{8} -y \"{9}\"",
                inputModifier,
                GetInputArgument(state, encodingOptions, null),
                keyFrame,
                GetMapArgs(state),
                GetProgressiveVideoArguments(state, encodingOptions, videoCodec, defaultPreset),
                threads,
                GetProgressiveVideoAudioArguments(state, encodingOptions),
                GetSubtitleEmbedArguments(state),
                format,
                outputPath).Trim();
        }

        public string GetOutputFFlags(EncodingJobInfo state)
        {
            var flags = new List<string>();
            if (state.GenPtsOutput)
            {
                flags.Add("+genpts");
            }

            if (flags.Count > 0)
            {
                return " -fflags " + string.Join(string.Empty, flags);
            }

            return string.Empty;
        }

        public string GetProgressiveVideoArguments(EncodingJobInfo state, EncodingOptions encodingOptions, string videoCodec, EncoderPreset defaultPreset)
        {
            var args = "-codec:v:0 " + videoCodec;

            if (state.BaseRequest.EnableMpegtsM2TsMode)
            {
                args += " -mpegts_m2ts_mode 1";
            }

            if (IsCopyCodec(videoCodec))
            {
                if (state.VideoStream is not null
                    && string.Equals(state.OutputContainer, "ts", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(state.VideoStream.NalLengthSize, "0", StringComparison.OrdinalIgnoreCase))
                {
                    var bitStreamArgs = GetBitStreamArgs(state, MediaStreamType.Video);
                    if (!string.IsNullOrEmpty(bitStreamArgs))
                    {
                        args += " " + bitStreamArgs;
                    }
                }

                if (state.RunTimeTicks.HasValue && state.BaseRequest.CopyTimestamps)
                {
                    args += " -copyts -avoid_negative_ts disabled -start_at_zero";
                }

                if (!state.RunTimeTicks.HasValue)
                {
                    args += " -fflags +genpts";
                }
            }
            else
            {
                var keyFrameArg = string.Format(
                    CultureInfo.InvariantCulture,
                    " -force_key_frames \"expr:gte(t,n_forced*{0})\"",
                    5);

                args += keyFrameArg;

                var hasGraphicalSubs = state.SubtitleStream is not null && !state.SubtitleStream.IsTextSubtitleStream && ShouldEncodeSubtitle(state);

                var hasCopyTs = false;

                // video processing filters.
                var videoProcessParam = GetVideoProcessingFilterParam(state, encodingOptions, videoCodec);

                var negativeMapArgs = GetNegativeMapArgsByFilters(state, videoProcessParam);

                args = negativeMapArgs + args + videoProcessParam;

                hasCopyTs = videoProcessParam.Contains("copyts", StringComparison.OrdinalIgnoreCase);

                if (state.RunTimeTicks.HasValue && state.BaseRequest.CopyTimestamps)
                {
                    if (!hasCopyTs)
                    {
                        args += " -copyts";
                    }

                    args += " -avoid_negative_ts disabled";

                    if (!(state.SubtitleStream is not null && state.SubtitleStream.IsExternal && !state.SubtitleStream.IsTextSubtitleStream))
                    {
                        args += " -start_at_zero";
                    }
                }

                var qualityParam = GetVideoQualityParam(state, videoCodec, encodingOptions, defaultPreset);

                if (!string.IsNullOrEmpty(qualityParam))
                {
                    args += " " + qualityParam.Trim();
                }
            }

            if (!string.IsNullOrEmpty(state.OutputVideoSync))
            {
                args += GetVideoSyncOption(state.OutputVideoSync, _mediaEncoder.EncoderVersion);
            }

            args += GetOutputFFlags(state);

            return args;
        }

        public string GetProgressiveVideoAudioArguments(EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            // If the video doesn't have an audio stream, return a default.
            if (state.AudioStream is null && state.VideoStream is not null)
            {
                return string.Empty;
            }

            // Get the output codec name
            var codec = GetAudioEncoder(state);

            var args = "-codec:a:0 " + codec;

            if (IsCopyCodec(codec))
            {
                return args;
            }

            var channels = state.OutputAudioChannels;

            var useDownMixAlgorithm = state.AudioStream is not null
                                      && DownMixAlgorithmsHelper.AlgorithmFilterStrings.ContainsKey((encodingOptions.DownMixStereoAlgorithm, DownMixAlgorithmsHelper.InferChannelLayout(state.AudioStream)));

            if (channels.HasValue && !useDownMixAlgorithm)
            {
                args += " -ac " + channels.Value;
            }

            var bitrate = state.OutputAudioBitrate;
            if (bitrate.HasValue && !LosslessAudioCodecs.Contains(codec, StringComparison.OrdinalIgnoreCase))
            {
                var vbrParam = GetAudioVbrModeParam(codec, bitrate.Value, channels ?? 2);
                if (encodingOptions.EnableAudioVbr && state.EnableAudioVbrEncoding && vbrParam is not null)
                {
                    args += vbrParam;
                }
                else
                {
                    args += " -ab " + bitrate.Value.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (state.OutputAudioSampleRate.HasValue)
            {
                args += " -ar " + state.OutputAudioSampleRate.Value.ToString(CultureInfo.InvariantCulture);
            }

            args += GetAudioFilterParam(state, encodingOptions);

            return args;
        }

        public string GetProgressiveAudioFullCommandLine(EncodingJobInfo state, EncodingOptions encodingOptions, string outputPath)
        {
            var audioTranscodeParams = new List<string>();

            var bitrate = state.OutputAudioBitrate;
            var channels = state.OutputAudioChannels;
            var outputCodec = state.OutputAudioCodec;

            if (bitrate.HasValue && !LosslessAudioCodecs.Contains(outputCodec, StringComparison.OrdinalIgnoreCase))
            {
                var vbrParam = GetAudioVbrModeParam(GetAudioEncoder(state), bitrate.Value, channels ?? 2);
                if (encodingOptions.EnableAudioVbr && state.EnableAudioVbrEncoding && vbrParam is not null)
                {
                    audioTranscodeParams.Add(vbrParam);
                }
                else
                {
                    audioTranscodeParams.Add("-ab " + bitrate.Value.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (channels.HasValue)
            {
                audioTranscodeParams.Add("-ac " + channels.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrEmpty(outputCodec))
            {
                audioTranscodeParams.Add("-acodec " + GetAudioEncoder(state));
            }

            // The pcm_* encoders emit raw samples that carry no header of their own, so the header
            // has to come from the muxer. Only force the matching raw muxer when the client actually
            // asked for a raw container (added in #10321 for I2S/MCU clients): applying it to every
            // pcm_* codec also strips the RIFF header from a `stream.wav` request, which then serves
            // headerless PCM behind an audio/wav content type.
            var audioEncoder = GetAudioEncoder(state);
            if (audioEncoder.StartsWith("pcm_", StringComparison.Ordinal)
                && string.Equals(state.OutputContainer, "pcm", StringComparison.OrdinalIgnoreCase))
            {
                audioTranscodeParams.Add(string.Concat("-f ", audioEncoder.AsSpan(4)));
            }

            var sampleRate = state.OutputAudioSampleRate;
            if (sampleRate.HasValue)
            {
                var sampleRateValue = sampleRate.Value;
                if (string.Equals(outputCodec, "opus", StringComparison.OrdinalIgnoreCase))
                {
                    // opus only supports specific sampling rates
                    sampleRateValue = sampleRate.Value switch
                    {
                        <= 8000 => 8000,
                        <= 12000 => 12000,
                        <= 16000 => 16000,
                        <= 24000 => 24000,
                        _ => 48000
                    };
                }

                audioTranscodeParams.Add("-ar " + sampleRateValue.ToString(CultureInfo.InvariantCulture));
            }

            // Copy the movflags from GetProgressiveVideoFullCommandLine
            // See #9248 and the associated PR for why this is needed
            if (_mp4ContainerNames.Contains(state.OutputContainer))
            {
                audioTranscodeParams.Add("-movflags empty_moov+delay_moov");
            }

            var threads = GetNumberOfThreads(state, encodingOptions, null);

            var inputModifier = GetInputModifier(state, encodingOptions, null);

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}{7}{8} -threads {2}{3} {4} -id3v2_version 3 -write_id3v1 1{6} -y \"{5}\"",
                inputModifier,
                GetInputArgument(state, encodingOptions, null),
                threads,
                " -vn",
                string.Join(' ', audioTranscodeParams),
                outputPath,
                string.Empty,
                string.Empty,
                string.Empty).Trim();
        }
    }
}
