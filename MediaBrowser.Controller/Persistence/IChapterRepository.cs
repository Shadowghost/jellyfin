using System;
using System.Collections.Generic;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Controller.Persistence;

/// <summary>
/// Interface IChapterRepository.
/// </summary>
public interface IChapterRepository
{
    /// <summary>
    /// Deletes the chapters.
    /// </summary>
    /// <param name="itemId">The item.</param>
    void DeleteChapters(Guid itemId);

    /// <summary>
    /// Saves the chapters.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="chapters">The set of chapters.</param>
    void SaveChapters(Guid itemId, IReadOnlyList<ChapterInfo> chapters);

    /// <summary>
    /// Gets all chapters associated with the baseItem.
    /// </summary>
    /// <param name="baseItemId">The BaseItems id.</param>
    /// <returns>A readonly list of chapter instances.</returns>
    IReadOnlyList<ChapterInfo> GetChapters(Guid baseItemId);

    /// <summary>
    /// Gets a single chapter of a BaseItem on a specific index.
    /// </summary>
    /// <param name="baseItemId">The BaseItems id.</param>
    /// <param name="index">The index of that chapter.</param>
    /// <returns>A chapter instance.</returns>
    ChapterInfo? GetChapter(Guid baseItemId, int index);
}
