using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientProjectAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Proyectos: campos opcionales de auditoría del cambio de estado.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "status_changed_at_utc",
                schema: "core",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "status_changed_by_user_id",
                schema: "core",
                table: "projects",
                type: "uuid",
                nullable: true);

            // Se crea temporalmente como nullable para permitir el backfill.
            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_user_id",
                schema: "core",
                table: "projects",
                type: "uuid",
                nullable: true);

            // Clientes: campos opcionales de auditoría del cambio de estado.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "status_changed_at_utc",
                schema: "core",
                table: "clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "status_changed_by_user_id",
                schema: "core",
                table: "clients",
                type: "uuid",
                nullable: true);

            // Se crea temporalmente como nullable para permitir el backfill.
            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_user_id",
                schema: "core",
                table: "clients",
                type: "uuid",
                nullable: true);

            // Conserva la trazabilidad de los proyectos existentes.
            migrationBuilder.Sql(
                """
                UPDATE core.projects
                SET updated_by_user_id = created_by_user_id
                WHERE updated_by_user_id IS NULL;
                """);

            // Conserva la trazabilidad de los clientes existentes.
            migrationBuilder.Sql(
                """
                UPDATE core.clients
                SET updated_by_user_id = created_by_user_id
                WHERE updated_by_user_id IS NULL;
                """);

            // Después del backfill, la última persona que modificó es obligatoria.
            migrationBuilder.AlterColumn<Guid>(
                name: "updated_by_user_id",
                schema: "core",
                table: "projects",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "updated_by_user_id",
                schema: "core",
                table: "clients",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_status_changed_by_user_id",
                schema: "core",
                table: "projects",
                column: "status_changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_updated_by_user_id",
                schema: "core",
                table: "projects",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_clients_status_changed_by_user_id",
                schema: "core",
                table: "clients",
                column: "status_changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_clients_updated_by_user_id",
                schema: "core",
                table: "clients",
                column: "updated_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_clients_users_status_changed_by_user_id",
                schema: "core",
                table: "clients",
                column: "status_changed_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_clients_users_updated_by_user_id",
                schema: "core",
                table: "clients",
                column: "updated_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_projects_users_status_changed_by_user_id",
                schema: "core",
                table: "projects",
                column: "status_changed_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_projects_users_updated_by_user_id",
                schema: "core",
                table: "projects",
                column: "updated_by_user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_clients_users_status_changed_by_user_id",
                schema: "core",
                table: "clients");

            migrationBuilder.DropForeignKey(
                name: "FK_clients_users_updated_by_user_id",
                schema: "core",
                table: "clients");

            migrationBuilder.DropForeignKey(
                name: "FK_projects_users_status_changed_by_user_id",
                schema: "core",
                table: "projects");

            migrationBuilder.DropForeignKey(
                name: "FK_projects_users_updated_by_user_id",
                schema: "core",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "ix_projects_status_changed_by_user_id",
                schema: "core",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "ix_projects_updated_by_user_id",
                schema: "core",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "ix_clients_status_changed_by_user_id",
                schema: "core",
                table: "clients");

            migrationBuilder.DropIndex(
                name: "ix_clients_updated_by_user_id",
                schema: "core",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "status_changed_at_utc",
                schema: "core",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "status_changed_by_user_id",
                schema: "core",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "updated_by_user_id",
                schema: "core",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "status_changed_at_utc",
                schema: "core",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "status_changed_by_user_id",
                schema: "core",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "updated_by_user_id",
                schema: "core",
                table: "clients");
        }
    }
}