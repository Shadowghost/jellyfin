#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace MediaBrowser.Controller.Persistence;

public interface IPeopleRepository
{
    /// <summary>
    /// Gets the people.
    /// </summary>
    /// <param name="filter">The query.</param>
    /// <returns>The list of people matching the filter.</returns>
    IReadOnlyList<PersonInfo> GetPeople(InternalPeopleQuery filter);

    /// <summary>
    /// Updates the people.
    /// </summary>
    /// <param name="itemId">The item identifier.</param>
    /// <param name="people">The people.</param>
    void UpdatePeople(Guid itemId, IReadOnlyList<PersonInfo> people);

    /// <summary>
    /// Gets the people names.
    /// </summary>
    /// <param name="filter">The query.</param>
    /// <returns>The list of people names matching the filter.</returns>
    IReadOnlyList<string> GetPeopleNames(InternalPeopleQuery filter);

    /// <summary>
    /// Gets distinct people names for multiple items efficiently by querying from the mapping table.
    /// </summary>
    /// <param name="itemIds">The item IDs to get people for.</param>
    /// <param name="personTypes">The person types to include (e.g. "Actor", "Director").</param>
    /// <param name="maxListOrder">When set, only include people whose ListOrder for a given item is at most this value (i.e. top-billed).</param>
    /// <param name="limit">Maximum number of names to return.</param>
    /// <returns>The distinct people names ordered by frequency then billing prominence.</returns>
    IReadOnlyList<string> GetPeopleNamesByItems(IReadOnlyList<Guid> itemIds, IReadOnlyList<string> personTypes, int? maxListOrder, int limit);
}
