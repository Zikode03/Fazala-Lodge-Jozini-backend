using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using FalazaLodge.Api.Data;

#nullable disable

namespace FalazaLodge.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260928130000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AdminUsers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AdminUsers", x => x.Id));

        migrationBuilder.CreateTable(
            name: "AuditLogs",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                AdminUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                AdminEmail = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                EntityType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                EntityId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Details = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AuditLogs", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ErrorLogs",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                TraceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Method = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                ExceptionType = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                StackTrace = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ErrorLogs", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Rooms",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Rooms", x => x.Id));

        migrationBuilder.CreateTable(
            name: "PasswordResetTokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AdminUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                TokenHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UsedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_PasswordResetTokens_AdminUsers_AdminUserId",
                    column: x => x.AdminUserId,
                    principalTable: "AdminUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BookingRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                EnquiryType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                CheckIn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                CheckOut = table.Column<DateOnly>(type: "TEXT", nullable: true),
                Adults = table.Column<int>(type: "INTEGER", nullable: false),
                Children = table.Column<int>(type: "INTEGER", nullable: false),
                RoomId = table.Column<Guid>(type: "TEXT", nullable: true),
                RoomPreference = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                GuestName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                Phone = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Details = table.Column<string>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookingRequests", x => x.Id);
                table.ForeignKey(
                    name: "FK_BookingRequests_Rooms_RoomId",
                    column: x => x.RoomId,
                    principalTable: "Rooms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(name: "IX_AdminUsers_Email", table: "AdminUsers", column: "Email", unique: true);
        migrationBuilder.CreateIndex(name: "IX_AuditLogs_CreatedAtUtc", table: "AuditLogs", column: "CreatedAtUtc");
        migrationBuilder.CreateIndex(name: "IX_ErrorLogs_CreatedAtUtc", table: "ErrorLogs", column: "CreatedAtUtc");
        migrationBuilder.CreateIndex(name: "IX_Rooms_Name", table: "Rooms", column: "Name", unique: true);
        migrationBuilder.CreateIndex(name: "IX_PasswordResetTokens_AdminUserId", table: "PasswordResetTokens", column: "AdminUserId");
        migrationBuilder.CreateIndex(name: "IX_PasswordResetTokens_TokenHash", table: "PasswordResetTokens", column: "TokenHash", unique: true);
        migrationBuilder.CreateIndex(name: "IX_BookingRequests_CheckIn", table: "BookingRequests", column: "CheckIn");
        migrationBuilder.CreateIndex(name: "IX_BookingRequests_CreatedAtUtc", table: "BookingRequests", column: "CreatedAtUtc");
        migrationBuilder.CreateIndex(name: "IX_BookingRequests_Status", table: "BookingRequests", column: "Status");
        migrationBuilder.CreateIndex(name: "IX_BookingRequests_RoomId_Status_CheckIn_CheckOut", table: "BookingRequests", columns: new[] { "RoomId", "Status", "CheckIn", "CheckOut" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AuditLogs");
        migrationBuilder.DropTable(name: "BookingRequests");
        migrationBuilder.DropTable(name: "ErrorLogs");
        migrationBuilder.DropTable(name: "PasswordResetTokens");
        migrationBuilder.DropTable(name: "Rooms");
        migrationBuilder.DropTable(name: "AdminUsers");
    }
}
