using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SENGENSystem.Server.Common.Persistence.Migrations
{
    /// <summary>
    /// Replaces the two "is it a lab?" booleans with the distinctions the timetable actually needs:
    /// a room's <c>Kind</c> (lecture room / computer laboratory / kitchen laboratory) and a
    /// subject's <c>Delivery</c> with its lecture/laboratory hour split and the laboratory kind
    /// that half requires.
    /// <para>
    /// The scaffolded version dropped the old columns before adding the new ones, which would have
    /// thrown the existing data away. The order here is add → backfill → drop, so every existing
    /// room and subject carries its meaning across, and <c>Down</c> reverses it the same way.
    /// </para>
    /// </summary>
    public partial class AddRoomKindAndSubjectDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Rooms",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "LectureRoom");

            migrationBuilder.AddColumn<string>(
                name: "Delivery",
                table: "Subjects",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "LectureOnly");

            migrationBuilder.AddColumn<string>(
                name: "LabRoomKind",
                table: "Subjects",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LaboratoryHours",
                table: "Subjects",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LectureHours",
                table: "Subjects",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Existing laboratories become computer laboratories — the only lab kind the system
            // could previously express — unless the room names itself a kitchen.
            migrationBuilder.Sql(@"
                UPDATE Rooms
                SET Kind = CASE
                    WHEN IsLaboratory = 0 THEN 'LectureRoom'
                    WHEN Name LIKE '%Kitchen%' OR Name LIKE '%Culinary%' THEN 'KitchenLaboratory'
                    ELSE 'ComputerLaboratory'
                END;");

            // A subject that required a lab was, in the old model, entirely a laboratory: all of
            // its weekly hours met in one. Everything else was entirely a lecture. Which lab it
            // needs is inferred from the program — HRA/HRS are the culinary ones.
            migrationBuilder.Sql(@"
                UPDATE Subjects
                SET Delivery        = CASE WHEN RequiresLaboratory = 1 THEN 'LaboratoryOnly' ELSE 'LectureOnly' END,
                    LectureHours    = CASE WHEN RequiresLaboratory = 1 THEN 0 ELSE Hours END,
                    LaboratoryHours = CASE WHEN RequiresLaboratory = 1 THEN Hours ELSE 0 END,
                    LabRoomKind     = CASE
                        WHEN RequiresLaboratory = 0 THEN NULL
                        WHEN ProgramCode IN ('HRA', 'HRS') THEN 'KitchenLaboratory'
                        ELSE 'ComputerLaboratory'
                    END;");

            migrationBuilder.DropColumn(
                name: "IsLaboratory",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "RequiresLaboratory",
                table: "Subjects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLaboratory",
                table: "Rooms",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresLaboratory",
                table: "Subjects",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Collapsing back loses which laboratory was needed and how the hours split; Hours is
            // already the total, so it stays correct on its own.
            migrationBuilder.Sql(
                "UPDATE Rooms SET IsLaboratory = CASE WHEN Kind = 'LectureRoom' THEN 0 ELSE 1 END;");
            migrationBuilder.Sql(
                "UPDATE Subjects SET RequiresLaboratory = CASE WHEN Delivery = 'LectureOnly' THEN 0 ELSE 1 END;");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Delivery",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "LabRoomKind",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "LaboratoryHours",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "LectureHours",
                table: "Subjects");
        }
    }
}
