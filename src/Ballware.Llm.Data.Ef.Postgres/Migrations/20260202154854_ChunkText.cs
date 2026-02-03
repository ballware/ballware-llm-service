using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ballware.Llm.Data.Ef.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ChunkText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "text",
                table: "chunk",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "text",
                table: "chunk");
        }
    }
}
