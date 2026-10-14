using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementExperienceTechnicalSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requirement_item_experience_technical_selections",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    technical_proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technical_proposal_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    system_id = table.Column<Guid>(type: "uuid", nullable: true),
                    glass_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_item_experience_technical_selections", x => x.id);
                    table.CheckConstraint("ck_requirement_item_experience_technical_selections_revision", "\"revision\" > 0");
                    table.ForeignKey(
                        name: "FK_requirement_item_experience_technical_selections_glass_type~",
                        column: x => x.glass_type_id,
                        principalSchema: "core",
                        principalTable: "glass_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_item_experience_technical_selections_product_sy~",
                        column: x => x.system_id,
                        principalSchema: "core",
                        principalTable: "product_systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_item_experience_technical_selections_requiremen~",
                        column: x => x.technical_proposal_id,
                        principalSchema: "core",
                        principalTable: "requirement_technical_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_requirement_item_experience_technical_selections_requireme~1",
                        column: x => x.technical_proposal_item_id,
                        principalSchema: "core",
                        principalTable: "requirement_technical_proposal_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_requirement_item_experience_technical_selections_users_upda~",
                        column: x => x.updated_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_requirement_item_exp_technical_sel_glass_type_id",
                schema: "core",
                table: "requirement_item_experience_technical_selections",
                column: "glass_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_item_exp_technical_sel_proposal_id",
                schema: "core",
                table: "requirement_item_experience_technical_selections",
                column: "technical_proposal_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_item_exp_technical_sel_system_id",
                schema: "core",
                table: "requirement_item_experience_technical_selections",
                column: "system_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_item_exp_technical_sel_updated_by_user_id",
                schema: "core",
                table: "requirement_item_experience_technical_selections",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_requirement_item_exp_technical_sel_item_id",
                schema: "core",
                table: "requirement_item_experience_technical_selections",
                column: "technical_proposal_item_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requirement_item_experience_technical_selections",
                schema: "core");
        }
    }
}
