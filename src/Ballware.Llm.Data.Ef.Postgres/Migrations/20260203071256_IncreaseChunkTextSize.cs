using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ballware.Llm.Data.Ef.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class IncreaseChunkTextSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "chunk",
                type: "character varying(102400)",
                maxLength: 102400,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "chunk",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(102400)",
                oldMaxLength: 102400);
        }
    }
}
