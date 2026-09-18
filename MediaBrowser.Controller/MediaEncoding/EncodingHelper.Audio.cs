#nullable disable

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
    /// Works out the audio stream: which encoder, how many channels, what bitrate and which filters.
    /// </summary>
    public partial class EncodingHelper
    {
        /// <summary>
        /// Infers the audio codec based on the url.
        /// </summary>
        /// <param name="container">Container to use.</param>
        /// <returns>Codec string.</returns>
        public string InferAudioCodec(string container)
        {
            if (string.IsNullOrWhiteSpace(container))
            {
                // this may not work, but if the client is that broken we cannot do anything better
                return "aac";
            }

            var inferredCodec = container.ToLowerInvariant();

            return inferredCodec switch
            {
                "ogg" or "oga" or "ogv" or "webm" or "webma" => "opus",
                "m4a" or "m4b" or "mp4" or "mov" or "mkv" or "mka" => "aac",
                "ts" or "avi" or "flv" or "f4v" or "swf" => "mp3",
                // Containers that share their name with the codec they carry.
                "aac" or "ac3" or "alac" or "dts" or "eac3" or "flac" or "mp2" or "mp3" or "opus" or "truehd" or "vorbis" => inferredCodec,
                // Anything else - manifests such as m3u8/mpd in particular - names a container that
                // is not an audio codec. Never hand that name to ffmpeg as an encoder.
                _ => "aac"
            };
        }

        /// <summary>
        /// Gets the audio encoder.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        public string GetAudioEncoder(EncodingJobInfo state)
        {
            var codec = state.OutputAudioCodec;

            if (!ContainerValidationRegex().IsMatch(codec))
            {
                codec = "aac";
            }

            if (string.Equals(codec, "aac", StringComparison.OrdinalIgnoreCase))
            {
                // Use Apple's aac encoder if available as it provides best audio quality
                if (_mediaEncoder.SupportsEncoder("aac_at"))
                {
                    return "aac_at";
                }

                // Use libfdk_aac for better audio quality if using custom build of FFmpeg which has fdk_aac support
                if (_mediaEncoder.SupportsEncoder("libfdk_aac"))
                {
                    return "libfdk_aac";
                }

                return "aac";
            }

            if (string.Equals(codec, "mp3", StringComparison.OrdinalIgnoreCase))
            {
                return "libmp3lame";
            }

            if (string.Equals(codec, "vorbis", StringComparison.OrdinalIgnoreCase))
            {
                return "libvorbis";
            }

            if (string.Equals(codec, "opus", StringComparison.OrdinalIgnoreCase))
            {
                return "libopus";
            }

            if (string.Equals(codec, "flac", StringComparison.OrdinalIgnoreCase))
            {
                return "flac";
            }

            if (string.Equals(codec, "dts", StringComparison.OrdinalIgnoreCase))
            {
                return "dca";
            }

            if (string.Equals(codec, "alac", StringComparison.OrdinalIgnoreCase))
            {
                // The ffmpeg upstream breaks the AudioToolbox ALAC encoder in version 6.1 but fixes it in version 7.0.
                // Since ALAC is lossless in quality and the AudioToolbox encoder is not faster,
                // its only benefit is a smaller file size.
                // To prevent problems, use the ffmpeg native encoder instead.
                return "alac";
            }

            return codec.ToLowerInvariant();
        }

        public string GetAudioBitStreamArguments(EncodingJobInfo state, string segmentContainer, string mediaSourceContainer)
        {
            var filters = new List<string>();

            var noiseFilter = GetCopiedAudioTrimBsf(state);
            if (!string.IsNullOrEmpty(noiseFilter))
            {
                filters.Add(noiseFilter);
            }

            var segmentFormat = GetSegmentFileExtension(segmentContainer).TrimStart('.');

            // Apply aac_adtstoasc bitstream filter when media source is in mpegts.
            if (string.Equals(segmentFormat, "mp4", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(mediaSourceContainer, "ts", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mediaSourceContainer, "aac", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mediaSourceContainer, "hls", StringComparison.OrdinalIgnoreCase))
                && IsAAC(state.AudioStream))
            {
                filters.Add("aac_adtstoasc");
            }

            return filters.Count == 0
                ? string.Empty
                : " -bsf:a " + string.Join(',', filters);
        }

        private string GetCopiedAudioTrimBsf(EncodingJobInfo state)
        {
            if (state.TranscodingType is not TranscodingJobType.Hls
                || !state.IsVideoRequest
                || IsCopyCodec(state.OutputVideoCodec)
                || !IsCopyCodec(state.OutputAudioCodec)
                || string.Equals(state.InputContainer, "wtv", StringComparison.OrdinalIgnoreCase)
                || _mediaEncoder.EncoderVersion < _minFFmpegNoiseBsfDrop)
            {
                return null;
            }

            var startTicks = state.BaseRequest.StartTimeTicks ?? 0;
            if (startTicks <= 0)
            {
                return null;
            }

            var seekSeconds = startTicks / (double)TimeSpan.TicksPerSecond;
            return string.Format(
                CultureInfo.InvariantCulture,
                "noise=drop='lt(pts*tb\\,{0:F3})'",
                seekSeconds);
        }

        private static TranscodeReason GetAudioStreamCopyFailureReasons(EncodingJobInfo state, MediaStream audioStream, IEnumerable<string> supportedAudioCodecs)
        {
            var request = state.BaseRequest;
            TranscodeReason reasons = 0;

            var maxBitDepth = state.GetRequestedAudioBitDepth(audioStream.Codec);
            if (maxBitDepth.HasValue
                && audioStream.BitDepth.HasValue
                && audioStream.BitDepth.Value > maxBitDepth.Value)
            {
                reasons |= TranscodeReason.AudioBitDepthNotSupported;
            }

            // Source and target codecs must match
            if (string.IsNullOrEmpty(audioStream.Codec)
                || !supportedAudioCodecs.Contains(audioStream.Codec, StringComparison.OrdinalIgnoreCase))
            {
                reasons |= TranscodeReason.AudioCodecNotSupported;
            }

            // Channels must fall within requested value
            var channels = state.GetRequestedAudioChannels(audioStream.Codec);
            if (channels.HasValue
                && (!audioStream.Channels.HasValue
                    || audioStream.Channels.Value <= 0
                    || audioStream.Channels.Value > channels.Value))
            {
                reasons |= TranscodeReason.AudioChannelsNotSupported;
            }

            // Sample rate must fall within requested value
            if (request.AudioSampleRate.HasValue
                && (!audioStream.SampleRate.HasValue
                    || audioStream.SampleRate.Value <= 0
                    || audioStream.SampleRate.Value > request.AudioSampleRate.Value))
            {
                reasons |= TranscodeReason.AudioSampleRateNotSupported;
            }

            // Audio bitrate must fall within requested value
            if (request.AudioBitRate.HasValue
                && audioStream.BitRate.HasValue
                && audioStream.BitRate.Value > request.AudioBitRate.Value)
            {
                reasons |= TranscodeReason.AudioBitrateNotSupported;
            }

            return reasons;
        }

        public int? GetAudioBitrateParam(BaseEncodingJobOptions request, MediaStream audioStream, int? outputAudioChannels)
        {
            return GetAudioBitrateParam(request.AudioBitRate, request.AudioCodec, audioStream, outputAudioChannels);
        }

        public int? GetAudioBitrateParam(int? audioBitRate, string audioCodec, MediaStream audioStream, int? outputAudioChannels)
        {
            if (audioStream is null)
            {
                return null;
            }

            var inputChannels = audioStream.Channels ?? 0;
            var outputChannels = outputAudioChannels ?? 0;
            var bitrate = audioBitRate ?? int.MaxValue;

            if (string.IsNullOrEmpty(audioCodec)
                || string.Equals(audioCodec, "aac", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "mp3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "opus", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "vorbis", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "ac3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "eac3", StringComparison.OrdinalIgnoreCase))
            {
#pragma warning disable SA1008
                return (inputChannels, outputChannels) switch
                {
                    ( >= 6, >= 6 or 0) => Math.Min(640000, bitrate),
                    ( > 0, > 0) => Math.Min(outputChannels * 128000, bitrate),
                    ( > 0, _) => Math.Min(inputChannels * 128000, bitrate),
                    (_, _) => Math.Min(384000, bitrate)
                };
#pragma warning restore SA1008
            }

            if (string.Equals(audioCodec, "dts", StringComparison.OrdinalIgnoreCase)
                || string.Equals(audioCodec, "dca", StringComparison.OrdinalIgnoreCase))
            {
#pragma warning disable SA1008
                return (inputChannels, outputChannels) switch
                {
                    ( >= 6, >= 6 or 0) => Math.Min(768000, bitrate),
                    ( > 0, > 0) => Math.Min(outputChannels * 136000, bitrate),
                    ( > 0, _) => Math.Min(inputChannels * 136000, bitrate),
                    (_, _) => Math.Min(672000, bitrate)
                };
#pragma warning restore SA1008
            }

            // Empty bitrate area is not allow on iOS
            // Default audio bitrate to 128K per channel if we don't have codec specific defaults
            // https://ffmpeg.org/ffmpeg-codecs.html#toc-Codec-Options
            return 128000 * (outputAudioChannels ?? audioStream.Channels ?? 2);
        }

        public string GetAudioVbrModeParam(string encoder, int bitrate, int channels)
        {
            var bitratePerChannel = bitrate / Math.Max(channels, 1);
            if (string.Equals(encoder, "libfdk_aac", StringComparison.OrdinalIgnoreCase))
            {
                return " -vbr:a " + bitratePerChannel switch
                {
                    < 32000 => "1",
                    < 48000 => "2",
                    < 64000 => "3",
                    < 96000 => "4",
                    _ => "5"
                };
            }

            if (string.Equals(encoder, "libmp3lame", StringComparison.OrdinalIgnoreCase))
            {
                // lame's VBR is only good for a certain bitrate range
                // For very low and very high bitrate, use abr mode
                if (bitratePerChannel is < 122500 and > 48000)
                {
                    return " -qscale:a " + bitratePerChannel switch
                    {
                        < 64000 => "6",
                        < 88000 => "4",
                        < 112000 => "2",
                        _ => "0"
                    };
                }

                return " -abr:a 1" + " -b:a " + bitrate;
            }

            if (string.Equals(encoder, "aac_at", StringComparison.OrdinalIgnoreCase))
            {
                // aac_at's CVBR mode
                return " -aac_at_mode:a 2" + " -b:a " + bitrate;
            }

            if (string.Equals(encoder, "libvorbis", StringComparison.OrdinalIgnoreCase))
            {
                return " -qscale:a " + bitratePerChannel switch
                {
                    < 40000 => "0",
                    < 56000 => "2",
                    < 80000 => "4",
                    < 112000 => "6",
                    _ => "8"
                };
            }

            return null;
        }

        public string GetAudioFilterParam(EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            var channels = state.OutputAudioChannels;

            var filters = new List<string>();

            if (channels is 2 && state.AudioStream?.Channels is > 2)
            {
                var hasDownMixFilter = DownMixAlgorithmsHelper.AlgorithmFilterStrings.TryGetValue((encodingOptions.DownMixStereoAlgorithm, DownMixAlgorithmsHelper.InferChannelLayout(state.AudioStream)), out var downMixFilterString);
                if (hasDownMixFilter)
                {
                    filters.Add(downMixFilterString);
                }

                if (!encodingOptions.DownMixAudioBoost.Equals(1))
                {
                    filters.Add("volume=" + encodingOptions.DownMixAudioBoost.ToString(CultureInfo.InvariantCulture));
                }
            }

            var isCopyingTimestamps = state.CopyTimestamps || state.TranscodingType != TranscodingJobType.Progressive;
            if (state.SubtitleStream is not null && state.SubtitleStream.IsTextSubtitleStream && ShouldEncodeSubtitle(state) && !isCopyingTimestamps)
            {
                var seconds = TimeSpan.FromTicks(state.StartTimeTicks ?? 0).TotalSeconds;

                filters.Add(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "asetpts=PTS-{0}/TB",
                        Math.Round(seconds)));
            }

            if (filters.Count > 0)
            {
                return " -af \"" + string.Join(',', filters) + "\"";
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets the number of audio channels to specify on the command line.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="audioStream">The audio stream.</param>
        /// <param name="outputAudioCodec">The output audio codec.</param>
        /// <returns>System.Nullable{System.Int32}.</returns>
        public int? GetNumAudioChannelsParam(EncodingJobInfo state, MediaStream audioStream, string outputAudioCodec)
        {
            if (audioStream is null)
            {
                return null;
            }

            var request = state.BaseRequest;

            var codec = outputAudioCodec ?? string.Empty;

            int? resultChannels = state.GetRequestedAudioChannels(codec);

            var inputChannels = audioStream.Channels;

            if (inputChannels > 0)
            {
                resultChannels = inputChannels < resultChannels ? inputChannels : resultChannels ?? inputChannels;
            }

            var isTranscodingAudio = !IsCopyCodec(codec);

            if (isTranscodingAudio)
            {
                var audioEncoder = GetAudioEncoder(state);
                if (!_audioTranscodeChannelLookup.TryGetValue(audioEncoder, out var transcoderChannelLimit))
                {
                    // Set default max transcoding channels to 8 to prevent encoding errors due to asking for too many channels.
                    transcoderChannelLimit = 8;
                }

                // Set resultChannels to minimum between resultChannels, TranscodingMaxAudioChannels, transcoderChannelLimit
                resultChannels = transcoderChannelLimit < resultChannels ? transcoderChannelLimit : resultChannels ?? transcoderChannelLimit;

                if (request.TranscodingMaxAudioChannels < resultChannels)
                {
                    resultChannels = request.TranscodingMaxAudioChannels;
                }

                // Avoid transcoding to audio channels other than 1ch, 2ch, 6ch (5.1 layout) and 8ch (7.1 layout).
                // https://developer.apple.com/documentation/http_live_streaming/hls_authoring_specification_for_apple_devices
                if (state.TranscodingType != TranscodingJobType.Progressive
                    && ((resultChannels > 2 && resultChannels < 6) || resultChannels == 7))
                {
                    // We can let FFMpeg supply an extra LFE channel for 5ch and 7ch to make them 5.1 and 7.1
                    if (resultChannels == 5)
                    {
                        resultChannels = 6;
                    }
                    else if (resultChannels == 7)
                    {
                        resultChannels = 8;
                    }
                    else
                    {
                        // For other weird layout, just downmix to stereo for compatibility
                        resultChannels = 2;
                    }
                }
            }

            return resultChannels;
        }
    }
}
