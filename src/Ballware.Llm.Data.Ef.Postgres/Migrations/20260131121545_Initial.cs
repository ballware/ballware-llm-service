using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ballware.Llm.Data.Ef.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chunk",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    uuid = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    create_stamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_changer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_change_stamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chunk", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "embedding",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    uuid = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    create_stamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_changer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_change_stamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embedding", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "origin",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    uuid = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    create_stamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_changer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_change_stamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_origin", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chunk_tenant_id",
                table: "chunk",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_chunk_tenant_id_uuid",
                table: "chunk",
                columns: new[] { "tenant_id", "uuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_embedding_tenant_id",
                table: "embedding",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_embedding_tenant_id_uuid",
                table: "embedding",
                columns: new[] { "tenant_id", "uuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_origin_tenant_id",
                table: "origin",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_origin_tenant_id_uuid",
                table: "origin",
                columns: new[] { "tenant_id", "uuid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chunk");

            migrationBuilder.DropTable(
                name: "embedding");

            migrationBuilder.DropTable(
                name: "origin");
        }
    }
}
