using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommercialRevisionToRequirementPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "commercial_revision",
                schema: "core",
                table: "requirement_technical_proposals",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "technical_proposal_commercial_revision",
                schema: "core",
                table: "requirement_pricing_snapshots",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "commercial_revision",
                schema: "core",
                table: "requirement_technical_proposals");

            migrationBuilder.DropColumn(
                name: "technical_proposal_commercial_revision",
                schema: "core",
                table: "requirement_pricing_snapshots");
        }
    }
}
