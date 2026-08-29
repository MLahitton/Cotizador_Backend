using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementProcessingCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requirement_processing_attempts_lifecycle",
                schema: "core",
                table: "requirement_processing_attempts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_requirement_processing_attempts_lifecycle",
                schema: "core",
                table: "requirement_processing_attempts",
                sql: "((\"processing_state\" = 'Pending' AND \"started_at_utc\" IS NULL AND \"outcome\" IS NULL AND \"completed_at_utc\" IS NULL AND \"error_code\" IS NULL) OR (\"processing_state\" = 'Processing' AND \"started_at_utc\" IS NOT NULL AND \"started_at_utc\" >= \"created_at_utc\" AND \"outcome\" IS NULL AND \"completed_at_utc\" IS NULL AND \"error_code\" IS NULL) OR (\"processing_state\" = 'Finished' AND \"started_at_utc\" IS NOT NULL AND \"started_at_utc\" >= \"created_at_utc\" AND \"completed_at_utc\" IS NOT NULL AND \"completed_at_utc\" >= \"started_at_utc\" AND ((\"outcome\" IN ('Completed', 'RequiresReview') AND \"error_code\" IS NULL) OR (\"outcome\" = 'Failed' AND \"error_code\" IS NOT NULL AND \"error_code\" <> '') OR (\"outcome\" = 'Cancelled' AND \"error_code\" IS NULL))))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requirement_processing_attempts_lifecycle",
                schema: "core",
                table: "requirement_processing_attempts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_requirement_processing_attempts_lifecycle",
                schema: "core",
                table: "requirement_processing_attempts",
                sql: "((\"processing_state\" = 'Pending' AND \"started_at_utc\" IS NULL AND \"outcome\" IS NULL AND \"completed_at_utc\" IS NULL AND \"error_code\" IS NULL) OR (\"processing_state\" = 'Processing' AND \"started_at_utc\" IS NOT NULL AND \"started_at_utc\" >= \"created_at_utc\" AND \"outcome\" IS NULL AND \"completed_at_utc\" IS NULL AND \"error_code\" IS NULL) OR (\"processing_state\" = 'Finished' AND \"started_at_utc\" IS NOT NULL AND \"started_at_utc\" >= \"created_at_utc\" AND \"completed_at_utc\" IS NOT NULL AND \"completed_at_utc\" >= \"started_at_utc\" AND ((\"outcome\" IN ('Completed', 'RequiresReview') AND \"error_code\" IS NULL) OR (\"outcome\" = 'Failed' AND \"error_code\" IS NOT NULL AND \"error_code\" <> ''))))");
        }
    }
}
