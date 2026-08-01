using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredItemGlassValuations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "structured_extraction_item_glass_valuations",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    structured_extraction_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    glass_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    glass_price_range_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    price_range_version = table.Column<int>(type: "integer", nullable: true),
                    price_range_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    unit_area_square_meters = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    total_area_square_meters = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    minimum_price_per_square_meter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    maximum_price_per_square_meter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    minimum_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    maximum_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    calculated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_structured_extraction_item_glass_valuations", x => x.id);
                    table.CheckConstraint("ck_structured_glass_valuation_amounts", "\"minimum_amount\" IS NULL OR \"minimum_amount\" >= 0 AND \"maximum_amount\" >= \"minimum_amount\"");
                    table.CheckConstraint("ck_structured_glass_valuation_areas", "\"unit_area_square_meters\" IS NULL OR \"unit_area_square_meters\" >= 0 AND \"total_area_square_meters\" >= 0");
                    table.CheckConstraint("ck_structured_glass_valuation_currency", "\"currency\" IS NULL OR char_length(\"currency\") = 3");
                    table.CheckConstraint("ck_structured_glass_valuation_prices", "\"minimum_price_per_square_meter\" IS NULL OR \"minimum_price_per_square_meter\" > 0 AND \"maximum_price_per_square_meter\" >= \"minimum_price_per_square_meter\"");
                    table.ForeignKey(
                        name: "FK_structured_extraction_item_glass_valuations_glass_price_ran~",
                        column: x => x.glass_price_range_version_id,
                        principalSchema: "core",
                        principalTable: "glass_price_range_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_structured_extraction_item_glass_valuations_glass_types_gla~",
                        column: x => x.glass_type_id,
                        principalSchema: "core",
                        principalTable: "glass_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_structured_extraction_item_glass_valuations_structured_extr~",
                        column: x => x.structured_extraction_item_id,
                        principalSchema: "core",
                        principalTable: "structured_extraction_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_item_glass_valuations_glass_price_ran~",
                schema: "core",
                table: "structured_extraction_item_glass_valuations",
                column: "glass_price_range_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_item_glass_valuations_glass_type_id",
                schema: "core",
                table: "structured_extraction_item_glass_valuations",
                column: "glass_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_structured_extraction_item_glass_valuations_structured_extr~",
                schema: "core",
                table: "structured_extraction_item_glass_valuations",
                column: "structured_extraction_item_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "structured_extraction_item_glass_valuations",
                schema: "core");
        }
    }
}
