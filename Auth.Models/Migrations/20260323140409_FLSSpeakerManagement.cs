using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auth.Models.Migrations
{
    /// <inheritdoc />
    public partial class FLSSpeakerManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpeakerProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    SpeakerType = table.Column<int>(type: "integer", nullable: false),
                    Organization = table.Column<string>(type: "text", nullable: true),
                    Biography = table.Column<string>(type: "text", nullable: true),
                    IsDeregistered = table.Column<bool>(type: "boolean", nullable: false),
                    DeregisteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeregistrationReason = table.Column<string>(type: "text", nullable: true),
                    AccommodationBooked = table.Column<bool>(type: "boolean", nullable: false),
                    FlightTicketBooked = table.Column<bool>(type: "boolean", nullable: false),
                    ModificationDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakerProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FLSDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    OriginalFileName = table.Column<string>(type: "text", nullable: false),
                    StoredFileName = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "text", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "text", nullable: false),
                    TargetSpeakerProfileId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FLSDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FLSDocuments_SpeakerProfiles_TargetSpeakerProfileId",
                        column: x => x.TargetSpeakerProfileId,
                        principalTable: "SpeakerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MeetingTimeSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrganizerName = table.Column<string>(type: "text", nullable: false),
                    IsBooked = table.Column<bool>(type: "boolean", nullable: false),
                    BookedBySpeakerProfileId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    MeetingLink = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingTimeSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeetingTimeSlots_SpeakerProfiles_BookedBySpeakerProfileId",
                        column: x => x.BookedBySpeakerProfileId,
                        principalTable: "SpeakerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SpeakerNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SpeakerProfileId = table.Column<int>(type: "integer", nullable: false),
                    NotificationType = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    EmailSent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakerNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakerNotifications_SpeakerProfiles_SpeakerProfileId",
                        column: x => x.SpeakerProfileId,
                        principalTable: "SpeakerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakerTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SpeakerProfileId = table.Column<int>(type: "integer", nullable: false),
                    TaskType = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Location = table.Column<string>(type: "text", nullable: true),
                    TaskStartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TaskEndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakerTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakerTasks_SpeakerProfiles_SpeakerProfileId",
                        column: x => x.SpeakerProfileId,
                        principalTable: "SpeakerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakerUploads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SpeakerProfileId = table.Column<int>(type: "integer", nullable: false),
                    UploadType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "text", nullable: false),
                    StoredFileName = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "text", nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: true),
                    VerifiedByUserId = table.Column<string>(type: "text", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerificationNotes = table.Column<string>(type: "text", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakerUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakerUploads_SpeakerProfiles_SpeakerProfileId",
                        column: x => x.SpeakerProfileId,
                        principalTable: "SpeakerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakerComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SpeakerProfileId = table.Column<int>(type: "integer", nullable: false),
                    FLSDocumentId = table.Column<int>(type: "integer", nullable: false),
                    IsApproval = table.Column<bool>(type: "boolean", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakerComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakerComments_FLSDocuments_FLSDocumentId",
                        column: x => x.FLSDocumentId,
                        principalTable: "FLSDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SpeakerComments_SpeakerProfiles_SpeakerProfileId",
                        column: x => x.SpeakerProfileId,
                        principalTable: "SpeakerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FLSDocuments_TargetSpeakerProfileId",
                table: "FLSDocuments",
                column: "TargetSpeakerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingTimeSlots_BookedBySpeakerProfileId",
                table: "MeetingTimeSlots",
                column: "BookedBySpeakerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MeetingTimeSlots_StartTime",
                table: "MeetingTimeSlots",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakerComments_FLSDocumentId",
                table: "SpeakerComments",
                column: "FLSDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakerComments_SpeakerProfileId",
                table: "SpeakerComments",
                column: "SpeakerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakerNotifications_SpeakerProfileId_IsRead",
                table: "SpeakerNotifications",
                columns: new[] { "SpeakerProfileId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakerProfiles_UserId",
                table: "SpeakerProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakerTasks_SpeakerProfileId",
                table: "SpeakerTasks",
                column: "SpeakerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakerUploads_SpeakerProfileId_UploadType",
                table: "SpeakerUploads",
                columns: new[] { "SpeakerProfileId", "UploadType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeetingTimeSlots");

            migrationBuilder.DropTable(
                name: "SpeakerComments");

            migrationBuilder.DropTable(
                name: "SpeakerNotifications");

            migrationBuilder.DropTable(
                name: "SpeakerTasks");

            migrationBuilder.DropTable(
                name: "SpeakerUploads");

            migrationBuilder.DropTable(
                name: "FLSDocuments");

            migrationBuilder.DropTable(
                name: "SpeakerProfiles");
        }
    }
}
