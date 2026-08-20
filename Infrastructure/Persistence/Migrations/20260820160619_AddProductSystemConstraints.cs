using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductSystemConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_system_constraints",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_system_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    constraint_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    scope = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evaluation_stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    knowledge_class = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    min_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    max_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    text_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    allowed_values = table.Column<string[]>(type: "text[]", nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    requires_review_when_unknown = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    effective_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    effective_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_system_constraints", x => x.id);
                    table.CheckConstraint("ck_product_system_constraints_effective_range", "\"effective_from_utc\" IS NULL OR \"effective_to_utc\" IS NULL OR \"effective_from_utc\" <= \"effective_to_utc\"");
                    table.CheckConstraint("ck_product_system_constraints_hard_verified", "\"severity\" <> 'Hard' OR \"knowledge_class\" = 'VerifiedTechnical'");
                    table.CheckConstraint("ck_product_system_constraints_values", "\"min_value\" IS NULL OR \"max_value\" IS NULL OR \"min_value\" <= \"max_value\"");
                    table.ForeignKey(
                        name: "FK_product_system_constraints_product_systems_product_system_id",
                        column: x => x.product_system_id,
                        principalSchema: "core",
                        principalTable: "product_systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_system_constraints_system_active_stage",
                schema: "core",
                table: "product_system_constraints",
                columns: new[] { "product_system_id", "is_active", "evaluation_stage" });

            migrationBuilder.CreateIndex(
                name: "ux_product_system_constraints_system_code",
                schema: "core",
                table: "product_system_constraints",
                columns: new[] { "product_system_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_system_constraints",
                schema: "core");
        }
    }
}
