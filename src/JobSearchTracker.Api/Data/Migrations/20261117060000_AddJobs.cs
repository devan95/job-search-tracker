using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSearchTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ApplyUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CanonicalUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NormalizedLocation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WorkMode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    EmploymentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Department = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequisitionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NormalizedRequisitionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExperienceMinYears = table.Column<int>(type: "integer", nullable: true),
                    ExperienceMaxYears = table.Column<int>(type: "integer", nullable: true),
                    PostedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    FoundOn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedVia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.Id);
                    table.CheckConstraint("CK_jobs_employment_type_values", "\"EmploymentType\" IS NULL OR \"EmploymentType\" IN ('fullTime', 'partTime', 'contract', 'internship', 'temporary', 'other')");
                    table.CheckConstraint("CK_jobs_experience_min_not_above_max", "\"ExperienceMinYears\" IS NULL OR \"ExperienceMaxYears\" IS NULL OR \"ExperienceMinYears\" <= \"ExperienceMaxYears\"");
                    table.CheckConstraint("CK_jobs_experience_years_not_negative", "(\"ExperienceMinYears\" IS NULL OR \"ExperienceMinYears\" >= 0) AND (\"ExperienceMaxYears\" IS NULL OR \"ExperienceMaxYears\" >= 0)");
                    table.CheckConstraint("CK_jobs_status_values", "\"Status\" IN ('new', 'open', 'closed', 'expired', 'reopened', 'unavailable', 'verificationPending')");
                    table.CheckConstraint("CK_jobs_title_not_blank", "char_length(btrim(\"Title\")) > 0");
                    table.CheckConstraint("CK_jobs_work_mode_values", "\"WorkMode\" IS NULL OR \"WorkMode\" IN ('remote', 'onSite', 'hybrid')");
                    table.ForeignKey(
                        name: "FK_jobs_employers_EmployerId",
                        column: x => x.EmployerId,
                        principalTable: "employers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_status_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_status_history", x => x.Id);
                    table.CheckConstraint("CK_job_status_history_from_status_values", "\"FromStatus\" IS NULL OR \"FromStatus\" IN ('new', 'open', 'closed', 'expired', 'reopened', 'unavailable', 'verificationPending')");
                    table.CheckConstraint("CK_job_status_history_to_status_values", "\"ToStatus\" IN ('new', 'open', 'closed', 'expired', 'reopened', 'unavailable', 'verificationPending')");
                    table.ForeignKey(
                        name: "FK_job_status_history_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_status_history_JobId_ChangedAtUtc",
                table: "job_status_history",
                columns: new[] { "JobId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_jobs_CanonicalUrl",
                table: "jobs",
                column: "CanonicalUrl");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_EmployerId_NormalizedRequisitionId",
                table: "jobs",
                columns: new[] { "EmployerId", "NormalizedRequisitionId" },
                unique: true,
                filter: "\"NormalizedRequisitionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_EmployerId_NormalizedTitle",
                table: "jobs",
                columns: new[] { "EmployerId", "NormalizedTitle" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "job_status_history");
            migrationBuilder.DropTable(name: "jobs");
        }
    }
}
