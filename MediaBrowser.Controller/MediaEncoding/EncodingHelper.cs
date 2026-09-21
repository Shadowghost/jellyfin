#nullable enable

#pragma warning disable CS1591
// We need lowercase normalized string for ffmpeg
#pragma warning disable CA1308

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using Jellyfin.MediaEncoding.Pipeline.BitStreams;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.IO;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaEncoding.Hardware;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Configuration;
using IConfigurationManager = MediaBrowser.Common.Configuration.IConfigurationManager;

namespace MediaBrowser.Controller.MediaEncoding
{
    public partial class EncodingHelper
    {
        /// <summary>
        /// The codec validation regex string.
        /// This regular expression matches strings that consist of alphanumeric characters, hyphens,
        /// periods, underscores, commas, and vertical bars, with a length between 0 and 40 characters.
        /// This should matches all common valid codecs.
        /// </summary>
        public const string ContainerValidationRegexStr = @"^[a-zA-Z0-9\-\._,|]{0,40}$";

        /// <summary>
        /// The level validation regex string.
        /// This regular expression matches strings representing a double.
        /// </summary>
        public const string LevelValidationRegexStr = @"-?[0-9]+(?:\.[0-9]+)?";

        private const string _defaultMjpegEncoder = "mjpeg";

        private const string QsvAlias = "qs";
        private const string VaapiAlias = "va";
        private const string D3d11vaAlias = "dx11";
        private const string VideotoolboxAlias = "vt";
        private const string RkmppAlias = "rk";
        private const string OpenclAlias = "ocl";
        private const string CudaAlias = "cu";
        private const string DrmAlias = "dr";
        private const string VulkanAlias = "vk";
        private readonly IApplicationPaths _appPaths;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly ISubtitleEncoder _subtitleEncoder;
        private readonly IConfiguration _config;
        private readonly IConfigurationManager _configurationManager;
        private readonly IHardwareCapabilitiesProvider _hardwareCapabilities;
        private readonly IPathManager _pathManager;

        // i915 hang was fixed by linux 6.2 (3f882f2)
        private readonly Version _minKerneli915Hang = new Version(5, 18);
        private readonly Version _maxKerneli915Hang = new Version(6, 1, 3);
        private readonly Version _minFixedKernel60i915Hang = new Version(6, 0, 18);
        private readonly Version _minKernelVersionAmdVkFmtModifier = new Version(5, 15);

        private readonly Version _minFFmpegImplicitHwaccel = new Version(6, 0);
        private readonly Version _minFFmpegHwaUnsafeOutput = new Version(6, 0);
        private readonly Version _minFFmpegOclCuTonemapMode = new Version(5, 1, 3);
        private readonly Version _minFFmpegSvtAv1Params = new Version(5, 1);
        private readonly Version _minFFmpegVaapiH26xEncA53CcSei = new Version(6, 0);
        private readonly Version _minFFmpegReadrateOption = new Version(5, 0);
        private readonly Version _minFFmpegWorkingVtHwSurface = new Version(7, 0, 1);
        private readonly Version _minFFmpegDisplayRotationOption = new Version(6, 0);
        private readonly Version _minFFmpegAdvancedTonemapMode = new Version(7, 0, 1);
        private readonly Version _minFFmpegAlteredVaVkInterop = new Version(7, 0, 1);
        private readonly Version _minFFmpegQsvVppTonemapOption = new Version(7, 0, 1);
        private readonly Version _minFFmpegQsvVppOutRangeOption = new Version(7, 0, 1);
        private readonly Version _minFFmpegVaapiDeviceVendorId = new Version(7, 0, 1);
        private readonly Version _minFFmpegQsvVppScaleModeOption = new Version(6, 0);
        private readonly Version _minFFmpegRkmppHevcDecDoviRpu = new Version(7, 1, 1);
        private readonly Version _minFFmpegReadrateCatchupOption = new Version(8, 0);
        private readonly Version _minFFmpegNoiseBsfDrop = new Version(5, 0);
        private readonly Version _minFFmpegHwCrop = new Version(8, 0);

        private static readonly string[] _swFormats8Bit = ["yuv420p", "yuvj420p"];

        private static readonly string[] _swFormats8To10Bit = ["yuv420p", "yuvj420p", "yuv420p10le"];

        // 4:2:2 and 4:4:4 at 8, 10 and 12 bits, the widest set any of our decoders claims.
        private static readonly string[] _swFormats8To12Bit =
        [
            "yuv420p", "yuvj420p", "yuv420p10le",
            "yuv422p", "yuv444p",
            "yuv422p10le", "yuv444p10le",
            "yuv420p12le", "yuv422p12le", "yuv444p12le"
        ];

        private static readonly string[] _videoProfilesH264 =
        [
            "ConstrainedBaseline",
            "Baseline",
            "Extended",
            "Main",
            "High",
            "ProgressiveHigh",
            "ConstrainedHigh",
            "High10"
        ];

        private static readonly string[] _videoProfilesH265 =
        [
            "Main",
            "Main10"
        ];

        private static readonly string[] _videoProfilesAv1 =
        [
            "Main",
            "High",
            "Professional",
        ];

        private static readonly HashSet<string> _mp4ContainerNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "mp4",
            "m4a",
            "m4p",
            "m4b",
            "m4r",
            "m4v",
        };

        private static readonly TonemappingMode[] _legacyTonemapModes = [TonemappingMode.max, TonemappingMode.rgb];
        private static readonly TonemappingMode[] _advancedTonemapModes = [TonemappingMode.lum, TonemappingMode.itp];

        // Set max transcoding channels for encoders that can't handle more than a set amount of channels
        // AAC, FLAC, ALAC, libopus, libvorbis encoders all support at least 8 channels
        private static readonly Dictionary<string, int> _audioTranscodeChannelLookup = new(StringComparer.OrdinalIgnoreCase)
        {
            { "libmp3lame", 2 },
            { "libfdk_aac", 6 },
            { "ac3", 6 },
            { "eac3", 6 },
            { "dca", 6 },
            { "mlp", 6 },
            { "truehd", 6 },
        };

        private static readonly Dictionary<HardwareAccelerationType, string> _mjpegCodecMap = new()
        {
            { HardwareAccelerationType.vaapi, _defaultMjpegEncoder + "_vaapi" },
            { HardwareAccelerationType.qsv, _defaultMjpegEncoder + "_qsv" },
            { HardwareAccelerationType.videotoolbox, _defaultMjpegEncoder + "_videotoolbox" },
            { HardwareAccelerationType.rkmpp, _defaultMjpegEncoder + "_rkmpp" }
        };

        public static readonly string[] LosslessAudioCodecs =
        [
            "alac",
            "ape",
            "flac",
            "mlp",
            "truehd",
            "wavpack"
        ];

        public EncodingHelper(
            IApplicationPaths appPaths,
            IMediaEncoder mediaEncoder,
            ISubtitleEncoder subtitleEncoder,
            IConfiguration config,
            IConfigurationManager configurationManager,
            IPathManager pathManager,
            IHardwareCapabilitiesProvider hardwareCapabilities)
        {
            _appPaths = appPaths;
            _mediaEncoder = mediaEncoder;
            _subtitleEncoder = subtitleEncoder;
            _config = config;
            _configurationManager = configurationManager;
            _pathManager = pathManager;
            _hardwareCapabilities = hardwareCapabilities;
        }

        private enum DynamicHdrMetadataRemovalPlan
        {
            None,
            RemoveDovi,
            RemoveHdr10Plus,
        }

        /// <summary>
        /// The codec validation regex.
        /// This regular expression matches strings that consist of alphanumeric characters, hyphens,
        /// periods, underscores, commas, and vertical bars, with a length between 0 and 40 characters.
        /// This should matches all common valid codecs.
        /// </summary>
        [GeneratedRegex(ContainerValidationRegexStr)]
        public static partial Regex ContainerValidationRegex();

        /// <summary>
        /// The level validation regex string.
        /// This regular expression matches strings representing a double.
        /// </summary>
        [GeneratedRegex(LevelValidationRegexStr)]
        public static partial Regex LevelValidationRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhiteSpaceRegex();

        public string GetH264Encoder(EncodingJobInfo state, EncodingOptions encodingOptions)
            => GetH26xOrAv1Encoder("libx264", "h264", state, encodingOptions);

