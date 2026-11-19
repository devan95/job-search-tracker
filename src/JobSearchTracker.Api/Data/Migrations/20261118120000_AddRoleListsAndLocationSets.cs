using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSearchTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleListsAndLocationSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "location_sets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_location_sets", x => x.Id);
                    table.CheckConstraint("CK_location_sets_name_not_blank", "char_length(btrim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "role_lists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_lists", x => x.Id);
                    table.CheckConstraint("CK_role_lists_name_not_blank", "char_length(btrim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "location_set_locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_location_set_locations", x => x.Id);
                    table.CheckConstraint("CK_location_set_locations_country_code_format", "\"CountryCode\" IS NULL OR \"CountryCode\" ~ '^[A-Z]{2}$'");
                    table.CheckConstraint("CK_location_set_locations_label_not_blank", "char_length(btrim(\"Label\")) > 0");
                    table.CheckConstraint("CK_location_set_locations_scope_country", "(\"Scope\" <> 'remoteCountry' OR \"CountryCode\" IS NOT NULL) AND (\"Scope\" <> 'remoteWorldwide' OR \"CountryCode\" IS NULL)");
                    table.CheckConstraint("CK_location_set_locations_scope_values", "\"Scope\" IN ('city', 'state', 'country', 'remoteCountry', 'remoteWorldwide')");
                    table.ForeignKey(
                        name: "FK_location_set_locations_location_sets_LocationSetId",
                        column: x => x.LocationSetId,
                        principalTable: "location_sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_list_titles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleListId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PrimaryTitleId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_list_titles", x => x.Id);
                    table.CheckConstraint("CK_role_list_titles_not_own_primary", "\"PrimaryTitleId\" IS NULL OR \"PrimaryTitleId\" <> \"Id\"");
                    table.CheckConstraint("CK_role_list_titles_title_not_blank", "char_length(btrim(\"Title\")) > 0");
                    table.ForeignKey(
                        name: "FK_role_list_titles_role_list_titles_PrimaryTitleId",
                        column: x => x.PrimaryTitleId,
                        principalTable: "role_list_titles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_role_list_titles_role_lists_RoleListId",
                        column: x => x.RoleListId,
                        principalTable: "role_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "search_profile_location_sets",
                columns: table => new
                {
                    SearchProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationSetId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_profile_location_sets", x => new { x.SearchProfileId, x.LocationSetId });
                    table.ForeignKey(
                        name: "FK_search_profile_location_sets_location_sets_LocationSetId",
                        column: x => x.LocationSetId,
                        principalTable: "location_sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_search_profile_location_sets_search_profiles_SearchProfileId",
                        column: x => x.SearchProfileId,
                        principalTable: "search_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "search_profile_role_lists",
                columns: table => new
                {
                    SearchProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleListId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_profile_role_lists", x => new { x.SearchProfileId, x.RoleListId });
                    table.ForeignKey(
                        name: "FK_search_profile_role_lists_role_lists_RoleListId",
                        column: x => x.RoleListId,
                        principalTable: "role_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_search_profile_role_lists_search_profiles_SearchProfileId",
                        column: x => x.SearchProfileId,
                        principalTable: "search_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_location_set_locations_unique_no_country",
                table: "location_set_locations",
                columns: new[] { "LocationSetId", "Scope", "NormalizedLabel" },
                unique: true,
                filter: "\"CountryCode\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_location_set_locations_unique_with_country",
                table: "location_set_locations",
                columns: new[] { "LocationSetId", "Scope", "NormalizedLabel", "CountryCode" },
                unique: true,
                filter: "\"CountryCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_location_sets_NormalizedName",
                table: "location_sets",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_list_titles_PrimaryTitleId",
                table: "role_list_titles",
                column: "PrimaryTitleId");

            migrationBuilder.CreateIndex(
                name: "IX_role_list_titles_RoleListId_NormalizedTitle",
                table: "role_list_titles",
                columns: new[] { "RoleListId", "NormalizedTitle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_lists_NormalizedName",
                table: "role_lists",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_search_profile_location_sets_LocationSetId",
                table: "search_profile_location_sets",
                column: "LocationSetId");

            migrationBuilder.CreateIndex(
                name: "IX_search_profile_role_lists_RoleListId",
                table: "search_profile_role_lists",
                column: "RoleListId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "location_set_locations");
            migrationBuilder.DropTable(name: "role_list_titles");
            migrationBuilder.DropTable(name: "search_profile_location_sets");
            migrationBuilder.DropTable(name: "search_profile_role_lists");
            migrationBuilder.DropTable(name: "location_sets");
            migrationBuilder.DropTable(name: "role_lists");
        }
    }
}
