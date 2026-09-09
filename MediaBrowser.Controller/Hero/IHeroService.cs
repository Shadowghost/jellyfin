using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Dto;

namespace MediaBrowser.Controller.Hero;

/// <summary>
/// Picks and describes the items shown by the hero section.
/// </summary>
public interface IHeroService
{
    /// <summary>
    /// Gets the items the hero section should show for a user.
    /// </summary>
    /// <param name="user">The user the items are picked for.</param>
    /// <param name="parentId">Optional. Restrict the items to a library or folder.</param>
    /// <param name="limit">Optional. The number of items to return, capped by the configuration.</param>
    /// <returns>
    /// The hero items, or an empty list when the hero section is disabled or nothing qualifies.
    /// </returns>
    IReadOnlyList<HeroItemDto> GetHeroItems(User user, Guid? parentId, int? limit);
}
