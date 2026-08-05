using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreliminaryEconomicPricingV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "accessories_expected_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "accessories_maximum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "accessories_minimum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "accessory_factor",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "assembly_expected_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "assembly_maximum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "assembly_minimum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assembly_profile_code",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "assumptions",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<decimal>(
                name: "billable_area_unit_square_meters",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "calculated_at_utc",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "confidence_level",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "confidence_score",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "finish_code",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "finish_factor_expected",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "finish_factor_maximum",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "finish_factor_minimum",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "frame_code",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "glass_code",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "glass_expected_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "glass_expected_price_per_square_meter",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "glass_maximum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "glass_maximum_price_per_square_meter",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "glass_minimum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "glass_minimum_price_per_square_meter",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "glass_price_range_version",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "item_expected_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "item_maximum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "item_minimum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "labor_expected_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "labor_maximum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "labor_minimum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "labor_profile_code",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "missing_data",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "pricing_profile_version",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_review",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "system_code",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "system_source",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accessories_expected_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "accessories_maximum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "accessories_minimum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "accessory_factor",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "assembly_expected_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "assembly_maximum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "assembly_minimum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "assembly_profile_code",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "assumptions",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "billable_area_unit_square_meters",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "calculated_at_utc",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "confidence_level",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "confidence_score",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "finish_code",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "finish_factor_expected",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "finish_factor_maximum",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "finish_factor_minimum",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "frame_code",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "glass_code",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "glass_expected_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "glass_expected_price_per_square_meter",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "glass_maximum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "glass_maximum_price_per_square_meter",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "glass_minimum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "glass_minimum_price_per_square_meter",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "glass_price_range_version",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "item_expected_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "item_maximum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "item_minimum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "labor_expected_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "labor_maximum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "labor_minimum_amount",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "labor_profile_code",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "missing_data",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "pricing_profile_version",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "requires_review",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "system_code",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");

            migrationBuilder.DropColumn(
                name: "system_source",
                schema: "core",
                table: "pre_quote_draft_item_valuation_snapshots");
        }
    }
}
