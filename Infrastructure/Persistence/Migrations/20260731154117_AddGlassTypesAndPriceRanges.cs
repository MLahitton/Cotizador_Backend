using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGlassTypesAndPriceRanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "glass_types",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_glass_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "glass_price_range_versions",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    glass_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    minimum_price_per_square_meter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    maximum_price_per_square_meter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valid_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_glass_price_range_versions", x => x.id);
                    table.CheckConstraint("ck_glass_price_range_versions_maximum_price", "\"maximum_price_per_square_meter\" >= \"minimum_price_per_square_meter\"");
                    table.CheckConstraint("ck_glass_price_range_versions_minimum_price", "\"minimum_price_per_square_meter\" > 0");
                    table.CheckConstraint("ck_glass_price_range_versions_validity", "\"valid_to_utc\" IS NULL OR \"valid_to_utc\" > \"valid_from_utc\"");
                    table.CheckConstraint("ck_glass_price_range_versions_version", "\"version\" > 0");
                    table.ForeignKey(
                        name: "FK_glass_price_range_versions_glass_types_glass_type_id",
                        column: x => x.glass_type_id,
                        principalSchema: "core",
                        principalTable: "glass_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "glass_types",
                columns: new[] { "id", "code", "created_at_utc", "description", "is_active", "name", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), "LAM_4_4", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Laminado 4+4", null },
                    { new Guid("10000000-0000-0000-0000-000000000002"), "LAM_4_4_GRAY", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Laminado 4+4 gris", null },
                    { new Guid("10000000-0000-0000-0000-000000000003"), "LAM_5_5", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Laminado 5+5", null },
                    { new Guid("10000000-0000-0000-0000-000000000004"), "LAM_5_5_GRAY", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Laminado 5+5 gris", null }
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "glass_price_range_versions",
                columns: new[] { "id", "created_at_utc", "currency", "glass_type_id", "maximum_price_per_square_meter", "minimum_price_per_square_meter", "status", "valid_from_utc", "valid_to_utc", "version" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "COP", new Guid("10000000-0000-0000-0000-000000000001"), 110000m, 90000m, "PRELIMINARY", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("20000000-0000-0000-0000-000000000002"), new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "COP", new Guid("10000000-0000-0000-0000-000000000002"), 95000m, 95000m, "PRELIMINARY", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("20000000-0000-0000-0000-000000000003"), new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "COP", new Guid("10000000-0000-0000-0000-000000000003"), 140000m, 120000m, "PRELIMINARY", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 },
                    { new Guid("20000000-0000-0000-0000-000000000004"), new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "COP", new Guid("10000000-0000-0000-0000-000000000004"), 145000m, 125000m, "PRELIMINARY", new DateTimeOffset(new DateTime(2026, 7, 31, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_glass_price_range_versions_glass_type_id_valid_to_utc",
                schema: "core",
                table: "glass_price_range_versions",
                columns: new[] { "glass_type_id", "valid_to_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_glass_price_range_versions_open_type",
                schema: "core",
                table: "glass_price_range_versions",
                column: "glass_type_id",
                unique: true,
                filter: "\"valid_to_utc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_glass_price_range_versions_type_version",
                schema: "core",
                table: "glass_price_range_versions",
                columns: new[] { "glass_type_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_glass_types_code",
                schema: "core",
                table: "glass_types",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "glass_price_range_versions",
                schema: "core");

            migrationBuilder.DropTable(
                name: "glass_types",
                schema: "core");
        }
    }
}
