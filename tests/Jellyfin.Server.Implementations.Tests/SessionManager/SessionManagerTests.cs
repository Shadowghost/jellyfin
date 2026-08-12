using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.SessionManager;

public class SessionManagerTests
{
    [Theory]
    [InlineData("", typeof(ArgumentException))]
    [InlineData(null, typeof(ArgumentNullException))]
    public async Task GetAuthorizationToken_Should_ThrowException(string? deviceId, Type exceptionType)
    {
        await using var sessionManager = new Emby.Server.Implementations.Session.SessionManager(
            NullLogger<Emby.Server.Implementations.Session.SessionManager>.Instance,
            Mock.Of<IEventManager>(),
            Mock.Of<IUserDataManager>(),
            Mock.Of<IServerConfigurationManager>(),
            Mock.Of<ILibraryManager>(),
            Mock.Of<IUserManager>(),
            Mock.Of<IMusicManager>(),
            Mock.Of<IDtoService>(),
            Mock.Of<IImageProcessor>(),
            Mock.Of<IServerApplicationHost>(),
            Mock.Of<IDeviceManager>(),
            Mock.Of<IMediaSourceManager>(),
            Mock.Of<IHostApplicationLifetime>(),
            Mock.Of<IPlaybackHistoryManager>());

        await Assert.ThrowsAsync(exceptionType, () => sessionManager.GetAuthorizationToken(
            new User("test", "default", "default"),
            deviceId,
            "app_name",
            "0.0.0",
            "device_name"));
    }

    [Theory]
    [MemberData(nameof(AuthenticateNewSessionInternal_Exception_TestData))]
    public async Task AuthenticateNewSessionInternal_Should_ThrowException(AuthenticationRequest authenticationRequest, Type exceptionType)
    {
        await using var sessionManager = new Emby.Server.Implementations.Session.SessionManager(
            NullLogger<Emby.Server.Implementations.Session.SessionManager>.Instance,
            Mock.Of<IEventManager>(),
            Mock.Of<IUserDataManager>(),
            Mock.Of<IServerConfigurationManager>(),
            Mock.Of<ILibraryManager>(),
            Mock.Of<IUserManager>(),
            Mock.Of<IMusicManager>(),
            Mock.Of<IDtoService>(),
            Mock.Of<IImageProcessor>(),
            Mock.Of<IServerApplicationHost>(),
            Mock.Of<IDeviceManager>(),
            Mock.Of<IMediaSourceManager>(),
            Mock.Of<IHostApplicationLifetime>(),
            Mock.Of<IPlaybackHistoryManager>());

        await Assert.ThrowsAsync(exceptionType, () => sessionManager.AuthenticateNewSessionInternal(authenticationRequest, false));
    }

