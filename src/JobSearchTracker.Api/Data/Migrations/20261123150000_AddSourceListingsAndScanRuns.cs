using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSearchTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceListingsAndScanRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scan_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SearchProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_runs", x => x.Id);
                    table.CheckConstraint("CK_scan_runs_finished_not_before_started", "\"FinishedAtUtc\" IS NULL OR \"FinishedAtUtc\" >= \"StartedAtUtc\"");
                    table.CheckConstraint("CK_scan_runs_finished_with_outcome", "(\"FinishedAtUtc\" IS NULL) = (\"Outcome\" IS NULL)");
                    table.CheckConstraint("CK_scan_runs_outcome_values", "\"Outcome\" IS NULL OR \"Outcome\" IN ('success', 'partial', 'failed', 'unsupported', 'notChecked')");
                    table.CheckConstraint("CK_scan_runs_trigger_values", "\"Trigger\" IN ('manual', 'scheduled', 'boardTest')");
                    table.ForeignKey(
                        name: "FK_scan_runs_search_profiles_SearchProfileId",
                        column: x => x.SearchProfileId,
                        principalTable: "search_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "source_listings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceUpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_listings", x => x.Id);
                    table.CheckConstraint("CK_source_listings_content_hash_format", "\"ContentHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_source_listings_external_id_not_blank", "char_length(btrim(\"ExternalId\")) > 0");
                    table.CheckConstraint("CK_source_listings_seen_order", "\"LastSeenAtUtc\" >= \"FirstSeenAtUtc\"");
                    table.CheckConstraint("CK_source_listings_source_type_not_blank", "char_length(btrim(\"SourceType\")) > 0");
                    table.CheckConstraint("CK_source_listings_source_url_http", "\"SourceUrl\" IS NULL OR \"SourceUrl\" ~* '^https?://'");
                    table.ForeignKey(
                        name: "FK_source_listings_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "source_outcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EmployerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ErrorText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecommendedNextStep = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ListingsFound = table.Column<int>(type: "integer", nullable: false),
                    ListingsRejected = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_outcomes", x => x.Id);
                    table.CheckConstraint("CK_source_outcomes_counts_valid", "\"ListingsFound\" >= 0 AND \"ListingsRejected\" >= 0 AND \"Attempts\" >= 1");
                    table.CheckConstraint("CK_source_outcomes_finished_not_before_started", "\"FinishedAtUtc\" >= \"StartedAtUtc\"");
                    table.CheckConstraint("CK_source_outcomes_listings_only_when_read", "\"Outcome\" IN ('success', 'partial') OR \"ListingsFound\" = 0");
                    table.CheckConstraint("CK_source_outcomes_method_values", "\"Method\" IN ('api', 'careerPage', 'linkOnly', 'emailDerived')");
                    table.CheckConstraint("CK_source_outcomes_outcome_values", "\"Outcome\" IN ('success', 'partial', 'failed', 'unsupported', 'notChecked')");
                    table.CheckConstraint("CK_source_outcomes_restricted_method_not_searched", "\"Method\" NOT IN ('linkOnly', 'emailDerived') OR \"Outcome\" NOT IN ('success', 'partial')");
                    table.CheckConstraint("CK_source_outcomes_source_type_not_blank", "char_length(btrim(\"SourceType\")) > 0");
                    table.ForeignKey(
                        name: "FK_source_outcomes_employers_EmployerId",
                        column: x => x.EmployerId,
                        principalTable: "employers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_source_outcomes_scan_runs_ScanRunId",
                        column: x => x.ScanRunId,
                        principalTable: "scan_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scan_runs_SearchProfileId",
                table: "scan_runs",
                column: "SearchProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_source_listings_JobId",
                table: "source_listings",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_source_listings_SourceType_ExternalId",
                table: "source_listings",
                columns: new[] { "SourceType", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_source_outcomes_EmployerId",
                table: "source_outcomes",
                column: "EmployerId");

            migrationBuilder.CreateIndex(
                name: "IX_source_outcomes_ScanRunId",
                table: "source_outcomes",
                column: "ScanRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "source_listings");

            migrationBuilder.DropTable(
                name: "source_outcomes");

            migrationBuilder.DropTable(
                name: "scan_runs");
        }
    }
}
