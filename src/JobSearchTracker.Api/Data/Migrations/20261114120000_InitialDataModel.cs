using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSearchTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialDataModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "candidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candidates", x => x.Id);
                    table.CheckConstraint("CK_candidates_name_not_blank", "char_length(btrim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "company_lists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocationLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DefaultWorkModes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_lists", x => x.Id);
                    table.CheckConstraint("CK_company_lists_default_work_modes_range", "\"DefaultWorkModes\" BETWEEN 0 AND 7");
                    table.CheckConstraint("CK_company_lists_name_not_blank", "char_length(btrim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "employers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employers", x => x.Id);
                    table.CheckConstraint("CK_employers_name_not_blank", "char_length(btrim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "search_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WorkModes = table.Column<int>(type: "integer", nullable: true),
                    CompanyMonitoringEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BroadSearchEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ProfileVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_profiles", x => x.Id);
                    table.CheckConstraint("CK_search_profiles_name_not_blank", "char_length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_search_profiles_profile_version_min", "\"ProfileVersion\" >= 1");
                    table.CheckConstraint("CK_search_profiles_work_modes_range", "\"WorkModes\" IS NULL OR \"WorkModes\" BETWEEN 0 AND 7");
                    table.ForeignKey(
                        name: "FK_search_profiles_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_list_employers",
                columns: table => new
                {
                    CompanyListId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_list_employers", x => new { x.CompanyListId, x.EmployerId });
                    table.ForeignKey(
                        name: "FK_company_list_employers_company_lists_CompanyListId",
                        column: x => x.CompanyListId,
                        principalTable: "company_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_company_list_employers_employers_EmployerId",
                        column: x => x.EmployerId,
                        principalTable: "employers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "search_profile_company_lists",
                columns: table => new
                {
                    SearchProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyListId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_profile_company_lists", x => new { x.SearchProfileId, x.CompanyListId });
                    table.ForeignKey(
                        name: "FK_search_profile_company_lists_company_lists_CompanyListId",
                        column: x => x.CompanyListId,
                        principalTable: "company_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_search_profile_company_lists_search_profiles_SearchProfileId",
                        column: x => x.SearchProfileId,
                        principalTable: "search_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_list_employers_EmployerId",
                table: "company_list_employers",
                column: "EmployerId");

            migrationBuilder.CreateIndex(
                name: "IX_company_lists_NormalizedName",
                table: "company_lists",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employers_NormalizedName",
                table: "employers",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_search_profile_company_lists_CompanyListId",
                table: "search_profile_company_lists",
                column: "CompanyListId");

            migrationBuilder.CreateIndex(
                name: "IX_search_profiles_CandidateId_NormalizedName",
                table: "search_profiles",
                columns: new[] { "CandidateId", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "company_list_employers");
            migrationBuilder.DropTable(name: "search_profile_company_lists");
            migrationBuilder.DropTable(name: "employers");
            migrationBuilder.DropTable(name: "company_lists");
            migrationBuilder.DropTable(name: "search_profiles");
            migrationBuilder.DropTable(name: "candidates");
        }
    }
}
