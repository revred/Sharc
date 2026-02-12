using System;
using Microsoft.Data.Sqlite;

class Program
{
    static void Main()
    {
        string dbPath = @"C:\Users\End User\AppData\Roaming\Antigravity\User\globalStorage\state.vscdb";
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM ItemTable WHERE key LIKE '%account%' OR key LIKE '%identity%' OR key LIKE '%google%'";
        
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            Console.WriteLine($"Key: {reader["key"]}");
            // Value might be long, so we just print a snippet
            string val = reader["value"].ToString();
            Console.WriteLine($"Value Snippet: {(val.Length > 100 ? val.Substring(0, 100) : val)}");
            Console.WriteLine("-----------------------------------");
        }
    }
}
