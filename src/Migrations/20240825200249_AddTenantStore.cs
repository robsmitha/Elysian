using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE [TenantInfo] (
    [Id] nvarchar(64) NOT NULL,
    [Identifier] nvarchar(450) NULL,
    [Name] nvarchar(max) NULL,
    [CmsUrl] nvarchar(max) NULL,
    CONSTRAINT [PK_TenantInfo] PRIMARY KEY ([Id])
);
EXEC(N'CREATE UNIQUE INDEX [IX_TenantInfo_Identifier] ON [TenantInfo] ([Identifier]) WHERE [Identifier] IS NOT NULL');
");

            migrationBuilder.Sql(@"
INSERT INTO [dbo].[TenantInfo]
    ([Id]
    ,[Identifier]
    ,[Name]
    ,[CmsUrl])
VALUES
    (convert(nvarchar(64), newid())
    ,'robsmitha'
    ,'robsmitha',
    NULL),
	(convert(nvarchar(64), newid())
    ,'geekscloset'
    ,'geekscloset',
    NULL)
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
