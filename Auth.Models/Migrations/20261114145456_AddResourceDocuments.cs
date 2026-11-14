using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auth.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResourceDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ExternalPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceDocumentFiles",
                columns: table => new
                {
                    DocumentId = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceDocumentFiles", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_ResourceDocumentFiles_ResourceDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "ResourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceDocuments_IsPublished_SortOrder",
                table: "ResourceDocuments",
                columns: new[] { "IsPublished", "SortOrder" });

            // The PDFs the web app has always shipped, so staff can rename or remove them too.
            // Already-published, and not notified: scholars have had these all along.
            migrationBuilder.Sql(@"
INSERT INTO ""ResourceDocuments"" (""Title"", ""FileName"", ""ContentType"", ""SizeBytes"", ""ExternalPath"", ""IsPublished"", ""SortOrder"", ""CreatedAt"", ""NotifiedAt"")
SELECT v.title, v.file, v.ct, v.size, v.path, v.pub, v.sort, v.created, v.created
FROM (VALUES
('Posjeta BHFF događaju', 'Visiting a BHFF Event.pdf', 'application/pdf', 83650, '/Visiting a BHFF Event.pdf', TRUE, 1, NOW()),
('Kako stipendista može dobiti preporuku', 'How can a Scholar get a Recommendation Letter.pdf', 'application/pdf', 44596, '/How can a Scholar get a Recommendation Letter.pdf', TRUE, 2, NOW()),
('Inicijativa za kritično finansiranje', 'Critical Funding Initiative.pdf', 'application/pdf', 81675, '/Critical Funding Initiative.pdf', TRUE, 3, NOW()),
('BHFF smjernice za procjenu', 'BHFF Assessment Guidelines.pdf', 'application/pdf', 70391, '/BHFF Assessment Guidelines.pdf', TRUE, 4, NOW()),
('Kodeks ponašanja 2021-23', 'Code of Conduct 2021-23.pdf', 'application/pdf', 278384, '/Code of Conduct 2021-23.pdf', TRUE, 5, NOW()),
('Vodič za ciljeve', 'Goals.pdf', 'application/pdf', 50181, '/Goals.pdf', TRUE, 6, NOW()),
('Priručnik', 'Guidebook.pdf', 'application/pdf', 2428966, '/Guidebook.pdf', TRUE, 7, NOW()),
('Vodič za refleksiju', 'Reflection.pdf', 'application/pdf', 48846, '/Reflection.pdf', TRUE, 8, NOW())
) AS v(title, file, ct, size, path, pub, sort, created);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceDocumentFiles");

            migrationBuilder.DropTable(
                name: "ResourceDocuments");
        }
    }
}
