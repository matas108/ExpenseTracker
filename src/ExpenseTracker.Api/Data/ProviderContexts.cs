using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ExpenseTracker.Api.Data;

public static class DatabaseProvider
{
    public const string Sqlite = "Sqlite";
    public const string SqlServer = "SqlServer";
}

// Local dev (zero setup). Migrations: Data/Migrations/Sqlite.
public class SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options) : AppDbContext(options);

// Deployed (Azure SQL). Migrations: Data/Migrations/SqlServer.
public class SqlServerAppDbContext(DbContextOptions<SqlServerAppDbContext> options) : AppDbContext(options);

// Design-time factories so `dotnet ef` can build either context regardless of the configured provider.
// Migrations don't connect, so placeholder connection strings are enough.
public class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
    public SqliteAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite("Data Source=design.db").Options);
}

public class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerAppDbContext>
{
    public SqlServerAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqlServerAppDbContext>().UseSqlServer("Server=.;Database=design").Options);
}
