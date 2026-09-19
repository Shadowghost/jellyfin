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
    /// Works out whether a stream can be handed to the client untouched.
    /// </summary>
    public partial class EncodingHelper
    {
        public bool CanStreamCopyVideo(EncodingJobInfo state, MediaStream videoStream)
        {
            var request = state.BaseRequest;

            if (!request.AllowVideoStreamCopy)
            {
                return false;
            }

            if (videoStream.IsInterlaced
                && state.DeInterlace(videoStream.Codec, false))
            {
                return false;
            }

            if (videoStream.IsAnamorphic ?? false)
            {
                if (request.RequireNonAnamorphic)
                {
                    return false;
                }
            }

            // Can't stream copy if we're burning in subtitles
            if (request.SubtitleStreamIndex.HasValue
                && request.SubtitleStreamIndex.Value >= 0
                && state.SubtitleDeliveryMethod == SubtitleDeliveryMethod.Encode)
            {
                return false;
            }

            if (string.Equals("h264", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                && videoStream.IsAVC.HasValue
                && !videoStream.IsAVC.Value
                && request.RequireAvc)
            {
                return false;
            }

            // Source and target codecs must match
            if (string.IsNullOrEmpty(videoStream.Codec)
                || (state.SupportedVideoCodecs.Length != 0
                    && !state.SupportedVideoCodecs.Contains(videoStream.Codec, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var requestedProfiles = state.GetRequestedProfiles(videoStream.Codec);

            // If client is requesting a specific video profile, it must match the source
            if (requestedProfiles.Length > 0)
            {
                if (string.IsNullOrEmpty(videoStream.Profile))
                {
                    // return false;
                }

                var requestedProfile = requestedProfiles[0];
                // strip spaces because they may be stripped out on the query string as well
                if (!string.IsNullOrEmpty(videoStream.Profile)
                    && !requestedProfiles.Contains(videoStream.Profile.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
                {
                    var currentScore = GetVideoProfileScore(videoStream.Codec, videoStream.Profile);
                    var requestedScore = GetVideoProfileScore(videoStream.Codec, requestedProfile);

                    if (currentScore == -1 || currentScore > requestedScore)
                    {
                        return false;
                    }
                }
            }

            var requestedRangeTypes = state.GetRequestedRangeTypes(videoStream.Codec);
            if (requestedRangeTypes.Length > 0)
            {
                if (videoStream.VideoRangeType == VideoRangeType.Unknown)
                {
                    return false;
                }

                // DOVIWithHDR10 should be compatible with HDR10 supporting players. Same goes with HLG and of course SDR. So allow copy of those formats
                var requestHasHDR10 = requestedRangeTypes.Contains(VideoRangeType.HDR10.ToString(), StringComparison.OrdinalIgnoreCase);
                var requestHasHLG = requestedRangeTypes.Contains(VideoRangeType.HLG.ToString(), StringComparison.OrdinalIgnoreCase);
                var requestHasSDR = requestedRangeTypes.Contains(VideoRangeType.SDR.ToString(), StringComparison.OrdinalIgnoreCase);
                var requestHasDOVI = requestedRangeTypes.Contains(VideoRangeType.DOVI.ToString(), StringComparison.OrdinalIgnoreCase);

                // If SDR is the only supported range, we should not copy any of the HDR streams.
                // All the following copy check assumes at least one HDR format is supported.
                if (requestedRangeTypes.Length == 1 && requestHasSDR && videoStream.VideoRangeType != VideoRangeType.SDR)
                {
                    return false;
                }

                // If the client does not support DOVI and the video stream is DOVI without fallback, we should not copy it.
                if (!requestHasDOVI && videoStream.VideoRangeType == VideoRangeType.DOVI)
                {
                    return false;
                }

                if (!requestedRangeTypes.Contains(videoStream.VideoRangeType.ToString(), StringComparison.OrdinalIgnoreCase)
                     && !((requestHasHDR10 && videoStream.VideoRangeType == VideoRangeType.DOVIWithHDR10)
                            || (requestHasHLG && videoStream.VideoRangeType == VideoRangeType.DOVIWithHLG)
                            || (requestHasSDR && videoStream.VideoRangeType == VideoRangeType.DOVIWithSDR)
                            || (requestHasHDR10 && videoStream.VideoRangeType == VideoRangeType.HDR10Plus)))
                {
                    // If the video stream is in HDR10+ or a static HDR format, don't allow copy if the client does not support HDR10 or HLG.
                    if (videoStream.VideoRangeType is VideoRangeType.HDR10Plus or VideoRangeType.HDR10 or VideoRangeType.HLG)
                    {
                        return false;
                    }

                    // Check complicated cases where we need to remove dynamic metadata
                    // Conservatively refuse to copy if the encoder can't remove dynamic metadata,
                    // but a removal is required for compatability reasons.
                    var dynamicHdrMetadataRemovalPlan = ShouldRemoveDynamicHdrMetadata(state);
                    if (!CanEncoderRemoveDynamicHdrMetadata(dynamicHdrMetadataRemovalPlan, videoStream))
                    {
                        return false;
                    }
                }
            }

            var requestedRotations = state.GetRequestedRotations(videoStream.Codec);
            if (requestedRotations.Length > 0)
            {
                var (rotation, _) = GetChainRotation(state);
                if (rotation != 0
                    && !requestedRotations.Contains(rotation.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                {
                    return false;
                }
            }

            // Video width must fall within requested value
            if (request.MaxWidth.HasValue
                && (!videoStream.Width.HasValue || videoStream.Width.Value > request.MaxWidth.Value))
            {
                return false;
            }

            // Video height must fall within requested value
            if (request.MaxHeight.HasValue
                && (!videoStream.Height.HasValue || videoStream.Height.Value > request.MaxHeight.Value))
            {
                return false;
            }

            // Video framerate must fall within requested value
            var requestedFramerate = request.MaxFramerate ?? request.Framerate;
            if (requestedFramerate.HasValue)
            {
                var videoFrameRate = videoStream.ReferenceFrameRate;

                // Add a little tolerance to the framerate check because some videos might record a framerate
                // that is slightly greater than the intended framerate, but the device can still play it correctly.
                // 0.05 fps tolerance should be safe enough.
                if (!videoFrameRate.HasValue || videoFrameRate.Value > requestedFramerate.Value + 0.05f)
                {
                    return false;
                }
            }

            // Video bitrate must fall within requested value
            if (request.VideoBitRate.HasValue
                && (!videoStream.BitRate.HasValue || videoStream.BitRate.Value > request.VideoBitRate.Value))
            {
                // For LiveTV that has no bitrate, let's try copy if other conditions are met
                if (string.IsNullOrWhiteSpace(request.LiveStreamId) || videoStream.BitRate.HasValue)
                {
                    return false;
                }
            }

            var maxBitDepth = state.GetRequestedVideoBitDepth(videoStream.Codec);
            if (maxBitDepth.HasValue)
            {
                if (videoStream.BitDepth.HasValue && videoStream.BitDepth.Value > maxBitDepth.Value)
                {
                    return false;
                }
            }

            var maxRefFrames = state.GetRequestedMaxRefFrames(videoStream.Codec);
            if (maxRefFrames.HasValue
                && videoStream.RefFrames.HasValue && videoStream.RefFrames.Value > maxRefFrames.Value)
            {
                return false;
            }

            // If a specific level was requested, the source must match or be less than
            var level = state.GetRequestedLevel(videoStream.Codec);
            if (double.TryParse(level, CultureInfo.InvariantCulture, out var requestLevel))
            {
                if (!videoStream.Level.HasValue)
                {
                    // return false;
                }

                if (videoStream.Level.HasValue && videoStream.Level.Value > requestLevel)
                {
                    return false;
                }
            }

            if (string.Equals(state.InputContainer, "avi", StringComparison.OrdinalIgnoreCase)
                && string.Equals(videoStream.Codec, "h264", StringComparison.OrdinalIgnoreCase)
                && !(videoStream.IsAVC ?? false))
            {
                // see Coach S01E01 - Kelly and the Professor(0).avi
                return false;
            }

            return true;
        }

        public bool CanStreamCopyAudio(EncodingJobInfo state, MediaStream audioStream, IEnumerable<string> supportedAudioCodecs)
            => CanStreamCopyAudio(state, audioStream, supportedAudioCodecs, out _);

        /// <summary>
        /// Determines whether the given audio stream can be stream-copied and, regardless of the outcome,
        /// reports the codec/parameter incompatibilities that would force a re-encode via <paramref name="failureReasons"/>.
        /// </summary>
        /// <param name="state">The encoding job state.</param>
        /// <param name="audioStream">The source audio stream.</param>
        /// <param name="supportedAudioCodecs">The audio codecs the target supports.</param>
        /// <param name="failureReasons">The codec/parameter incompatibilities preventing a copy, or <c>0</c> if the stream is copy-compatible.</param>
        /// <returns><c>true</c> if the audio stream can be stream-copied; otherwise, <c>false</c>.</returns>
        public bool CanStreamCopyAudio(EncodingJobInfo state, MediaStream audioStream, IEnumerable<string> supportedAudioCodecs, out TranscodeReason failureReasons)
        {
            var request = state.BaseRequest;

            // Policy-independent compatibility check, so the reasons are reported even when a policy gate is what ultimately prevents the copy.
            failureReasons = GetAudioStreamCopyFailureReasons(state, audioStream, supportedAudioCodecs);

            return request.AllowAudioStreamCopy
                && request.EnableAutoStreamCopy
                && failureReasons == 0;
        }

        public void TryStreamCopy(EncodingJobInfo state, EncodingOptions options)
        {
            if (state.VideoStream is not null && CanStreamCopyVideo(state, state.VideoStream))
            {
                state.OutputVideoCodec = "copy";
            }
            else
            {
                var user = state.User;

                // If the user doesn't have access to transcoding, then force stream copy, regardless of whether it will be compatible or not
                if (user is not null && !user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding))
                {
                    state.OutputVideoCodec = "copy";
                }
            }

            var preventHlsAudioCopy = state.TranscodingType is TranscodingJobType.Hls
                && state.VideoStream is not null
                && !IsCopyCodec(state.OutputVideoCodec)
                && options.HlsAudioSeekStrategy is HlsAudioSeekStrategy.TranscodeAudio;

            TranscodeReason audioCopyFailureReasons = 0;
            if (state.AudioStream is not null
                && CanStreamCopyAudio(state, state.AudioStream, state.SupportedAudioCodecs, out audioCopyFailureReasons)
                && !preventHlsAudioCopy)
            {
                state.OutputAudioCodec = "copy";
            }
            else
            {
                var user = state.User;

                // If the user doesn't have access to transcoding, then force stream copy, regardless of whether it will be compatible or not
                if (user is not null && !user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding))
                {
                    state.OutputAudioCodec = "copy";
                }
                else if (state.AudioStream is not null && !IsCopyCodec(state.OutputAudioCodec))
                {
                    // Audio is actually being re-encoded although the playback determination may have considered the source copyable.
                    // Only carry the primary "cannot be passed through" cause - the codec mismatch.
                    // Bitrate/channels/sample-rate/bit-depth copy refusals are consequences of the chosen transcode target.
                    state.AddTranscodeReason(audioCopyFailureReasons & TranscodeReason.AudioCodecNotSupported);
                }
            }
        }
    }
}
