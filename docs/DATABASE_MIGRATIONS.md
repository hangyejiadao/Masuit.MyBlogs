# 数据库迁移

项目使用 EF Core + PostgreSQL 管理数据库结构。主业务上下文为 `DataContext`，日志上下文为 `LoggerDbContext`，两者共存于同一个 PostgreSQL 数据库，但使用独立的迁移历史表。

## 启动时自动迁移

应用启动时会依次执行：

```csharp
maindb.MigrateWithLegacyBaseline(DatabaseMigrationExtensions.MainHistoryTable, "SystemSetting", "Post");
loggerdb.MigrateWithLegacyBaseline(DatabaseMigrationExtensions.LoggerHistoryTable, "RequestLogDetail", "PerformanceCounter");
```

自动迁移逻辑位于：

```text
src/Masuit.MyBlogs.Core/Infrastructure/DatabaseMigrationExtensions.cs
```

处理规则：

1. 数据库不存在时执行 `Migrate()`，创建数据库和全部表。
2. 数据库已存在且已有业务表、但没有迁移历史时，将首个迁移自动登记为基线，不重复建表。
3. 数据库已有迁移历史时，只执行尚未应用的迁移。
4. 数据库已存在时使用 PostgreSQL advisory lock，避免多个应用实例同时执行后续迁移；首次创建数据库建议单实例启动。

生产环境仍应在发布前备份数据库，并优先在测试库验证迁移。

## 修改数据库表的流程

以主业务表为例：

1. 修改 `Models/Entity` 下的实体类。
2. 需要时修改 `DataContext.OnModelCreating()`、实体配置、索引、约束或关系。
3. 修改 DTO、ViewModel、Mapper、Service、Controller 和前端代码。
4. 在仓库根目录执行：

```powershell
dotnet tool restore
dotnet ef migrations add AddXxxField --project src\Masuit.MyBlogs.Core\Masuit.MyBlogs.Core.csproj --startup-project src\Masuit.MyBlogs.Core\Masuit.MyBlogs.Core.csproj --context Masuit.MyBlogs.Core.Infrastructure.DataContext --output-dir Infrastructure\Migrations
```

5. 检查生成的迁移文件以及 `DataContextModelSnapshot.cs`。
6. 编译并生成幂等部署 SQL：

```powershell
dotnet build src\Masuit.MyBlogs.Core\Masuit.MyBlogs.Core.csproj

dotnet ef migrations script --idempotent --project src\Masuit.MyBlogs.Core\Masuit.MyBlogs.Core.csproj --startup-project src\Masuit.MyBlogs.Core\Masuit.MyBlogs.Core.csproj --context Masuit.MyBlogs.Core.Infrastructure.DataContext --output database\postgres\migrations.sql
```

7. 将迁移文件和 `DataContextModelSnapshot.cs` 一起提交。
8. 部署新版本时，应用会在启动阶段自动执行迁移。

## 日志数据库迁移

`LoggerDbContext` 的迁移文件应放在独立目录：

```powershell
dotnet ef migrations add AddXxxField --project src\Masuit.MyBlogs.Core\Masuit.MyBlogs.Core.csproj --startup-project src\Masuit.MyBlogs.Core\Masuit.MyBlogs.Core.csproj --context Masuit.MyBlogs.Core.Infrastructure.LoggerDbContext --output-dir Infrastructure\Migrations\Logger
```

它的迁移历史表为：

```text
__EFMigrationsHistoryLogger
```

## 常用检查命令

查看主上下文迁移：

```powershell
dotnet ef migrations list --project src\Masuit.MyBlogs.Core\Masuit.MyBlogs.Core.csproj --startup-project src\Masuit.MyBlogs.Core\Masuit.MyBlogs.Core.csproj --context Masuit.MyBlogs.Core.Infrastructure.DataContext
```

查看日志上下文迁移：

```powershell
dotnet ef migrations list --project src\Masuit.MyBlogs.Core\Masuit.MyBlogs.Core.csproj --startup-project src\Masuit.MyBlogs.Core\Masuit.MyBlogs.Core.csproj --context Masuit.MyBlogs.Core.Infrastructure.LoggerDbContext
```

如果迁移生成错误，可以在尚未应用到数据库前移除最后一次迁移：

```powershell
dotnet ef migrations remove --project src\Masuit.MyBlogs.Core\Masuit.MyBlogs.Core.csproj --startup-project src\Masuit.MyBlogs.Core\Masuit.MyBlogs.Core.csproj --context Masuit.MyBlogs.Core.Infrastructure.DataContext
```