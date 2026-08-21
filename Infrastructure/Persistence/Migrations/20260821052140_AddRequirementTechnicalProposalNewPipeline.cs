using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementTechnicalProposalNewPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requirement_technical_proposals",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_extraction_result_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_processing_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_technical_proposals", x => x.id);
                    table.CheckConstraint("ck_requirement_technical_proposals_status", "\"status\" IN ('Completed', 'RequiresReview')");
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposals_requirement_extraction_resu~",
                        column: x => x.requirement_extraction_result_id,
                        principalSchema: "core",
                        principalTable: "requirement_extraction_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposals_requirement_processing_atte~",
                        column: x => x.requirement_processing_attempt_id,
                        principalSchema: "core",
                        principalTable: "requirement_processing_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposals_requirements_requirement_id",
                        column: x => x.requirement_id,
                        principalSchema: "core",
                        principalTable: "requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requirement_technical_proposal_items",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    technical_proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_extracted_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    suggested_system_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suggested_glass_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suggested_finish_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    overall_confidence = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    system_confidence = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    glass_confidence = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    finish_confidence = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false),
                    is_technically_complete = table.Column<bool>(type: "boolean", nullable: false),
                    is_priceable = table.Column<bool>(type: "boolean", nullable: false),
                    review_reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    system_resolution_reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    glass_resolution_reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    finish_resolution_reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    historical_support_count = table.Column<int>(type: "integer", nullable: false),
                    historical_best_similarity = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    historical_average_similarity = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    historical_similarity_status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_technical_proposal_items", x => x.id);
                    table.CheckConstraint("ck_requirement_technical_proposal_items_confidence", "\"overall_confidence\" >= 0 AND \"overall_confidence\" <= 1 AND \"system_confidence\" >= 0 AND \"system_confidence\" <= 1 AND \"glass_confidence\" >= 0 AND \"glass_confidence\" <= 1 AND \"finish_confidence\" >= 0 AND \"finish_confidence\" <= 1 AND (\"historical_best_similarity\" IS NULL OR (\"historical_best_similarity\" >= 0 AND \"historical_best_similarity\" <= 1)) AND (\"historical_average_similarity\" IS NULL OR (\"historical_average_similarity\" >= 0 AND \"historical_average_similarity\" <= 1))");
                    table.CheckConstraint("ck_requirement_technical_proposal_items_historical_support", "\"historical_support_count\" >= 0");
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposal_items_finish_types_suggested~",
                        column: x => x.suggested_finish_type_id,
                        principalSchema: "core",
                        principalTable: "finish_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposal_items_glass_types_suggested_~",
                        column: x => x.suggested_glass_type_id,
                        principalSchema: "core",
                        principalTable: "glass_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposal_items_product_systems_sugges~",
                        column: x => x.suggested_system_id,
                        principalSchema: "core",
                        principalTable: "product_systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposal_items_requirement_extracted_~",
                        column: x => x.requirement_extracted_item_id,
                        principalSchema: "core",
                        principalTable: "requirement_extracted_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposal_items_requirement_technical_~",
                        column: x => x.technical_proposal_id,
                        principalSchema: "core",
                        principalTable: "requirement_technical_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requirement_technical_proposal_finish_alternatives",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finish_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    reasons = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_technical_proposal_finish_alternatives", x => x.id);
                    table.CheckConstraint("ck_req_tech_proposal_finish_alt_confidence", "\"confidence\" >= 0 AND \"confidence\" <= 1");
                    table.CheckConstraint("ck_req_tech_proposal_finish_alt_rank", "\"rank\" > 0");
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposal_finish_alternatives_finish_t~",
                        column: x => x.finish_type_id,
                        principalSchema: "core",
                        principalTable: "finish_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposal_finish_alternatives_requirem~",
                        column: x => x.proposal_item_id,
                        principalSchema: "core",
                        principalTable: "requirement_technical_proposal_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requirement_technical_proposal_glass_alternatives",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    glass_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    reasons = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_technical_proposal_glass_alternatives", x => x.id);
                    table.CheckConstraint("ck_req_tech_proposal_glass_alt_confidence", "\"confidence\" >= 0 AND \"confidence\" <= 1");
                    table.CheckConstraint("ck_req_tech_proposal_glass_alt_rank", "\"rank\" > 0");
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposal_glass_alternatives_glass_typ~",
                        column: x => x.glass_type_id,
                        principalSchema: "core",
                        principalTable: "glass_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposal_glass_alternatives_requireme~",
                        column: x => x.proposal_item_id,
                        principalSchema: "core",
                        principalTable: "requirement_technical_proposal_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requirement_technical_proposal_historical_examples",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    quote_id = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    historical_reference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    similarity_score = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    matched_features = table.Column<string[]>(type: "text[]", nullable: false),
                    differences = table.Column<string[]>(type: "text[]", nullable: false),
                    technical_explanation = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_technical_proposal_historical_examples", x => x.id);
                    table.CheckConstraint("ck_req_tech_proposal_hist_examples_similarity", "\"similarity_score\" >= 0 AND \"similarity_score\" <= 1");
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposal_historical_examples_requirem~",
                        column: x => x.proposal_item_id,
                        principalSchema: "core",
                        principalTable: "requirement_technical_proposal_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "requirement_technical_proposal_system_alternatives",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_system_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    reasons = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_technical_proposal_system_alternatives", x => x.id);
                    table.CheckConstraint("ck_requirement_technical_proposal_system_alternatives_confiden~", "\"confidence\" >= 0 AND \"confidence\" <= 1");
                    table.CheckConstraint("ck_requirement_technical_proposal_system_alternatives_rank", "\"rank\" > 0");
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposal_system_alternatives_product_~",
                        column: x => x.product_system_id,
                        principalSchema: "core",
                        principalTable: "product_systems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_requirement_technical_proposal_system_alternatives_requirem~",
                        column: x => x.proposal_item_id,
                        principalSchema: "core",
                        principalTable: "requirement_technical_proposal_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_requirement_technical_proposal_finish_alternatives_finish_t~",
                schema: "core",
                table: "requirement_technical_proposal_finish_alternatives",
                column: "finish_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_req_tech_proposal_finish_alt_item_rank",
                schema: "core",
                table: "requirement_technical_proposal_finish_alternatives",
                columns: new[] { "proposal_item_id", "rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_requirement_technical_proposal_glass_alternatives_glass_typ~",
                schema: "core",
                table: "requirement_technical_proposal_glass_alternatives",
                column: "glass_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_req_tech_proposal_glass_alt_item_rank",
                schema: "core",
                table: "requirement_technical_proposal_glass_alternatives",
                columns: new[] { "proposal_item_id", "rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_req_tech_proposal_hist_examples_item_id",
                schema: "core",
                table: "requirement_technical_proposal_historical_examples",
                column: "proposal_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_technical_proposal_items_proposal_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "technical_proposal_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_technical_proposal_items_suggested_finish_type_~",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "suggested_finish_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_technical_proposal_items_suggested_glass_type_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "suggested_glass_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_requirement_technical_proposal_items_suggested_system_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "suggested_system_id");

            migrationBuilder.CreateIndex(
                name: "ux_requirement_technical_proposal_items_extracted_item_id",
                schema: "core",
                table: "requirement_technical_proposal_items",
                column: "requirement_extracted_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_requirement_technical_proposal_system_alternatives_product_~",
                schema: "core",
                table: "requirement_technical_proposal_system_alternatives",
                column: "product_system_id");

            migrationBuilder.CreateIndex(
                name: "ux_req_tech_proposal_system_alt_item_rank",
                schema: "core",
                table: "requirement_technical_proposal_system_alternatives",
                columns: new[] { "proposal_item_id", "rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requirement_technical_proposals_requirement_created_id",
                schema: "core",
                table: "requirement_technical_proposals",
                columns: new[] { "requirement_id", "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_requirement_technical_proposals_requirement_id",
                schema: "core",
                table: "requirement_technical_proposals",
                column: "requirement_id");

            migrationBuilder.CreateIndex(
                name: "ux_requirement_technical_proposals_extraction_result_id",
                schema: "core",
                table: "requirement_technical_proposals",
                column: "requirement_extraction_result_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_requirement_technical_proposals_processing_attempt_id",
                schema: "core",
                table: "requirement_technical_proposals",
                column: "requirement_processing_attempt_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requirement_technical_proposal_finish_alternatives",
                schema: "core");

            migrationBuilder.DropTable(
                name: "requirement_technical_proposal_glass_alternatives",
                schema: "core");

            migrationBuilder.DropTable(
                name: "requirement_technical_proposal_historical_examples",
                schema: "core");

            migrationBuilder.DropTable(
                name: "requirement_technical_proposal_system_alternatives",
                schema: "core");

            migrationBuilder.DropTable(
                name: "requirement_technical_proposal_items",
                schema: "core");

            migrationBuilder.DropTable(
                name: "requirement_technical_proposals",
                schema: "core");
        }
    }
}
