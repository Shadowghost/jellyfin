using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Library
{
    /// <summary>
    /// Interface IUserDataManager.
    /// </summary>
    public interface IUserDataManager
    {
        /// <summary>
        /// Occurs when [user data saved].
        /// </summary>
        event EventHandler<UserDataSaveEventArgs>? UserDataSaved;

        /// <summary>
        /// Saves the user data.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="item">The item.</param>
        /// <param name="userData">The user data.</param>
        /// <param name="reason">The reason.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        void SaveUserData(User user, BaseItem item, UserItemData userData, UserDataSaveReason reason, CancellationToken cancellationToken);

        /// <summary>
        /// Save the provided user data for the given user.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="item">The item.</param>
        /// <param name="userDataDto">The reason for updating the user data.</param>
        /// <param name="reason">The reason.</param>
        void SaveUserData(User user, BaseItem item, UpdateUserItemDataDto userDataDto, UserDataSaveReason reason);

        /// <summary>
        /// Gets the user data.
        /// </summary>
        /// <param name="user">User to use.</param>
        /// <param name="item">Item to use.</param>
        /// <returns>User data.</returns>
        UserItemData? GetUserData(User user, BaseItem item);

        /// <summary>
        /// Gets the user data dto.
        /// </summary>
        /// <param name="item">Item to use.</param>
        /// <param name="user">User to use.</param>
        /// <returns>User data dto.</returns>
        UserItemDataDto? GetUserDataDto(BaseItem item, User user);

        /// <summary>
        /// Gets user data for multiple items in a single batch operation.
        /// </summary>
        /// <param name="items">The items to get user data for.</param>
        /// <param name="user">The user.</param>
        /// <returns>A dictionary mapping item IDs to their user data.</returns>
        Dictionary<Guid, UserItemData> GetUserDataBatch(IReadOnlyList<BaseItem> items, User user);

        /// <summary>
        /// Gets the user data that should drive resume for a multi-version item: the data of the most
        /// recently played alternate version (including the item itself) that has a resume point.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="item">The item.</param>
        /// <returns>The resume version's data, or <c>null</c> when the item has no versions or none has a resume point.</returns>
        VersionResumeData? GetResumeUserData(User user, BaseItem item);

        /// <summary>
        /// Gets the resume-driving user data for multiple items in a single batch operation.
        /// See <see cref="GetResumeUserData(User, BaseItem)"/>.
        /// </summary>
        /// <param name="items">The items to get resume data for.</param>
        /// <param name="user">The user.</param>
        /// <returns>A dictionary mapping item ids to their resume version's data; items without one are omitted.</returns>
        IReadOnlyDictionary<Guid, VersionResumeData> GetResumeUserDataBatch(IReadOnlyList<BaseItem> items, User user);

        /// <summary>
        /// Gets the user data dto.
        /// </summary>
        /// <param name="item">Item to use.</param>
        /// <param name="itemDto">Item dto to use.</param>
        /// <param name="user">User to use.</param>
        /// <param name="options">Dto options to use.</param>
        /// <returns>User data dto.</returns>
        UserItemDataDto? GetUserDataDto(BaseItem item, BaseItemDto? itemDto, User user, DtoOptions options);

        /// <summary>
        /// Updates playstate for an item and returns true or false indicating if it was played to completion.
        /// </summary>
        /// <param name="item">Item to update.</param>
        /// <param name="data">Data to update.</param>
        /// <param name="reportedPositionTicks">New playstate.</param>
        /// <returns>True if playstate was updated.</returns>
        bool UpdatePlayState(BaseItem item, UserItemData data, long? reportedPositionTicks);

        /// <summary>
        /// Rewrites the played state, play count, and played date this user has for an item from its
        /// recorded playback history, and saves the result.
        /// </summary>
        /// <remarks>
        /// The history is the source of truth for what was played; these columns are the projection of
        /// it that the item queries read, because a played/unplayed filter cannot afford an aggregate
        /// over one row per play. The resume position is not part of the projection - it is mutable
        /// state governed by the resume thresholds and has no equivalent in an append-only record.
        /// </remarks>
        /// <param name="user">The user.</param>
        /// <param name="item">The item.</param>
        /// <param name="stats">The item's totals, from <see cref="IPlaybackHistoryManager.GetUserItemStatsAsync"/>.</param>
        void ApplyPlaybackStats(User user, BaseItem item, PlaybackItemStats stats);

        /// <summary>
        /// Drops the in-memory user data cache.
        /// </summary>
        /// <remarks>
        /// Needed after the projection is rebuilt in bulk: that rewrite goes straight to the database
        /// to avoid a transaction per item, so nothing here observes it and the cache would keep
        /// serving the values it replaced.
        /// </remarks>
        void ClearCache();

        /// <summary>
        /// Clears any stored audio and subtitle stream selections for the given user/item pair.
        /// Used when the user has opted out of remembering selections.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="item">The item.</param>
        void ResetPlaybackStreamSelections(User user, BaseItem item);
    }
}
