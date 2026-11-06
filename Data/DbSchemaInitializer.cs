using Microsoft.EntityFrameworkCore;
using ReleaseManager.Models;

namespace ReleaseManager.Data;

/// <summary>
/// Applies small forward-compatible schema changes for installations created
/// before EF migrations were introduced. EnsureCreated does not update an
/// existing database, so these checks must run after it.
/// </summary>
public static class DbSchemaInitializer
{
    public static async Task EnsureCurrentAsync(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await EnsureColumnAsync(db, "Releases", "Status", $"varchar(30) NOT NULL DEFAULT '{ReleaseStatus.ON_DEVELOPING}'");
            await EnsureColumnAsync(db, "Releases", "Notes", "varchar(4000) NULL");
            await EnsureColumnAsync(db, "Releases", "EmailPreparedAt", "datetime(6) NULL");
            await EnsureColumnAsync(db, "Releases", "WhatsAppSentAt", "datetime(6) NULL");
            await EnsureColumnAsync(db, "Releases", "ZipFilePath", "varchar(1000) NULL");
            await EnsureColumnAsync(db, "Users", "WhatsAppMessageFormat", "varchar(4000) NULL");
            await EnsureColumnAsync(db, "Programs", "ZipFileExtensions", "varchar(500) NULL");
            var programVersionAdded = await EnsureColumnAsync(db, "ReleaseItems", "ProgramVersion", "varchar(50) NOT NULL DEFAULT ''");
            if (programVersionAdded)
            {
                await using var migrateProgramVersion = db.Database.GetDbConnection().CreateCommand();
                migrateProgramVersion.CommandText = "UPDATE `ReleaseItems` AS ri JOIN `Releases` AS r ON r.`Id` = ri.`ReleaseId` SET ri.`ProgramVersion` = r.`Version` WHERE ri.`ProgramVersion` = ''";
                await migrateProgramVersion.ExecuteNonQueryAsync();
            }
            var createdAtAdded = await EnsureColumnAsync(db, "ReleaseItems", "CreatedAt", "datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)");
            await EnsureColumnAsync(db, "ReleaseItems", "UserId", "int NULL");
            if (await ColumnExistsAsync(db, "ReleaseItems", "Complaint"))
            {
                await using var legacyComplaint = db.Database.GetDbConnection().CreateCommand();
                legacyComplaint.CommandText = "ALTER TABLE `ReleaseItems` ALTER COLUMN `Complaint` SET DEFAULT 0";
                await legacyComplaint.ExecuteNonQueryAsync();
            }
            if (createdAtAdded && await ColumnExistsAsync(db, "ReleaseItems", "Date"))
            {
                await using var migrateCreatedAt = db.Database.GetDbConnection().CreateCommand();
                migrateCreatedAt.CommandText = "UPDATE `ReleaseItems` SET `CreatedAt` = `Date`";
                await migrateCreatedAt.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> EnsureColumnAsync(AppDbContext db, string table, string column, string definition)
    {
        if (await ColumnExistsAsync(db, table, column)) return false;

        await using var command = db.Database.GetDbConnection().CreateCommand();
        // Table, column, and definition are fixed constants supplied by this initializer.
        command.CommandText = $"ALTER TABLE `{table}` ADD COLUMN `{column}` {definition}";
        await command.ExecuteNonQueryAsync();
        return true;
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string table, string column)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @table
              AND COLUMN_NAME = @column
            """;
        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@table";
        tableParameter.Value = table;
        command.Parameters.Add(tableParameter);
        var columnParameter = command.CreateParameter();
        columnParameter.ParameterName = "@column";
        columnParameter.Value = column;
        command.Parameters.Add(columnParameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync()) != 0;
    }
}
