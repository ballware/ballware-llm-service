using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ballware.Llm.Data.Ef.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ContextMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "context",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    uuid = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    create_stamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_changer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_change_stamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    identifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    system_prompt_template = table.Column<string>(type: "text", nullable: true),
                    welcome_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_context", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_context_tenant_id",
                table: "context",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_context_tenant_id_identifier",
                table: "context",
                columns: new[] { "tenant_id", "identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_context_tenant_id_uuid",
                table: "context",
                columns: new[] { "tenant_id", "uuid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "context");
        }
    }
}
