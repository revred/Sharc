using Sharc;
using System.Diagnostics;

Console.WriteLine("Sharc Point Lookup (Seek) Sample");
Console.WriteLine("--------------------------------");

using var db = SharcDatabase.Open("sample.db");
using var reader = db.CreateReader("users");

// Example: Seek to ID 1234
long targetId = 1234;

var sw = Stopwatch.StartNew();
bool found = reader.Seek(targetId);
sw.Stop();

if (found)
{
    Console.WriteLine($"SUCCESS: Found user with ID {targetId} in {sw.Elapsed.TotalMicroseconds:F2} us");
    Console.WriteLine($"Name: {reader.GetString("name")}");
}
else
{
    Console.WriteLine($"FAILED: User with ID {targetId} not found.");
}
