using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class DedupeTransactionsAndSyncLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BankTransaction_PlaidTransactionId",
                table: "BankTransaction");

            migrationBuilder.CreateTable(
                name: "SyncLease",
                columns: table => new
                {
                    InstitutionAccessItemId = table.Column<int>(type: "int", nullable: false),
                    LeasedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Holder = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncLease", x => x.InstitutionAccessItemId);
                    table.ForeignKey(
                        name: "FK_SyncLease_InstitutionAccessItem_InstitutionAccessItemId",
                        column: x => x.InstitutionAccessItemId,
                        principalTable: "InstitutionAccessItem",
                        principalColumn: "InstitutionAccessItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            // Two syncs of the same bank at once stored some transactions twice. Keep one row per Plaid transaction:
            // the one with the person's own choices (category, treatment, note, split or goal), otherwise the oldest.
            // A goal contribution on a removed copy moves to the kept row. One statement, so the temp table lasts.
            migrationBuilder.Sql(@"
IF OBJECT_ID('tempdb..#dupes') IS NOT NULL DROP TABLE #dupes;

SELECT r.BankTransactionId, r.PlaidTransactionId, r.rn
INTO #dupes
FROM (
    SELECT t.BankTransactionId, t.PlaidTransactionId,
           COUNT(*) OVER (PARTITION BY t.PlaidTransactionId) AS copies,
           ROW_NUMBER() OVER (PARTITION BY t.PlaidTransactionId ORDER BY
               CASE WHEN t.CategorySource = 'User' OR t.TreatmentSource = 'User' OR t.Notes IS NOT NULL
                      OR EXISTS (SELECT 1 FROM TransactionSplit s WHERE s.BankTransactionId = t.BankTransactionId)
                      OR EXISTS (SELECT 1 FROM GoalContribution g WHERE g.BankTransactionId = t.BankTransactionId)
                    THEN 0 ELSE 1 END,
               t.BankTransactionId) AS rn
    FROM BankTransaction t
) r
WHERE r.copies > 1;

UPDATE g SET BankTransactionId = k.BankTransactionId
FROM GoalContribution g
JOIN #dupes l ON l.BankTransactionId = g.BankTransactionId AND l.rn > 1
JOIN #dupes k ON k.PlaidTransactionId = l.PlaidTransactionId AND k.rn = 1
WHERE NOT EXISTS (SELECT 1 FROM GoalContribution x WHERE x.BankTransactionId = k.BankTransactionId)
  AND g.GoalContributionId = (
      SELECT MIN(g2.GoalContributionId)
      FROM GoalContribution g2
      JOIN #dupes l2 ON l2.BankTransactionId = g2.BankTransactionId AND l2.rn > 1
      WHERE l2.PlaidTransactionId = l.PlaidTransactionId);

DELETE g FROM GoalContribution g JOIN #dupes l ON l.BankTransactionId = g.BankTransactionId AND l.rn > 1;
DELETE t FROM BankTransaction t JOIN #dupes l ON l.BankTransactionId = t.BankTransactionId AND l.rn > 1;

DROP TABLE #dupes;
");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_PlaidTransactionId",
                table: "BankTransaction",
                column: "PlaidTransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncLease");

            migrationBuilder.DropIndex(
                name: "IX_BankTransaction_PlaidTransactionId",
                table: "BankTransaction");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_PlaidTransactionId",
                table: "BankTransaction",
                column: "PlaidTransactionId");
        }
    }
}
