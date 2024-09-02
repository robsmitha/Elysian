using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "FinancialCategory",
                columns: new[] { "Name" },
                values: new object[,]
                {
                    { "Rent" },
                    { "Renters Insurance" },
                    { "Medical Insurance" },
                    { "Utilities" },
                    { "Car" },
                    { "Student Loan" },
                    { "Phone" },
                    { "Hulu" },
                    { "Spotify" },
                    { "HBO Max" },
                    { "Gas" },
                    { "Grocery" },
                    { "Doctor" },
                    { "Pets" },
                    { "Savings" },
                    { "Shopping" },
                    { "Hobbies" },
                    { "Flex" }
                });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
