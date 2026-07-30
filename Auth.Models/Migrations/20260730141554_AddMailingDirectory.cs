using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auth.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddMailingDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FirmGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ColorHex = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FirmImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    CreatedCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorReport = table.Column<string>(type: "text", nullable: true),
                    WasDryRun = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedByName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FirmTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    FirmGroupId = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    MatchKeywords = table.Column<string>(type: "text", nullable: true),
                    ColorHex = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmTypes_FirmGroups_FirmGroupId",
                        column: x => x.FirmGroupId,
                        principalTable: "FirmGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Firms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LegalName = table.Column<string>(type: "text", nullable: true),
                    FirmTypeId = table.Column<int>(type: "integer", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    NormalizedEmail = table.Column<string>(type: "text", nullable: true),
                    Website = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    ContactPersonName = table.Column<string>(type: "text", nullable: true),
                    ContactPersonRole = table.Column<string>(type: "text", nullable: true),
                    ContactNameSource = table.Column<int>(type: "integer", nullable: false),
                    ContactNameConfidence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ImportBatchId = table.Column<int>(type: "integer", nullable: true),
                    LastContactedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContactCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Firms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Firms_FirmImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "FirmImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Firms_FirmTypes_FirmTypeId",
                        column: x => x.FirmTypeId,
                        principalTable: "FirmTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MailingTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    FirmTypeId = table.Column<int>(type: "integer", nullable: true),
                    SubjectFirmVariant = table.Column<string>(type: "text", nullable: false),
                    BodyFirmVariant = table.Column<string>(type: "text", nullable: false),
                    PersonVariantEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SubjectPersonVariant = table.Column<string>(type: "text", nullable: true),
                    BodyPersonVariant = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailingTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MailingTemplates_FirmTypes_FirmTypeId",
                        column: x => x.FirmTypeId,
                        principalTable: "FirmTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MailingSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TemplateId = table.Column<int>(type: "integer", nullable: false),
                    Audience = table.Column<int>(type: "integer", nullable: false),
                    FirmTypeIds = table.Column<string>(type: "text", nullable: true),
                    FirmGroupIds = table.Column<string>(type: "text", nullable: true),
                    SelectedFirmIds = table.Column<string>(type: "text", nullable: true),
                    Cadence = table.Column<int>(type: "integer", nullable: false),
                    IntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCampaignId = table.Column<int>(type: "integer", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    DelayBetweenEmailsMs = table.Column<int>(type: "integer", nullable: false),
                    SendWindowStartHourUtc = table.Column<int>(type: "integer", nullable: false),
                    SendWindowEndHourUtc = table.Column<int>(type: "integer", nullable: false),
                    SkipAlreadyContacted = table.Column<bool>(type: "boolean", nullable: false),
                    MaxTotalSends = table.Column<int>(type: "integer", nullable: true),
                    TotalSent = table.Column<int>(type: "integer", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: true),
                    CustomFieldsJson = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedByName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailingSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MailingSchedules_MailingTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "MailingTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MailingCampaigns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TemplateId = table.Column<int>(type: "integer", nullable: true),
                    SubjectFirmVariant = table.Column<string>(type: "text", nullable: false),
                    BodyFirmVariant = table.Column<string>(type: "text", nullable: false),
                    PersonVariantEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SubjectPersonVariant = table.Column<string>(type: "text", nullable: true),
                    BodyPersonVariant = table.Column<string>(type: "text", nullable: true),
                    Audience = table.Column<int>(type: "integer", nullable: false),
                    FirmTypeIds = table.Column<string>(type: "text", nullable: true),
                    FirmGroupIds = table.Column<string>(type: "text", nullable: true),
                    SelectedFirmIds = table.Column<string>(type: "text", nullable: true),
                    ProviderKey = table.Column<string>(type: "text", nullable: true),
                    CustomFieldsJson = table.Column<string>(type: "text", nullable: true),
                    ScheduleId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalRecipients = table.Column<int>(type: "integer", nullable: false),
                    SentCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedByName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailingCampaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MailingCampaigns_MailingSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "MailingSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MailingCampaigns_MailingTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "MailingTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MailingCampaignRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampaignId = table.Column<int>(type: "integer", nullable: false),
                    FirmId = table.Column<int>(type: "integer", nullable: false),
                    ToEmail = table.Column<string>(type: "text", nullable: false),
                    ToName = table.Column<string>(type: "text", nullable: true),
                    VariantUsed = table.Column<int>(type: "integer", nullable: false),
                    RenderedSubject = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderUsed = table.Column<string>(type: "text", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailingCampaignRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MailingCampaignRecipients_Firms_FirmId",
                        column: x => x.FirmId,
                        principalTable: "Firms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MailingCampaignRecipients_MailingCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "MailingCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FirmGroups_Slug",
                table: "FirmGroups",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Firms_FirmTypeId",
                table: "Firms",
                column: "FirmTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Firms_ImportBatchId",
                table: "Firms",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Firms_LastContactedAt",
                table: "Firms",
                column: "LastContactedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Firms_Name",
                table: "Firms",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Firms_NormalizedEmail",
                table: "Firms",
                column: "NormalizedEmail",
                unique: true,
                filter: "\"NormalizedEmail\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Firms_Status_FirmTypeId",
                table: "Firms",
                columns: new[] { "Status", "FirmTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_FirmTypes_FirmGroupId",
                table: "FirmTypes",
                column: "FirmGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmTypes_Slug",
                table: "FirmTypes",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MailingCampaignRecipients_CampaignId_Status",
                table: "MailingCampaignRecipients",
                columns: new[] { "CampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MailingCampaignRecipients_FirmId_Status",
                table: "MailingCampaignRecipients",
                columns: new[] { "FirmId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MailingCampaigns_CreatedAt",
                table: "MailingCampaigns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MailingCampaigns_ScheduleId",
                table: "MailingCampaigns",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_MailingCampaigns_Status",
                table: "MailingCampaigns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MailingCampaigns_TemplateId",
                table: "MailingCampaigns",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MailingSchedules_IsEnabled_NextRunAt",
                table: "MailingSchedules",
                columns: new[] { "IsEnabled", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MailingSchedules_TemplateId",
                table: "MailingSchedules",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MailingTemplates_FirmTypeId",
                table: "MailingTemplates",
                column: "FirmTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_MailingTemplates_IsActive_FirmTypeId",
                table: "MailingTemplates",
                columns: new[] { "IsActive", "FirmTypeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailingCampaignRecipients");

            migrationBuilder.DropTable(
                name: "Firms");

            migrationBuilder.DropTable(
                name: "MailingCampaigns");

            migrationBuilder.DropTable(
                name: "FirmImportBatches");

            migrationBuilder.DropTable(
                name: "MailingSchedules");

            migrationBuilder.DropTable(
                name: "MailingTemplates");

            migrationBuilder.DropTable(
                name: "FirmTypes");

            migrationBuilder.DropTable(
                name: "FirmGroups");
        }
    }
}