        public string GetH265Encoder(EncodingJobInfo state, EncodingOptions encodingOptions)
            => GetH26xOrAv1Encoder("libx265", "hevc", state, encodingOptions);

        public string GetAv1Encoder(EncodingJobInfo state, EncodingOptions encodingOptions)
            => GetH26xOrAv1Encoder("libsvtav1", "av1", state, encodingOptions);

        private string GetH26xOrAv1Encoder(string defaultEncoder, string hwEncoder, EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            // Only use alternative encoders for video files.
            // When using concat with folder rips, if the mfx session fails to initialize, ffmpeg will be stuck retrying and will not exit gracefully
            // Since transcoding of folder rips is experimental anyway, it's not worth adding additional variables such as this.
            if (state.VideoType == VideoType.VideoFile && !state.HardwareAccelerationDisabled)
            {
                var hwType = encodingOptions.HardwareAccelerationType;

                var codecMap = new Dictionary<HardwareAccelerationType, string>()
                {
                    { HardwareAccelerationType.amf,                  hwEncoder + "_amf" },
                    { HardwareAccelerationType.nvenc,                hwEncoder + "_nvenc" },
                    { HardwareAccelerationType.qsv,                  hwEncoder + "_qsv" },
                    { HardwareAccelerationType.vaapi,                hwEncoder + "_vaapi" },
                    { HardwareAccelerationType.videotoolbox,         hwEncoder + "_videotoolbox" },
                    { HardwareAccelerationType.v4l2m2m,              hwEncoder + "_v4l2m2m" },
                    { HardwareAccelerationType.rkmpp,                hwEncoder + "_rkmpp" },
                };

                if (hwType != HardwareAccelerationType.none
                    && encodingOptions.EnableHardwareEncoding
                    && codecMap.TryGetValue(hwType, out var preferredEncoder)
                    && _mediaEncoder.SupportsEncoder(preferredEncoder)
                    && CanDeviceEncode(state, encodingOptions, hwEncoder))
                {
                    return preferredEncoder;
                }
            }

            return defaultEncoder;
        }

        private string GetMjpegEncoder(EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            if (state.VideoType == VideoType.VideoFile)
            {
                var hwType = encodingOptions.HardwareAccelerationType;

                // Only enable VA-API MJPEG encoder on Intel iHD driver.
                // Legacy platforms supported ONLY by i965 do not support MJPEG encoder.
                if (hwType == HardwareAccelerationType.vaapi
                    && !_mediaEncoder.IsVaapiDeviceInteliHD)
                {
                    return _defaultMjpegEncoder;
                }

                if (hwType != HardwareAccelerationType.none
                    && encodingOptions.EnableHardwareEncoding
                    && _mjpegCodecMap.TryGetValue(hwType, out var preferredEncoder)
                    && _mediaEncoder.SupportsEncoder(preferredEncoder)
                    && CanDeviceEncode(state, encodingOptions, "mjpeg"))
                {
                    return preferredEncoder;
                }
            }

            return _defaultMjpegEncoder;
        }

        /// <summary>
        /// Whether the configured device reports an encoder that can take the output of this job.
        /// </summary>
        /// <remarks>
        /// The filter chain hands the encoder an 8 bit device surface, so that is the format the
        /// reported limits are read against. A device that reported no encoders at all says nothing,
        /// and the hand maintained rules keep their say.
        /// </remarks>
        /// <param name="state">The encoding job info.</param>
        /// <param name="options">The encoding options.</param>
        /// <param name="codec">The codec the job encodes to.</param>
        /// <returns><c>true</c> if the device can encode the output, <c>false</c> otherwise.</returns>
        private bool CanDeviceEncode(EncodingJobInfo state, EncodingOptions options, string codec)
            => _hardwareCapabilities.CanEncode(
                options.HardwareAccelerationType,
                options,
                codec,
                state.OutputWidth ?? 0,
                state.OutputHeight ?? 0,
                GetHwSurfaceFormat(8),
                state.TargetVideoProfile);

        private EncodingOptions GetConfiguredEncodingOptions() => _configurationManager.GetEncodingOptions();

        /// <summary>
        /// Whether the configured device reports the given video processing operation.
        /// </summary>
        /// <remarks>
        /// Devices that report no video processing at all, such as cuda and videotoolbox, always answer yes;
        /// only the filter availability of the ffmpeg build can rule those out.
        /// </remarks>
        private bool CanDeviceFilter(
            HardwareAccelerationType type,
            HwVppKind kind,
            int width = 0,
            int height = 0,
            string? pixelFormat = null)
            => _hardwareCapabilities.CanFilter(type, GetConfiguredEncodingOptions(), kind, width, height, pixelFormat);

        /// <summary>
        /// Whether the capability settings are answered from the detected hardware rather than the configuration.
        /// </summary>
        /// <param name="options">The encoding options.</param>
        /// <returns><c>true</c> if the detected hardware decides, <c>false</c> otherwise.</returns>
        private bool IsHwTuningAutomatic(EncodingOptions options)
            => options.HardwareTuning == HardwareTuningMode.Auto
                && options.HardwareCapabilityDetection != HardwareCapabilityDetectionMode.Disabled
                && _hardwareCapabilities.IsPopulated;

        /// <summary>
        /// Maps a bit depth to the hardware surface format a decoder reports, or <c>null</c> when it is not one we can name.
        /// </summary>
        /// <param name="bitDepth">The video bit depth.</param>
        /// <returns>The surface format name.</returns>
        private static string? GetHwSurfaceFormat(int bitDepth) => bitDepth switch
        {
            <= 8 => "nv12",
            10 => "p010le",
            _ => null
        };

        private bool IsVaapiSupported(EncodingJobInfo state)
        {
            // vaapi will throw an error with this input
            // [vaapi @ 0x7faed8000960] No VAAPI support for codec mpeg4 profile -99.
            if (string.Equals(state.VideoStream?.Codec, "mpeg4", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return _mediaEncoder.SupportsHwaccel("vaapi");
        }

        private bool IsVaapiFullSupported()
        {
            return _mediaEncoder.SupportsHwaccel("drm")
                   && _mediaEncoder.SupportsHwaccel("vaapi")
                   && _mediaEncoder.SupportsFilter("scale_vaapi")
                   && _mediaEncoder.SupportsFilter("deinterlace_vaapi")
                   && _mediaEncoder.SupportsFilter("tonemap_vaapi")
                   && _mediaEncoder.SupportsFilter("procamp_vaapi")
                   && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.OverlayVaapiFrameSync)
                   && _mediaEncoder.SupportsFilter("transpose_vaapi")
                   && _mediaEncoder.SupportsFilter("hwupload_vaapi")
                   && CanDeviceFilter(HardwareAccelerationType.vaapi, HwVppKind.Scale)
                   && CanDeviceFilter(HardwareAccelerationType.vaapi, HwVppKind.Deinterlace)
                   && CanDeviceFilter(HardwareAccelerationType.vaapi, HwVppKind.Procamp)
                   && CanDeviceFilter(HardwareAccelerationType.vaapi, HwVppKind.Overlay);
        }

        private bool IsRkmppFullSupported()
        {
            return _mediaEncoder.SupportsHwaccel("rkmpp")
                   && _mediaEncoder.SupportsFilter("scale_rkrga")
                   && _mediaEncoder.SupportsFilter("vpp_rkrga")
                   && _mediaEncoder.SupportsFilter("overlay_rkrga")
                   && CanDeviceFilter(HardwareAccelerationType.rkmpp, HwVppKind.Scale)
                   && CanDeviceFilter(HardwareAccelerationType.rkmpp, HwVppKind.Overlay);
        }

        private bool IsOpenclFullSupported()
        {
            return _mediaEncoder.SupportsHwaccel("opencl")
                   && _mediaEncoder.SupportsFilter("scale_opencl")
                   && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.TonemapOpenclBt2390)
                   && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.OverlayOpenclFrameSync);

