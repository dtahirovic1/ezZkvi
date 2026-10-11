using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ezZkvi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyUserInheritance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Do not silently discard legacy counters that need an explicit data review.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "AspNetUsers"
                        WHERE COALESCE("BrojOdgovorenihPitanja", 0) <> 0
                           OR COALESCE("BrojTacnihOdgovora", 0) <> 0
                           OR COALESCE("Student_BrojOdgovorenihPitanja", 0) <> 0
                           OR COALESCE("Student_BrojTacnihOdgovora", 0) <> 0
                    ) THEN
                        RAISE EXCEPTION 'Legacy user counters contain data. Back up and review them before removing inheritance.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "BrojOdgovorenihPitanja",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BrojTacnihOdgovora",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Discriminator",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Student_BrojOdgovorenihPitanja",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Student_BrojTacnihOdgovora",
                table: "AspNetUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BrojOdgovorenihPitanja",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrojTacnihOdgovora",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Discriminator",
                table: "AspNetUsers",
                type: "character varying(13)",
                maxLength: 13,
                nullable: false,
                defaultValue: "Korisnik");

            migrationBuilder.AddColumn<int>(
                name: "Student_BrojOdgovorenihPitanja",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Student_BrojTacnihOdgovora",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);
            migrationBuilder.Sql("ALTER TABLE \"AspNetUsers\" ALTER COLUMN \"Discriminator\" DROP DEFAULT;");
        }
    }
}
