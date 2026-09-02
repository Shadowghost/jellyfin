using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using Emby.Server.Implementations.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Server.Implementations.Item;
using MediaBrowser.Controller.Entities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using BaseItemKind = Jellyfin.Data.Enums.BaseItemKind;

namespace Jellyfin.Server.Implementations.Tests.Item;

/// <summary>
/// Covers the item value filter queries. These assert on the generated SQL as well as on the results,
/// because the defect being guarded is a query shape, not a wrong answer: EF Core translates a GroupBy
/// over a projected navigation property into a correlated scalar subquery repeated in the projection and
/// the ORDER BY, which is correct but takes minutes on a library with tens of thousands of tags.
/// </summary>
public sealed class BaseItemRepositoryLegacyFilterTests : SqliteDbTestFixture
{
    private readonly CommandRecordingInterceptor _interceptor;
    private readonly BaseItemRepository _repository;
    private readonly string _audioTypeName;
    private readonly string _movieTypeName;

    public BaseItemRepositoryLegacyFilterTests()
        : this(new CommandRecordingInterceptor())
    {
    }

    private BaseItemRepositoryLegacyFilterTests(CommandRecordingInterceptor interceptor)
        : base(interceptor)
    {
        _interceptor = interceptor;

        var itemTypeLookup = new ItemTypeLookup();
        _audioTypeName = itemTypeLookup.BaseItemKindNames[BaseItemKind.Audio];
        _movieTypeName = itemTypeLookup.BaseItemKindNames[BaseItemKind.Movie];
        _repository = CreateBaseItemRepository(itemTypeLookup);
    }

    /// <summary>
    /// Both scan shapes have to return the same normalized values, and neither may fall back to a
    /// correlated aggregate. The filler items push the matched share of the library below the threshold
    /// that selects the map-driven shape, so the two cases exercise opposite branches.
    /// </summary>
    /// <param name="fillerItemCount">Number of non-matching items to seed alongside the two movies.</param>
    /// <param name="expectValueDrivenScan">Whether the seeded ratio should select the ItemValues-driven shape.</param>
    [Theory]
    [InlineData(0, true)]
    [InlineData(40, false)]
    public void GetQueryFiltersLegacy_ItemValues_GroupsByCleanValueWithoutCorrelatedAggregate(int fillerItemCount, bool expectValueDrivenScan)
    {
        var firstItem = CreateMovieEntity(Guid.NewGuid(), "First");
        var secondItem = CreateMovieEntity(Guid.NewGuid(), "Second");
        var excludedItem = CreateAudioEntity(Guid.NewGuid(), "Excluded Audio");

        // "Alpha" and "alpha" normalize to the same CleanValue. SQLite's default BINARY collation orders
        // upper case first, so MIN over the group is "Alpha" - the query picks a representative by
        // collation order, it does not case-fold.
        var firstTag = CreateItemValue(ItemValueType.Tags, "Alpha", "alpha");
        var duplicateTag = CreateItemValue(ItemValueType.Tags, "alpha", "alpha");
        var secondTag = CreateItemValue(ItemValueType.Tags, "Beta", "beta");
        var genre = CreateItemValue(ItemValueType.Genre, "Genre Leak", "genre leak");
        var excludedTag = CreateItemValue(ItemValueType.Tags, "Excluded Tag", "excluded tag");
        var excludedGenre = CreateItemValue(ItemValueType.Genre, "Excluded Genre", "excluded genre");

        using (var context = CreateDbContext())
        {
            context.BaseItems.AddRange(firstItem, secondItem, excludedItem);
            for (var i = 0; i < fillerItemCount; i++)
            {
                context.BaseItems.Add(CreateAudioEntity(Guid.NewGuid(), "Filler " + i.ToString(CultureInfo.InvariantCulture)));
            }

            context.ItemValues.AddRange(firstTag, duplicateTag, secondTag, genre, excludedTag, excludedGenre);
            context.ItemValuesMap.AddRange(
                CreateMap(firstItem, firstTag),
                CreateMap(firstItem, duplicateTag),
                CreateMap(secondItem, secondTag),
                CreateMap(firstItem, genre),
                CreateMap(excludedItem, excludedTag),
                CreateMap(excludedItem, excludedGenre));
            context.SaveChanges();
        }

        _interceptor.Commands.Clear();

        var result = _repository.GetQueryFiltersLegacy(new InternalItemsQuery(new Database.Implementations.Entities.User("test", "auth", "reset"))
        {
            IncludeItemTypes = [BaseItemKind.Movie]
        });

        Assert.Equal(["Alpha", "Beta"], result.Tags);
        Assert.Equal(["Genre Leak"], result.Genres);

        foreach (var itemValueType in new[] { ItemValueType.Tags, ItemValueType.Genre })
        {
            var command = SingleGroupedCommandFor(itemValueType);
            AssertNoCorrelatedAggregate(command);

            // Key on the driving table: the value-driven shape selects FROM ItemValues and probes the map
            // through an EXISTS, while the map-driven shape selects FROM ItemValuesMap and joins
            // ItemValues. Both statements mention ItemValuesMap somewhere, so only the FROM separates them.
            var isValueDriven = command.Contains("FROM \"ItemValues\" AS", StringComparison.Ordinal);
            Assert.Equal(expectValueDrivenScan, isValueDriven);
        }
    }

