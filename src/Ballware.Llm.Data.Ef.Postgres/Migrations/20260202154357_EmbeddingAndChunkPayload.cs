using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Ballware.Llm.Data.Ef.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class EmbeddingAndChunkPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "active",
                table: "embedding",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "model",
                table: "embedding",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "origin_id",
                table: "embedding",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Vector>(
                name: "data",
                table: "chunk",
                type: "vector(768)",
                nullable: false);

            migrationBuilder.AddColumn<Guid>(
                name: "embedding_id",
                table: "chunk",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "active",
                table: "embedding");

            migrationBuilder.DropColumn(
                name: "model",
                table: "embedding");

            migrationBuilder.DropColumn(
                name: "origin_id",
                table: "embedding");

            migrationBuilder.DropColumn(
                name: "data",
                table: "chunk");

            migrationBuilder.DropColumn(
                name: "embedding_id",
                table: "chunk");
        }
    }
}
