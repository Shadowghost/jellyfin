using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Server.Implementations.Migrations
{
    /// <inheritdoc />
    public partial class DropExtraIdsColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraIds",
                table: "BaseItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtraIds",
                table: "BaseItems",
                type: "TEXT",
                nullable: true);

            // Re-populate ExtraIds by concatenating the IDs of linked extras with '|'
            // Exclude additional parts by filtering ExtraType IS NOT NULL
            // Additional parts have NULL ExtraType, while actual extras have a valid ExtraType value
            migrationBuilder.Sql(
                @"UPDATE BaseItems
                  SET ExtraIds = (
                      SELECT GROUP_CONCAT(LOWER(Extras.Id), '|')
                      FROM BaseItems AS Extras
                      WHERE Extras.OwnerId = BaseItems.Id
                        AND Extras.ExtraType IS NOT NULL
                  )
                  WHERE EXISTS (
                      SELECT 1
                      FROM BaseItems AS Extras
                      WHERE Extras.OwnerId = BaseItems.Id
                        AND Extras.ExtraType IS NOT NULL
                  )");

            migrationBuilder.Sql(
                @"DELETE FROM __EFMigrationsHistory
                  WHERE MigrationId = '20260113230000_CleanupOrphanedExtras'");
        }
    }
}
