using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieDate.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchRecomputeStamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MatchesComputedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MatchesRequestedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchesComputedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MatchesRequestedAt",
                table: "AspNetUsers");
        }
    }
}
