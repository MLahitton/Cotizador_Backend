using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreQuoteIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pre_quote_serial_counters",
                schema: "core",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    next_sequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pre_quote_serial_counters", x => x.year);
                    table.CheckConstraint("ck_pre_quote_serial_counters_next_sequence", "\"next_sequence\" > 0");
                });

            migrationBuilder.AddColumn<string>(
                name: "name",
                schema: "core",
                table: "pre_quotes",
                type: "varchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "serial",
                schema: "core",
                table: "pre_quotes",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(
                """
                WITH numbered AS (
                    SELECT
                        id,
                        EXTRACT(YEAR FROM created_at_utc AT TIME ZONE 'UTC')::int AS serial_year,
                        ROW_NUMBER() OVER (
                            PARTITION BY EXTRACT(YEAR FROM created_at_utc AT TIME ZONE 'UTC')::int
                            ORDER BY created_at_utc, id
                        ) AS serial_sequence
                    FROM core.pre_quotes
                )
                UPDATE core.pre_quotes AS pre_quote
                SET serial = format(
                    'PC-%s-%s',
                    numbered.serial_year,
                    lpad(numbered.serial_sequence::text, 4, '0'))
                FROM numbered
                WHERE pre_quote.id = numbered.id;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO core.pre_quote_serial_counters (year, next_sequence)
                SELECT
                    serial_year,
                    MAX(serial_sequence)::int + 1
                FROM (
                    SELECT
                        EXTRACT(YEAR FROM created_at_utc AT TIME ZONE 'UTC')::int AS serial_year,
                        ROW_NUMBER() OVER (
                            PARTITION BY EXTRACT(YEAR FROM created_at_utc AT TIME ZONE 'UTC')::int
                            ORDER BY created_at_utc, id
                        ) AS serial_sequence
                    FROM core.pre_quotes
                ) AS numbered
                GROUP BY serial_year;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "serial",
                schema: "core",
                table: "pre_quotes",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_pre_quotes_serial",
                schema: "core",
                table: "pre_quotes",
                column: "serial",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pre_quote_serial_counters",
                schema: "core");

            migrationBuilder.DropIndex(
                name: "ux_pre_quotes_serial",
                schema: "core",
                table: "pre_quotes");

            migrationBuilder.DropColumn(
                name: "name",
                schema: "core",
                table: "pre_quotes");

            migrationBuilder.DropColumn(
                name: "serial",
                schema: "core",
                table: "pre_quotes");
        }
    }
}