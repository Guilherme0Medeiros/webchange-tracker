using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace WebChangeRadar.Api.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261015030000_AddMonitorBaselineResetAt")]
public sealed class AddMonitorBaselineResetAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "BaselineResetAt",
            table: "Monitors",
            type: "timestamp with time zone",
            nullable: true);
        migrationBuilder.AlterColumn<DateTime>(
            name: "NextCheckAt", table: "Monitors", type: "timestamp with time zone",
            nullable: true, oldClrType: typeof(DateTime), oldType: "timestamp with time zone");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE \"Monitors\" SET \"NextCheckAt\" = NOW() WHERE \"NextCheckAt\" IS NULL");
        migrationBuilder.AlterColumn<DateTime>(
            name: "NextCheckAt", table: "Monitors", type: "timestamp with time zone",
            nullable: false, oldClrType: typeof(DateTime), oldType: "timestamp with time zone", oldNullable: true);
        migrationBuilder.DropColumn(name: "BaselineResetAt", table: "Monitors");
    }
}
