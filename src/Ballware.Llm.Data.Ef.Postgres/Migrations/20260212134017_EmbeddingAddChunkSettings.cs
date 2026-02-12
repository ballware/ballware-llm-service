using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ballware.Llm.Data.Ef.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class EmbeddingAddChunkSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_tokens_per_chunk",
                table: "embedding",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "overlap_tokens",
                table: "embedding",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_tokens_per_chunk",
                table: "embedding");

            migrationBuilder.DropColumn(
                name: "overlap_tokens",
                table: "embedding");
        }
    }
}