    /// <summary>
    /// GetItemValueNames feeds the library validators and has no item filter at all, so it always reads
    /// every value of the requested types and must never take the correlated shape either.
    /// </summary>
    [Fact]
    public void GetGenreNames_GroupsByCleanValueWithoutCorrelatedAggregate()
    {
        var item = CreateMovieEntity(Guid.NewGuid(), "First");
        var firstGenre = CreateItemValue(ItemValueType.Genre, "Comedy", "comedy");
        var duplicateGenre = CreateItemValue(ItemValueType.Genre, "comedy", "comedy");
        var unrelatedTag = CreateItemValue(ItemValueType.Tags, "Some Tag", "some tag");

        using (var context = CreateDbContext())
        {
            context.BaseItems.Add(item);
            context.ItemValues.AddRange(firstGenre, duplicateGenre, unrelatedTag);
            context.ItemValuesMap.AddRange(
                CreateMap(item, firstGenre),
                CreateMap(item, duplicateGenre),
                CreateMap(item, unrelatedTag));
            context.SaveChanges();
        }

        _interceptor.Commands.Clear();

        var names = _repository.GetGenreNames();

        Assert.Equal(["Comedy"], names);
        AssertNoCorrelatedAggregate(Assert.Single(_interceptor.Commands, c => IsGroupedCommand(c.Sql)).Sql);
    }

    /// <summary>
    /// An unmapped value belongs to no item, so it must not appear in the filters regardless of which
    /// scan shape ran. The value-driven shape reads ItemValues directly and would return it without the
    /// existence check.
    /// </summary>
    [Fact]
    public void GetQueryFiltersLegacy_OrphanedItemValue_IsNotReturned()
    {
        var item = CreateMovieEntity(Guid.NewGuid(), "First");
        var mappedTag = CreateItemValue(ItemValueType.Tags, "Mapped", "mapped");
        var orphanedTag = CreateItemValue(ItemValueType.Tags, "Orphaned", "orphaned");

        using (var context = CreateDbContext())
        {
            context.BaseItems.Add(item);
            context.ItemValues.AddRange(mappedTag, orphanedTag);
            context.ItemValuesMap.Add(CreateMap(item, mappedTag));
            context.SaveChanges();
        }

        var result = _repository.GetQueryFiltersLegacy(new InternalItemsQuery(new Database.Implementations.Entities.User("test", "auth", "reset"))
        {
            IncludeItemTypes = [BaseItemKind.Movie]
        });

        Assert.Equal(["Mapped"], result.Tags);
    }

    /// <summary>
    /// The regression is a correlated scalar aggregate: EF Core wraps the MIN in a nested
    /// "SELECT (SELECT MIN(...) ... WHERE outer.CleanValue = inner.CleanValue)" that it repeats in the
    /// projection and the ORDER BY, joining ItemValues twice more inside it. Counting MIN does not
    /// separate the two shapes - both mention it twice - so assert on the correlated projection and on
    /// the repeated join instead. Before the fix this saw five ItemValues joins.
    /// </summary>
    /// <param name="command">The recorded SQL statement.</param>
    private static void AssertNoCorrelatedAggregate(string command)
    {
        Assert.DoesNotContain("SELECT (", command, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(command, "INNER JOIN \"ItemValues\"") <= 1,
            "ItemValues is joined more than once, the aggregate is correlated:\n" + command);
    }

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private static bool IsGroupedCommand(string sql)
        => sql.Contains("GROUP BY", StringComparison.Ordinal);

    private static ItemValueMap CreateMap(BaseItemEntity item, ItemValue itemValue)
    {
        return new ItemValueMap
        {
            ItemId = item.Id,
            ItemValueId = itemValue.ItemValueId,
            Item = item,
            ItemValue = itemValue
        };
    }

    private static ItemValue CreateItemValue(ItemValueType type, string value, string cleanValue)
    {
        return new ItemValue
        {
            ItemValueId = Guid.NewGuid(),
            Type = type,
            Value = value,
            CleanValue = cleanValue
        };
    }

    /// <summary>
    /// Finds the grouped statement for one value type. Tags and genres share one statement that differs
    /// only in its bound type parameter, so the parameter value is what identifies them - and matching on
    /// the enum rather than on a literal in the SQL keeps this working if ItemValueType is renumbered or
    /// if EF Core stops parameterizing the filter.
    /// </summary>
    /// <param name="itemValueType">The value type whose statement to find.</param>
    /// <returns>The single matching statement.</returns>
    private string SingleGroupedCommandFor(ItemValueType itemValueType)
    {
        var expected = (long)itemValueType;
        var literal = "\"Type\" = " + expected.ToString(CultureInfo.InvariantCulture);

        return Assert.Single(
            _interceptor.Commands,
            c => IsGroupedCommand(c.Sql)
                && (c.Parameters.Any(v => v is int or long && Convert.ToInt64(v, CultureInfo.InvariantCulture) == expected)
                    || c.Sql.Contains(literal, StringComparison.Ordinal))).Sql;
    }

    private BaseItemEntity CreateMovieEntity(Guid id, string name)
    {
        return new BaseItemEntity
        {
            Id = id,
            Type = _movieTypeName,
            Name = name,
            MediaType = "Video",
            IsMovie = true,
            IsFolder = false,
            IsVirtualItem = false
        };
    }

    private BaseItemEntity CreateAudioEntity(Guid id, string name)
    {
        return new BaseItemEntity
        {
            Id = id,
            Type = _audioTypeName,
            Name = name,
            MediaType = "Audio",
            IsMovie = false,
            IsFolder = false,
            IsVirtualItem = false
        };
    }

    private sealed record RecordedCommand(string Sql, IReadOnlyList<object?> Parameters);

    private sealed class CommandRecordingInterceptor : DbCommandInterceptor
    {
        public List<RecordedCommand> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(new RecordedCommand(
                command.CommandText,
                command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray()));
            return result;
        }
    }
}
