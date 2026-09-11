using Microsoft.Data.Sqlite;

namespace CodeMuster.Infrastructure.Tests;

public static class RawSqlite
{
    public static async Task<T> ScalarAsync<T>(string databasePath, string sql)
    {
        await using var connection = Connect(databasePath);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public static async Task ExecuteAsync(string databasePath, string sql)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using var connection = Connect(databasePath);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<List<string>> StringsAsync(string databasePath, string sql)
    {
        await using var connection = Connect(databasePath);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static SqliteConnection Connect(string databasePath) =>
        new(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
}
