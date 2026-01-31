using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ballware.Llm.Data.Ef.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class OriginPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "origin",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "origin",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "owner_entity",
                table: "origin",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                table: "origin",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "refering_entity",
                table: "origin",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "refering_id",
                table: "origin",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_origin_tenant_id_owner_entity_owner_id_name",
                table: "origin",
                columns: new[] { "tenant_id", "owner_entity", "owner_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_origin_tenant_id_owner_entity_owner_id_name",
                table: "origin");

            migrationBuilder.DropColumn(
                name: "description",
                table: "origin");

            migrationBuilder.DropColumn(
                name: "name",
                table: "origin");

            migrationBuilder.DropColumn(
                name: "owner_entity",
                table: "origin");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "origin");

            migrationBuilder.DropColumn(
                name: "refering_entity",
                table: "origin");

            migrationBuilder.DropColumn(
                name: "refering_id",
                table: "origin");
        }
    }
}
