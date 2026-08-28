using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementPricingSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requirement_pricing_snapshots",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technical_proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    pricing_basis = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    original_grand_total = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    current_grand_total = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_pricing_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_requirement_pricing_snapshots_requirement_technical_proposa~",
                        column: x => x.technical_proposal_id,
                        principalSchema: "core",
                        principalTable: "requirement_technical_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_pricing_snapshots_requirements_requirement_id",
                        column: x => x.requirement_id,
                        principalSchema: "core",
                        principalTable: "requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requirement_pricing_item_snapshots",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pricing_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technical_proposal_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_system_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_glass_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_finish_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_system_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_glass_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_finish_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    current_status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    original_unit_minimum = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    original_unit_expected = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    original_unit_maximum = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    original_line_minimum = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    original_line_expected = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    original_line_maximum = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    current_unit_minimum = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    current_unit_expected = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    current_unit_maximum = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    current_line_minimum = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    current_line_expected = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    current_line_maximum = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_pricing_item_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_requirement_pricing_item_snapshots_finish_types_current_fin~",
                        column: x => x.current_finish_type_id,
                        principalSchema: "core",
                        principalTable: "finish_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_pricing_item_snapshots_finish_types_original_fi~",
                        column: x => x.original_finish_type_id,
                        principalSchema: "core",
                        principalTable: "finish_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_pricing_item_snapshots_glass_types_current_glas~",
                        column: x => x.current_glass_type_id,
                        principalSchema: "core",
                        principalTable: "glass_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_pricing_item_snapshots_glass_types_original_gla~",
                        column: x => x.original_glass_type_id,
                        principalSchema: "core",
                        principalTable: "glass_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_pricing_item_snapshots_product_systems_current_~",
                        column: x => x.current_system_id,
                        principalSchema: "core",
                        principalTable: "product_systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_pricing_item_snapshots_product_systems_original~",
                        column: x => x.original_system_id,
                        principalSchema: "core",
                        principalTable: "product_systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_pricing_item_snapshots_requirement_pricing_snap~",
                        column: x => x.pricing_snapshot_id,
                        principalSchema: "core",
                        principalTable: "requirement_pricing_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_pricing_item_snapshots_requirement_technical_pr~",
                        column: x => x.technical_proposal_item_id,
                        principalSchema: "core",
                        principalTable: "requirement_technical_proposal_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_requirement_pricing_item_snapshots_current_finish_type_id",
                schema: "core",
                table: "requirement_pricing_item_snapshots",
                column: "current_finish_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_pricing_item_snapshots_current_glass_type_id",
                schema: "core",
                table: "requirement_pricing_item_snapshots",
                column: "current_glass_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_pricing_item_snapshots_current_system_id",
                schema: "core",
                table: "requirement_pricing_item_snapshots",
                column: "current_system_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_pricing_item_snapshots_original_finish_type_id",
                schema: "core",
                table: "requirement_pricing_item_snapshots",
                column: "original_finish_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_pricing_item_snapshots_original_glass_type_id",
                schema: "core",
                table: "requirement_pricing_item_snapshots",
                column: "original_glass_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_pricing_item_snapshots_original_system_id",
                schema: "core",
                table: "requirement_pricing_item_snapshots",
                column: "original_system_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_pricing_item_snapshots_pricing_snapshot_id",
                schema: "core",
                table: "requirement_pricing_item_snapshots",
                column: "pricing_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ux_requirement_pricing_item_snapshots_proposal_item_id",
                schema: "core",
                table: "requirement_pricing_item_snapshots",
                column: "technical_proposal_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_requirement_pricing_snapshots_requirement_id",
                schema: "core",
                table: "requirement_pricing_snapshots",
                column: "requirement_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_requirement_pricing_snapshots_technical_proposal_id",
                schema: "core",
                table: "requirement_pricing_snapshots",
                column: "technical_proposal_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requirement_pricing_item_snapshots",
                schema: "core");

            migrationBuilder.DropTable(
                name: "requirement_pricing_snapshots",
                schema: "core");
        }
    }
}
