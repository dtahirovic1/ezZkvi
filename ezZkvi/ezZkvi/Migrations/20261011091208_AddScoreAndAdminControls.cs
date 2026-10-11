using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ezZkvi.Migrations
{
    /// <inheritdoc />
    public partial class AddScoreAndAdminControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {


            migrationBuilder.AlterColumn<decimal>(
                name: "BrojBodova",
                table: "KvizSesijaPitanje",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                oldClrType: typeof(double),
                oldType: "double precision");

            migrationBuilder.AddColumn<int>(
                name: "BrojNetacnih",
                table: "KvizSesija",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrojPreskocenih",
                table: "KvizSesija",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaksimalniPoeni",
                table: "KvizSesija",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<double>(
                name: "NoviScore",
                table: "KvizSesija",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "NoviScorePredmeta",
                table: "KvizSesija",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<decimal>(
                name: "OsvojeniPoeni",
                table: "KvizSesija",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<double>(
                name: "PrethodniScore",
                table: "KvizSesija",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PrethodniScorePredmeta",
                table: "KvizSesija",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "ApplicationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequireAdminApproval = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ApplicationSettings",
                columns: new[] { "Id", "RequireAdminApproval" },
                values: new object[] { 1, true });

            migrationBuilder.Sql("""

-- Keep existing lifetime aggregates; fill missing/undersized ones from available history.
INSERT INTO "StudentStatistika" ("KorisnikId", "PredmetId", "BrojKvizova", "UkupnoPitanja", "TacniOdgovori")
SELECT "StudentId", "PredmetId", COUNT(*)::int, SUM("TraziBrojPitanja")::int, SUM("BrojTacnih")::int
FROM "KvizSesija" WHERE "Status" IN (1,2) AND "StudentId" IS NOT NULL AND "PredmetId" IS NOT NULL
AND "StudentId" IN (SELECT "Id" FROM "AspNetUsers")
GROUP BY "StudentId", "PredmetId"
ON CONFLICT ("KorisnikId", "PredmetId") DO UPDATE SET
"BrojKvizova" = GREATEST("StudentStatistika"."BrojKvizova", EXCLUDED."BrojKvizova"),
"UkupnoPitanja" = GREATEST("StudentStatistika"."UkupnoPitanja", EXCLUDED."UkupnoPitanja"),
"TacniOdgovori" = GREATEST("StudentStatistika"."TacniOdgovori", EXCLUDED."TacniOdgovori");

UPDATE "KvizSesija" SET "MaksimalniPoeni" = "TraziBrojPitanja" * 0.10,
"OsvojeniPoeni" = ROUND("TraziBrojPitanja" * 0.10 * "Procenat" / 100, 2)
WHERE "Status" IN (1,2);

-- Reconstruct historical Score changes using aggregate totals minus later sessions.
CREATE FUNCTION pg_temp.ezkvi_score(t double precision, n double precision) RETURNS double precision LANGUAGE SQL AS $$
SELECT CASE WHEN n <= 0 THEN 0 ELSE
1000 * POWER(GREATEST(0, LEAST(1,
(t/n + 5.4289/(2*n) - 2.33*SQRT(GREATEST(0, (t/n)*(1-t/n)/n + 5.4289/(4*n*n)))) / (1+5.4289/n))), 2)
* (1+0.25*LN(1+n/18)/LN(2)) END
$$;
WITH totals AS (
SELECT "KorisnikId" uid, SUM("TacniOdgovori")::double precision t, SUM("UkupnoPitanja")::double precision n FROM "StudentStatistika" GROUP BY "KorisnikId"
), history AS (
SELECT s.*, totals.t, totals.n, stats."TacniOdgovori" st, stats."UkupnoPitanja" sn,
COALESCE(SUM(s."BrojTacnih") OVER (PARTITION BY s."StudentId" ORDER BY s."DatumZavrsetka", s."ID" ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING),0) later_t,
COALESCE(SUM(s."TraziBrojPitanja") OVER (PARTITION BY s."StudentId" ORDER BY s."DatumZavrsetka", s."ID" ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING),0) later_n,
COALESCE(SUM(s."BrojTacnih") OVER (PARTITION BY s."StudentId",s."PredmetId" ORDER BY s."DatumZavrsetka", s."ID" ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING),0) subject_later_t,
COALESCE(SUM(s."TraziBrojPitanja") OVER (PARTITION BY s."StudentId",s."PredmetId" ORDER BY s."DatumZavrsetka", s."ID" ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING),0) subject_later_n
FROM "KvizSesija" s JOIN totals ON totals.uid=s."StudentId"
JOIN "StudentStatistika" stats ON stats."KorisnikId"=s."StudentId" AND stats."PredmetId"=s."PredmetId"
WHERE s."Status" IN (1,2)
)
UPDATE "KvizSesija" s SET
"PrethodniScore"=pg_temp.ezkvi_score(GREATEST(0,h.t-h.later_t-h."BrojTacnih"),GREATEST(0,h.n-h.later_n-h."TraziBrojPitanja")),
"NoviScore"=pg_temp.ezkvi_score(GREATEST(0,h.t-h.later_t),GREATEST(0,h.n-h.later_n)),
"PrethodniScorePredmeta"=pg_temp.ezkvi_score(GREATEST(0,h.st-h.subject_later_t-h."BrojTacnih"),GREATEST(0,h.sn-h.subject_later_n-h."TraziBrojPitanja")),
"NoviScorePredmeta"=pg_temp.ezkvi_score(GREATEST(0,h.st-h.subject_later_t),GREATEST(0,h.sn-h.subject_later_n))
FROM history h WHERE s."ID"=h."ID";

WITH old AS (SELECT "ID", ROW_NUMBER() OVER (PARTITION BY "StudentId" ORDER BY "DatumZavrsetka" DESC, "ID" DESC) rn FROM "KvizSesija" WHERE "Status" IN (1,2) AND "StudentId" IS NOT NULL)
DELETE FROM "KvizSesijaPitanje" WHERE "KvizSesijaId" IN (SELECT "ID" FROM old WHERE rn>5);
WITH old AS (SELECT "ID", ROW_NUMBER() OVER (PARTITION BY "StudentId" ORDER BY "DatumZavrsetka" DESC, "ID" DESC) rn FROM "KvizSesija" WHERE "Status" IN (1,2) AND "StudentId" IS NOT NULL)
DELETE FROM "KvizSesija" WHERE "ID" IN (SELECT "ID" FROM old WHERE rn>5);

""");

            migrationBuilder.CreateIndex(
                name: "IX_KvizSesija_StudentId_Status_DatumZavrsetka",
                table: "KvizSesija",
                columns: new[] { "StudentId", "Status", "DatumZavrsetka" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationSettings");

            migrationBuilder.DropIndex(
                name: "IX_KvizSesija_StudentId_Status_DatumZavrsetka",
                table: "KvizSesija");

            migrationBuilder.DropColumn(
                name: "BrojNetacnih",
                table: "KvizSesija");

            migrationBuilder.DropColumn(
                name: "BrojPreskocenih",
                table: "KvizSesija");

            migrationBuilder.DropColumn(
                name: "MaksimalniPoeni",
                table: "KvizSesija");

            migrationBuilder.DropColumn(
                name: "NoviScore",
                table: "KvizSesija");

            migrationBuilder.DropColumn(
                name: "NoviScorePredmeta",
                table: "KvizSesija");

            migrationBuilder.DropColumn(
                name: "OsvojeniPoeni",
                table: "KvizSesija");

            migrationBuilder.DropColumn(
                name: "PrethodniScore",
                table: "KvizSesija");

            migrationBuilder.DropColumn(
                name: "PrethodniScorePredmeta",
                table: "KvizSesija");



            migrationBuilder.AlterColumn<double>(
                name: "BrojBodova",
                table: "KvizSesijaPitanje",
                type: "double precision",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2);
        }
    }
}
