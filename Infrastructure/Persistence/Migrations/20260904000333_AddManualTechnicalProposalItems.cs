using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualTechnicalProposalItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requirement_technical_proposal_items_confidence",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.AlterColumn<Guid>(
                name: "requirement_extracted_item_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<int>(
                name: "base_height_millimeters",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "base_quantity",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "base_width_millimeters",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "element_type",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "manual_note",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sequence",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE core.requirement_technical_proposal_items AS proposal_item
                SET source = 'AiExtracted',
                    sequence = extracted_item.sequence,
                    reference = extracted_item.reference,
                    description = extracted_item.description,
                    element_type = extracted_item.element_type,
                    base_quantity = extracted_item.quantity,
                    base_width_millimeters = extracted_item.width_millimeters,
                    base_height_millimeters = extracted_item.height_millimeters
                FROM core.requirement_extracted_items AS extracted_item
                WHERE proposal_item.requirement_extracted_item_id = extracted_item.id;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_requirement_technical_proposal_items_proposal_sequence",
                schema: "core",
                table: "requirement_technical_proposal_items",
                columns: new[] { "technical_proposal_id", "sequence" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_requirement_technical_proposal_items_confidence",
                schema: "core",
                table: "requirement_technical_proposal_items",
                sql: "\"overall_confidence\" >= 0 AND \"overall_confidence\" <= 1 AND \"system_confidence\" >= 0 AND \"system_confidence\" <= 1 AND \"glass_confidence\" >= 0 AND \"glass_confidence\" <= 1 AND \"finish_confidence\" >= 0 AND \"finish_confidence\" <= 1 AND (\"historical_best_similarity\" IS NULL OR (\"historical_best_similarity\" >= 0 AND \"historical_best_similarity\" <= 1)) AND (\"historical_average_similarity\" IS NULL OR (\"historical_average_similarity\" >= 0 AND \"historical_average_similarity\" <= 1)) AND (\"manual_quantity_override\" IS NULL OR \"manual_quantity_override\" > 0) AND (\"manual_width_millimeters_override\" IS NULL OR \"manual_width_millimeters_override\" > 0) AND (\"manual_height_millimeters_override\" IS NULL OR \"manual_height_millimeters_override\" > 0) AND (\"base_quantity\" IS NULL OR \"base_quantity\" > 0) AND (\"base_width_millimeters\" IS NULL OR \"base_width_millimeters\" > 0) AND (\"base_height_millimeters\" IS NULL OR \"base_height_millimeters\" > 0) AND \"sequence\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_requirement_technical_proposal_items_source_extracted_item",
                schema: "core",
                table: "requirement_technical_proposal_items",
                sql: "(\"source\" = 'AiExtracted' AND \"requirement_extracted_item_id\" IS NOT NULL) OR (\"source\" = 'Manual' AND \"requirement_extracted_item_id\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_requirement_technical_proposal_items_proposal_sequence",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_requirement_technical_proposal_items_confidence",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_requirement_technical_proposal_items_source_extracted_item",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "base_height_millimeters",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "base_quantity",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "base_width_millimeters",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "element_type",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "manual_note",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "reference",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "sequence",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "core",
                table: "requirement_technical_proposal_items");

            migrationBuilder.Sql("""
                DELETE FROM core.requirement_technical_proposal_items
                WHERE requirement_extracted_item_id IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "requirement_extracted_item_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_requirement_technical_proposal_items_confidence",
                schema: "core",
                table: "requirement_technical_proposal_items",
                sql: "\"overall_confidence\" >= 0 AND \"overall_confidence\" <= 1 AND \"system_confidence\" >= 0 AND \"system_confidence\" <= 1 AND \"glass_confidence\" >= 0 AND \"glass_confidence\" <= 1 AND \"finish_confidence\" >= 0 AND \"finish_confidence\" <= 1 AND (\"historical_best_similarity\" IS NULL OR (\"historical_best_similarity\" >= 0 AND \"historical_best_similarity\" <= 1)) AND (\"historical_average_similarity\" IS NULL OR (\"historical_average_similarity\" >= 0 AND \"historical_average_similarity\" <= 1)) AND (\"manual_quantity_override\" IS NULL OR \"manual_quantity_override\" > 0) AND (\"manual_width_millimeters_override\" IS NULL OR \"manual_width_millimeters_override\" > 0) AND (\"manual_height_millimeters_override\" IS NULL OR \"manual_height_millimeters_override\" > 0)");
        }
    }
}
