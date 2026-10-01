using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementExperienceDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requirement_item_experience_drafts",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicalProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicalProposalItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SpaceTypeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResolutionState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_item_experience_drafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_requirement_item_experience_drafts_requirement_technical_pr~",
                        column: x => x.TechnicalProposalId,
                        principalSchema: "core",
                        principalTable: "requirement_technical_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_requirement_item_experience_drafts_requirement_technical_p~1",
                        column: x => x.TechnicalProposalItemId,
                        principalSchema: "core",
                        principalTable: "requirement_technical_proposal_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_requirement_item_experience_drafts_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requirement_item_experience_answers",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    BenefitCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OptionCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_item_experience_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_requirement_item_experience_answers_requirement_item_experi~",
                        column: x => x.DraftId,
                        principalSchema: "core",
                        principalTable: "requirement_item_experience_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_requirement_item_experience_answers_DraftId_BenefitCode",
                schema: "core",
                table: "requirement_item_experience_answers",
                columns: new[] { "DraftId", "BenefitCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_requirement_item_experience_drafts_TechnicalProposalId",
                schema: "core",
                table: "requirement_item_experience_drafts",
                column: "TechnicalProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_item_experience_drafts_TechnicalProposalItemId",
                schema: "core",
                table: "requirement_item_experience_drafts",
                column: "TechnicalProposalItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_requirement_item_experience_drafts_UpdatedByUserId",
                schema: "core",
                table: "requirement_item_experience_drafts",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requirement_item_experience_answers",
                schema: "core");

            migrationBuilder.DropTable(
                name: "requirement_item_experience_drafts",
                schema: "core");
        }
    }
}
