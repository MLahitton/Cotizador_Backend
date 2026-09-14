using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role",
                schema: "identity",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "USER");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_role",
                schema: "identity",
                table: "users",
                sql: "role IN ('USER', 'ADMIN')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_role",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "role",
                schema: "identity",
                table: "users");
        }
    }
}
