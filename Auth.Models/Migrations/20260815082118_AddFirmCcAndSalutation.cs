using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddFirmCcAndSalutation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CcEmail",
                table: "Firms",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Salutation",
                table: "Firms",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CcEmail",
                table: "Firms");

            migrationBuilder.DropColumn(
                name: "Salutation",
                table: "Firms");
        }
    }
}
