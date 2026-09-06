using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Server.Implementations.Events.Consumers.Users
{
    /// <summary>
    /// Purges a deleted user's playback history. The history store keeps <c>UserId</c> as a plain
    /// column rather than a foreign key, so these rows have to be removed deliberately.
    /// </summary>
    public class PlaybackHistoryUserCleanup : IEventConsumer<UserDeletedEventArgs>
    {
        private readonly IPlaybackHistoryManager _playbackHistoryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlaybackHistoryUserCleanup"/> class.
        /// </summary>
        /// <param name="playbackHistoryManager">The playback history manager.</param>
        public PlaybackHistoryUserCleanup(IPlaybackHistoryManager playbackHistoryManager)
        {
            _playbackHistoryManager = playbackHistoryManager;
        }

        /// <inheritdoc />
        public async Task OnEvent(UserDeletedEventArgs eventArgs)
        {
            await _playbackHistoryManager.DeleteUserHistoryAsync(eventArgs.Argument.Id).ConfigureAwait(false);
        }
    }
}
