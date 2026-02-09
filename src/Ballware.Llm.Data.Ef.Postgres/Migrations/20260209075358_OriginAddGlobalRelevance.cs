using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ballware.Llm.Data.Ef.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class OriginAddGlobalRelevance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "global_relevance",
                table: "origin",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "global_relevance",
                table: "origin");
        }
    }
}
