using System.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Masuit.MyBlogs.Core.Infrastructure;

/// <summary>
/// EF Core 自动迁移扩展。
/// </summary>
public static class DatabaseMigrationExtensions
{
    public const string MainHistoryTable = "__EFMigrationsHistory";
    public const string LoggerHistoryTable = "__EFMigrationsHistoryLogger";

    private const long MigrationLockKey = 621528734639826421L;

    /// <summary>
    /// 自动迁移数据库；对于没有迁移历史但已经存在业务表的旧数据库，先自动登记首个迁移为基线。
    /// </summary>
    /// <typeparam name="TContext">数据库上下文类型。</typeparam>
    /// <param name="context">数据库上下文。</param>
    /// <param name="historyTable">迁移历史表名。</param>
    /// <param name="legacyTable">用于判断旧数据库是否已初始化的标志表。</param>
    public static void MigrateWithLegacyBaseline<TContext>(this TContext context, string historyTable, params string[] legacyTables) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyTable);
        ArgumentNullException.ThrowIfNull(legacyTables);
        if (legacyTables.Length == 0 || legacyTables.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one legacy table name is required.", nameof(legacyTables));
        }

        var database = context.Database;
        if (!database.CanConnect())
        {
            database.Migrate();
            return;
        }

        var connection = database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            connection.Open();
        }

        try
        {
            LockDatabase(connection);
            var initialMigrationId = GetInitialMigrationId(database);

            if (legacyTables.Any(table => TableExists(database, table)) && !MigrationApplied(database, historyTable, initialMigrationId))
            {
                CreateBaseline(database, historyTable, initialMigrationId);
            }

            database.Migrate();
        }
        finally
        {
            if (connection.State == ConnectionState.Open)
            {
                UnlockDatabase(connection);
            }

            if (shouldCloseConnection && connection.State != ConnectionState.Closed)
            {
                connection.Close();
            }
        }
    }

    private static string GetInitialMigrationId(DatabaseFacade database)
    {
        var migrationsAssembly = database.GetService<IMigrationsAssembly>();
        return migrationsAssembly.Migrations.Keys.OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault()
               ?? throw new InvalidOperationException("未找到任何 EF Core 迁移。");
    }

    private static bool TableExists(DatabaseFacade database, string tableName)
    {
        var regclass = $"public.{QuoteIdentifier(tableName)}";
        return database.SqlQueryRaw<int>("SELECT CASE WHEN to_regclass({0}) IS NULL THEN 0 ELSE 1 END AS \"Value\"", regclass).Single() == 1;
    }

    private static bool MigrationApplied(DatabaseFacade database, string historyTable, string migrationId)
    {
        if (!TableExists(database, historyTable))
        {
            return false;
        }

        var sql = $"SELECT COUNT(*)::integer AS \"Value\" FROM {QuoteIdentifier(historyTable)} WHERE \"MigrationId\" = {{0}}";
        return database.SqlQueryRaw<int>(sql, migrationId).Single() > 0;
    }

    private static void CreateBaseline(DatabaseFacade database, string historyTable, string initialMigrationId)
    {
        var table = QuoteIdentifier(historyTable);
        var constraint = QuoteIdentifier($"PK_{historyTable}");
        var productVersion = typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "10.0.0";
        var createTableSql = $@"
            CREATE TABLE IF NOT EXISTS {table} (
                ""MigrationId"" character varying(150) NOT NULL,
                ""ProductVersion"" character varying(32) NOT NULL,
                CONSTRAINT {constraint} PRIMARY KEY (""MigrationId"")
            );";
        database.ExecuteSqlRaw(createTableSql);

        var insertBaselineSql = $@"
            INSERT INTO {table} (""MigrationId"", ""ProductVersion"")
            VALUES ({{0}}, {{1}})
            ON CONFLICT (""MigrationId"") DO NOTHING;";
        database.ExecuteSqlRaw(insertBaselineSql, initialMigrationId, productVersion);
    }

    private static void LockDatabase(System.Data.Common.DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_lock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = MigrationLockKey;
        command.Parameters.Add(parameter);
        command.ExecuteNonQuery();
    }

    private static void UnlockDatabase(System.Data.Common.DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = MigrationLockKey;
        command.Parameters.Add(parameter);
        command.ExecuteNonQuery();
    }

    private static string QuoteIdentifier(string identifier)
    {
        return '"' + identifier.Replace("\"", "\"\"") + '"';
    }
}