using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementExperienceConfigurationConfirmations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requirement_experience_configuration_confirmations",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technical_proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_experience_configuration_confirmations", x => x.id);
                    table.CheckConstraint("ck_requirement_experience_config_confirmations_revision", "\"revision\" > 0");
                    table.ForeignKey(
                        name: "FK_requirement_experience_configuration_confirmations_requirem~",
                        column: x => x.requirement_id,
                        principalSchema: "core",
                        principalTable: "requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_requirement_experience_configuration_confirmations_require~1",
                        column: x => x.technical_proposal_id,
                        principalSchema: "core",
                        principalTable: "requirement_technical_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_requirement_experience_configuration_confirmations_users_co~",
                        column: x => x.confirmed_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requirement_experience_configuration_confirmation_items",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technical_proposal_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_included = table.Column<bool>(type: "boolean", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: true),
                    width_mm = table.Column<int>(type: "integer", nullable: true),
                    height_mm = table.Column<int>(type: "integer", nullable: true),
                    area_m2 = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    system_id = table.Column<Guid>(type: "uuid", nullable: true),
                    glass_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    finish_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    system_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    glass_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    finish_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    system_override_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    glass_override_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    review_reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    blocking_reasons = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_experience_configuration_confirmation_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_requirement_experience_configuration_confirmation_items_fin~",
                        column: x => x.finish_type_id,
                        principalSchema: "core",
                        principalTable: "finish_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_experience_configuration_confirmation_items_gla~",
                        column: x => x.glass_type_id,
                        principalSchema: "core",
                        principalTable: "glass_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_experience_configuration_confirmation_items_pro~",
                        column: x => x.system_id,
                        principalSchema: "core",
                        principalTable: "product_systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_experience_configuration_confirmation_items_req~",
                        column: x => x.confirmation_id,
                        principalSchema: "core",
                        principalTable: "requirement_experience_configuration_confirmations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_requirement_experience_configuration_confirmation_items_re~1",
                        column: x => x.technical_proposal_item_id,
                        principalSchema: "core",
                        principalTable: "requirement_technical_proposal_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_requirement_exp_config_confirmation_items_confirmation_id",
                schema: "core",
                table: "requirement_experience_configuration_confirmation_items",
                column: "confirmation_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_exp_config_confirmation_items_finish_type_id",
                schema: "core",
                table: "requirement_experience_configuration_confirmation_items",
                column: "finish_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_exp_config_confirmation_items_glass_type_id",
                schema: "core",
                table: "requirement_experience_configuration_confirmation_items",
                column: "glass_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_exp_config_confirmation_items_system_id",
                schema: "core",
                table: "requirement_experience_configuration_confirmation_items",
                column: "system_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_exp_config_confirmation_items_technical_item_id",
                schema: "core",
                table: "requirement_experience_configuration_confirmation_items",
                column: "technical_proposal_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_requirement_exp_config_confirmation_items_confirmation_item",
                schema: "core",
                table: "requirement_experience_configuration_confirmation_items",
                columns: new[] { "confirmation_id", "technical_proposal_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requirement_exp_config_confirmations_confirmed_by_user_id",
                schema: "core",
                table: "requirement_experience_configuration_confirmations",
                column: "confirmed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_exp_config_confirmations_proposal_id",
                schema: "core",
                table: "requirement_experience_configuration_confirmations",
                column: "technical_proposal_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_exp_config_confirmations_requirement_id",
                schema: "core",
                table: "requirement_experience_configuration_confirmations",
                column: "requirement_id");

            migrationBuilder.CreateIndex(
                name: "ux_requirement_exp_config_confirmations_proposal_revision",
                schema: "core",
                table: "requirement_experience_configuration_confirmations",
                columns: new[] { "technical_proposal_id", "revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requirement_experience_configuration_confirmation_items",
                schema: "core");

            migrationBuilder.DropTable(
                name: "requirement_experience_configuration_confirmations",
                schema: "core");
        }
    }
}
