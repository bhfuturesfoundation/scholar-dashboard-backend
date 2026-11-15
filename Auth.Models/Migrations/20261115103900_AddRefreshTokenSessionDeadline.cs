using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenSessionDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SessionExpiresAt",
                table: "RefreshTokens",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionExpiresAt",
                table: "RefreshTokens");
        }
    }
}
