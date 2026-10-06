using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddBillTrackingSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "BillTracking",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "BillType",
                table: "BillTracking",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BillUpdateDate",
                table: "BillTracking",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "BillTracking",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "BillTracking",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "HasUnseenActivity",
                table: "BillTracking",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IntroducedDate",
                table: "BillTracking",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "BillTracking",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastViewedAt",
                table: "BillTracking",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LatestActionDate",
                table: "BillTracking",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LatestActionText",
                table: "BillTracking",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LawNumber",
                table: "BillTracking",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifiedAt",
                table: "BillTracking",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "ModifiedByUserId",
                table: "BillTracking",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "BillTracking",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginChamber",
                table: "BillTracking",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyArea",
                table: "BillTracking",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SnapshotRefreshedAt",
                table: "BillTracking",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "SponsorBioguideId",
                table: "BillTracking",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SponsorName",
                table: "BillTracking",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SponsorParty",
                table: "BillTracking",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SponsorState",
                table: "BillTracking",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "BillTracking",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "AK_BillTracking_UserId_Bill",
                table: "BillTracking",
                columns: new[] { "UserId", "Congress", "BillType", "BillNumber", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "AK_BillTracking_UserId_Bill",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "BillUpdateDate",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "HasUnseenActivity",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "IntroducedDate",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "LastViewedAt",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "LatestActionDate",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "LatestActionText",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "LawNumber",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "ModifiedByUserId",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "OriginChamber",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "PolicyArea",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "SnapshotRefreshedAt",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "SponsorBioguideId",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "SponsorName",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "SponsorParty",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "SponsorState",
                table: "BillTracking");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "BillTracking");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "BillTracking",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "BillType",
                table: "BillTracking",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);
        }
    }
}
