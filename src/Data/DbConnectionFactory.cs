using Microsoft.Data.Sqlite;

namespace EigenfocusApi.Data;

/// <summary>
/// Creates SQLite connections to the Eigenfocus database.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Returns a new <see cref="SqliteConnection"/> that has already been opened and is ready to use.
    /// Callers are responsible for disposing the connection.
    /// </summary>
    SqliteConnection Create();
}

/// <summary>
/// Default <see cref="IDbConnectionFactory"/> backed by the <c>ConnectionStrings:DefaultConnection</c> setting.
/// </summary>
public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Resolves the connection string from configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <c>ConnectionStrings:DefaultConnection</c> is not configured.</exception>
    public DbConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
    }

    /// <summary>
    /// Returns a new <see cref="SqliteConnection"/> that has already been opened.
    /// </summary>
    public SqliteConnection Create()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
