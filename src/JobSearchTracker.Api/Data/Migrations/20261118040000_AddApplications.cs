using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSearchTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsApplied = table.Column<bool>(type: "boolean", nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AppliedConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.Id);
                    table.CheckConstraint("CK_applications_applied_fact_complete", "(\"IsApplied\" AND \"Method\" IS NOT NULL AND \"AppliedAtUtc\" IS NOT NULL AND \"AppliedConfirmedAtUtc\" IS NOT NULL) OR (NOT \"IsApplied\" AND \"Method\" IS NULL AND \"AppliedAtUtc\" IS NULL AND \"AppliedConfirmedAtUtc\" IS NULL)");
                    table.CheckConstraint("CK_applications_method_values", "\"Method\" IS NULL OR \"Method\" IN ('manual')");
                    table.CheckConstraint("CK_applications_status_applied_consistent", "(\"Status\" NOT IN ('applied', 'interview', 'offer') OR \"IsApplied\") AND (\"Status\" NOT IN ('saved', 'pendingVerification') OR NOT \"IsApplied\")");
                    table.CheckConstraint("CK_applications_status_values", "\"Status\" IN ('saved', 'applied', 'pendingVerification', 'interview', 'offer', 'rejected', 'withdrawn')");
                    table.ForeignKey(
                        name: "FK_applications_candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_applications_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "application_audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Actor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OldStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    OldIsApplied = table.Column<bool>(type: "boolean", nullable: true),
                    OldAppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NewStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    NewIsApplied = table.Column<bool>(type: "boolean", nullable: false),
                    NewAppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_audit", x => x.Id);
                    table.CheckConstraint("CK_application_audit_action_values", "\"Action\" IN ('created', 'statusChanged', 'appliedConfirmed', 'unapplied', 'corrected')");
                    table.CheckConstraint("CK_application_audit_actor_values", "\"Actor\" IN ('user')");
                    table.CheckConstraint("CK_application_audit_new_status_values", "\"NewStatus\" IN ('saved', 'applied', 'pendingVerification', 'interview', 'offer', 'rejected', 'withdrawn')");
                    table.CheckConstraint("CK_application_audit_old_status_values", "\"OldStatus\" IS NULL OR \"OldStatus\" IN ('saved', 'applied', 'pendingVerification', 'interview', 'offer', 'rejected', 'withdrawn')");
                    table.CheckConstraint("CK_application_audit_reason_required", "\"Action\" NOT IN ('unapplied', 'corrected') OR (\"Reason\" IS NOT NULL AND char_length(btrim(\"Reason\")) > 0)");
                    table.ForeignKey(
                        name: "FK_application_audit_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_audit_ApplicationId_ChangedAtUtc",
                table: "application_audit",
                columns: new[] { "ApplicationId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_applications_CandidateId_JobId",
                table: "applications",
                columns: new[] { "CandidateId", "JobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_applications_JobId",
                table: "applications",
                column: "JobId");

            // Append-only audit trail (OQ-P1.9): the database itself rejects any UPDATE or DELETE of an audit row.
            // This is not part of the EF model, so it does not appear in the model snapshot.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION application_audit_append_only() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'application_audit is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'integrity_constraint_violation';
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_application_audit_append_only
                    BEFORE UPDATE OR DELETE ON application_audit
                    FOR EACH ROW EXECUTE FUNCTION application_audit_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "application_audit");
            migrationBuilder.DropTable(name: "applications");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS application_audit_append_only();");
        }
    }
}
