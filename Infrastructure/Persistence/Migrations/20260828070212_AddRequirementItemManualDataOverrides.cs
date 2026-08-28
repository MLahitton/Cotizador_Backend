using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementItemManualDataOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requirement_technical_proposal_items_confidence",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.AddColumn<int>(
                name: "manual_height_millimeters_override",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "manual_quantity_override",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "manual_width_millimeters_override",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_requirement_technical_proposal_items_confidence",
                schema: "core",
                table: "requirement_technical_proposal_items",
                sql: "\"overall_confidence\" >= 0 AND \"overall_confidence\" <= 1 AND \"system_confidence\" >= 0 AND \"system_confidence\" <= 1 AND \"glass_confidence\" >= 0 AND \"glass_confidence\" <= 1 AND \"finish_confidence\" >= 0 AND \"finish_confidence\" <= 1 AND (\"historical_best_similarity\" IS NULL OR (\"historical_best_similarity\" >= 0 AND \"historical_best_similarity\" <= 1)) AND (\"historical_average_similarity\" IS NULL OR (\"historical_average_similarity\" >= 0 AND \"historical_average_similarity\" <= 1)) AND (\"manual_quantity_override\" IS NULL OR \"manual_quantity_override\" > 0) AND (\"manual_width_millimeters_override\" IS NULL OR \"manual_width_millimeters_override\" > 0) AND (\"manual_height_millimeters_override\" IS NULL OR \"manual_height_millimeters_override\" > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requirement_technical_proposal_items_confidence",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "manual_height_millimeters_override",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "manual_quantity_override",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "manual_width_millimeters_override",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.AddCheckConstraint(
                name: "ck_requirement_technical_proposal_items_confidence",
                schema: "core",
                table: "requirement_technical_proposal_items",
                sql: "\"overall_confidence\" >= 0 AND \"overall_confidence\" <= 1 AND \"system_confidence\" >= 0 AND \"system_confidence\" <= 1 AND \"glass_confidence\" >= 0 AND \"glass_confidence\" <= 1 AND \"finish_confidence\" >= 0 AND \"finish_confidence\" <= 1 AND (\"historical_best_similarity\" IS NULL OR (\"historical_best_similarity\" >= 0 AND \"historical_best_similarity\" <= 1)) AND (\"historical_average_similarity\" IS NULL OR (\"historical_average_similarity\" >= 0 AND \"historical_average_similarity\" <= 1))");
        }
    }
}
