using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebChangeRadar.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitorContentFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentSelector",
                table: "Monitors",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeywordsJson",
                table: "Monitors",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentSelector",
                table: "Monitors");

            migrationBuilder.DropColumn(
                name: "KeywordsJson",
                table: "Monitors");
        }
    }
}
