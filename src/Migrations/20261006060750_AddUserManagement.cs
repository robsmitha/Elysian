using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddUserManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManagedUser",
                columns: table => new
                {
                    ManagedUserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntraObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UserType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    InvitationStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AccessStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AccountEnabled = table.Column<bool>(type: "bit", nullable: true),
                    AppRoleAssignmentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    InvitedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InvitedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedUser", x => x.ManagedUserId);
                    table.ForeignKey(
                        name: "FK_ManagedUser_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserAuditLog",
                columns: table => new
                {
                    UserAuditLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TargetManagedUserId = table.Column<int>(type: "int", nullable: true),
                    TargetEntraObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TargetEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAuditLog", x => x.UserAuditLogId);
                });

            migrationBuilder.CreateIndex(
                name: "AK_ManagedUser_Email",
                table: "ManagedUser",
                columns: new[] { "Email", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_ManagedUser_EntraObjectId",
                table: "ManagedUser",
                columns: new[] { "EntraObjectId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_ManagedUser_UserId",
                table: "ManagedUser",
                column: "UserId",
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserAuditLog_TargetManagedUserId",
                table: "UserAuditLog",
                column: "TargetManagedUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManagedUser");

            migrationBuilder.DropTable(
                name: "UserAuditLog");
        }
    }
}