            // Let transpose_opencl optional for the time being.
        }

        private bool IsCudaFullSupported()
        {
            return _mediaEncoder.SupportsHwaccel("cuda")
                   && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.ScaleCudaFormat)
                   && _mediaEncoder.SupportsFilter("yadif_cuda")
                   && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.TonemapCudaName)
                   && _mediaEncoder.SupportsFilter("overlay_cuda")
                   && _mediaEncoder.SupportsFilter("hwupload_cuda");

            // Let transpose_cuda optional for the time being.
        }

        private bool IsVulkanFullSupported()
        {
            return _mediaEncoder.SupportsHwaccel("vulkan")
                   && _mediaEncoder.SupportsFilter("libplacebo")
                   && _mediaEncoder.SupportsFilter("scale_vulkan")
                   && _mediaEncoder.SupportsFilterWithOption(FilterOptionType.OverlayVulkanFrameSync)
                   && _mediaEncoder.SupportsFilter("transpose_vulkan")
                   && _mediaEncoder.SupportsFilter("flip_vulkan");
        }

        /// <summary>
        /// Picks the device the filter chain will run on.
        /// </summary>
        /// <remarks>
        /// libplacebo comes last on purpose: it is neither lightweight nor flexible, so the vaapi pipeline
        /// only takes the vulkan route when the vaapi filters cannot do the job at all.
        /// </remarks>
        /// <param name="options">The encoding options.</param>
        /// <returns>The filter device.</returns>
        private HwFilterDevice GetHwFilterDevice(EncodingOptions options)
        {
            switch (options.HardwareAccelerationType)
            {
                case HardwareAccelerationType.qsv:
                    // The system native decoder on Linux hands vaapi surfaces to the vaapi filters instead.
                    return OperatingSystem.IsLinux() && options.PreferSystemNativeHwDecoder
                        ? HwFilterDevice.Vaapi
                        : HwFilterDevice.Qsv;
                case HardwareAccelerationType.vaapi:
                    return IsVaapiVulkanPipeline() ? HwFilterDevice.Vulkan : HwFilterDevice.Vaapi;
                case HardwareAccelerationType.nvenc:
                    return HwFilterDevice.Cuda;
                case HardwareAccelerationType.amf:
                    return IsOpenclFullSupported() ? HwFilterDevice.OpenCl : HwFilterDevice.None;
                case HardwareAccelerationType.videotoolbox:
                    return HwFilterDevice.VideoToolbox;
                case HardwareAccelerationType.rkmpp:
                    return HwFilterDevice.Rkmpp;
                default:
                    return HwFilterDevice.None;
            }
        }

        private bool IsVaapiVulkanPipeline()
        {
            return _mediaEncoder.IsVaapiDeviceAmd
                && !_mediaEncoder.IsVaapiDeviceInteliHD
                && IsVulkanFullSupported()
                && _mediaEncoder.IsVaapiDeviceSupportVulkanDrmInterop
                && Environment.OSVersion.Version >= _minKernelVersionAmdVkFmtModifier;
        }

        /// <summary>
        /// Whether the given device applies the crop rectangle of its input frames, keeping the crop in VRAM.
        /// </summary>
        /// <remarks>
        /// Cropping is an implicit feature of the scale filter: the fixed function VPP scalers have always
        /// honoured it, the shader and kernel based ones only from FFmpeg 8.
        /// </remarks>
        /// <param name="device">The filter device.</param>
        /// <returns><c>true</c> if the device can crop, <c>false</c> otherwise.</returns>
        private bool CanCropOnDevice(HwFilterDevice device) => device switch
        {
            HwFilterDevice.Qsv or HwFilterDevice.Vaapi or HwFilterDevice.Rkmpp => true,
            HwFilterDevice.Cuda or HwFilterDevice.OpenCl or HwFilterDevice.Vulkan or HwFilterDevice.VideoToolbox
                => _mediaEncoder.EncoderVersion >= _minFFmpegHwCrop,
            _ => false
        };

        /// <summary>
        /// Decides what a job intends to do in hardware, before any filter chain is built.
        /// </summary>
        /// <param name="state">The encoding job info.</param>
        /// <param name="options">The encoding options.</param>
        /// <returns>The pipeline plan.</returns>
        private HwPipelinePlan BuildHwPipelinePlan(EncodingJobInfo state, EncodingOptions options)
        {
            var device = state.HardwareAccelerationDisabled ? HwFilterDevice.None : GetHwFilterDevice(options);
            var ops = HwOps.Scale;

            if (!string.IsNullOrEmpty(GetVideo3DFilter(state.MediaSource?.Video3DFormat)))
            {
                ops |= HwOps.Crop;
            }

            if (IsDeinterlaceAvailable(state))
            {
                ops |= HwOps.Deinterlace;
            }

            // Deliberately not IsHwTonemapAvailable: its DoVi branch asks for the decoder, which asks for this plan.
            if (options.EnableTonemapping
                && state.VideoStream?.VideoRange == VideoRange.HDR
                && GetVideoColorBitDepth(state) >= 10)
            {
                ops |= HwOps.Tonemap;
            }

            if (state.SubtitleStream is not null && ShouldEncodeSubtitle(state))
            {
                ops |= HwOps.SubtitleOverlay;
            }

            if ((state.VideoStream?.Rotation ?? 0) != 0)
            {
                ops |= HwOps.Transpose;
            }

            // This must not consult the decoder: the decoders gate their hardware surface output on the plan.
            var width = state.VideoStream?.Width ?? 0;
            var height = state.VideoStream?.Height ?? 0;
            var canCrop = CanCropOnDevice(device)
                && CanDeviceFilter(
                    options.HardwareAccelerationType,
                    HwVppKind.Scale,
                    width,
                    height,
                    GetHwSurfaceFormat(GetVideoColorBitDepth(state)));

            return new HwPipelinePlan(options.HardwareAccelerationType, device, ops, canCrop);
        }

        private bool IsSwVideo3DFlatteningRequired(EncodingJobInfo state, EncodingOptions options)
            => BuildHwPipelinePlan(state, options).RequiresSoftwareCrop;

        private bool CanFlatten3DInVram(EncodingJobInfo state, EncodingOptions options)
        {
            var plan = BuildHwPipelinePlan(state, options);
            return plan.RequiredOps.HasFlag(HwOps.Crop) && plan.CanCropInVram;
        }

        private bool IsVideoToolboxFullSupported()
        {
            return _mediaEncoder.SupportsHwaccel("videotoolbox")
                && _mediaEncoder.SupportsFilter("yadif_videotoolbox")
                && _mediaEncoder.SupportsFilter("overlay_videotoolbox")
                && _mediaEncoder.SupportsFilter("tonemap_videotoolbox")
                && _mediaEncoder.SupportsFilter("scale_vt");

            // Let transpose_vt optional for the time being.
        }

        private bool IsSwTonemapAvailable(EncodingJobInfo state, EncodingOptions options)
        {
            if (state.VideoStream is null
                || GetVideoColorBitDepth(state) < 10
                || !_mediaEncoder.SupportsFilter("tonemapx"))
            {
                return false;
            }

            return state.VideoStream.VideoRange == VideoRange.HDR;
        }

        private bool IsHwTonemapAvailable(EncodingJobInfo state, EncodingOptions options)
        {
            if (state.VideoStream is null
                || !options.EnableTonemapping
                || GetVideoColorBitDepth(state) < 10)
            {
                return false;
            }

            if (state.VideoStream.VideoRange == VideoRange.HDR
                && state.VideoStream.VideoRangeType == VideoRangeType.DOVI)
            {
                // Only native SW decoder, HW accelerator and hevc_rkmpp decoder can parse dovi rpu.
                var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;

                var isRkmppDecoder = vidDecoder.Contains("rkmpp", StringComparison.OrdinalIgnoreCase);
                if (isRkmppDecoder
                    && _mediaEncoder.EncoderVersion >= _minFFmpegRkmppHevcDecDoviRpu
                    && string.Equals(state.VideoStream?.Codec, "hevc", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var isSwDecoder = string.IsNullOrEmpty(vidDecoder);
                var isNvdecDecoder = vidDecoder.Contains("cuda", StringComparison.OrdinalIgnoreCase);
                var isVaapiDecoder = vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
                var isD3d11vaDecoder = vidDecoder.Contains("d3d11va", StringComparison.OrdinalIgnoreCase);
                var isVideoToolBoxDecoder = vidDecoder.Contains("videotoolbox", StringComparison.OrdinalIgnoreCase);
                return isSwDecoder || isNvdecDecoder || isVaapiDecoder || isD3d11vaDecoder || isVideoToolBoxDecoder;
            }

            // GPU tonemapping supports all HDR RangeTypes
            return state.VideoStream.VideoRange == VideoRange.HDR;
        }

        private bool IsIntelVppTonemapAvailable(EncodingJobInfo state, EncodingOptions options)
        {
            if (state.VideoStream is null
                || !options.EnableVppTonemapping
                || GetVideoColorBitDepth(state) < 10)
            {
                return false;
            }

            // prefer 'tonemap_vaapi' over 'vpp_qsv' on Linux for supporting Gen9/KBLx.
            // 'vpp_qsv' requires VPL, which is only supported on Gen12/TGLx and newer.
            if (OperatingSystem.IsWindows()
                && options.HardwareAccelerationType == HardwareAccelerationType.qsv
                && _mediaEncoder.EncoderVersion < _minFFmpegQsvVppTonemapOption)
            {
                return false;
            }

            return state.VideoStream.VideoRange == VideoRange.HDR
                   && (state.VideoStream.VideoRangeType == VideoRangeType.HDR10
                       || IsHdr10Plus(state.VideoStream)
                       || IsDoviWithHdr10Bl(state.VideoStream));
        }

        private static bool IsDeinterlaceAvailable(EncodingJobInfo state)
        {
            var doDeintH264 = state.DeInterlace("h264", true) || state.DeInterlace("avc", true);
            var doDeintHevc = state.DeInterlace("h265", true) || state.DeInterlace("hevc", true);
            return doDeintH264 || doDeintHevc;
        }

        private bool IsVideoStreamHevcRext(EncodingJobInfo state)
        {
            var videoStream = state.VideoStream;
            if (videoStream is null)
            {
                return false;
            }

            return string.Equals(videoStream.Codec, "hevc", StringComparison.OrdinalIgnoreCase)
                   && (string.Equals(videoStream.Profile, "Rext", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(videoStream.PixelFormat, "yuv420p12le", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(videoStream.PixelFormat, "yuv422p", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(videoStream.PixelFormat, "yuv422p10le", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(videoStream.PixelFormat, "yuv422p12le", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(videoStream.PixelFormat, "yuv444p", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(videoStream.PixelFormat, "yuv444p10le", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(videoStream.PixelFormat, "yuv444p12le", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the name of the output video codec.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="encodingOptions">Encoding options.</param>
        /// <returns>Encoder string.</returns>
        public string GetVideoEncoder(EncodingJobInfo state, EncodingOptions encodingOptions)
        {
            var codec = state.OutputVideoCodec;

            if (!string.IsNullOrEmpty(codec))
            {
                if (string.Equals(codec, "av1", StringComparison.OrdinalIgnoreCase))
                {
                    return GetAv1Encoder(state, encodingOptions);
                }

                if (string.Equals(codec, "h265", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(codec, "hevc", StringComparison.OrdinalIgnoreCase))
                {
                    return GetH265Encoder(state, encodingOptions);
                }

                if (string.Equals(codec, "h264", StringComparison.OrdinalIgnoreCase))
                {
                    return GetH264Encoder(state, encodingOptions);
                }

                if (string.Equals(codec, "mjpeg", StringComparison.OrdinalIgnoreCase))
                {
                    return GetMjpegEncoder(state, encodingOptions);
                }

                if (ContainerValidationRegex().IsMatch(codec))
                {
                    return codec.ToLowerInvariant();
                }
            }

            return "copy";
        }

        /// <summary>
        /// Gets the user agent param.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        public string GetUserAgentParam(EncodingJobInfo state)
        {
            if (state.RemoteHttpHeaders.TryGetValue("User-Agent", out var useragent))
            {
                return "-user_agent \"" + useragent + "\"";
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets the referer param.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <returns>System.String.</returns>
        public string GetRefererParam(EncodingJobInfo state)
        {
            if (state.RemoteHttpHeaders.TryGetValue("Referer", out var referer))
            {
                return "-referer \"" + referer + "\"";
            }

            return string.Empty;
        }

        /// <summary>
        /// Infers the video codec.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <returns>System.Nullable{VideoCodecs}.</returns>
        public string InferVideoCodec(string url)
        {
            var ext = Path.GetExtension(url.AsSpan());

            if (ext.Equals(".asf", StringComparison.OrdinalIgnoreCase))
            {
                return "wmv";
            }

            if (ext.Equals(".webm", StringComparison.OrdinalIgnoreCase))
            {
                // TODO: this may not always mean VP8, as the codec ages
                return "vp8";
            }

            if (ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ogv", StringComparison.OrdinalIgnoreCase))
            {
                return "theora";
            }

            if (ext.Equals(".m3u8", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ts", StringComparison.OrdinalIgnoreCase))
            {
                return "h264";
            }

            return "copy";
        }

        public int GetVideoProfileScore(string videoCodec, string videoProfile)
        {
            // strip spaces because they may be stripped out on the query string
            string profile = videoProfile.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (string.Equals("h264", videoCodec, StringComparison.OrdinalIgnoreCase))
            {
                return Array.FindIndex(_videoProfilesH264, x => string.Equals(x, profile, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals("hevc", videoCodec, StringComparison.OrdinalIgnoreCase))
            {
                return Array.FindIndex(_videoProfilesH265, x => string.Equals(x, profile, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals("av1", videoCodec, StringComparison.OrdinalIgnoreCase))
            {
                return Array.FindIndex(_videoProfilesAv1, x => string.Equals(x, profile, StringComparison.OrdinalIgnoreCase));
            }

            return -1;
        }

        public string GetGraphicalSubCanvasSize(EncodingJobInfo state)
        {
            // DVBSUB uses the fixed canvas size 720x576
            if (state.SubtitleStream is not null
                && ShouldEncodeSubtitle(state)
                && !state.SubtitleStream.IsTextSubtitleStream
                && !string.Equals(state.SubtitleStream.Codec, "DVBSUB", StringComparison.OrdinalIgnoreCase))
            {
                var subtitleWidth = state.SubtitleStream?.Width;
                var subtitleHeight = state.SubtitleStream?.Height;

                if (subtitleWidth.HasValue
                    && subtitleHeight.HasValue
                    && subtitleWidth.Value > 0
                    && subtitleHeight.Value > 0)
                {
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        " -canvas_size {0}x{1}",
                        subtitleWidth.Value,
                        subtitleHeight.Value);
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets the devices the job runs on and the decoder that feeds it.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <returns>The input arguments.</returns>
        public string GetInputVideoHwaccelArgs(EncodingJobInfo state, EncodingOptions options)
            => GetPipelineInputArgs(state, options);

        /// <summary>
        /// Determines whether the specified stream is H264.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <returns><c>true</c> if the specified stream is H264; otherwise, <c>false</c>.</returns>
        public static bool IsH264(MediaStream stream)
        {
            var codec = stream.Codec ?? string.Empty;

            return codec.Contains("264", StringComparison.OrdinalIgnoreCase)
                    || codec.Contains("avc", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsH265(MediaStream stream)
        {
            var codec = stream.Codec ?? string.Empty;

            return codec.Contains("265", StringComparison.OrdinalIgnoreCase)
                || codec.Contains("hevc", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAv1(MediaStream stream)
        {
            var codec = stream.Codec ?? string.Empty;

            return codec.Contains("av1", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAAC(MediaStream stream)
        {
            var codec = stream.Codec ?? string.Empty;

            return codec.Contains("aac", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDoviWithHdr10Bl(MediaStream? stream)
        {
            var rangeType = stream?.VideoRangeType;

            return rangeType is VideoRangeType.DOVIWithHDR10
                or VideoRangeType.DOVIWithEL
                or VideoRangeType.DOVIWithHDR10Plus
                or VideoRangeType.DOVIWithELHDR10Plus
                || (rangeType == VideoRangeType.DOVIInvalid
                    && string.Equals(stream?.ColorTransfer, "smpte2084", StringComparison.OrdinalIgnoreCase)); // invalid may be hlg now
        }

        public static bool IsDovi(MediaStream? stream)
        {
            var rangeType = stream?.VideoRangeType;

            return IsDoviWithHdr10Bl(stream)
                   || (rangeType is VideoRangeType.DOVI
                       or VideoRangeType.DOVIWithHLG
                       or VideoRangeType.DOVIWithSDR
                       or VideoRangeType.DOVIInvalid);
        }

        public static bool IsHdr10Plus(MediaStream stream)
        {
            var rangeType = stream?.VideoRangeType;

            return rangeType is VideoRangeType.HDR10Plus
                       or VideoRangeType.DOVIWithHDR10Plus
                       or VideoRangeType.DOVIWithELHDR10Plus;
        }

        /// <summary>
        /// Check if dynamic HDR metadata should be removed during stream copy.
        /// Please note this check assumes the range check has already been done
        /// and trivial fallbacks like HDR10+ to HDR10, DOVIWithHDR10 to HDR10 is already checked.
        /// </summary>
        private static DynamicHdrMetadataRemovalPlan ShouldRemoveDynamicHdrMetadata(EncodingJobInfo state)
        {
            var videoStream = state.VideoStream;
            if (videoStream.VideoRange is not VideoRange.HDR
                && videoStream.VideoRangeType != VideoRangeType.DOVIInvalid)
            {
                return DynamicHdrMetadataRemovalPlan.None;
            }

            var requestedRangeTypes = state.GetRequestedRangeTypes(state.VideoStream.Codec);
            if (requestedRangeTypes.Length == 0)
            {
                return DynamicHdrMetadataRemovalPlan.None;
            }

            var requestHasHDR10 = requestedRangeTypes.Contains(VideoRangeType.HDR10.ToString(), StringComparison.OrdinalIgnoreCase);
            var requestHasDOVI = requestedRangeTypes.Contains(VideoRangeType.DOVI.ToString(), StringComparison.OrdinalIgnoreCase);
            var requestHasDOVIwithEL = requestedRangeTypes.Contains(VideoRangeType.DOVIWithEL.ToString(), StringComparison.OrdinalIgnoreCase);
            var requestHasDOVIwithELHDR10plus = requestedRangeTypes.Contains(VideoRangeType.DOVIWithELHDR10Plus.ToString(), StringComparison.OrdinalIgnoreCase);

            var shouldRemoveHdr10Plus = false;
            // Case 1: Client supports HDR10, does not support DOVI with EL but EL presets
            var shouldRemoveDovi = (!requestHasDOVIwithEL && requestHasHDR10) && videoStream.VideoRangeType == VideoRangeType.DOVIWithEL;

            // Case 2: Client supports DOVI, does not support broken DOVI config
            // Client does not report DOVI support should be allowed to copy bad data for remuxing as HDR10 players would not crash
            shouldRemoveDovi = shouldRemoveDovi || (requestHasDOVI && videoStream.VideoRangeType == VideoRangeType.DOVIInvalid);

            // Special case: we have a video with both EL and HDR10+
            // If the client supports EL but not in the case of coexistence with HDR10+, remove HDR10+ for compatibility reasons.
            // Otherwise, remove DOVI if the client is not a DOVI player
            if (videoStream.VideoRangeType == VideoRangeType.DOVIWithELHDR10Plus)
            {
                shouldRemoveHdr10Plus = requestHasDOVIwithEL && !requestHasDOVIwithELHDR10plus;
                shouldRemoveDovi = shouldRemoveDovi || !shouldRemoveHdr10Plus;
            }

            if (shouldRemoveDovi)
            {
                return DynamicHdrMetadataRemovalPlan.RemoveDovi;
            }

            // If the client is a Dolby Vision Player, remove the HDR10+ metadata to avoid playback issues
            shouldRemoveHdr10Plus = shouldRemoveHdr10Plus || (requestHasDOVI && videoStream.VideoRangeType == VideoRangeType.DOVIWithHDR10Plus);
            return shouldRemoveHdr10Plus ? DynamicHdrMetadataRemovalPlan.RemoveHdr10Plus : DynamicHdrMetadataRemovalPlan.None;
        }

        private bool CanEncoderRemoveDynamicHdrMetadata(DynamicHdrMetadataRemovalPlan plan, MediaStream videoStream)
        {
            return plan switch
            {
                DynamicHdrMetadataRemovalPlan.RemoveDovi => _mediaEncoder.SupportsBitStreamFilterWithOption(BitStreamFilterOptionType.DoviRpuStrip)
                                                            || (IsH265(videoStream) && _mediaEncoder.SupportsBitStreamFilterWithOption(BitStreamFilterOptionType.HevcMetadataRemoveDovi))
                                                            || (IsAv1(videoStream) && _mediaEncoder.SupportsBitStreamFilterWithOption(BitStreamFilterOptionType.Av1MetadataRemoveDovi)),
                DynamicHdrMetadataRemovalPlan.RemoveHdr10Plus => (IsH265(videoStream) && _mediaEncoder.SupportsBitStreamFilterWithOption(BitStreamFilterOptionType.HevcMetadataRemoveHdr10Plus))
                                                                 || (IsAv1(videoStream) && _mediaEncoder.SupportsBitStreamFilterWithOption(BitStreamFilterOptionType.Av1MetadataRemoveHdr10Plus)),
                _ => true,
            };
        }

        public bool IsDoviRemoved(EncodingJobInfo state)
        {
            return state?.VideoStream is not null && ShouldRemoveDynamicHdrMetadata(state) == DynamicHdrMetadataRemovalPlan.RemoveDovi
                                              && CanEncoderRemoveDynamicHdrMetadata(DynamicHdrMetadataRemovalPlan.RemoveDovi, state.VideoStream);
        }

        public bool IsHdr10PlusRemoved(EncodingJobInfo state)
        {
            return state?.VideoStream is not null && ShouldRemoveDynamicHdrMetadata(state) == DynamicHdrMetadataRemovalPlan.RemoveHdr10Plus
                                                  && CanEncoderRemoveDynamicHdrMetadata(DynamicHdrMetadataRemovalPlan.RemoveHdr10Plus, state.VideoStream);
        }

        /// <summary>
        /// Gets the bitstream filters a copied stream needs.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="streamType">The stream being copied.</param>
        /// <returns>The argument, or <c>null</c> when the stream needs no filter.</returns>
        public string? GetBitStreamArgs(EncodingJobInfo state, MediaStreamType streamType)
        {
            var stream = streamType == MediaStreamType.Audio ? state.AudioStream : state.VideoStream;
            if (stream is null)
            {
                return null;
            }

            var copied = new CopiedTrack(
                stream.Codec,
                stream.VideoRange,
                stream.VideoRangeType,
                state.GetRequestedRangeTypes(state.VideoStream?.Codec));

            return BitStreamFilters.For(copied, new ServerPipelineCapabilities(_mediaEncoder, _hardwareCapabilities, GetConfiguredEncodingOptions()));
        }

        // When video is transcoded, accurate_seek (the default) trims video to the
        // exact seek point via decoder-side frame discard. But stream-copied audio
        // bypasses the decoder, so it starts from the nearest keyframe — potentially
        // seconds before the target. Use the noise bsf to drop copied audio packets
        // before the seek target, achieving the same trim precision without
        // re-encoding. The noise bsf's drop= parameter requires ffmpeg >= 5.0.
        // Important: make sure not to use it with wtv because it breaks seeking

        public double? GetFramerateParam(EncodingJobInfo state)
        {
            var request = state.BaseRequest;

            if (request.Framerate.HasValue)
            {
                return request.Framerate.Value;
            }

            var maxrate = request.MaxFramerate;

            if (maxrate.HasValue && state.VideoStream is not null)
            {
                var contentRate = state.VideoStream.ReferenceFrameRate;

                if (contentRate.HasValue && contentRate.Value > maxrate.Value)
                {
                    return maxrate;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines which stream will be used for playback.
        /// </summary>
        /// <param name="allStream">All stream.</param>
        /// <param name="desiredIndex">Index of the desired.</param>
        /// <param name="type">The type.</param>
        /// <param name="returnFirstIfNoIndex">if set to <c>true</c> [return first if no index].</param>
        /// <returns>MediaStream.</returns>
        public MediaStream? GetMediaStream(IEnumerable<MediaStream> allStream, int? desiredIndex, MediaStreamType type, bool returnFirstIfNoIndex = true)
        {
            var streams = allStream.Where(s => s.Type == type).OrderBy(i => i.Index).ToList();

            if (desiredIndex.HasValue)
            {
                var stream = streams.FirstOrDefault(s => s.Index == desiredIndex.Value);

                if (stream is not null)
                {
                    return stream;
                }
            }

            if (returnFirstIfNoIndex && type == MediaStreamType.Audio)
            {
                return streams.FirstOrDefault(i => i.Channels.HasValue && i.Channels.Value > 0) ??
                       streams.FirstOrDefault();
            }

            // Just return the first one
            return returnFirstIfNoIndex ? streams.FirstOrDefault() : null;
        }

        public static (int? Width, int? Height) GetFixedOutputSize(
            int? videoWidth,
            int? videoHeight,
            int? requestedWidth,
            int? requestedHeight,
            int? requestedMaxWidth,
            int? requestedMaxHeight)
        {
            if (!videoWidth.HasValue && !requestedWidth.HasValue)
            {
                return (null, null);
            }

            if (!videoHeight.HasValue && !requestedHeight.HasValue)
            {
                return (null, null);
            }

            int inputWidth = Convert.ToInt32(videoWidth ?? requestedWidth, CultureInfo.InvariantCulture);
            int inputHeight = Convert.ToInt32(videoHeight ?? requestedHeight, CultureInfo.InvariantCulture);
            int outputWidth = requestedWidth ?? inputWidth;
            int outputHeight = requestedHeight ?? inputHeight;

            // Don't transcode video to bigger than 4k when using HW.
            int maximumWidth = Math.Min(requestedMaxWidth ?? outputWidth, 4096);
            int maximumHeight = Math.Min(requestedMaxHeight ?? outputHeight, 4096);

            if (outputWidth > maximumWidth || outputHeight > maximumHeight)
            {
                var scaleW = (double)maximumWidth / outputWidth;
                var scaleH = (double)maximumHeight / outputHeight;
                var scale = Math.Min(scaleW, scaleH);
                outputWidth = Math.Min(maximumWidth, Convert.ToInt32(outputWidth * scale));
                outputHeight = Math.Min(maximumHeight, Convert.ToInt32(outputHeight * scale));
            }

            outputWidth = 2 * (outputWidth / 2);
            outputHeight = 2 * (outputHeight / 2);

            return (outputWidth, outputHeight);
        }

        public static bool IsScaleRatioSupported(
            int? videoWidth,
            int? videoHeight,
            int? requestedWidth,
            int? requestedHeight,
            int? requestedMaxWidth,
            int? requestedMaxHeight,
            double? maxScaleRatio)
        {
            var (outWidth, outHeight) = GetFixedOutputSize(
                videoWidth,
                videoHeight,
                requestedWidth,
                requestedHeight,
                requestedMaxWidth,
                requestedMaxHeight);

            if (!videoWidth.HasValue
                 || !videoHeight.HasValue
                 || !outWidth.HasValue
                 || !outHeight.HasValue
                 || !maxScaleRatio.HasValue
                 || (maxScaleRatio.Value < 1.0f))
            {
                return false;
            }

            var minScaleRatio = 1.0f / maxScaleRatio;
            var scaleRatioW = (double)outWidth / (double)videoWidth;
            var scaleRatioH = (double)outHeight / (double)videoHeight;

            if (scaleRatioW < minScaleRatio
                || scaleRatioW > maxScaleRatio
                || scaleRatioH < minScaleRatio
                || scaleRatioH > maxScaleRatio)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets the video 3d filter for a source.
        /// </summary>
        /// <param name="threedFormat">The 3D format of the source.</param>
        /// <returns>The video 3d filter.</returns>
        public static string GetVideo3DFilter(Video3DFormat? threedFormat)
        {
            // Keep the left eye and drop the other one. The half formats squeeze both eyes into a
            // single frame, so the remaining eye has to be stretched back to the full frame size.
            // Cropped dimensions are rounded down to a multiple of two for the encoders.
            return threedFormat switch
            {
                Video3DFormat.FullSideBySide => "crop=trunc(iw/4)*2:ih:0:0",
                Video3DFormat.HalfSideBySide => "crop=trunc(iw/4)*2:ih:0:0,scale=iw*2:ih,setsar=sar=1",
                Video3DFormat.FullTopAndBottom => "crop=iw:trunc(ih/4)*2:0:0",
                Video3DFormat.HalfTopAndBottom => "crop=iw:trunc(ih/4)*2:0:0,scale=iw:ih*2,setsar=sar=1",
                // The base view of an MVC stream already is a plain 2D frame, the decoder drops the other view.
                _ => string.Empty
            };
        }

        /// <summary>
        /// Gets the frame size a source has once its packed 3D views have been flattened down to one.
        /// </summary>
        /// <param name="threedFormat">The 3D format of the source.</param>
        /// <param name="videoWidth">The width of the source.</param>
        /// <param name="videoHeight">The height of the source.</param>
        /// <returns>The flattened frame size.</returns>
        public static (int? Width, int? Height) GetVideo3DFlattenedSize(Video3DFormat? threedFormat, int? videoWidth, int? videoHeight)
        {
            // The half formats are stretched back up to the full frame, only the full ones end up smaller.
            return threedFormat switch
            {
                Video3DFormat.FullSideBySide => (videoWidth / 4 * 2, videoHeight),
                Video3DFormat.FullTopAndBottom => (videoWidth, videoHeight / 4 * 2),
                _ => (videoWidth, videoHeight)
            };
        }

        /// <summary>
        /// Gets the frame size the filters downstream of the 3D flattening see, rotation included.
        /// </summary>
        /// <remarks>
        /// Subtitle pre-scaling and overlay placement have to follow the flattened frame, not the packed one.
        /// </remarks>
        /// <param name="state">The encoding job info.</param>
        /// <param name="swapWidthAndHeight">Whether the chain rotates the frame by a quarter turn.</param>
        /// <returns>The frame size.</returns>
        private static (int? Width, int? Height) GetFlattenedFilterInputSize(EncodingJobInfo state, bool swapWidthAndHeight)
        {
            var (flatWidth, flatHeight) = GetVideo3DFlattenedSize(
                state.MediaSource?.Video3DFormat,
                state.VideoStream?.Width,
                state.VideoStream?.Height);

            return swapWidthAndHeight ? (flatHeight, flatWidth) : (flatWidth, flatHeight);
        }

        /// <summary>
        /// Gets the crop filter that drops the second view of a frame packed 3D frame.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="GetVideo3DFilter"/> this never stretches the remaining view back with a software
        /// scale filter, because the hardware scaler downstream already targets the flattened frame size.
        /// The crop filter itself only rewrites frame metadata, so it works on hardware frames too.
        /// </remarks>
        /// <param name="threedFormat">The 3D format of the source.</param>
        /// <returns>The crop filter, or an empty string when the source needs no flattening.</returns>
        public static string GetVideo3DCropFilter(Video3DFormat? threedFormat)
        {
            // setsar only rewrites metadata, so it runs on hardware frames as happily as crop does.
            return threedFormat switch
            {
                Video3DFormat.FullSideBySide => "crop=trunc(iw/4)*2:ih:0:0",
                Video3DFormat.HalfSideBySide => "crop=trunc(iw/4)*2:ih:0:0,setsar=sar=1",
                Video3DFormat.FullTopAndBottom => "crop=iw:trunc(ih/4)*2:0:0",
                Video3DFormat.HalfTopAndBottom => "crop=iw:trunc(ih/4)*2:0:0,setsar=sar=1",
                _ => string.Empty
            };
        }

        public string GetVideoTransposeDirection(EncodingJobInfo state)
        {
            return (state.VideoStream?.Rotation ?? 0) switch
            {
                90 => "cclock",
                180 => "reversal",
                -90 => "clock",
                -180 => "reversal",
                _ => string.Empty
            };
        }

        /// <summary>
        /// Gets which side of the pipeline runs in hardware, from the decoder and encoder the job settled on.
        /// </summary>
        /// <param name="vidDecoder">The hardware decoder arguments, empty for software decoding.</param>
        /// <param name="vidEncoder">The video encoder.</param>
        /// <param name="hwEncoderSuffix">The suffix that marks this pipeline's hardware encoders.</param>
        /// <returns>The decoder and encoder kinds.</returns>
        private static (bool IsSwDecoder, bool IsSwEncoder, bool IsMjpegEncoder) GetChainEndpoints(
            string vidDecoder,
            string vidEncoder,
            string hwEncoderSuffix)
            => (string.IsNullOrEmpty(vidDecoder),
                !vidEncoder.Contains(hwEncoderSuffix, StringComparison.OrdinalIgnoreCase),
                vidEncoder.Contains("mjpeg", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Gets the frame geometry every filter chain builder works from.
        /// </summary>
        /// <param name="state">The encoding job info.</param>
        /// <returns>The source size, the requested size and the 3D format.</returns>
        private static (int? InWidth, int? InHeight, int? ReqWidth, int? ReqHeight, int? ReqMaxWidth, int? ReqMaxHeight, Video3DFormat? ThreeDFormat)
            GetChainGeometry(EncodingJobInfo state)
            => (state.VideoStream?.Width,
                state.VideoStream?.Height,
                state.BaseRequest.Width,
                state.BaseRequest.Height,
                state.BaseRequest.MaxWidth,
                state.BaseRequest.MaxHeight,
                state.MediaSource?.Video3DFormat);

        /// <summary>
        /// Gets what every filter chain builder needs to know about the subtitle stream.
        /// </summary>
        /// <param name="state">The encoding job info.</param>
        /// <returns>The subtitle kind.</returns>
        private static (bool HasSubs, bool HasTextSubs, bool HasGraphicalSubs, bool HasAssSubs)
            GetChainSubtitles(EncodingJobInfo state)
        {
            var subtitleStream = state.SubtitleStream;
            if (subtitleStream is null || !ShouldEncodeSubtitle(state))
            {
                return (false, false, false, false);
            }

            var hasTextSubs = subtitleStream.IsTextSubtitleStream;

            return (true,
                hasTextSubs,
                !hasTextSubs,
                string.Equals(subtitleStream.Codec, "ass", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(subtitleStream.Codec, "ssa", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the rotation every filter chain builder has to undo.
        /// </summary>
        /// <param name="state">The encoding job info.</param>
        /// <returns>The rotation and the transpose direction it maps to.</returns>
        private (int Rotation, string TransposeDirection) GetChainRotation(EncodingJobInfo state)
        {
            var rotation = state.VideoStream?.Rotation ?? 0;

            return (rotation, rotation == 0 ? string.Empty : GetVideoTransposeDirection(state));
        }

        /// <summary>
        /// Drops the filters a chain decided it did not need.
        /// </summary>
        /// <param name="filters">The branch of the graph, which may not exist at all.</param>
        /// <returns>The filters that emit something.</returns>
        private static List<string> Used(IReadOnlyList<string>? filters)
            => filters is null ? [] : filters.Where(f => !string.IsNullOrEmpty(f)).ToList();

        /// <summary>
        /// Gets the parameter of video processing filters.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="outputVideoCodec">Video codec to use.</param>
        /// <returns>The video processing filters parameter.</returns>
        public string GetVideoProcessingFilterParam(
            EncodingJobInfo state,
            EncodingOptions options,
            string outputVideoCodec)
        {
            var videoStream = state.VideoStream;
            if (videoStream is null)
            {
                return string.Empty;
            }

            var (hasSubs, hasTextSubs, hasGraphicalSubs, _) = GetChainSubtitles(state);

            var chains = GetPipelineVidFilterChain(state, options, outputVideoCodec);

            var mainFilters = Used(chains?.Main);
            var subFilters = Used(chains?.Subtitle);
            var overlayFilters = Used(chains?.Overlay);

            var framerate = GetFramerateParam(state);
            if (framerate.HasValue)
            {
                var doDeintH2645 = IsDeinterlaceAvailable(state);
                var fpsFilter = string.Format(CultureInfo.InvariantCulture, "fps={0}", framerate.Value);

                // For filter chain containing the deinterlace filter,
                // place the fps filter at the end to preserve temporal info.
                if (doDeintH2645)
                {
                    mainFilters.Add(fpsFilter);
                }
                else
                {
                    mainFilters.Insert(0, fpsFilter);
                }
            }

            var mainStr = string.Empty;
            if (mainFilters.Count > 0)
            {
                mainStr = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}",
                    string.Join(',', mainFilters));
            }

            if (overlayFilters.Count == 0)
            {
                // -vf "scale..."
                return string.IsNullOrEmpty(mainStr) ? string.Empty : " -vf \"" + mainStr + "\"";
            }

            if (overlayFilters.Count > 0
                && subFilters.Count > 0
                && state.SubtitleStream is not null)
            {
                // overlay graphical/text subtitles
                var subStr = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}",
                        string.Join(',', subFilters));

                var overlayStr = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}",
                        string.Join(',', overlayFilters));

                var mapPrefix = Convert.ToInt32(state.SubtitleStream.IsExternal);
                var subtitleStreamIndex = GetSubtitleStreamIndexForFfmpeg(state.MediaSource, state.SubtitleStream);
                var videoStreamIndex = FindIndex(state.MediaSource.MediaStreams, state.VideoStream);

                if (hasSubs)
                {
                    // -filter_complex "[0:s]scale=s[sub]..."
                    var filterStr = string.IsNullOrEmpty(mainStr)
                        ? " -filter_complex \"[{0}:{1}]{4}[sub];[0:{2}][sub]{5}\""
                        : " -filter_complex \"[{0}:{1}]{4}[sub];[0:{2}]{3}[main];[main][sub]{5}\"";

                    if (hasTextSubs)
                    {
                        filterStr = string.IsNullOrEmpty(mainStr)
                            ? " -filter_complex \"{4}[sub];[0:{2}][sub]{5}\""
                            : " -filter_complex \"{4}[sub];[0:{2}]{3}[main];[main][sub]{5}\"";
                    }

                    return string.Format(
                        CultureInfo.InvariantCulture,
                        filterStr,
                        mapPrefix,
                        subtitleStreamIndex,
                        videoStreamIndex,
                        mainStr,
                        subStr,
                        overlayStr);
                }
            }

            return string.Empty;
        }

        public string GetInputHdrParam(string? colorTransfer)
        {
            if (string.Equals(colorTransfer, "arib-std-b67", StringComparison.OrdinalIgnoreCase))
            {
                // HLG
                return "setparams=color_primaries=bt2020:color_trc=arib-std-b67:colorspace=bt2020nc";
            }

            // HDR10
            return "setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc";
        }

        public static int GetVideoColorBitDepth(EncodingJobInfo state)
        {
            var videoStream = state.VideoStream;
            if (videoStream is not null)
            {
                if (videoStream.BitDepth.HasValue)
                {
                    return videoStream.BitDepth.Value;
                }

                if (string.Equals(videoStream.PixelFormat, "yuv420p", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoStream.PixelFormat, "yuvj420p", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoStream.PixelFormat, "yuv422p", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoStream.PixelFormat, "yuv444p", StringComparison.OrdinalIgnoreCase))
                {
                    return 8;
                }

                if (string.Equals(videoStream.PixelFormat, "yuv420p10le", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoStream.PixelFormat, "yuv422p10le", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoStream.PixelFormat, "yuv444p10le", StringComparison.OrdinalIgnoreCase))
                {
                    return 10;
                }

                if (string.Equals(videoStream.PixelFormat, "yuv420p12le", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoStream.PixelFormat, "yuv422p12le", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoStream.PixelFormat, "yuv444p12le", StringComparison.OrdinalIgnoreCase))
                {
                    return 12;
                }

                return 8;
            }

            return 0;
        }

        /// <summary>
        /// Whether the stream carries one of the given software pixel formats.
        /// </summary>
        /// <param name="videoStream">The video stream.</param>
        /// <param name="formats">The formats to accept.</param>
        /// <returns><c>true</c> if the format matches, <c>false</c> otherwise.</returns>
        private static bool IsSwFormat(MediaStream videoStream, string[] formats)
            => formats.Contains(videoStream.PixelFormat, StringComparer.OrdinalIgnoreCase);

        public void AttachMediaSourceInfo(
            EncodingJobInfo state,
            EncodingOptions encodingOptions,
            MediaSourceInfo mediaSource,
            string? requestedUrl)
        {
            ArgumentNullException.ThrowIfNull(state);

            ArgumentNullException.ThrowIfNull(mediaSource);

            var path = mediaSource.Path;
            var protocol = mediaSource.Protocol;

            if (!string.IsNullOrEmpty(mediaSource.EncoderPath) && mediaSource.EncoderProtocol.HasValue)
            {
                path = mediaSource.EncoderPath;
                protocol = mediaSource.EncoderProtocol.Value;
            }

            state.MediaPath = path;
            state.InputProtocol = protocol;
            state.InputContainer = mediaSource.Container;
            state.RunTimeTicks = mediaSource.RunTimeTicks;
            state.RemoteHttpHeaders = mediaSource.RequiredHttpHeaders;

            state.IsoType = mediaSource.IsoType;

            if (mediaSource.Timestamp.HasValue)
            {
                state.InputTimestamp = mediaSource.Timestamp.Value;
            }

            state.RunTimeTicks = mediaSource.RunTimeTicks;
            state.RemoteHttpHeaders = mediaSource.RequiredHttpHeaders;
            state.ReadInputAtNativeFramerate = mediaSource.ReadAtNativeFramerate;

            if ((state.ReadInputAtNativeFramerate && !state.IsSegmentedLiveStream)
                || (mediaSource.Protocol == MediaProtocol.File
                && string.Equals(mediaSource.Container, "wtv", StringComparison.OrdinalIgnoreCase)))
            {
                state.InputVideoSync = "-1";
                state.InputAudioSync = "1";
            }

            if (string.Equals(mediaSource.Container, "wma", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaSource.Container, "asf", StringComparison.OrdinalIgnoreCase))
            {
                // Seeing some stuttering when transcoding wma to audio-only HLS
                state.InputAudioSync = "1";
            }

            var mediaStreams = mediaSource.MediaStreams;

            if (state.IsVideoRequest)
            {
                var videoRequest = state.BaseRequest;

                if (string.IsNullOrEmpty(videoRequest.VideoCodec))
                {
                    if (string.IsNullOrEmpty(requestedUrl))
                    {
                        requestedUrl = "test." + videoRequest.Container;
                    }

                    videoRequest.VideoCodec = InferVideoCodec(requestedUrl);
                }

                state.VideoStream = GetMediaStream(mediaStreams, videoRequest.VideoStreamIndex, MediaStreamType.Video);
                state.SubtitleStream = GetMediaStream(mediaStreams, videoRequest.SubtitleStreamIndex, MediaStreamType.Subtitle, false);
                state.SubtitleDeliveryMethod = videoRequest.SubtitleMethod;
                state.AudioStream = GetMediaStream(mediaStreams, videoRequest.AudioStreamIndex, MediaStreamType.Audio);

                if (state.SubtitleStream is not null && !state.SubtitleStream.IsExternal)
                {
                    state.InternalSubtitleStreamOffset = mediaStreams.Where(i => i.Type == MediaStreamType.Subtitle && !i.IsExternal).ToList().IndexOf(state.SubtitleStream);
                }

                EnforceResolutionLimit(state);

                NormalizeSubtitleEmbed(state);
            }
            else
            {
                state.AudioStream = GetMediaStream(mediaStreams, null, MediaStreamType.Audio, true);
            }

            state.MediaSource = mediaSource;

            var request = state.BaseRequest;
            var supportedAudioCodecs = state.SupportedAudioCodecs;
            if (request is not null && supportedAudioCodecs is not null && supportedAudioCodecs.Length > 0)
            {
                var supportedAudioCodecsList = supportedAudioCodecs.ToList();

                ShiftAudioCodecsIfNeeded(supportedAudioCodecsList, state.AudioStream);

                state.SupportedAudioCodecs = supportedAudioCodecsList.ToArray();

                request.AudioCodec = state.SupportedAudioCodecs.FirstOrDefault(_mediaEncoder.CanEncodeToAudioCodec)
                    ?? state.SupportedAudioCodecs.FirstOrDefault();
            }

            var supportedVideoCodecs = state.SupportedVideoCodecs;
            if (request is not null && supportedVideoCodecs is not null && supportedVideoCodecs.Length > 0)
            {
                var supportedVideoCodecsList = supportedVideoCodecs.ToList();

                ShiftVideoCodecsIfNeeded(supportedVideoCodecsList, encodingOptions);

                state.SupportedVideoCodecs = supportedVideoCodecsList.ToArray();

                request.VideoCodec = state.SupportedVideoCodecs.FirstOrDefault();
            }
        }

        private void ShiftAudioCodecsIfNeeded(List<string> audioCodecs, MediaStream? audioStream)
        {
            // No need to shift if there is only one supported audio codec.
            if (audioCodecs.Count < 2)
            {
                return;
            }

            var inputChannels = audioStream is null ? 6 : audioStream.Channels ?? 6;
            var shiftAudioCodecs = new List<string>();
            if (inputChannels >= 6)
            {
                // DTS and TrueHD are not supported by HLS
                // Keep them in the supported codecs list, but shift them to the end of the list so that if transcoding happens, another codec is used
                shiftAudioCodecs.Add("dts");
                shiftAudioCodecs.Add("truehd");
            }
            else
            {
                // Transcoding to 2ch ac3 or eac3 almost always causes a playback failure
                // Keep them in the supported codecs list, but shift them to the end of the list so that if transcoding happens, another codec is used
                shiftAudioCodecs.Add("ac3");
                shiftAudioCodecs.Add("eac3");
            }

            if (audioCodecs.All(i => shiftAudioCodecs.Contains(i, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            while (shiftAudioCodecs.Contains(audioCodecs[0], StringComparison.OrdinalIgnoreCase))
            {
                var removed = audioCodecs[0];
                audioCodecs.RemoveAt(0);
                audioCodecs.Add(removed);
            }
        }

        private void ShiftVideoCodecsIfNeeded(List<string> videoCodecs, EncodingOptions encodingOptions)
        {
            // No need to shift if there is only one supported video codec.
            if (videoCodecs.Count < 2)
            {
                return;
            }

            // Shift codecs to the end of list if it's not allowed.
            var shiftVideoCodecs = new List<string>();
            if (!encodingOptions.AllowHevcEncoding)
            {
                shiftVideoCodecs.Add("hevc");
                shiftVideoCodecs.Add("h265");
            }

            if (!encodingOptions.AllowAv1Encoding)
            {
                shiftVideoCodecs.Add("av1");
            }

            if (videoCodecs.All(i => shiftVideoCodecs.Contains(i, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            while (shiftVideoCodecs.Contains(videoCodecs[0], StringComparison.OrdinalIgnoreCase))
            {
                var removed = videoCodecs[0];
                videoCodecs.RemoveAt(0);
                videoCodecs.Add(removed);
            }
        }

        public static int FindIndex(IReadOnlyList<MediaStream> mediaStreams, MediaStream streamToFind)
        {
            var index = 0;
            var length = mediaStreams.Count;

            for (var i = 0; i < length; i++)
            {
                var currentMediaStream = mediaStreams[i];
                if (currentMediaStream == streamToFind)
                {
                    return index;
                }

                if (string.Equals(currentMediaStream.Path, streamToFind.Path, StringComparison.Ordinal))
                {
                    index++;
                }
            }

            return -1;
        }

        public static int GetSubtitleStreamIndexForFfmpeg(MediaSourceInfo mediaSource, MediaStream subtitleStream)
        {
            var index = FindIndex(mediaSource.MediaStreams, subtitleStream);
            if (index == -1 || subtitleStream.IsExternal || mediaSource.VideoType != VideoType.BluRay)
            {
                return index;
            }

            var hiddenStreamsBefore = mediaSource.MediaStreams.Count(s =>
                s.Type == MediaStreamType.Audio
                && !s.IsExternal
                && (string.Equals(s.Codec, "truehd", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.Codec, "atmos", StringComparison.OrdinalIgnoreCase))
                && s.Index < subtitleStream.Index);

            return index + hiddenStreamsBefore;
        }

        public static bool IsCopyCodec(string? codec)
        {
            return string.Equals(codec, "copy", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldEncodeSubtitle(EncodingJobInfo state)
        {
            return state.SubtitleDeliveryMethod == SubtitleDeliveryMethod.Encode
                   || (state.BaseRequest.AlwaysBurnInSubtitleWhenTranscoding && !IsCopyCodec(state.OutputVideoCodec));
        }

        private static bool NeedsExternalSubtitleMuxing(EncodingJobInfo state)
        {
            return state.SubtitleStream is not null
                && state.SubtitleStream.IsExternal
                && (state.SubtitleDeliveryMethod == SubtitleDeliveryMethod.Embed
                    || (ShouldEncodeSubtitle(state) && !state.SubtitleStream.IsTextSubtitleStream));
        }
    }
}
