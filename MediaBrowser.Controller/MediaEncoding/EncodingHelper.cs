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
                    && _mediaEncoder.SupportsEncoder(preferredEncoder))
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
                    && _mediaEncoder.SupportsEncoder(preferredEncoder))
                {
                    return preferredEncoder;
                }
            }

            return _defaultMjpegEncoder;
        }

        private EncodingOptions GetConfiguredEncodingOptions() => _configurationManager.GetEncodingOptions();

        /// <summary>
        /// Whether the configured device reports the given video processing operation.
        /// </summary>
        /// <remarks>
        /// Devices that report no video processing at all, such as cuda and videotoolbox, always answer yes;
        /// only the filter availability of the ffmpeg build can rule those out.
        /// </remarks>
        private bool CanDeviceFilter(HardwareAccelerationType type, HwVppKind kind, int width = 0, int height = 0)
            => _hardwareCapabilities.CanFilter(type, GetConfiguredEncodingOptions(), kind, width, height);

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
        /// Whether the user allows hardware decoding of the given codec.
        /// </summary>
        /// <remarks>
        /// Automatic tuning defers to the reported decoder list, which <see cref="CanDeviceDecode"/> checks
        /// separately, so this only has to stop saying no on the configuration's behalf.
        /// </remarks>
        /// <param name="options">The encoding options.</param>
        /// <param name="videoCodec">The codec name.</param>
        /// <returns><c>true</c> if the codec may be decoded in hardware, <c>false</c> otherwise.</returns>
        private bool IsHwDecodingCodecAllowed(EncodingOptions options, string videoCodec)
            => IsHwTuningAutomatic(options)
                || options.HardwareDecodingCodecs.Contains(videoCodec, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the user allows hardware decoding of the given codec at the given bit depth.
        /// </summary>
        /// <param name="options">The encoding options.</param>
        /// <param name="videoCodec">The codec name.</param>
        /// <param name="bitDepth">The video bit depth.</param>
        /// <param name="isHevcRext">Whether the stream is HEVC range extension.</param>
        /// <returns><c>true</c> if the bit depth may be decoded in hardware, <c>false</c> otherwise.</returns>
        private bool IsHwDecodingColorDepthAllowed(EncodingOptions options, string videoCodec, int bitDepth, bool isHevcRext)
        {
            if (IsHwTuningAutomatic(options))
            {
                return true;
            }

            if (string.Equals(videoCodec, "hevc", StringComparison.OrdinalIgnoreCase))
            {
                if (isHevcRext)
                {
                    return bitDepth == 12 ? options.EnableDecodingColorDepth12HevcRext : options.EnableDecodingColorDepth10HevcRext;
                }

                return bitDepth != 10 || options.EnableDecodingColorDepth10Hevc;
            }

            if (string.Equals(videoCodec, "vp9", StringComparison.OrdinalIgnoreCase))
            {
                return bitDepth != 10 || options.EnableDecodingColorDepth10Vp9;
            }

            return true;
        }

        private bool CanDeviceDecode(EncodingJobInfo state, EncodingOptions options, string videoCodec, int bitDepth)
            => _hardwareCapabilities.CanDecode(
                options.HardwareAccelerationType,
                options,
                videoCodec,
                state.VideoStream?.Width ?? 0,
                state.VideoStream?.Height ?? 0,
                GetHwSurfaceFormat(bitDepth));

        /// <summary>
        /// Maps a bit depth to the hardware surface format a decoder reports, or <c>null</c> when it is not one we can name.
        /// </summary>
        /// <param name="bitDepth">The video bit depth.</param>
        /// <returns>The surface format name.</returns>
        private static string GetHwSurfaceFormat(int bitDepth) => bitDepth switch
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
                    return HwFilterDevice.Rkrga;
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
            HwFilterDevice.Qsv or HwFilterDevice.Vaapi or HwFilterDevice.Rkrga => true,
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
                && CanDeviceFilter(options.HardwareAccelerationType, HwVppKind.Scale, width, height);

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
            if (state.RemoteHttpHeaders.TryGetValue("User-Agent", out string useragent))
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
            if (state.RemoteHttpHeaders.TryGetValue("Referer", out string referer))
            {
                return "-referer \"" + referer + "\"";
            }

            return string.Empty;
        }

        public static string GetInputFormat(string container)
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

        private string GetRkmppDeviceArgs(string alias)
        {
            alias ??= RkmppAlias;

            // device selection in rk is not supported.
            return " -init_hw_device rkmpp=" + alias;
        }

        private string GetVideoToolboxDeviceArgs(string alias)
        {
            alias ??= VideotoolboxAlias;

            // device selection in vt is not supported.
            return " -init_hw_device videotoolbox=" + alias;
        }

        private string GetCudaDeviceArgs(int deviceIndex, string alias)
        {
            alias ??= CudaAlias;
            deviceIndex = deviceIndex >= 0
                ? deviceIndex
                : 0;

            return string.Format(
                CultureInfo.InvariantCulture,
                " -init_hw_device cuda={0}:{1}",
                alias,
                deviceIndex);
        }

        private string GetVulkanDeviceArgs(int deviceIndex, string deviceName, string srcDeviceAlias, string alias)
        {
            alias ??= VulkanAlias;
            deviceIndex = deviceIndex >= 0
                ? deviceIndex
                : 0;
            var vendorOpts = string.IsNullOrEmpty(deviceName)
                ? ":" + deviceIndex
                : ":" + "\"" + deviceName + "\"";
            var options = string.IsNullOrEmpty(srcDeviceAlias)
                ? vendorOpts
                : "@" + srcDeviceAlias;

            return string.Format(
                CultureInfo.InvariantCulture,
                " -init_hw_device vulkan={0}{1}",
                alias,
                options);
        }

        private string GetOpenclDeviceArgs(int deviceIndex, string deviceVendorName, string srcDeviceAlias, string alias)
        {
            alias ??= OpenclAlias;
            deviceIndex = deviceIndex >= 0
                ? deviceIndex
                : 0;
            var vendorOpts = string.IsNullOrEmpty(deviceVendorName)
                ? ":0.0"
                : ":." + deviceIndex + ",device_vendor=\"" + deviceVendorName + "\"";
            var options = string.IsNullOrEmpty(srcDeviceAlias)
                ? vendorOpts
                : "@" + srcDeviceAlias;

            return string.Format(
                CultureInfo.InvariantCulture,
                " -init_hw_device opencl={0}{1}",
                alias,
                options);
        }

        private string GetD3d11vaDeviceArgs(int deviceIndex, string deviceVendorId, string alias)
        {
            alias ??= D3d11vaAlias;
            deviceIndex = deviceIndex >= 0 ? deviceIndex : 0;
            var options = string.IsNullOrEmpty(deviceVendorId)
                ? deviceIndex.ToString(CultureInfo.InvariantCulture)
                : ",vendor=" + deviceVendorId;

            return string.Format(
                CultureInfo.InvariantCulture,
                " -init_hw_device d3d11va={0}:{1}",
                alias,
                options);
        }

        private string GetVaapiDeviceArgs(string renderNodePath, string driver, string kernelDriver, string vendorId, string srcDeviceAlias, string alias)
        {
            alias ??= VaapiAlias;
            var haveVendorId = !string.IsNullOrEmpty(vendorId)
                && _mediaEncoder.EncoderVersion >= _minFFmpegVaapiDeviceVendorId;

            // Priority: 'renderNodePath' > 'vendorId' > 'kernelDriver'
            var driverOpts = File.Exists(renderNodePath)
                ? renderNodePath
                : (haveVendorId ? $",vendor_id={vendorId}" : (string.IsNullOrEmpty(kernelDriver) ? string.Empty : $",kernel_driver={kernelDriver}"));

            // 'driver' behaves similarly to env LIBVA_DRIVER_NAME
            driverOpts += string.IsNullOrEmpty(driver) ? string.Empty : ",driver=" + driver;

            var options = string.IsNullOrEmpty(srcDeviceAlias)
                ? (string.IsNullOrEmpty(driverOpts) ? string.Empty : ":" + driverOpts)
                : "@" + srcDeviceAlias;

            return string.Format(
                CultureInfo.InvariantCulture,
                " -init_hw_device vaapi={0}{1}",
                alias,
                options);
        }

        private string GetDrmDeviceArgs(string renderNodePath, string alias)
        {
            alias ??= DrmAlias;
            renderNodePath = renderNodePath ?? "/dev/dri/renderD128";

            return string.Format(
                CultureInfo.InvariantCulture,
                " -init_hw_device drm={0}:{1}",
                alias,
                renderNodePath);
        }

        private string GetQsvDeviceArgs(string renderNodePath, string alias)
        {
            var arg = " -init_hw_device qsv=" + (alias ?? QsvAlias);
            if (OperatingSystem.IsLinux())
            {
                // derive qsv from vaapi device
                return GetVaapiDeviceArgs(renderNodePath, "iHD", "i915", "0x8086", null, VaapiAlias) + arg + "@" + VaapiAlias;
            }

            if (OperatingSystem.IsWindows())
            {
                // on Windows, the deviceIndex is an int
                if (int.TryParse(renderNodePath, NumberStyles.Integer, CultureInfo.InvariantCulture, out int deviceIndex))
                {
                    return GetD3d11vaDeviceArgs(deviceIndex, string.Empty, D3d11vaAlias) + arg + "@" + D3d11vaAlias;
                }

                // derive qsv from d3d11va device
                return GetD3d11vaDeviceArgs(0, "0x8086", D3d11vaAlias) + arg + "@" + D3d11vaAlias;
            }

            return null;
        }

        private string GetFilterHwDeviceArgs(string alias)
        {
            return string.IsNullOrEmpty(alias)
                ? string.Empty
                : " -filter_hw_device " + alias;
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
        /// Gets the input video hwaccel argument.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <returns>Input video hwaccel arguments.</returns>
        public string GetInputVideoHwaccelArgs(EncodingJobInfo state, EncodingOptions options)
        {
            if (!state.IsVideoRequest)
            {
                return string.Empty;
            }

            var vidEncoder = GetVideoEncoder(state, options) ?? string.Empty;
            if (IsCopyCodec(vidEncoder))
            {
                return string.Empty;
            }

            var args = new StringBuilder();
            var isWindows = OperatingSystem.IsWindows();
            var isLinux = OperatingSystem.IsLinux();
            var isMacOS = OperatingSystem.IsMacOS();
            var optHwaccelType = options.HardwareAccelerationType;
            var vidDecoder = GetHardwareVideoDecoder(state, options) ?? string.Empty;
            var isHwTonemapAvailable = IsHwTonemapAvailable(state, options);

            if (optHwaccelType == HardwareAccelerationType.vaapi)
            {
                if (!isLinux || !_mediaEncoder.SupportsHwaccel("vaapi"))
                {
                    return string.Empty;
                }

                var isVaapiDecoder = vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
                var isVaapiEncoder = vidEncoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
                if (!isVaapiDecoder && !isVaapiEncoder)
                {
                    return string.Empty;
                }

                if (_mediaEncoder.IsVaapiDeviceInteliHD)
                {
                    args.Append(GetVaapiDeviceArgs(options.VaapiDevice, "iHD", null, null, null, VaapiAlias));
                }
                else if (_mediaEncoder.IsVaapiDeviceInteli965)
                {
                    // Only override i965 since it has lower priority than iHD in libva lookup.
                    Environment.SetEnvironmentVariable("LIBVA_DRIVER_NAME", "i965");
                    Environment.SetEnvironmentVariable("LIBVA_DRIVER_NAME_JELLYFIN", "i965");
                    args.Append(GetVaapiDeviceArgs(options.VaapiDevice, "i965", null, null, null, VaapiAlias));
                }

                var filterDevArgs = string.Empty;
                var doOclTonemap = isHwTonemapAvailable && IsOpenclFullSupported();

                if (_mediaEncoder.IsVaapiDeviceInteliHD || _mediaEncoder.IsVaapiDeviceInteli965)
                {
                    if (doOclTonemap && !isVaapiDecoder)
                    {
                        args.Append(GetOpenclDeviceArgs(0, null, VaapiAlias, OpenclAlias));
                        filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
                    }
                }
                else if (_mediaEncoder.IsVaapiDeviceAmd)
                {
                    // Disable AMD EFC feature since it's still unstable in upstream Mesa.
                    Environment.SetEnvironmentVariable("AMD_DEBUG", "noefc");

                    if (IsVulkanFullSupported()
                        && _mediaEncoder.IsVaapiDeviceSupportVulkanDrmInterop
                        && Environment.OSVersion.Version >= _minKernelVersionAmdVkFmtModifier)
                    {
                        args.Append(GetDrmDeviceArgs(options.VaapiDevice, DrmAlias));
                        args.Append(GetVaapiDeviceArgs(null, null, null, null, DrmAlias, VaapiAlias));
                        args.Append(GetVulkanDeviceArgs(0, null, DrmAlias, VulkanAlias));

                        // libplacebo wants an explicitly set vulkan filter device.
                        filterDevArgs = GetFilterHwDeviceArgs(VulkanAlias);
                    }
                    else
                    {
                        args.Append(GetVaapiDeviceArgs(options.VaapiDevice, null, null, null, null, VaapiAlias));
                        filterDevArgs = GetFilterHwDeviceArgs(VaapiAlias);

                        if (doOclTonemap)
                        {
                            // ROCm/ROCr OpenCL runtime
                            args.Append(GetOpenclDeviceArgs(0, "Advanced Micro Devices", null, OpenclAlias));
                            filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
                        }
                    }
                }
                else if (doOclTonemap)
                {
                    args.Append(GetOpenclDeviceArgs(0, null, null, OpenclAlias));
                    filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
                }

                args.Append(filterDevArgs);
            }
            else if (optHwaccelType == HardwareAccelerationType.qsv)
            {
                if ((!isLinux && !isWindows) || !_mediaEncoder.SupportsHwaccel("qsv"))
                {
                    return string.Empty;
                }

                var isD3d11vaDecoder = vidDecoder.Contains("d3d11va", StringComparison.OrdinalIgnoreCase);
                var isVaapiDecoder = vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
                var isQsvDecoder = vidDecoder.Contains("qsv", StringComparison.OrdinalIgnoreCase);
                var isQsvEncoder = vidEncoder.Contains("qsv", StringComparison.OrdinalIgnoreCase);
                var isHwDecoder = isQsvDecoder || isVaapiDecoder || isD3d11vaDecoder;
                if (!isHwDecoder && !isQsvEncoder)
                {
                    return string.Empty;
                }

                args.Append(GetQsvDeviceArgs(options.QsvDevice, QsvAlias));
                var filterDevArgs = GetFilterHwDeviceArgs(QsvAlias);
                // child device used by qsv.
                if (_mediaEncoder.SupportsHwaccel("vaapi") || _mediaEncoder.SupportsHwaccel("d3d11va"))
                {
                    if (isHwTonemapAvailable && IsOpenclFullSupported())
                    {
                        var srcAlias = isLinux ? VaapiAlias : D3d11vaAlias;
                        args.Append(GetOpenclDeviceArgs(0, null, srcAlias, OpenclAlias));
                        if (!isHwDecoder)
                        {
                            filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
                        }
                    }
                }

                args.Append(filterDevArgs);
            }
            else if (optHwaccelType == HardwareAccelerationType.nvenc)
            {
                if ((!isLinux && !isWindows) || !IsCudaFullSupported())
                {
                    return string.Empty;
                }

                var isCuvidDecoder = vidDecoder.Contains("cuvid", StringComparison.OrdinalIgnoreCase);
                var isNvdecDecoder = vidDecoder.Contains("cuda", StringComparison.OrdinalIgnoreCase);
                var isNvencEncoder = vidEncoder.Contains("nvenc", StringComparison.OrdinalIgnoreCase);
                var isHwDecoder = isNvdecDecoder || isCuvidDecoder;
                if (!isHwDecoder && !isNvencEncoder)
                {
                    return string.Empty;
                }

                args.Append(GetCudaDeviceArgs(0, CudaAlias))
                     .Append(GetFilterHwDeviceArgs(CudaAlias));
            }
            else if (optHwaccelType == HardwareAccelerationType.amf)
            {
                if (!isWindows || !_mediaEncoder.SupportsHwaccel("d3d11va"))
                {
                    return string.Empty;
                }

                var isD3d11vaDecoder = vidDecoder.Contains("d3d11va", StringComparison.OrdinalIgnoreCase);
                var isAmfEncoder = vidEncoder.Contains("amf", StringComparison.OrdinalIgnoreCase);
                if (!isD3d11vaDecoder && !isAmfEncoder)
                {
                    return string.Empty;
                }

                // no dxva video processor hw filter.
                args.Append(GetD3d11vaDeviceArgs(0, "0x1002", D3d11vaAlias));
                var filterDevArgs = string.Empty;
                if (IsOpenclFullSupported())
                {
                    args.Append(GetOpenclDeviceArgs(0, null, D3d11vaAlias, OpenclAlias));
                    filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
                }

                args.Append(filterDevArgs);
            }
            else if (optHwaccelType == HardwareAccelerationType.videotoolbox)
            {
                if (!isMacOS || !_mediaEncoder.SupportsHwaccel("videotoolbox"))
                {
                    return string.Empty;
                }

                var isVideotoolboxDecoder = vidDecoder.Contains("videotoolbox", StringComparison.OrdinalIgnoreCase);
                var isVideotoolboxEncoder = vidEncoder.Contains("videotoolbox", StringComparison.OrdinalIgnoreCase);
                if (!isVideotoolboxDecoder && !isVideotoolboxEncoder)
                {
                    return string.Empty;
                }

                // videotoolbox hw filter does not require device selection
                args.Append(GetVideoToolboxDeviceArgs(VideotoolboxAlias));
            }
            else if (optHwaccelType == HardwareAccelerationType.rkmpp)
            {
                if (!isLinux || !_mediaEncoder.SupportsHwaccel("rkmpp"))
                {
                    return string.Empty;
                }

                var isRkmppDecoder = vidDecoder.Contains("rkmpp", StringComparison.OrdinalIgnoreCase);
                var isRkmppEncoder = vidEncoder.Contains("rkmpp", StringComparison.OrdinalIgnoreCase);
                if (!isRkmppDecoder && !isRkmppEncoder)
                {
                    return string.Empty;
                }

                args.Append(GetRkmppDeviceArgs(RkmppAlias));

                var filterDevArgs = string.Empty;
                var doOclTonemap = isHwTonemapAvailable && IsOpenclFullSupported();

                if (doOclTonemap && !isRkmppDecoder)
                {
                    args.Append(GetOpenclDeviceArgs(0, null, RkmppAlias, OpenclAlias));
                    filterDevArgs = GetFilterHwDeviceArgs(OpenclAlias);
                }

                args.Append(filterDevArgs);
            }

            if (!string.IsNullOrEmpty(vidDecoder))
            {
                args.Append(vidDecoder);
            }

            return args.ToString().Trim();
        }

        /// <summary>
        /// Gets the input argument.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="segmentContainer">Segment Container.</param>
        /// <returns>Input arguments.</returns>
        public string GetInputArgument(EncodingJobInfo state, EncodingOptions options, string segmentContainer)
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

        public static bool IsDoviWithHdr10Bl(MediaStream stream)
        {
            var rangeType = stream?.VideoRangeType;

            return rangeType is VideoRangeType.DOVIWithHDR10
                or VideoRangeType.DOVIWithEL
                or VideoRangeType.DOVIWithHDR10Plus
                or VideoRangeType.DOVIWithELHDR10Plus
                || (rangeType == VideoRangeType.DOVIInvalid
                    && string.Equals(stream.ColorTransfer, "smpte2084", StringComparison.OrdinalIgnoreCase)); // invalid may be hlg now
        }

        public static bool IsDovi(MediaStream stream)
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
        public string GetBitStreamArgs(EncodingJobInfo state, MediaStreamType streamType)
        {
            var stream = streamType == MediaStreamType.Audio ? state?.AudioStream : state?.VideoStream;
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

        // When video is transcoded, accurate_seek (the default) trims video to the
        // exact seek point via decoder-side frame discard. But stream-copied audio
        // bypasses the decoder, so it starts from the nearest keyframe — potentially
        // seconds before the target. Use the noise bsf to drop copied audio packets
        // before the seek target, achieving the same trim precision without
        // re-encoding. The noise bsf's drop= parameter requires ffmpeg >= 5.0.
        // Important: make sure not to use it with wtv because it breaks seeking
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

        public static string GetSegmentFileExtension(string segmentContainer)
        {
            if (!string.IsNullOrWhiteSpace(segmentContainer))
            {
                return "." + segmentContainer;
            }

            return ".ts";
        }

        private string GetVideoBitrateParam(EncodingJobInfo state, string videoCodec)
        {
            if (state.OutputVideoBitrate is null)
            {
                return string.Empty;
            }

            int bitrate = state.OutputVideoBitrate.Value;

            // Bit rate under 1000k is not allowed in h264_qsv.
            if (string.Equals(videoCodec, "h264_qsv", StringComparison.OrdinalIgnoreCase))
            {
                bitrate = Math.Max(bitrate, 1000);
            }

            // Currently use the same buffer size for all non-QSV encoders.
            // Use long arithmetic to prevent int32 overflow for very high bitrate values.
            int bufsize = (int)Math.Min((long)bitrate * 2, int.MaxValue);

            if (string.Equals(videoCodec, "libsvtav1", StringComparison.OrdinalIgnoreCase))
            {
                return FormattableString.Invariant($" -b:v {bitrate} -bufsize {bufsize}");
            }

            if (string.Equals(videoCodec, "libx264", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoCodec, "libx265", StringComparison.OrdinalIgnoreCase))
            {
                return FormattableString.Invariant($" -maxrate {bitrate} -bufsize {bufsize}");
            }

            if (string.Equals(videoCodec, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoCodec, "hevc_qsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoCodec, "av1_qsv", StringComparison.OrdinalIgnoreCase))
            {
                // TODO: probe QSV encoders' capabilities and enable more tuning options
                // See also https://github.com/intel/media-delivery/blob/master/doc/quality.rst

                // Enable MacroBlock level bitrate control for better subjective visual quality
                var mbbrcOpt = string.Empty;
                if (string.Equals(videoCodec, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoCodec, "hevc_qsv", StringComparison.OrdinalIgnoreCase))
                {
                    mbbrcOpt = " -mbbrc 1";
                }

                // Some less powerful H.264 HW decoders require strict CPB size
                // So bufsize optimizations should not be applied to them
                int factor = 2;
                var codec = state.ActualOutputVideoCodec;
                var level = state.GetRequestedLevel(codec);
                if (string.Equals(codec, "h264", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(level, CultureInfo.InvariantCulture, out double requestedLevel)
                    && requestedLevel < 51)
                {
                    factor = 1;
                }

                // Set (maxrate == bitrate + 1) to trigger VBR for better bitrate allocation
                // Set (rc_init_occupancy == 2 * bitrate) and (bufsize == 4 * bitrate) to deal with drastic scene changes
                // Use long arithmetic and clamp to int.MaxValue to prevent int32 overflow
                // (e.g. bitrate * 4 wraps to a negative value for bitrates above ~537 million)
                int qsvMaxrate = (int)Math.Min((long)bitrate + 1, int.MaxValue);
                int qsvInitOcc = (int)Math.Min((long)bitrate * 1 * factor, int.MaxValue);
                int qsvBufsize = (int)Math.Min((long)bitrate * 2 * factor, int.MaxValue);

                return FormattableString.Invariant($"{mbbrcOpt} -b:v {bitrate} -maxrate {qsvMaxrate} -rc_init_occupancy {qsvInitOcc} -bufsize {qsvBufsize}");
            }

            if (string.Equals(videoCodec, "h264_amf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoCodec, "hevc_amf", StringComparison.OrdinalIgnoreCase))
            {
                // Override the too high default qmin 18 in transcoding preset in legacy h26x_amf
                return FormattableString.Invariant($" -rc cbr -qmin 0 -qmax 32 -b:v {bitrate} -maxrate {bitrate} -bufsize {bufsize}");
            }

            if (string.Equals(videoCodec, "h264_vaapi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoCodec, "hevc_vaapi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoCodec, "av1_vaapi", StringComparison.OrdinalIgnoreCase))
            {
                // VBR in i965 driver may result in pixelated output.
                if (_mediaEncoder.IsVaapiDeviceInteli965)
                {
                    return FormattableString.Invariant($" -rc_mode CBR -b:v {bitrate} -maxrate {bitrate} -bufsize {bufsize}");
                }

                return FormattableString.Invariant($" -rc_mode VBR -b:v {bitrate} -maxrate {bitrate} -bufsize {bufsize}");
            }

            if (string.Equals(videoCodec, "h264_videotoolbox", StringComparison.OrdinalIgnoreCase)
                || string.Equals(videoCodec, "hevc_videotoolbox", StringComparison.OrdinalIgnoreCase))
            {
                // The `maxrate` and `bufsize` options can potentially lead to performance regression
                // and even encoder hangs, especially when the value is very high.
                return FormattableString.Invariant($" -b:v {bitrate} -qmin -1 -qmax -1");
            }

            return FormattableString.Invariant($" -b:v {bitrate} -maxrate {bitrate} -bufsize {bufsize}");
        }

        private string GetEncoderParam(EncoderPreset? preset, EncoderPreset defaultPreset, EncodingOptions encodingOptions, string videoEncoder, bool isLibX265)
        {
            var param = string.Empty;
            var encoderPreset = preset ?? defaultPreset;
            if (string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase) || isLibX265)
            {
                var presetString = encoderPreset switch
                {
                    EncoderPreset.auto => EncoderPreset.veryfast.ToString().ToLowerInvariant(),
                    _ => encoderPreset.ToString().ToLowerInvariant()
                };

                param += " -preset " + presetString;

                int encodeCrf = encodingOptions.H264Crf;
                if (isLibX265)
                {
                    encodeCrf = encodingOptions.H265Crf;
                }

                if (encodeCrf >= 0 && encodeCrf <= 51)
                {
                    param += " -crf " + encodeCrf.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    string defaultCrf = "23";
                    if (isLibX265)
                    {
                        defaultCrf = "28";
                    }

                    param += " -crf " + defaultCrf;
                }
            }
            else if (string.Equals(videoEncoder, "libsvtav1", StringComparison.OrdinalIgnoreCase))
            {
                // Default to use the recommended preset 10.
                // Omit presets < 5, which are too slow for on the fly encoding.
                // https://gitlab.com/AOMediaCodec/SVT-AV1/-/blob/master/Docs/Ffmpeg.md
                param += encoderPreset switch
                {
                    EncoderPreset.veryslow => " -preset 5",
                    EncoderPreset.slower => " -preset 6",
                    EncoderPreset.slow => " -preset 7",
                    EncoderPreset.medium => " -preset 8",
                    EncoderPreset.fast => " -preset 9",
                    EncoderPreset.faster => " -preset 10",
                    EncoderPreset.veryfast => " -preset 11",
                    EncoderPreset.superfast => " -preset 12",
                    EncoderPreset.ultrafast => " -preset 13",
                    _ => " -preset 10"
                };
            }
            else if (string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(videoEncoder, "hevc_vaapi", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(videoEncoder, "av1_vaapi", StringComparison.OrdinalIgnoreCase))
            {
                // -compression_level is not reliable on AMD.
                if (_mediaEncoder.IsVaapiDeviceInteliHD)
                {
                    param += encoderPreset switch
                    {
                        EncoderPreset.veryslow => " -compression_level 1",
                        EncoderPreset.slower => " -compression_level 2",
                        EncoderPreset.slow => " -compression_level 3",
                        EncoderPreset.medium => " -compression_level 4",
                        EncoderPreset.fast => " -compression_level 5",
                        EncoderPreset.faster => " -compression_level 6",
                        EncoderPreset.veryfast => " -compression_level 7",
                        EncoderPreset.superfast => " -compression_level 7",
                        EncoderPreset.ultrafast => " -compression_level 7",
                        _ => string.Empty
                    };
                }
            }
            else if (string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase) // h264 (h264_qsv)
                     || string.Equals(videoEncoder, "hevc_qsv", StringComparison.OrdinalIgnoreCase) // hevc (hevc_qsv)
                     || string.Equals(videoEncoder, "av1_qsv", StringComparison.OrdinalIgnoreCase)) // av1 (av1_qsv)
            {
                EncoderPreset[] valid_presets = [EncoderPreset.veryslow, EncoderPreset.slower, EncoderPreset.slow, EncoderPreset.medium, EncoderPreset.fast, EncoderPreset.faster, EncoderPreset.veryfast];

                param += " -preset " + (valid_presets.Contains(encoderPreset) ? encoderPreset : EncoderPreset.veryfast).ToString().ToLowerInvariant();
            }
            else if (string.Equals(videoEncoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase) // h264 (h264_nvenc)
                        || string.Equals(videoEncoder, "hevc_nvenc", StringComparison.OrdinalIgnoreCase) // hevc (hevc_nvenc)
                        || string.Equals(videoEncoder, "av1_nvenc", StringComparison.OrdinalIgnoreCase) // av1 (av1_nvenc)
            )
            {
                param += encoderPreset switch
                {
                    EncoderPreset.veryslow => " -preset p7",
                    EncoderPreset.slower => " -preset p6",
                    EncoderPreset.slow => " -preset p5",
                    EncoderPreset.medium => " -preset p4",
                    EncoderPreset.fast => " -preset p3",
                    EncoderPreset.faster => " -preset p2",
                    _ => " -preset p1"
                };
            }
            else if (string.Equals(videoEncoder, "h264_amf", StringComparison.OrdinalIgnoreCase) // h264 (h264_amf)
                        || string.Equals(videoEncoder, "hevc_amf", StringComparison.OrdinalIgnoreCase) // hevc (hevc_amf)
                        || string.Equals(videoEncoder, "av1_amf", StringComparison.OrdinalIgnoreCase) // av1 (av1_amf)
            )
            {
                param += encoderPreset switch
                {
                    EncoderPreset.veryslow => " -quality quality",
                    EncoderPreset.slower => " -quality quality",
                    EncoderPreset.slow => " -quality quality",
                    EncoderPreset.medium => " -quality balanced",
                    _ => " -quality speed"
                };

                if (string.Equals(videoEncoder, "hevc_amf", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoEncoder, "av1_amf", StringComparison.OrdinalIgnoreCase))
                {
                    param += " -header_insertion_mode gop";
                }

                if (string.Equals(videoEncoder, "hevc_amf", StringComparison.OrdinalIgnoreCase))
                {
                    param += " -gops_per_idr 1";
                }
            }
            else if (string.Equals(videoEncoder, "h264_videotoolbox", StringComparison.OrdinalIgnoreCase) // h264 (h264_videotoolbox)
                        || string.Equals(videoEncoder, "hevc_videotoolbox", StringComparison.OrdinalIgnoreCase) // hevc (hevc_videotoolbox)
            )
            {
                param += encoderPreset switch
                {
                    EncoderPreset.veryslow => " -prio_speed 0",
                    EncoderPreset.slower => " -prio_speed 0",
                    EncoderPreset.slow => " -prio_speed 0",
                    EncoderPreset.medium => " -prio_speed 0",
                    _ => " -prio_speed 1"
                };
            }

            return param;
        }

        public static string NormalizeTranscodingLevel(EncodingJobInfo state, string level)
        {
            if (!double.TryParse(level, CultureInfo.InvariantCulture, out double requestLevel))
            {
                return null;
            }

            if (string.Equals(state.ActualOutputVideoCodec, "av1", StringComparison.OrdinalIgnoreCase))
            {
                // Transcode to level 5.3 (15) and lower for maximum compatibility.
                // https://en.wikipedia.org/wiki/AV1#Levels
                if (requestLevel < 0 || requestLevel >= 15)
                {
                    return "15";
                }
            }
            else if (string.Equals(state.ActualOutputVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(state.ActualOutputVideoCodec, "h265", StringComparison.OrdinalIgnoreCase))
            {
                // Transcode to level 5.0 and lower for maximum compatibility.
                // Level 5.0 is suitable for up to 4k 30fps hevc encoding, otherwise let the encoder to handle it.
                // https://en.wikipedia.org/wiki/High_Efficiency_Video_Coding_tiers_and_levels
                // MaxLumaSampleRate = 3840*2160*30 = 248832000 < 267386880.
                if (requestLevel < 0 || requestLevel >= 150)
                {
                    return "150";
                }
            }
            else if (string.Equals(state.ActualOutputVideoCodec, "h264", StringComparison.OrdinalIgnoreCase))
            {
                // Transcode to level 5.1 and lower for maximum compatibility.
                // h264 4k 30fps requires at least level 5.1 otherwise it will break on safari fmp4.
                // https://en.wikipedia.org/wiki/Advanced_Video_Coding#Levels
                if (requestLevel < 0 || requestLevel >= 51)
                {
                    return "51";
                }
            }

            return level;
        }

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

        public string GetHlsVideoKeyFrameArguments(
            EncodingJobInfo state,
            string codec,
            int segmentLength,
            bool isEventPlaylist,
            int? startNumber)
        {
            var args = string.Empty;
            var gopArg = string.Empty;

            var keyFrameArg = string.Format(
                CultureInfo.InvariantCulture,
                " -force_key_frames:0 \"expr:gte(t,n_forced*{0})\"",
                segmentLength);

            var framerate = state.VideoStream?.RealFrameRate;
            if (framerate.HasValue)
            {
                // This is to make sure keyframe interval is limited to our segment,
                // as forcing keyframes is not enough.
                // Example: we encoded half of desired length, then codec detected
                // scene cut and inserted a keyframe; next forced keyframe would
                // be created outside of segment, which breaks seeking.
                gopArg = string.Format(
                    CultureInfo.InvariantCulture,
                    " -g:v:0 {0} -keyint_min:v:0 {0}",
                    Math.Ceiling(segmentLength * framerate.Value));
            }

            // Unable to force key frames using these encoders, set key frames by GOP.
            if (string.Equals(codec, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "h264_nvenc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "h264_amf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "h264_rkmpp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "hevc_qsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "hevc_nvenc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "hevc_rkmpp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "av1_qsv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "av1_nvenc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "av1_amf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "libsvtav1", StringComparison.OrdinalIgnoreCase))
            {
                args += gopArg;
            }
            else if (string.Equals(codec, "libx264", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(codec, "libx265", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(codec, "h264_vaapi", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(codec, "hevc_vaapi", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(codec, "av1_vaapi", StringComparison.OrdinalIgnoreCase))
            {
                args += keyFrameArg;

                // prevent the libx264 from post processing to break the set keyframe.
                if (string.Equals(codec, "libx264", StringComparison.OrdinalIgnoreCase))
                {
                    args += " -sc_threshold:v:0 0";
                }
            }
            else
            {
                args += keyFrameArg + gopArg;
            }

            // The in-band Parameter Sets generated by the AMD HEVC VA-API encoder is inconsistent
            // with the extradata generated by ffmpeg, causing decoding failures when using hvc1.
            if (string.Equals(codec, "hevc_vaapi", StringComparison.OrdinalIgnoreCase)
                && _mediaEncoder.IsVaapiDeviceAmd)
            {
                // Extracting the extradata from the in-band PS to bypass the issue.
                // This can be removed once the issue is resolved in libva or Mesa.
                // Transcoding is unavoidable here, so using BSF will not conflict with BSF in remuxing.
                args += " -flags:v -global_header -bsf:v extract_extradata=remove=0";
            }

            return args;
        }

        /// <summary>
        /// Gets the video bitrate to specify on the command line.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="videoEncoder">Video encoder to use.</param>
        /// <param name="encodingOptions">Encoding options.</param>
        /// <param name="defaultPreset">Default present to use for encoding.</param>
        /// <returns>Video bitrate.</returns>
        public string GetVideoQualityParam(EncodingJobInfo state, string videoEncoder, EncodingOptions encodingOptions, EncoderPreset defaultPreset)
        {
            var param = string.Empty;

            // Tutorials: Enable Intel GuC / HuC firmware loading for Low Power Encoding.
            // https://01.org/group/43/downloads/firmware
            // https://wiki.archlinux.org/title/intel_graphics#Enable_GuC_/_HuC_firmware_loading
            // Intel Low Power Encoding can save unnecessary CPU-GPU synchronization,
            // which will reduce overhead in performance intensive tasks such as 4k transcoding and tonemapping.
            var intelLowPowerHwEncoding = false;

            // Workaround for linux 5.18 to 6.1.3 i915 hang at cost of performance.
            // https://github.com/intel/media-driver/issues/1456
            var enableWaFori915Hang = false;

            var hardwareAccelerationType = encodingOptions.HardwareAccelerationType;

            if (hardwareAccelerationType == HardwareAccelerationType.vaapi)
            {
                var isIntelVaapiDriver = _mediaEncoder.IsVaapiDeviceInteliHD || _mediaEncoder.IsVaapiDeviceInteli965;

                if (string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase))
                {
                    intelLowPowerHwEncoding = encodingOptions.EnableIntelLowPowerH264HwEncoder && isIntelVaapiDriver;
                }
                else if (string.Equals(videoEncoder, "hevc_vaapi", StringComparison.OrdinalIgnoreCase))
                {
                    intelLowPowerHwEncoding = encodingOptions.EnableIntelLowPowerHevcHwEncoder && isIntelVaapiDriver;
                }
            }
            else if (hardwareAccelerationType == HardwareAccelerationType.qsv)
            {
                if (OperatingSystem.IsLinux())
                {
                    var ver = Environment.OSVersion.Version;
                    var isFixedKernel60 = ver.Major == 6 && ver.Minor == 0 && ver >= _minFixedKernel60i915Hang;
                    var isUnaffectedKernel = ver < _minKerneli915Hang || ver > _maxKerneli915Hang;

                    if (!(isUnaffectedKernel || isFixedKernel60))
                    {
                        var vidDecoder = GetHardwareVideoDecoder(state, encodingOptions) ?? string.Empty;
                        var isIntelDecoder = vidDecoder.Contains("qsv", StringComparison.OrdinalIgnoreCase)
                                             || vidDecoder.Contains("vaapi", StringComparison.OrdinalIgnoreCase);
                        var doOclTonemap = _mediaEncoder.SupportsHwaccel("qsv")
                            && IsVaapiSupported(state)
                            && IsOpenclFullSupported()
                            && !IsIntelVppTonemapAvailable(state, encodingOptions)
                            && IsHwTonemapAvailable(state, encodingOptions);

                        enableWaFori915Hang = isIntelDecoder && doOclTonemap;
                    }
                }

                if (string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase))
                {
                    intelLowPowerHwEncoding = encodingOptions.EnableIntelLowPowerH264HwEncoder;
                }
                else if (string.Equals(videoEncoder, "hevc_qsv", StringComparison.OrdinalIgnoreCase))
                {
                    intelLowPowerHwEncoding = encodingOptions.EnableIntelLowPowerHevcHwEncoder;
                }
                else
                {
                    enableWaFori915Hang = false;
                }
            }

            if (intelLowPowerHwEncoding)
            {
                param += " -low_power 1";
            }

            if (enableWaFori915Hang)
            {
                param += " -async_depth 1";
            }

            var isLibX265 = string.Equals(videoEncoder, "libx265", StringComparison.OrdinalIgnoreCase);
            var encodingPreset = encodingOptions.EncoderPreset;

            param += GetEncoderParam(encodingPreset, defaultPreset, encodingOptions, videoEncoder, isLibX265);
            param += GetVideoBitrateParam(state, videoEncoder);

            var framerate = GetFramerateParam(state);
            if (framerate.HasValue)
            {
                param += string.Format(CultureInfo.InvariantCulture, " -r {0}", framerate.Value.ToString(CultureInfo.InvariantCulture));
            }

            var targetVideoCodec = state.ActualOutputVideoCodec;
            if (string.Equals(targetVideoCodec, "h265", StringComparison.OrdinalIgnoreCase)
                || string.Equals(targetVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase))
            {
                targetVideoCodec = "hevc";
            }

            var profile = state.GetRequestedProfiles(targetVideoCodec).FirstOrDefault() ?? string.Empty;
            profile = WhiteSpaceRegex().Replace(profile, string.Empty).ToLowerInvariant();

            var videoProfiles = Array.Empty<string>();
            if (string.Equals("h264", targetVideoCodec, StringComparison.OrdinalIgnoreCase))
            {
                videoProfiles = _videoProfilesH264;
            }
            else if (string.Equals("hevc", targetVideoCodec, StringComparison.OrdinalIgnoreCase))
            {
                videoProfiles = _videoProfilesH265;
            }
            else if (string.Equals("av1", targetVideoCodec, StringComparison.OrdinalIgnoreCase))
            {
                videoProfiles = _videoProfilesAv1;
            }

            if (!videoProfiles.Contains(profile, StringComparison.OrdinalIgnoreCase))
            {
                profile = string.Empty;
            }

            // We only transcode to HEVC 8-bit for now, force Main Profile.
            if (profile.Contains("main10", StringComparison.OrdinalIgnoreCase)
                || profile.Contains("mainstill", StringComparison.OrdinalIgnoreCase))
            {
                profile = "main";
            }

            // Extended Profile is not supported by any known h264 encoders, force Main Profile.
            if (profile.Contains("extended", StringComparison.OrdinalIgnoreCase))
            {
                profile = "main";
            }

            // Only libx264 support encoding H264 High 10 Profile, otherwise force High Profile.
            if (!string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase)
                && profile.Contains("high10", StringComparison.OrdinalIgnoreCase))
            {
                profile = "high";
            }

            // We only need Main profile of AV1 encoders.
            if (videoEncoder.Contains("av1", StringComparison.OrdinalIgnoreCase)
                && (profile.Contains("high", StringComparison.OrdinalIgnoreCase)
                    || profile.Contains("professional", StringComparison.OrdinalIgnoreCase)))
            {
                profile = "main";
            }

            // h264_vaapi does not support Baseline profile, force Constrained Baseline in this case,
            // which is compatible (and ugly).
            if (string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase)
                && profile.Contains("baseline", StringComparison.OrdinalIgnoreCase))
            {
                profile = "constrained_baseline";
            }

            // libx264, h264_{qsv,nvenc,rkmpp} does not support Constrained Baseline profile, force Baseline in this case.
            if ((string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_rkmpp", StringComparison.OrdinalIgnoreCase))
                && profile.Contains("baseline", StringComparison.OrdinalIgnoreCase))
            {
                profile = "baseline";
            }

            // libx264, h264_{qsv,nvenc,vaapi,rkmpp} does not support Constrained High profile, force High in this case.
            if ((string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(videoEncoder, "h264_rkmpp", StringComparison.OrdinalIgnoreCase))
                && profile.Contains("high", StringComparison.OrdinalIgnoreCase))
            {
                profile = "high";
            }

            if (string.Equals(videoEncoder, "h264_amf", StringComparison.OrdinalIgnoreCase)
                && profile.Contains("baseline", StringComparison.OrdinalIgnoreCase))
            {
                profile = "constrained_baseline";
            }

            if (string.Equals(videoEncoder, "h264_amf", StringComparison.OrdinalIgnoreCase)
                && profile.Contains("constrainedhigh", StringComparison.OrdinalIgnoreCase))
            {
                profile = "constrained_high";
            }

            if (string.Equals(videoEncoder, "h264_videotoolbox", StringComparison.OrdinalIgnoreCase)
                && profile.Contains("constrainedbaseline", StringComparison.OrdinalIgnoreCase))
            {
                profile = "constrained_baseline";
            }

            if (string.Equals(videoEncoder, "h264_videotoolbox", StringComparison.OrdinalIgnoreCase)
                && profile.Contains("constrainedhigh", StringComparison.OrdinalIgnoreCase))
            {
                profile = "constrained_high";
            }

            if (!string.IsNullOrEmpty(profile))
            {
                // Currently there's no profile option in av1_nvenc encoder
                if (!(string.Equals(videoEncoder, "av1_nvenc", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(videoEncoder, "h264_v4l2m2m", StringComparison.OrdinalIgnoreCase)))
                {
                    param += " -profile:v:0 " + profile;
                }
            }

            var level = NormalizeTranscodingLevel(state, state.GetRequestedLevel(targetVideoCodec));

            if (!string.IsNullOrEmpty(level))
            {
                // libx264, QSV, AMF can adjust the given level to match the output.
                if (string.Equals(videoEncoder, "h264_qsv", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase))
                {
                    param += " -level " + level;
                }
                else if (string.Equals(videoEncoder, "hevc_qsv", StringComparison.OrdinalIgnoreCase))
                {
                    // hevc_qsv use -level 51 instead of -level 153.
                    if (double.TryParse(level, CultureInfo.InvariantCulture, out double hevcLevel))
                    {
                        param += " -level " + (hevcLevel / 3);
                    }
                }
                else if (string.Equals(videoEncoder, "av1_qsv", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(videoEncoder, "libsvtav1", StringComparison.OrdinalIgnoreCase))
                {
                    // libsvtav1 and av1_qsv use -level 60 instead of -level 16
                    // https://aomedia.org/av1/specification/annex-a/
                    if (int.TryParse(level, NumberStyles.Any, CultureInfo.InvariantCulture, out int av1Level))
                    {
                        var x = 2 + (av1Level >> 2);
                        var y = av1Level & 3;
                        var res = (x * 10) + y;
                        param += " -level " + res;
                    }
                }
                else if (string.Equals(videoEncoder, "h264_amf", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(videoEncoder, "hevc_amf", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(videoEncoder, "av1_amf", StringComparison.OrdinalIgnoreCase))
                {
                    param += " -level " + level;
                }
                else if (string.Equals(videoEncoder, "h264_nvenc", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(videoEncoder, "hevc_nvenc", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(videoEncoder, "av1_nvenc", StringComparison.OrdinalIgnoreCase))
                {
                    // level option may cause NVENC to fail.
                    // NVENC cannot adjust the given level, just throw an error.
                }
                else if (string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(videoEncoder, "hevc_vaapi", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(videoEncoder, "av1_vaapi", StringComparison.OrdinalIgnoreCase))
                {
                    // level option may cause corrupted frames on AMD VAAPI.
                    if (_mediaEncoder.IsVaapiDeviceInteliHD || _mediaEncoder.IsVaapiDeviceInteli965)
                    {
                        param += " -level " + level;
                    }
                }
                else if (string.Equals(videoEncoder, "h264_rkmpp", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(videoEncoder, "hevc_rkmpp", StringComparison.OrdinalIgnoreCase))
                {
                    param += " -level " + level;
                }
                else if (!string.Equals(videoEncoder, "libx265", StringComparison.OrdinalIgnoreCase))
                {
                    param += " -level " + level;
                }
            }

            if (string.Equals(videoEncoder, "libx264", StringComparison.OrdinalIgnoreCase))
            {
                param += " -x264opts:0 subme=0:me_range=16:rc_lookahead=10:me=hex:open_gop=0";
            }

            if (string.Equals(videoEncoder, "libx265", StringComparison.OrdinalIgnoreCase))
            {
                // libx265 only accept level option in -x265-params.
                // level option may cause libx265 to fail.
                // libx265 cannot adjust the given level, just throw an error.
                param += " -x265-params:0 no-scenecut=1:no-open-gop=1:no-info=1";

                if (encodingOptions.EncoderPreset < EncoderPreset.ultrafast)
                {
                    // The following params are slower than the ultrafast preset, don't use when ultrafast is selected.
                    param += ":subme=3:merange=25:rc-lookahead=10:me=star:ctu=32:max-tu-size=32:min-cu-size=16:rskip=2:rskip-edge-threshold=2:no-sao=1:no-strong-intra-smoothing=1";
                }
            }

            if (string.Equals(videoEncoder, "libsvtav1", StringComparison.OrdinalIgnoreCase)
                && _mediaEncoder.EncoderVersion >= _minFFmpegSvtAv1Params)
            {
                param += " -svtav1-params:0 rc=1:tune=0:film-grain=0:enable-overlays=1:enable-tf=0";
            }

            /* Access unit too large: 8192 < 20880 error */
            if ((string.Equals(videoEncoder, "h264_vaapi", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(videoEncoder, "hevc_vaapi", StringComparison.OrdinalIgnoreCase)) &&
                 _mediaEncoder.EncoderVersion >= _minFFmpegVaapiH26xEncA53CcSei)
            {
                param += " -sei -a53_cc";
            }

            return param;
        }

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

        public int GetVideoBitrateParamValue(BaseEncodingJobOptions request, MediaStream videoStream, string outputVideoCodec)
        {
            var bitrate = request.VideoBitRate;

            if (videoStream is not null)
            {
                var isUpscaling = request.Height.HasValue
                    && videoStream.Height.HasValue
                    && request.Height.Value > videoStream.Height.Value
                    && request.Width.HasValue
                    && videoStream.Width.HasValue
                    && request.Width.Value > videoStream.Width.Value;

                // Don't allow bitrate increases unless upscaling
                if (!isUpscaling && bitrate.HasValue && videoStream.BitRate.HasValue)
                {
                    bitrate = GetMinBitrate(videoStream.BitRate.Value, bitrate.Value);
                }

                if (bitrate.HasValue)
                {
                    var inputVideoCodec = videoStream.Codec;
                    bitrate = ScaleBitrate(bitrate.Value, inputVideoCodec, outputVideoCodec);

                    // If a max bitrate was requested, don't let the scaled bitrate exceed it
                    if (request.VideoBitRate.HasValue)
                    {
                        bitrate = Math.Min(bitrate.Value, request.VideoBitRate.Value);
                    }
                }
            }

            // Cap the max target bitrate to 400 Mbps.
            // No consumer or professional hardware transcode target exceeds this value
            // (Intel QSV tops out at ~300 Mbps for H.264; HEVC High Tier Level 5.x is ~240 Mbps).
            // Without this cap, plugin-provided MPEG-TS streams with no usable bitrate metadata
            // can produce unreasonably large -bufsize/-maxrate values for the encoder.
            // Note: the existing FallbackMaxStreamingBitrate mechanism (default 30 Mbps) only
            // applies when a LiveStreamId is set (M3U/HDHR sources). Plugin streams and other
            // sources that bypass the LiveTV pipeline are not covered by it.
            const int MaxSaneBitrate = 400_000_000; // 400 Mbps
            return Math.Min(bitrate ?? 0, MaxSaneBitrate);
        }

        private int GetMinBitrate(int sourceBitrate, int requestedBitrate)
        {
            // these values were chosen from testing to improve low bitrate streams
            if (sourceBitrate <= 2000000)
            {
                sourceBitrate = Convert.ToInt32(sourceBitrate * 2.5);
            }
            else if (sourceBitrate <= 3000000)
            {
                sourceBitrate *= 2;
            }

            var bitrate = Math.Min(sourceBitrate, requestedBitrate);

            return bitrate;
        }

        private static double GetVideoBitrateScaleFactor(string codec)
        {
            // hevc & vp9 - 40% more efficient than h.264
            if (string.Equals(codec, "h265", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "hevc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(codec, "vp9", StringComparison.OrdinalIgnoreCase))
            {
                return .6;
            }

            // av1 - 50% more efficient than h.264
            if (string.Equals(codec, "av1", StringComparison.OrdinalIgnoreCase))
            {
                return .5;
            }

            return 1;
        }

        public static int ScaleBitrate(int bitrate, string inputVideoCodec, string outputVideoCodec)
        {
            var inputScaleFactor = GetVideoBitrateScaleFactor(inputVideoCodec);
            var outputScaleFactor = GetVideoBitrateScaleFactor(outputVideoCodec);

            // Don't scale the real bitrate lower than the requested bitrate
            var scaleFactor = Math.Max(outputScaleFactor / inputScaleFactor, 1);

            if (bitrate <= 500000)
            {
                scaleFactor = Math.Max(scaleFactor, 4);
            }
            else if (bitrate <= 1000000)
            {
                scaleFactor = Math.Max(scaleFactor, 3);
            }
            else if (bitrate <= 2000000)
            {
                scaleFactor = Math.Max(scaleFactor, 2.5);
            }
            else if (bitrate <= 3000000)
            {
                scaleFactor = Math.Max(scaleFactor, 2);
            }
            else if (bitrate >= 30000000)
            {
                // Don't scale beyond 30Mbps, it is hardly visually noticeable for most codecs with our prefer speed encoding
                // and will cause extremely high bitrate to be used for av1->h264 transcoding that will overload clients and encoders
                scaleFactor = 1;
            }

            return Convert.ToInt32(scaleFactor * bitrate);
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

        /// <summary>
        /// Enforces the resolution limit.
        /// </summary>
        /// <param name="state">The state.</param>
        public void EnforceResolutionLimit(EncodingJobInfo state)
        {
            var videoRequest = state.BaseRequest;

            // Switch the incoming params to be ceilings rather than fixed values
            videoRequest.MaxWidth = videoRequest.MaxWidth ?? videoRequest.Width;
            videoRequest.MaxHeight = videoRequest.MaxHeight ?? videoRequest.Height;

            videoRequest.Width = null;
            videoRequest.Height = null;
        }

        /// <summary>
        /// Gets the fast seek command line parameter.
        /// </summary>
        /// <param name="state">The state.</param>
        /// <param name="options">The options.</param>
        /// <param name="segmentContainer">Segment Container.</param>
        /// <returns>System.String.</returns>
        /// <value>The fast seek command line parameter.</value>
        public string GetFastSeekCommandLineParameter(EncodingJobInfo state, EncodingOptions options, string segmentContainer)
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

        /// <summary>
        /// Determines which stream will be used for playback.
        /// </summary>
        /// <param name="allStream">All stream.</param>
        /// <param name="desiredIndex">Index of the desired.</param>
        /// <param name="type">The type.</param>
        /// <param name="returnFirstIfNoIndex">if set to <c>true</c> [return first if no index].</param>
        /// <returns>MediaStream.</returns>
        public MediaStream GetMediaStream(IEnumerable<MediaStream> allStream, int? desiredIndex, MediaStreamType type, bool returnFirstIfNoIndex = true)
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

        public static string GetHwScaleFilter(
            string hwScalePrefix,
            string hwScaleSuffix,
            string videoFormat,
            bool swapOutputWandH,
            int? videoWidth,
            int? videoHeight,
            int? requestedWidth,
            int? requestedHeight,
            int? requestedMaxWidth,
            int? requestedMaxHeight,
            bool isCropped = false)
        {
            var (outWidth, outHeight) = GetFixedOutputSize(
                videoWidth,
                videoHeight,
                requestedWidth,
                requestedHeight,
                requestedMaxWidth,
                requestedMaxHeight);

            var isFormatFixed = !string.IsNullOrEmpty(videoFormat);

            // A preceding crop makes the sizes match again, so the scaler has to be told to resize anyway.
            var isSizeFixed = isCropped
                || !videoWidth.HasValue
                || outWidth.Value != videoWidth.Value
                || !videoHeight.HasValue
                || outHeight.Value != videoHeight.Value;

            var swpOutW = swapOutputWandH ? outHeight.Value : outWidth.Value;
            var swpOutH = swapOutputWandH ? outWidth.Value : outHeight.Value;

            var arg1 = isSizeFixed ? $"=w={swpOutW}:h={swpOutH}" : string.Empty;
            var arg2 = isFormatFixed ? $"format={videoFormat}" : string.Empty;
            if (isFormatFixed)
            {
                arg2 = (isSizeFixed ? ':' : '=') + arg2;
            }

            if (!string.IsNullOrEmpty(hwScaleSuffix) && (isSizeFixed || isFormatFixed))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1}{2}{3}",
                    hwScalePrefix ?? "scale",
                    hwScaleSuffix,
                    arg1,
                    arg2);
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets the filters that fit a graphical subtitle to the frame the chain will produce.
        /// </summary>
        /// <param name="state">The encoding job info, which carries the subtitle size.</param>
        /// <param name="videoWidth">The frame width the chain produces.</param>
        /// <param name="videoHeight">The frame height the chain produces.</param>
        /// <param name="requestedWidth">The requested width.</param>
        /// <param name="requestedHeight">The requested height.</param>
        /// <param name="requestedMaxWidth">The requested maximum width.</param>
        /// <param name="requestedMaxHeight">The requested maximum height.</param>
        /// <returns>The subtitle pre-processing filters.</returns>
        public static string GetGraphicalSubPreProcessFilters(
            EncodingJobInfo state,
            int? videoWidth,
            int? videoHeight,
            int? requestedWidth,
            int? requestedHeight,
            int? requestedMaxWidth,
            int? requestedMaxHeight)
            => GetGraphicalSubPreProcessFilters(
                videoWidth,
                videoHeight,
                state.SubtitleStream?.Width,
                state.SubtitleStream?.Height,
                requestedWidth,
                requestedHeight,
                requestedMaxWidth,
                requestedMaxHeight);

        public static string GetGraphicalSubPreProcessFilters(
            int? videoWidth,
            int? videoHeight,
            int? subtitleWidth,
            int? subtitleHeight,
            int? requestedWidth,
            int? requestedHeight,
            int? requestedMaxWidth,
            int? requestedMaxHeight)
        {
            var (outWidth, outHeight) = GetFixedOutputSize(
                videoWidth,
                videoHeight,
                requestedWidth,
                requestedHeight,
                requestedMaxWidth,
                requestedMaxHeight);

            if (!outWidth.HasValue
                || !outHeight.HasValue
                || outWidth.Value <= 0
                || outHeight.Value <= 0)
            {
                return string.Empty;
            }

            // Automatically add padding based on subtitle input
            var filters = @"scale,scale=-1:{1}:fast_bilinear,crop,pad=max({0}\,iw):max({1}\,ih):(ow-iw)/2:(oh-ih)/2:black@0,crop={0}:{1}";

            if (subtitleWidth.HasValue
                && subtitleHeight.HasValue
                && subtitleWidth.Value > 0
                && subtitleHeight.Value > 0)
            {
                var videoDar = (double)outWidth.Value / outHeight.Value;
                var subtitleDar = (double)subtitleWidth.Value / subtitleHeight.Value;

                // No need to add padding when DAR is the same -> 1080p PGSSUB on 2160p video
                if (Math.Abs(videoDar - subtitleDar) < 0.01f)
                {
                    filters = @"scale,scale={0}:{1}:fast_bilinear";
                }
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                filters,
                outWidth.Value,
                outHeight.Value);
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
            var hasSubs = state.SubtitleStream is not null && ShouldEncodeSubtitle(state);
            var hasTextSubs = hasSubs && state.SubtitleStream.IsTextSubtitleStream;

            return (hasSubs,
                hasTextSubs,
                hasSubs && !state.SubtitleStream.IsTextSubtitleStream,
                hasSubs
                    && (string.Equals(state.SubtitleStream.Codec, "ass", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(state.SubtitleStream.Codec, "ssa", StringComparison.OrdinalIgnoreCase)));
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
        private static List<string> Used(IReadOnlyList<string> filters)
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

        public string GetInputHdrParam(string colorTransfer)
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

        /// <summary>
        /// Gets the ffmpeg option string for the hardware accelerated video decoder.
        /// </summary>
        /// <param name="state">The encoding job info.</param>
        /// <param name="options">The encoding options.</param>
        /// <returns>The option string or null if none available.</returns>
        protected string GetHardwareVideoDecoder(EncodingJobInfo state, EncodingOptions options)
        {
            var videoStream = state.VideoStream;
            var mediaSource = state.MediaSource;
            if (videoStream is null || mediaSource is null)
            {
                return null;
            }

            // HWA decoders can handle both video files and video folders.
            var videoType = state.VideoType;
            if (videoType != VideoType.VideoFile
                && videoType != VideoType.Iso
                && videoType != VideoType.Dvd
                && videoType != VideoType.BluRay)
            {
                return null;
            }

            if (IsCopyCodec(state.OutputVideoCodec) || state.HardwareAccelerationDisabled)
            {
                return null;
            }

            var hardwareAccelerationType = options.HardwareAccelerationType;

            if (!string.IsNullOrEmpty(videoStream.Codec) && hardwareAccelerationType != HardwareAccelerationType.none)
            {
                var bitDepth = GetVideoColorBitDepth(state);

                // Only HEVC, VP9 and AV1 formats have 10-bit hardware decoder support for most platforms
                if (bitDepth == 10
                    && !(string.Equals(videoStream.Codec, "hevc", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(videoStream.Codec, "h265", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(videoStream.Codec, "vp9", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(videoStream.Codec, "av1", StringComparison.OrdinalIgnoreCase)))
                {
                    // RKMPP has H.264 Hi10P decoder
                    bool hasHardwareHi10P = hardwareAccelerationType == HardwareAccelerationType.rkmpp;

                    // VideoToolbox on Apple Silicon has H.264 Hi10P mode enabled after macOS 14.6
                    if (hardwareAccelerationType == HardwareAccelerationType.videotoolbox)
                    {
                        var ver = Environment.OSVersion.Version;
                        var arch = RuntimeInformation.OSArchitecture;
                        if (arch.Equals(Architecture.Arm64) && ver >= new Version(14, 6))
                        {
                            hasHardwareHi10P = true;
                        }
                    }

                    if (!hasHardwareHi10P
                        && string.Equals(videoStream.Codec, "h264", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }

                // Block unsupported H.264 Hi422P and Hi444PP profiles, which can be encoded with 4:2:0 pixel format
                if (string.Equals(videoStream.Codec, "h264", StringComparison.OrdinalIgnoreCase)
                    && ((videoStream.Profile?.Contains("4:2:2", StringComparison.OrdinalIgnoreCase) ?? false)
                        || (videoStream.Profile?.Contains("4:4:4", StringComparison.OrdinalIgnoreCase) ?? false)))
                {
                    // VideoToolbox on Apple Silicon has H.264 Hi444PP and theoretically also has Hi422P
                    if (!(hardwareAccelerationType == HardwareAccelerationType.videotoolbox
                          && RuntimeInformation.OSArchitecture.Equals(Architecture.Arm64)))
                    {
                        return null;
                    }
                }

                var decoder = hardwareAccelerationType switch
                {
                    HardwareAccelerationType.vaapi => GetVaapiVidDecoder(state, options, videoStream, bitDepth),
                    HardwareAccelerationType.amf => GetAmfVidDecoder(state, options, videoStream, bitDepth),
                    HardwareAccelerationType.qsv => GetQsvHwVidDecoder(state, options, videoStream, bitDepth),
                    HardwareAccelerationType.nvenc => GetNvdecVidDecoder(state, options, videoStream, bitDepth),
                    HardwareAccelerationType.videotoolbox => GetVideotoolboxVidDecoder(state, options, videoStream, bitDepth),
                    HardwareAccelerationType.rkmpp => GetRkmppVidDecoder(state, options, videoStream, bitDepth),
                    _ => string.Empty
                };

                if (!string.IsNullOrEmpty(decoder))
                {
                    return decoder;
                }
            }

            // leave blank so ffmpeg will decide
            return null;
        }

        /// <summary>
        /// Gets a hw decoder name.
        /// </summary>
        /// <param name="options">Encoding options.</param>
        /// <param name="decoderPrefix">Decoder prefix.</param>
        /// <param name="decoderSuffix">Decoder suffix.</param>
        /// <param name="videoCodec">Video codec to use.</param>
        /// <param name="bitDepth">Video color bit depth.</param>
        /// <returns>Hardware decoder name.</returns>
        public string GetHwDecoderName(EncodingOptions options, string decoderPrefix, string decoderSuffix, string videoCodec, int bitDepth)
        {
            if (string.IsNullOrEmpty(decoderPrefix) || string.IsNullOrEmpty(decoderSuffix))
            {
                return null;
            }

            var decoderName = decoderPrefix + '_' + decoderSuffix;

            var isCodecAvailable = _mediaEncoder.SupportsDecoder(decoderName) && IsHwDecodingCodecAllowed(options, videoCodec);

            // VideoToolbox decoders have built-in SW fallback
            if (bitDepth == 10
                && isCodecAvailable
                && options.HardwareAccelerationType != HardwareAccelerationType.videotoolbox
                && !IsHwDecodingColorDepthAllowed(options, videoCodec, bitDepth, false))
            {
                return null;
            }

            if (string.Equals(decoderSuffix, "cuvid", StringComparison.OrdinalIgnoreCase) && options.EnableEnhancedNvdecDecoder)
            {
                return null;
            }

            if (string.Equals(decoderSuffix, "qsv", StringComparison.OrdinalIgnoreCase) && options.PreferSystemNativeHwDecoder)
            {
                return null;
            }

            if (string.Equals(decoderSuffix, "rkmpp", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return isCodecAvailable ? (" -c:v " + decoderName) : null;
        }

        /// <summary>
        /// Gets a hwaccel type to use as a hardware decoder depending on the system.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="options">Encoding options.</param>
        /// <param name="videoCodec">Video codec to use.</param>
        /// <param name="bitDepth">Video color bit depth.</param>
        /// <param name="outputHwSurface">Specifies if output hw surface.</param>
        /// <returns>Hardware accelerator type.</returns>
        public string GetHwaccelType(EncodingJobInfo state, EncodingOptions options, string videoCodec, int bitDepth, bool outputHwSurface)
        {
            if (state.HardwareAccelerationDisabled)
            {
                return null;
            }

            var isWindows = OperatingSystem.IsWindows();
            var isLinux = OperatingSystem.IsLinux();
            var isMacOS = OperatingSystem.IsMacOS();
            var isD3d11Supported = isWindows && _mediaEncoder.SupportsHwaccel("d3d11va");
            var isVaapiSupported = isLinux && IsVaapiSupported(state);
            var isCudaSupported = (isLinux || isWindows) && IsCudaFullSupported();
            var isQsvSupported = (isLinux || isWindows) && _mediaEncoder.SupportsHwaccel("qsv");
            var isVideotoolboxSupported = isMacOS && _mediaEncoder.SupportsHwaccel("videotoolbox");
            var isRkmppSupported = isLinux && IsRkmppFullSupported();
            var isCodecAvailable = IsHwDecodingCodecAllowed(options, videoCodec);
            var hardwareAccelerationType = options.HardwareAccelerationType;

            var ffmpegVersion = _mediaEncoder.EncoderVersion;

            // Set the av1 codec explicitly to trigger hw accelerator, otherwise libdav1d will be used.
            var isAv1 = ffmpegVersion < _minFFmpegImplicitHwaccel
                && string.Equals(videoCodec, "av1", StringComparison.OrdinalIgnoreCase);

            // Allow profile mismatch if decoding H.264 baseline with d3d11va and vaapi hwaccels.
            var profileMismatch = string.Equals(videoCodec, "h264", StringComparison.OrdinalIgnoreCase)
                && string.Equals(state.VideoStream?.Profile, "baseline", StringComparison.OrdinalIgnoreCase);

            // Disable the extra internal copy in nvdec. We already handle it in filter chain.
            var nvdecNoInternalCopy = ffmpegVersion >= _minFFmpegHwaUnsafeOutput;

            // Strip the display rotation side data from the transposed fmp4 output stream.
            var stripRotationData = (state.VideoStream?.Rotation ?? 0) != 0
                && ffmpegVersion >= _minFFmpegDisplayRotationOption;
            var stripRotationDataArgs = stripRotationData ? " -display_rotation 0" : string.Empty;

            // The reported decoder limits settle resolution, profile and surface format before any of the
            // hand maintained rules below get a say, so an oversized 3D or VR source drops to software here.
            if (isCodecAvailable && !CanDeviceDecode(state, options, videoCodec, bitDepth))
            {
                return null;
            }

            // VideoToolbox decoders have built-in SW fallback
            if (isCodecAvailable
                && options.HardwareAccelerationType != HardwareAccelerationType.videotoolbox)
            {
                var isHevcRext = string.Equals(videoCodec, "hevc", StringComparison.OrdinalIgnoreCase)
                    && IsVideoStreamHevcRext(state);

                if (!IsHwDecodingColorDepthAllowed(options, videoCodec, bitDepth, isHevcRext))
                {
                    return null;
                }

                // Only the Intel iHD driver decodes hevc range extension.
                if (isHevcRext
                    && hardwareAccelerationType == HardwareAccelerationType.vaapi
                    && !_mediaEncoder.IsVaapiDeviceInteliHD)
                {
                    return null;
                }
            }

            // Intel qsv/d3d11va/vaapi
            if (hardwareAccelerationType == HardwareAccelerationType.qsv)
            {
                if (options.PreferSystemNativeHwDecoder)
                {
                    if (isVaapiSupported && isCodecAvailable)
                    {
                        return " -hwaccel vaapi" + (outputHwSurface ? " -hwaccel_output_format vaapi -noautorotate" + stripRotationDataArgs : string.Empty)
                            + (profileMismatch ? " -hwaccel_flags +allow_profile_mismatch" : string.Empty) + (isAv1 ? " -c:v av1" : string.Empty);
                    }

                    if (isD3d11Supported && isCodecAvailable)
                    {
                        return " -hwaccel d3d11va" + (outputHwSurface ? " -hwaccel_output_format d3d11 -noautorotate" + stripRotationDataArgs : string.Empty)
                            + (profileMismatch ? " -hwaccel_flags +allow_profile_mismatch" : string.Empty) + " -threads 2" + (isAv1 ? " -c:v av1" : string.Empty);
                    }
                }
                else
                {
                    if (isQsvSupported && isCodecAvailable)
                    {
                        return " -hwaccel qsv" + (outputHwSurface ? " -hwaccel_output_format qsv -noautorotate" + stripRotationDataArgs : string.Empty);
                    }
                }
            }

            // Nvidia cuda
            if (hardwareAccelerationType == HardwareAccelerationType.nvenc)
            {
                if (isCudaSupported && isCodecAvailable)
                {
                    if (options.EnableEnhancedNvdecDecoder)
                    {
                        // set -threads 1 to nvdec decoder explicitly since it doesn't implement threading support.
                        return " -hwaccel cuda" + (outputHwSurface ? " -hwaccel_output_format cuda -noautorotate" + stripRotationDataArgs : string.Empty)
                            + (nvdecNoInternalCopy ? " -hwaccel_flags +unsafe_output" : string.Empty) + " -threads 1" + (isAv1 ? " -c:v av1" : string.Empty);
                    }

                    // cuvid decoder doesn't have threading issue.
                    return " -hwaccel cuda" + (outputHwSurface ? " -hwaccel_output_format cuda -noautorotate" + stripRotationDataArgs : string.Empty);
                }
            }

            // Amd d3d11va
            if (hardwareAccelerationType == HardwareAccelerationType.amf)
            {
                if (isD3d11Supported && isCodecAvailable)
                {
                    return " -hwaccel d3d11va" + (outputHwSurface ? " -hwaccel_output_format d3d11 -noautorotate" + stripRotationDataArgs : string.Empty)
                        + (profileMismatch ? " -hwaccel_flags +allow_profile_mismatch" : string.Empty) + " -threads 2" + (isAv1 ? " -c:v av1" : string.Empty);
                }
            }

            // Vaapi
            if (hardwareAccelerationType == HardwareAccelerationType.vaapi
                && isVaapiSupported
                && isCodecAvailable)
            {
                return " -hwaccel vaapi" + (outputHwSurface ? " -hwaccel_output_format vaapi -noautorotate" + stripRotationDataArgs : string.Empty)
                    + (profileMismatch ? " -hwaccel_flags +allow_profile_mismatch" : string.Empty) + (isAv1 ? " -c:v av1" : string.Empty);
            }

            // Apple videotoolbox
            if (hardwareAccelerationType == HardwareAccelerationType.videotoolbox
                && isVideotoolboxSupported
                && isCodecAvailable)
            {
                return " -hwaccel videotoolbox" + (outputHwSurface ? " -hwaccel_output_format videotoolbox_vld" : string.Empty) + " -noautorotate" + stripRotationDataArgs;
            }

            // Rockchip rkmpp
            if (hardwareAccelerationType == HardwareAccelerationType.rkmpp
                && isRkmppSupported
                && isCodecAvailable)
            {
                return " -hwaccel rkmpp" + (outputHwSurface ? " -hwaccel_output_format drm_prime -noautorotate" + stripRotationDataArgs : string.Empty);
            }

            return null;
        }

        public string GetQsvHwVidDecoder(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            var isWindows = OperatingSystem.IsWindows();
            var isLinux = OperatingSystem.IsLinux();

            if ((!isWindows && !isLinux)
                || options.HardwareAccelerationType != HardwareAccelerationType.qsv)
            {
                return null;
            }

            var isQsvOclSupported = _mediaEncoder.SupportsHwaccel("qsv") && IsOpenclFullSupported();
            var isIntelDx11OclSupported = isWindows
                && _mediaEncoder.SupportsHwaccel("d3d11va")
                && isQsvOclSupported;
            var isIntelVaapiOclSupported = isLinux
                && IsVaapiSupported(state)
                && isQsvOclSupported;
            var hwSurface = (isIntelDx11OclSupported || isIntelVaapiOclSupported)
                && _mediaEncoder.SupportsFilter("alphasrc")
                && !IsSwVideo3DFlatteningRequired(state, options);

            var is8bitSwFormatsQsv = IsSwFormat(videoStream, _swFormats8Bit);
            var is8_10bitSwFormatsQsv = IsSwFormat(videoStream, _swFormats8To10Bit);
            var is8_10_12bitSwFormatsQsv = IsSwFormat(videoStream, _swFormats8To12Bit);
            // TODO: add more 8/10bit and 4:4:4 formats for Qsv after finishing the ffcheck tool

            if (is8bitSwFormatsQsv)
            {
                if (string.Equals(videoStream.Codec, "avc", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoStream.Codec, "h264", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "h264", bitDepth, hwSurface) + GetHwDecoderName(options, "h264", "qsv", "h264", bitDepth);
                }

                if (string.Equals(videoStream.Codec, "vc1", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vc1", bitDepth, hwSurface) + GetHwDecoderName(options, "vc1", "qsv", "vc1", bitDepth);
                }

                if (string.Equals(videoStream.Codec, "vp8", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp8", bitDepth, hwSurface) + GetHwDecoderName(options, "vp8", "qsv", "vp8", bitDepth);
                }

                if (string.Equals(videoStream.Codec, "mpeg2video", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg2video", bitDepth, hwSurface) + GetHwDecoderName(options, "mpeg2", "qsv", "mpeg2video", bitDepth);
                }
            }

            if (is8_10bitSwFormatsQsv)
            {
                if (string.Equals(videoStream.Codec, "vp9", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp9", bitDepth, hwSurface) + GetHwDecoderName(options, "vp9", "qsv", "vp9", bitDepth);
                }

                if (string.Equals(videoStream.Codec, "av1", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "av1", bitDepth, hwSurface) + GetHwDecoderName(options, "av1", "qsv", "av1", bitDepth);
                }
            }

            if (is8_10_12bitSwFormatsQsv)
            {
                if (string.Equals(videoStream.Codec, "hevc", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoStream.Codec, "h265", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "hevc", bitDepth, hwSurface) + GetHwDecoderName(options, "hevc", "qsv", "hevc", bitDepth);
                }
            }

            return null;
        }

        public string GetNvdecVidDecoder(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
                || options.HardwareAccelerationType != HardwareAccelerationType.nvenc)
            {
                return null;
            }

            var hwSurface = IsCudaFullSupported()
                && _mediaEncoder.SupportsFilter("alphasrc")
                && !IsSwVideo3DFlatteningRequired(state, options);
            var is8bitSwFormatsNvdec = IsSwFormat(videoStream, _swFormats8Bit);
            var is8_10bitSwFormatsNvdec = IsSwFormat(videoStream, _swFormats8To10Bit);
            var is8_10_12bitSwFormatsNvdec = is8_10bitSwFormatsNvdec
                || string.Equals("yuv444p", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase)
                || string.Equals("yuv444p10le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase)
                || string.Equals("yuv420p12le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase)
                || string.Equals("yuv444p12le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase);
            // TODO: add more 8/10/12bit and 4:4:4 formats for Nvdec after finishing the ffcheck tool

            if (is8bitSwFormatsNvdec)
            {
                if (string.Equals("avc", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                    || string.Equals("h264", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "h264", bitDepth, hwSurface) + GetHwDecoderName(options, "h264", "cuvid", "h264", bitDepth);
                }

                if (string.Equals("mpeg2video", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg2video", bitDepth, hwSurface) + GetHwDecoderName(options, "mpeg2", "cuvid", "mpeg2video", bitDepth);
                }

                if (string.Equals("vc1", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vc1", bitDepth, hwSurface) + GetHwDecoderName(options, "vc1", "cuvid", "vc1", bitDepth);
                }

                if (string.Equals("mpeg4", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg4", bitDepth, hwSurface) + GetHwDecoderName(options, "mpeg4", "cuvid", "mpeg4", bitDepth);
                }

                if (string.Equals("vp8", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp8", bitDepth, hwSurface) + GetHwDecoderName(options, "vp8", "cuvid", "vp8", bitDepth);
                }
            }

            if (is8_10bitSwFormatsNvdec)
            {
                if (string.Equals("vp9", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp9", bitDepth, hwSurface) + GetHwDecoderName(options, "vp9", "cuvid", "vp9", bitDepth);
                }

                if (string.Equals("av1", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "av1", bitDepth, hwSurface) + GetHwDecoderName(options, "av1", "cuvid", "av1", bitDepth);
                }
            }

            if (is8_10_12bitSwFormatsNvdec)
            {
                if (string.Equals("hevc", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                    || string.Equals("h265", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "hevc", bitDepth, hwSurface) + GetHwDecoderName(options, "hevc", "cuvid", "hevc", bitDepth);
                }
            }

            return null;
        }

        public string GetAmfVidDecoder(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            if (!OperatingSystem.IsWindows()
                || options.HardwareAccelerationType != HardwareAccelerationType.amf)
            {
                return null;
            }

            var hwSurface = _mediaEncoder.SupportsHwaccel("d3d11va")
                && IsOpenclFullSupported()
                && _mediaEncoder.SupportsFilter("alphasrc")
                && !IsSwVideo3DFlatteningRequired(state, options);
            var is8bitSwFormatsAmf = IsSwFormat(videoStream, _swFormats8Bit);
            var is8_10bitSwFormatsAmf = IsSwFormat(videoStream, _swFormats8To10Bit);

            if (is8bitSwFormatsAmf)
            {
                if (string.Equals("avc", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                    || string.Equals("h264", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "h264", bitDepth, hwSurface);
                }

                if (string.Equals("mpeg2video", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg2video", bitDepth, hwSurface);
                }

                if (string.Equals("vc1", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vc1", bitDepth, hwSurface);
                }
            }

            if (is8_10bitSwFormatsAmf)
            {
                if (string.Equals("hevc", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                    || string.Equals("h265", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "hevc", bitDepth, hwSurface);
                }

                if (string.Equals("vp9", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp9", bitDepth, hwSurface);
                }

                if (string.Equals("av1", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "av1", bitDepth, hwSurface);
                }
            }

            return null;
        }

        public string GetVaapiVidDecoder(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            if (!OperatingSystem.IsLinux()
                || options.HardwareAccelerationType != HardwareAccelerationType.vaapi)
            {
                return null;
            }

            var hwSurface = IsVaapiSupported(state)
                && IsVaapiFullSupported()
                && IsOpenclFullSupported()
                && _mediaEncoder.SupportsFilter("alphasrc")
                && !IsSwVideo3DFlatteningRequired(state, options);
            var is8bitSwFormatsVaapi = IsSwFormat(videoStream, _swFormats8Bit);
            var is8_10bitSwFormatsVaapi = IsSwFormat(videoStream, _swFormats8To10Bit);
            var is8_10_12bitSwFormatsVaapi = IsSwFormat(videoStream, _swFormats8To12Bit);

            if (is8bitSwFormatsVaapi)
            {
                if (string.Equals("avc", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                    || string.Equals("h264", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "h264", bitDepth, hwSurface);
                }

                if (string.Equals("mpeg2video", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg2video", bitDepth, hwSurface);
                }

                if (string.Equals("vc1", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vc1", bitDepth, hwSurface);
                }

                if (string.Equals("vp8", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp8", bitDepth, hwSurface);
                }
            }

            if (is8_10bitSwFormatsVaapi)
            {
                if (string.Equals("vp9", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp9", bitDepth, hwSurface);
                }

                if (string.Equals("av1", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "av1", bitDepth, hwSurface);
                }
            }

            if (is8_10_12bitSwFormatsVaapi)
            {
                if (string.Equals("hevc", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                    || string.Equals("h265", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "hevc", bitDepth, hwSurface);
                }
            }

            return null;
        }

        public string GetVideotoolboxVidDecoder(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            if (!OperatingSystem.IsMacOS()
                || options.HardwareAccelerationType != HardwareAccelerationType.videotoolbox)
            {
                return null;
            }

            var is8bitSwFormatsVt = IsSwFormat(videoStream, _swFormats8Bit);
            var is8_10bitSwFormatsVt = IsSwFormat(videoStream, _swFormats8To10Bit);
            var is8_10_12bitSwFormatsVt = IsSwFormat(videoStream, _swFormats8To12Bit);
            var isAv1SupportedSwFormatsVt = is8_10bitSwFormatsVt || string.Equals("yuv420p12le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase);

            // The related patches make videotoolbox hardware surface working is only available in jellyfin-ffmpeg 7.0.1 at the moment.
            bool useHwSurface = (_mediaEncoder.EncoderVersion >= _minFFmpegWorkingVtHwSurface)
                && IsVideoToolboxFullSupported()
                && !IsSwVideo3DFlatteningRequired(state, options);

            if (is8bitSwFormatsVt)
            {
                if (string.Equals("vp8", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp8", bitDepth, useHwSurface);
                }
            }

            if (is8_10bitSwFormatsVt)
            {
                if (string.Equals("avc", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                    || string.Equals("h264", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "h264", bitDepth, useHwSurface);
                }

                if (string.Equals("vp9", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp9", bitDepth, useHwSurface);
                }
            }

            if (is8_10_12bitSwFormatsVt)
            {
                if (string.Equals("hevc", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                    || string.Equals("h265", videoStream.Codec, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "hevc", bitDepth, useHwSurface);
                }

                if (string.Equals("av1", videoStream.Codec, StringComparison.OrdinalIgnoreCase)
                    && isAv1SupportedSwFormatsVt
                    && _mediaEncoder.IsVideoToolboxAv1DecodeAvailable)
                {
                    return GetHwaccelType(state, options, "av1", bitDepth, useHwSurface);
                }
            }

            return null;
        }

        public string GetRkmppVidDecoder(EncodingJobInfo state, EncodingOptions options, MediaStream videoStream, int bitDepth)
        {
            var isLinux = OperatingSystem.IsLinux();

            if (!isLinux
                || options.HardwareAccelerationType != HardwareAccelerationType.rkmpp)
            {
                return null;
            }

            var (inW, inH, reqW, reqH, reqMaxW, reqMaxH, threeDFormat) = GetChainGeometry(state);

            // rkrga RGA2e supports range from 1/16 to 16
            if (!IsScaleRatioSupported(inW, inH, reqW, reqH, reqMaxW, reqMaxH, 16.0f))
            {
                return null;
            }

            var isRkmppOclSupported = IsRkmppFullSupported() && IsOpenclFullSupported();
            var hwSurface = isRkmppOclSupported
                && _mediaEncoder.SupportsFilter("alphasrc")
                && !IsSwVideo3DFlatteningRequired(state, options);

            // rkrga RGA3 supports range from 1/8 to 8
            var isAfbcSupported = hwSurface && IsScaleRatioSupported(inW, inH, reqW, reqH, reqMaxW, reqMaxH, 8.0f);

            // TODO: add more 8/10bit and 4:2:2 formats for Rkmpp after finishing the ffcheck tool
            var is8bitSwFormatsRkmpp = IsSwFormat(videoStream, _swFormats8Bit);
            var is10bitSwFormatsRkmpp = string.Equals("yuv420p10le", videoStream.PixelFormat, StringComparison.OrdinalIgnoreCase);
            var is8_10bitSwFormatsRkmpp = is8bitSwFormatsRkmpp || is10bitSwFormatsRkmpp;

            // nv15 and nv20 are bit-stream only formats
            if (is10bitSwFormatsRkmpp && !hwSurface)
            {
                return null;
            }

            if (is8bitSwFormatsRkmpp)
            {
                if (string.Equals(videoStream.Codec, "mpeg1video", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg1video", bitDepth, hwSurface);
                }

                if (string.Equals(videoStream.Codec, "mpeg2video", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg2video", bitDepth, hwSurface);
                }

                if (string.Equals(videoStream.Codec, "mpeg4", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "mpeg4", bitDepth, hwSurface);
                }

                if (string.Equals(videoStream.Codec, "vp8", StringComparison.OrdinalIgnoreCase))
                {
                    return GetHwaccelType(state, options, "vp8", bitDepth, hwSurface);
                }
            }

            if (is8_10bitSwFormatsRkmpp)
            {
                if (string.Equals(videoStream.Codec, "avc", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoStream.Codec, "h264", StringComparison.OrdinalIgnoreCase))
                {
                    var accelType = GetHwaccelType(state, options, "h264", bitDepth, hwSurface);
                    return accelType + ((!string.IsNullOrEmpty(accelType) && isAfbcSupported) ? " -afbc rga" : string.Empty);
                }

                if (string.Equals(videoStream.Codec, "hevc", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(videoStream.Codec, "h265", StringComparison.OrdinalIgnoreCase))
                {
                    var accelType = GetHwaccelType(state, options, "hevc", bitDepth, hwSurface);
                    return accelType + ((!string.IsNullOrEmpty(accelType) && isAfbcSupported) ? " -afbc rga" : string.Empty);
                }

                if (string.Equals(videoStream.Codec, "vp9", StringComparison.OrdinalIgnoreCase))
                {
                    var accelType = GetHwaccelType(state, options, "vp9", bitDepth, hwSurface);
                    return accelType + ((!string.IsNullOrEmpty(accelType) && isAfbcSupported) ? " -afbc rga" : string.Empty);
                }

                if (string.Equals(videoStream.Codec, "av1", StringComparison.OrdinalIgnoreCase))
                {
                    // there's an issue about AV1 AFBC on RK3588, disable it for now until it's fixed upstream
                    return GetHwaccelType(state, options, "av1", bitDepth, hwSurface);
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the number of threads.
        /// </summary>
        /// <param name="state">Encoding state.</param>
        /// <param name="encodingOptions">Encoding options.</param>
        /// <param name="outputVideoCodec">Video codec to use.</param>
        /// <returns>Number of threads.</returns>
#nullable enable
        public static int GetNumberOfThreads(EncodingJobInfo? state, EncodingOptions encodingOptions, string? outputVideoCodec)
        {
            var threads = state?.BaseRequest.CpuCoreLimit ?? encodingOptions.EncodingThreadCount;

            if (threads <= 0)
            {
                // Automatically set thread count
                return 0;
            }

            return Math.Min(threads, Environment.ProcessorCount);
        }

#nullable disable
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

        public string GetInputModifier(EncodingJobInfo state, EncodingOptions encodingOptions, string segmentContainer)
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

        public void AttachMediaSourceInfo(
            EncodingJobInfo state,
            EncodingOptions encodingOptions,
            MediaSourceInfo mediaSource,
            string requestedUrl)
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

        private void ShiftAudioCodecsIfNeeded(List<string> audioCodecs, MediaStream audioStream)
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
                    string bitStreamArgs = GetBitStreamArgs(state, MediaStreamType.Video);
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
                audioTranscodeParams.Add("-ac " + state.OutputAudioChannels.Value.ToString(CultureInfo.InvariantCulture));
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

        public static bool IsCopyCodec(string codec)
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

        public static string GetVideoSyncOption(string videoSync, Version encoderVersion)
        {
            if (string.IsNullOrEmpty(videoSync))
            {
                return string.Empty;
            }

            if (encoderVersion >= new Version(5, 1))
            {
                if (int.TryParse(videoSync, CultureInfo.InvariantCulture, out var vsync))
                {
                    return vsync switch
                    {
                        -1 => " -fps_mode auto",
                        0 => " -fps_mode passthrough",
                        1 => " -fps_mode cfr",
                        2 => " -fps_mode vfr",
                        _ => string.Empty
                    };
                }

                return string.Empty;
            }

            // -vsync is deprecated in FFmpeg 5.1 and will be removed in the future.
            return $" -vsync {videoSync}";
        }
    }
}