    /// <summary>
    /// A client whose websocket drops before it posts its final stop report gets a brand-new
    /// <see cref="SessionInfo"/> for that request, with no play state and no transcoding info. The
    /// recorded history has to keep describing the transcode instead of reading as a direct play of
    /// the source streams.
    /// </summary>
    [Fact]
    public async Task OnPlaybackStopped_AfterSessionRecycled_Should_StillRecordTranscode()
    {
        const string DeviceId = "device_id";
        const string PlaySessionId = "play_session_id";

        var user = new User("test", "default", "default") { Id = Guid.NewGuid() };
        var item = new Movie { Id = Guid.NewGuid(), Name = "movie", RunTimeTicks = TimeSpan.FromHours(2).Ticks };

        var userManager = new Mock<IUserManager>();
        userManager.Setup(x => x.GetUserById(user.Id)).Returns(user);

        var configurationManager = new Mock<IServerConfigurationManager>();
        configurationManager.Setup(x => x.Configuration).Returns(new ServerConfiguration());

        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(x => x.GetItemById(item.Id)).Returns(item);

        var userDataManager = new Mock<IUserDataManager>();
        userDataManager.Setup(x => x.GetUserData(user, It.IsAny<BaseItem>())).Returns(new UserItemData { Key = "key" });
        // Completing the item is what gets the session recorded without waiting out the minimum span.
        userDataManager.Setup(x => x.UpdatePlayState(It.IsAny<BaseItem>(), It.IsAny<UserItemData>(), It.IsAny<long>())).Returns(true);

        PlaybackHistoryInfo? recorded = null;
        var playbackHistoryManager = new Mock<IPlaybackHistoryManager>();
        playbackHistoryManager
            .Setup(x => x.RecordPlaybackAsync(user, It.IsAny<BaseItem>(), It.IsAny<PlaybackHistoryInfo>(), It.IsAny<CancellationToken>()))
            .Callback<User, BaseItem, PlaybackHistoryInfo, CancellationToken>((_, _, info, _) => recorded = info)
            .Returns(Task.CompletedTask);

        await using var sessionManager = new Emby.Server.Implementations.Session.SessionManager(
            NullLogger<Emby.Server.Implementations.Session.SessionManager>.Instance,
            Mock.Of<IEventManager>(),
            userDataManager.Object,
            configurationManager.Object,
            libraryManager.Object,
            userManager.Object,
            Mock.Of<IMusicManager>(),
            Mock.Of<IDtoService>(),
            Mock.Of<IImageProcessor>(),
            Mock.Of<IServerApplicationHost>(),
            Mock.Of<IDeviceManager>(),
            Mock.Of<IMediaSourceManager>(),
            Mock.Of<IHostApplicationLifetime>(),
            playbackHistoryManager.Object);

        var session = await sessionManager.LogSessionActivity("app_name", "0.0.0", DeviceId, "device_name", "127.0.0.1", user);

        sessionManager.ReportTranscodingInfo(DeviceId, new TranscodingInfo
        {
            VideoCodec = "hevc",
            AudioCodec = "aac",
            AudioChannels = 2,
            Width = 1920,
            Height = 1080,
            Bitrate = 8_000_000,
            IsVideoDirect = false,
            IsAudioDirect = false
        });

        await sessionManager.OnPlaybackStart(new PlaybackStartInfo
        {
            SessionId = session.Id,
            ItemId = item.Id,
            PlaySessionId = PlaySessionId,
            PlayMethod = PlayMethod.Transcode,
            AudioStreamIndex = 2,
            PositionTicks = 0,
            Item = new BaseItemDto { Id = item.Id }
        });

        // The websocket closes: the device's session is torn down, then the final stop report comes
        // in over HTTP and gets a fresh, empty session under the same id.
        await sessionManager.ReportSessionEnded(session.Id);
        var recycled = await sessionManager.LogSessionActivity("app_name", "0.0.0", DeviceId, "device_name", "127.0.0.1", user);
        Assert.Null(recycled.TranscodingInfo);

        await sessionManager.OnPlaybackStopped(new PlaybackStopInfo
        {
            SessionId = recycled.Id,
            ItemId = item.Id,
            PlaySessionId = PlaySessionId,
            PositionTicks = TimeSpan.FromMinutes(30).Ticks,
            Item = new BaseItemDto
            {
                Id = item.Id,
                MediaStreams = new[]
                {
                    new MediaStream { Type = MediaStreamType.Video, Index = 0, Codec = "hevc", Width = 3840, Height = 2160, IsDefault = true },
                    new MediaStream { Type = MediaStreamType.Audio, Index = 1, Codec = "dts", Channels = 8, Language = "ger", IsDefault = true },
                    new MediaStream { Type = MediaStreamType.Audio, Index = 2, Codec = "dts", Channels = 8, Language = "eng" }
                }
            }
        });

        Assert.NotNull(recorded);
        Assert.True(recorded.Transcoded);

        // The audio track the client actually selected, not the default one.
        var sourceAudio = Assert.Single(
            recorded.Streams,
            s => s.StreamType == PlaybackHistoryStreamType.Audio && s.Origin == PlaybackHistoryStreamOrigin.Source);
        Assert.Equal("eng", sourceAudio.Language);

        var deliveredVideo = Assert.Single(
            recorded.Streams,
            s => s.StreamType == PlaybackHistoryStreamType.Video && s.Origin == PlaybackHistoryStreamOrigin.Delivered);
        Assert.Equal(1920, deliveredVideo.Width);
        Assert.Equal(1080, deliveredVideo.Height);

        var deliveredAudio = Assert.Single(
            recorded.Streams,
            s => s.StreamType == PlaybackHistoryStreamType.Audio && s.Origin == PlaybackHistoryStreamOrigin.Delivered);
        Assert.Equal("aac", deliveredAudio.Codec);
        Assert.Equal(2, deliveredAudio.Channels);
    }

    public static TheoryData<AuthenticationRequest, Type> AuthenticateNewSessionInternal_Exception_TestData()
    {
        var data = new TheoryData<AuthenticationRequest, Type>
        {
            {
                new AuthenticationRequest { App = string.Empty, DeviceId = "device_id", DeviceName = "device_name", AppVersion = "app_version" },
                typeof(ArgumentException)
            },
            {
                new AuthenticationRequest { App = null, DeviceId = "device_id", DeviceName = "device_name", AppVersion = "app_version" },
                typeof(ArgumentNullException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = string.Empty, DeviceName = "device_name", AppVersion = "app_version" },
                typeof(ArgumentException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = null, DeviceName = "device_name", AppVersion = "app_version" },
                typeof(ArgumentNullException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = "device_id", DeviceName = string.Empty, AppVersion = "app_version" },
                typeof(ArgumentException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = "device_id", DeviceName = null, AppVersion = "app_version" },
                typeof(ArgumentNullException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = "device_id", DeviceName = "device_name", AppVersion = string.Empty },
                typeof(ArgumentException)
            },
            {
                new AuthenticationRequest { App = "app_name", DeviceId = "device_id", DeviceName = "device_name", AppVersion = null },
                typeof(ArgumentNullException)
            }
        };

        return data;
    }
}
