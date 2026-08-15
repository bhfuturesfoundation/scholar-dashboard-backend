using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Models.Migrations
{
    /// <summary>
    /// Makes Order unique among active questions, after clearing any duplicates already there.
    /// </summary>
    /// <remarks>
    /// The repair step is not optional. CreateIndex on a table that already violates the
    /// constraint fails, and migrations run at startup — so shipping the index alone would
    /// take the API down on deploy rather than reporting a problem. Whether production
    /// currently holds a duplicate is unknown from here, which is exactly why this cannot
    /// assume it does not: the create path has always refused duplicates, but the update and
    /// reactivate paths did not, and both have been reachable for as long as they have
    /// existed.
    /// </remarks>
    public partial class AddUniqueActiveQuestionOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Within each set of active questions sharing an order, the lowest QuestionId keeps
            // the position and the rest are appended after the current maximum, in id order.
            //
            // Keeping the oldest in place is the least surprising choice: it is the one that
            // has been sitting at that position in the journal, so scholars and mentors have
            // been seeing it there. The later arrivals are the ones that should not have been
            // allowed in, and they move.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "QuestionId",
                           ROW_NUMBER() OVER (PARTITION BY "Order" ORDER BY "QuestionId") AS rn
                    FROM "Questions"
                    WHERE "Active"
                ),
                displaced AS (
                    SELECT "QuestionId",
                           ROW_NUMBER() OVER (ORDER BY "QuestionId") AS shift
                    FROM ranked
                    WHERE rn > 1
                )
                UPDATE "Questions" q
                SET "Order" = (SELECT COALESCE(MAX("Order"), 0) FROM "Questions" WHERE "Active") + d.shift
                FROM displaced d
                WHERE q."QuestionId" = d."QuestionId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Questions_Order_ActiveUnique",
                table: "Questions",
                column: "Order",
                unique: true,
                filter: "\"Active\"");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Only the index comes back off. The renumbering above cannot be undone — the
        /// original orders were duplicates and nothing records which row held which. Rolling
        /// back restores the ability to create duplicates, not the ones that were there.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Questions_Order_ActiveUnique",
                table: "Questions");
        }
    }
}
