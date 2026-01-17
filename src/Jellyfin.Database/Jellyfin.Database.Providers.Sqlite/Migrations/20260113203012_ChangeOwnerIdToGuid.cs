using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Server.Implementations.Migrations
{
    /// <inheritdoc />
    public partial class ChangeOwnerIdToGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Normalize OwnerId to uppercase GUID format
            migrationBuilder.Sql(
                @"UPDATE BaseItems
                  SET OwnerId = UPPER(OwnerId)
                  WHERE OwnerId IS NOT NULL");

            // Clear invalid OwnerId values (not 36 characters = not a valid GUID)
            migrationBuilder.Sql(
                @"UPDATE BaseItems
                  SET OwnerId = null
                  WHERE OwnerId IS NOT NULL AND length(OwnerId) != 36");

            // Clear placeholder/empty GUIDs
            migrationBuilder.UpdateData(
                table: "BaseItems",
                keyColumn: "OwnerId",
                keyValue: new Guid("00000000-0000-0000-0000-000000000000"),
                column: "OwnerId",
                value: null);

            migrationBuilder.UpdateData(
                table: "BaseItems",
                keyColumn: "OwnerId",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "OwnerId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_BaseItems_ExtraType",
                table: "BaseItems",
                column: "ExtraType");

            migrationBuilder.CreateIndex(
                name: "IX_BaseItems_OwnerId",
                table: "BaseItems",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_BaseItems_ExtraType_OwnerId",
                table: "BaseItems",
                columns: new[] { "ExtraType", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BaseItems",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "OwnerId",
                value: null);

            migrationBuilder.Sql(
                @"UPDATE BaseItems
                  SET OwnerId = LOWER(OwnerId)
                  WHERE OwnerId IS NOT NULL");

            migrationBuilder.DropIndex(
                name: "IX_BaseItems_ExtraType",
                table: "BaseItems");

            migrationBuilder.DropIndex(
                name: "IX_BaseItems_ExtraType_OwnerId",
                table: "BaseItems");

            migrationBuilder.DropIndex(
                name: "IX_BaseItems_OwnerId",
                table: "BaseItems");
        }
    }
}
