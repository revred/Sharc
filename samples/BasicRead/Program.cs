using Sharc;

Console.WriteLine("Sharc Basic Read Sample");
Console.WriteLine("-----------------------");

// Opening a database from a file (or memory-mapped source)
using var db = SharcDatabase.Open("sample.db");

Console.WriteLine($"Database: {db.Schema.Tables.Count} tables found.");

// Create a reader for the 'users' table
// Projection: only load 'id' and 'name' columns to save work
using var reader = db.CreateReader("users", "id", "name");

int rowCount = 0;
while (reader.Read())
{
    long id = reader.GetInt64(0);
    string name = reader.GetString(1);
    
    Console.WriteLine($"[{id}] {name}");
    rowCount++;
}

Console.WriteLine($"-----------------------");
Console.WriteLine($"Finished reading {rowCount} rows.");
