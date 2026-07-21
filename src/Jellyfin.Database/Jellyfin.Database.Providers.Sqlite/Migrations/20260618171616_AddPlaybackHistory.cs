using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Server.Implementations.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaybackHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaybackItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    MediaType = table.Column<string>(type: "TEXT", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlaybackItemKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaybackItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackItemKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaybackItemKeys_PlaybackItems_PlaybackItemId",
                        column: x => x.PlaybackItemId,
                        principalTable: "PlaybackItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserPlaybackHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaybackItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DateStarted = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateStopped = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartPositionTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    StopPositionTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    RunTimeTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    PlayedDurationTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayedToCompletion = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlaySessionId = table.Column<string>(type: "TEXT", nullable: true),
                    MediaSourceId = table.Column<string>(type: "TEXT", nullable: true),
                    Transcoded = table.Column<bool>(type: "INTEGER", nullable: false),
                    Bitrate = table.Column<int>(type: "INTEGER", nullable: true),
                    ActualBytesTransferred = table.Column<long>(type: "INTEGER", nullable: true),
                    DeviceId = table.Column<string>(type: "TEXT", nullable: true),
                    DeviceName = table.Column<string>(type: "TEXT", nullable: true),
                    ClientName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPlaybackHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPlaybackHistory_PlaybackItems_PlaybackItemId",
                        column: x => x.PlaybackItemId,
                        principalTable: "PlaybackItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserPlaybackHistoryStreams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HistoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StreamType = table.Column<int>(type: "INTEGER", nullable: false),
                    Origin = table.Column<int>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    VideoRange = table.Column<string>(type: "TEXT", nullable: true),
                    Codec = table.Column<string>(type: "TEXT", nullable: true),
                    Bitrate = table.Column<int>(type: "INTEGER", nullable: true),
                    Channels = table.Column<int>(type: "INTEGER", nullable: true),
                    Language = table.Column<string>(type: "TEXT", nullable: true),
                    IsForced = table.Column<bool>(type: "INTEGER", nullable: true),
                    IsHearingImpaired = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPlaybackHistoryStreams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPlaybackHistoryStreams_UserPlaybackHistory_HistoryId",
                        column: x => x.HistoryId,
                        principalTable: "UserPlaybackHistory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackItemKeys_Key",
                table: "PlaybackItemKeys",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackItemKeys_PlaybackItemId",
                table: "PlaybackItemKeys",
                column: "PlaybackItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackItems_ItemId",
                table: "PlaybackItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPlaybackHistory_PlaybackItemId_PlayedToCompletion",
                table: "UserPlaybackHistory",
                columns: new[] { "PlaybackItemId", "PlayedToCompletion" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPlaybackHistory_UserId_DateStopped",
                table: "UserPlaybackHistory",
                columns: new[] { "UserId", "DateStopped" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPlaybackHistory_UserId_PlaybackItemId_DateStopped",
                table: "UserPlaybackHistory",
                columns: new[] { "UserId", "PlaybackItemId", "DateStopped" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPlaybackHistoryStreams_HistoryId",
                table: "UserPlaybackHistoryStreams",
                column: "HistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPlaybackHistoryStreams_StreamType_Origin_Language",
                table: "UserPlaybackHistoryStreams",
                columns: new[] { "StreamType", "Origin", "Language" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPlaybackHistoryStreams_StreamType_Origin_VideoRange",
                table: "UserPlaybackHistoryStreams",
                columns: new[] { "StreamType", "Origin", "VideoRange" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaybackItemKeys");

            migrationBuilder.DropTable(
                name: "UserPlaybackHistoryStreams");

            migrationBuilder.DropTable(
                name: "UserPlaybackHistory");

            migrationBuilder.DropTable(
                name: "PlaybackItems");
        }
    }
}
