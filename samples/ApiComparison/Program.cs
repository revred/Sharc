// Sharc vs Microsoft.Data.Sqlite — End-to-End API Comparison
//
// This sample runs identical operations through both APIs with timing,
// showing the code patterns and performance differences side by side.

using Microsoft.Data.Sqlite;
using Sharc;
using Sharc.Core;
using System.Diagnostics;
using System.Text;

const int RowCount = 10_000;
const int WarmupIterations = 10;
const int TimedIterations = 100;

// ═══════════════════════════════════════════════════════════════
//  Setup: Create a test database with 10,000 users
// ═══════════════════════════════════════════════════════════════

var dbPath = Path.Combine(Path.GetTempPath(), $"sharc_apicomp_{Guid.NewGuid():N}.db");

using (var conn = new SqliteConnection($"Data Source={dbPath}"))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE users (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            email TEXT NOT NULL,
            age INTEGER,
            balance REAL,
            bio TEXT
        )
        """;
    cmd.ExecuteNonQuery();

    using var tx = conn.BeginTransaction();
    cmd.Transaction = tx;
    cmd.CommandText = "INSERT INTO users VALUES ($id, $name, $email, $age, $balance, $bio)";
    var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
    var pName = cmd.Parameters.Add("$name", SqliteType.Text);
    var pEmail = cmd.Parameters.Add("$email", SqliteType.Text);
    var pAge = cmd.Parameters.Add("$age", SqliteType.Integer);
    var pBalance = cmd.Parameters.Add("$balance", SqliteType.Real);
    var pBio = cmd.Parameters.Add("$bio", SqliteType.Text);
    for (int i = 1; i <= RowCount; i++)
    {
        pId.Value = i;
        pName.Value = $"User_{i}";
        pEmail.Value = $"user{i}@example.com";
        pAge.Value = 18 + (i % 60);
        pBalance.Value = 100.0 + (i * 0.37);
        pBio.Value = i % 5 == 0 ? DBNull.Value : $"Bio for user {i}, which is a longer text field.";
        cmd.ExecuteNonQuery();
    }
    tx.Commit();
}
SqliteConnection.ClearAllPools();

var dbBytes = File.ReadAllBytes(dbPath);

Console.WriteLine("Sharc vs SQLite — API Comparison");
Console.WriteLine($"Database: {RowCount:N0} users, {dbBytes.Length / 1024} KB");
Console.WriteLine(new string('═', 70));
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════
//  1. Point Lookup by Primary Key
// ═══════════════════════════════════════════════════════════════

PrintHeader("1. Point Lookup (Seek by rowid 5000)");

// --- Sharc ---
PrintSubHeader("Sharc: db.CreateReader → Seek");
PrintCode("""
    using var db = SharcDatabase.OpenMemory(bytes);
    using var reader = db.CreateReader("users", "name", "email", "age");
    reader.Seek(5000);
    var name = reader.GetString(0);
    """);

double sharcLookup = Measure(() =>
{
    using var db = SharcDatabase.OpenMemory(dbBytes);
    using var reader = db.CreateReader("users", "name", "email", "age");
    reader.Seek(5000);
    _ = reader.GetString(0);
});

// --- SQLite ---
PrintSubHeader("SQLite: SqliteConnection → ExecuteReader");
PrintCode("""
    using var conn = new SqliteConnection(connStr);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT name, email, age FROM users WHERE id = 5000";
    using var reader = cmd.ExecuteReader();
    reader.Read();
    var name = reader.GetString(0);
    """);

using var lookupConn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
lookupConn.Open();
using var lookupCmd = lookupConn.CreateCommand();
lookupCmd.CommandText = "SELECT name, email, age FROM users WHERE id = @id";
lookupCmd.Parameters.Add("@id", SqliteType.Integer).Value = 5000L;

double sqliteLookup = Measure(() =>
{
    using var reader = lookupCmd.ExecuteReader();
    reader.Read();
    _ = reader.GetString(0);
});

PrintResults("Point Lookup", sharcLookup, sqliteLookup);
lookupConn.Dispose();

// ═══════════════════════════════════════════════════════════════
//  2. Full Table Scan (read all rows)
// ═══════════════════════════════════════════════════════════════

PrintHeader("2. Full Table Scan (10,000 rows)");

PrintSubHeader("Sharc: CreateReader → while(Read())");
PrintCode("""
    using var db = SharcDatabase.OpenMemory(bytes);
    using var reader = db.CreateReader("users", "id", "name", "age");
    while (reader.Read()) sum += reader.GetInt64(0);
    """);

double sharcScan = Measure(() =>
{
    using var db = SharcDatabase.OpenMemory(dbBytes);
    using var reader = db.CreateReader("users", "id", "name", "age");
    long sum = 0;
    while (reader.Read())
    {
        sum += reader.GetInt64(0);
        _ = reader.GetString(1);
        sum += reader.GetInt64(2);
    }
});

PrintSubHeader("SQLite: SELECT → while(Read())");
PrintCode("""
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id, name, age FROM users";
    using var reader = cmd.ExecuteReader();
    while (reader.Read()) sum += reader.GetInt64(0);
    """);

using var scanConn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
scanConn.Open();

double sqliteScan = Measure(() =>
{
    using var cmd = scanConn.CreateCommand();
    cmd.CommandText = "SELECT id, name, age FROM users";
    using var reader = cmd.ExecuteReader();
    long sum = 0;
    while (reader.Read())
    {
        sum += reader.GetInt64(0);
        _ = reader.GetString(1);
        sum += reader.GetInt32(2);
    }
});

PrintResults("Full Scan", sharcScan, sqliteScan);
scanConn.Dispose();

// ═══════════════════════════════════════════════════════════════
//  3. Schema Introspection
// ═══════════════════════════════════════════════════════════════

PrintHeader("3. Schema Introspection (list tables + columns)");

PrintSubHeader("Sharc: db.Schema.Tables");
PrintCode("""
    using var db = SharcDatabase.OpenMemory(bytes);
    int tables = db.Schema.Tables.Count;
    int cols = db.Schema.Tables.Sum(t => t.Columns.Count);
    """);

double sharcSchema = Measure(() =>
{
    using var db = SharcDatabase.OpenMemory(dbBytes);
    int t = db.Schema.Tables.Count;
    int c = 0;
    foreach (var table in db.Schema.Tables) c += table.Columns.Count;
});

PrintSubHeader("SQLite: PRAGMA table_info");
PrintCode("""
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
    // ... then PRAGMA table_info for each table
    """);

using var schemaConn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
schemaConn.Open();

double sqliteSchema = Measure(() =>
{
    var tables = new List<string>();
    using (var cmd = schemaConn.CreateCommand())
    {
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) tables.Add(reader.GetString(0));
    }
    int c = 0;
    foreach (var table in tables)
    {
        using var cmd = schemaConn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{table}')";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) c++;
    }
});

PrintResults("Schema Read", sharcSchema, sqliteSchema);
schemaConn.Dispose();

// ═══════════════════════════════════════════════════════════════
//  4. Filtered Query (WHERE age > 50)
// ═══════════════════════════════════════════════════════════════

PrintHeader("4. Filtered Query (WHERE age > 50)");

PrintSubHeader("Sharc: FilterStar fluent API");
PrintCode("""
    using var db = SharcDatabase.OpenMemory(bytes);
    var jit = db.Jit("users");
    jit.Where(FilterStar.Column("age").Gt(50));
    using var reader = jit.Query("id", "name");
    while (reader.Read()) count++;
    """);

double sharcFilter = Measure(() =>
{
    using var db = SharcDatabase.OpenMemory(dbBytes);
    var jit = db.Jit("users");
    jit.Where(FilterStar.Column("age").Gt(50));
    using var reader = jit.Query("id", "name");
    int count = 0;
    while (reader.Read()) count++;
});

PrintSubHeader("SQLite: WHERE clause");
PrintCode("""
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id, name FROM users WHERE age > 50";
    using var reader = cmd.ExecuteReader();
    while (reader.Read()) count++;
    """);

using var filterConn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
filterConn.Open();

double sqliteFilter = Measure(() =>
{
    using var cmd = filterConn.CreateCommand();
    cmd.CommandText = "SELECT id, name FROM users WHERE age > 50";
    using var reader = cmd.ExecuteReader();
    int count = 0;
    while (reader.Read()) count++;
});

PrintResults("Filtered Query", sharcFilter, sqliteFilter);
filterConn.Dispose();

// ═══════════════════════════════════════════════════════════════
//  5. PreparedReader (hot-path reuse)
// ═══════════════════════════════════════════════════════════════

PrintHeader("5. Prepared Reader — Hot Path Reuse (100 seeks)");

PrintSubHeader("Sharc: PrepareReader → Execute → Seek (zero alloc after first)");
PrintCode("""
    using var db = SharcDatabase.OpenMemory(bytes);
    using var prepared = db.PrepareReader("users", "name", "age");
    for (int i = 0; i < 100; i++) {
        using var reader = prepared.Execute();
        reader.Seek(i + 1);
        _ = reader.GetString(0);
    }
    """);

using var prepDb = SharcDatabase.OpenMemory(dbBytes);
using var prepared = prepDb.PrepareReader("users", "name", "age");

// Warm up
for (int i = 0; i < WarmupIterations; i++)
{
    using var r = prepared.Execute();
    r.Seek(50);
}

double sharcPrepared = MeasureRaw(() =>
{
    for (int i = 0; i < 100; i++)
    {
        using var r = prepared.Execute();
        r.Seek(i + 1);
        _ = r.GetString(0);
    }
});

PrintSubHeader("SQLite: Prepared command with parameter binding");
PrintCode("""
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT name, age FROM users WHERE id = @id";
    cmd.Parameters.Add("@id", SqliteType.Integer);
    for (int i = 0; i < 100; i++) {
        cmd.Parameters[0].Value = (long)(i + 1);
        using var reader = cmd.ExecuteReader();
        reader.Read();
        _ = reader.GetString(0);
    }
    """);

using var prepConn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
prepConn.Open();
using var prepCmd = prepConn.CreateCommand();
prepCmd.CommandText = "SELECT name, age FROM users WHERE id = @id";
prepCmd.Parameters.Add("@id", SqliteType.Integer);

// Warm up
for (int i = 0; i < WarmupIterations; i++)
{
    prepCmd.Parameters[0].Value = 50L;
    using var r = prepCmd.ExecuteReader();
    r.Read();
}

double sqlitePrepared = MeasureRaw(() =>
{
    for (int i = 0; i < 100; i++)
    {
        prepCmd.Parameters[0].Value = (long)(i + 1);
        using var r = prepCmd.ExecuteReader();
        r.Read();
        _ = r.GetString(0);
    }
});

PrintResults("100 Prepared Seeks", sharcPrepared, sqlitePrepared);
prepConn.Dispose();
prepDb.Dispose();

// ═══════════════════════════════════════════════════════════════
//  6. Write: Insert 100 rows
// ═══════════════════════════════════════════════════════════════

PrintHeader("6. Write: Insert 100 Rows (transactional)");

PrintSubHeader("Sharc: SharcWriter → BeginTransaction → Insert → Commit");
PrintCode("""
    using var writer = SharcWriter.Open(path);
    using var tx = writer.BeginTransaction();
    for (int i = 0; i < 100; i++)
        tx.Insert("users", values...);
    tx.Commit();
    """);

// Create a fresh writable copy for Sharc
var sharcWritePath = Path.Combine(Path.GetTempPath(), $"sharc_write_{Guid.NewGuid():N}.db");
File.Copy(dbPath, sharcWritePath);
int sharcWriteCounter = RowCount + 1;

double sharcWrite = MeasureRaw(() =>
{
    using var writer = SharcWriter.Open(sharcWritePath);
    using var tx = writer.BeginTransaction();
    for (int i = 0; i < 100; i++)
    {
        int id = sharcWriteCounter++;
        var msg = Encoding.UTF8.GetBytes($"NewUser_{id}");
        var email = Encoding.UTF8.GetBytes($"new{id}@example.com");
        tx.Insert("users",
            ColumnValue.FromInt64(4, id),
            ColumnValue.Text(13 + 2 * msg.Length, msg),
            ColumnValue.Text(13 + 2 * email.Length, email),
            ColumnValue.FromInt64(2, 25),
            ColumnValue.FromDouble(50.0),
            ColumnValue.Null());
    }
    tx.Commit();
});

PrintSubHeader("SQLite: SqliteConnection → BeginTransaction → INSERT → Commit");
PrintCode("""
    using var conn = new SqliteConnection(connStr);
    conn.Open();
    using var tx = conn.BeginTransaction();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "INSERT INTO users VALUES (...)";
    for (int i = 0; i < 100; i++) cmd.ExecuteNonQuery();
    tx.Commit();
    """);

var sqliteWritePath = Path.Combine(Path.GetTempPath(), $"sqlite_write_{Guid.NewGuid():N}.db");
File.Copy(dbPath, sqliteWritePath);
int sqliteWriteCounter = RowCount + 1;

double sqliteWrite = MeasureRaw(() =>
{
    using var conn = new SqliteConnection($"Data Source={sqliteWritePath}");
    conn.Open();
    using var tx = conn.BeginTransaction();
    using var cmd = conn.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = "INSERT INTO users VALUES ($id, $name, $email, $age, $balance, $bio)";
    var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
    var pName = cmd.Parameters.Add("$name", SqliteType.Text);
    var pEmail = cmd.Parameters.Add("$email", SqliteType.Text);
    var pAge = cmd.Parameters.Add("$age", SqliteType.Integer);
    var pBalance = cmd.Parameters.Add("$balance", SqliteType.Real);
    var pBio = cmd.Parameters.Add("$bio", SqliteType.Text);
    for (int i = 0; i < 100; i++)
    {
        int id = sqliteWriteCounter++;
        pId.Value = id;
        pName.Value = $"NewUser_{id}";
        pEmail.Value = $"new{id}@example.com";
        pAge.Value = 25;
        pBalance.Value = 50.0;
        pBio.Value = DBNull.Value;
        cmd.ExecuteNonQuery();
    }
    tx.Commit();
});

PrintResults("100 Inserts", sharcWrite, sqliteWrite);

// ═══════════════════════════════════════════════════════════════
//  Cleanup
// ═══════════════════════════════════════════════════════════════

Console.WriteLine();
Console.WriteLine(new string('═', 70));
Console.WriteLine("Note: Times are median of 100 iterations (excluding warmup).");
Console.WriteLine("Sharc operates on in-memory bytes; SQLite uses file-backed connection.");

try { File.Delete(dbPath); } catch { }
try { File.Delete(sharcWritePath); } catch { }
try { File.Delete(sqliteWritePath); } catch { }
SqliteConnection.ClearAllPools();

// ═══════════════════════════════════════════════════════════════
//  Helpers
// ═══════════════════════════════════════════════════════════════

double Measure(Action action)
{
    // Warmup
    for (int i = 0; i < WarmupIterations; i++) action();

    return MeasureRaw(action);
}

double MeasureRaw(Action action)
{
    var times = new double[TimedIterations];
    for (int i = 0; i < TimedIterations; i++)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        times[i] = sw.Elapsed.TotalMicroseconds;
    }

    Array.Sort(times);
    return times[TimedIterations / 2]; // median
}

void PrintHeader(string title)
{
    Console.WriteLine();
    Console.WriteLine($"  {title}");
    Console.WriteLine($"  {new string('─', title.Length)}");
}

void PrintSubHeader(string title) => Console.WriteLine($"    {title}");

void PrintCode(string code)
{
    foreach (var line in code.Split('\n'))
        Console.WriteLine($"      {line.TrimStart()}");
    Console.WriteLine();
}

void PrintResults(string label, double sharcUs, double sqliteUs)
{
    double ratio = sqliteUs / sharcUs;
    string faster = ratio >= 1.0 ? "Sharc" : "SQLite";
    double factor = ratio >= 1.0 ? ratio : 1.0 / ratio;

    Console.WriteLine($"    ┌──────────────────────────────────────────────┐");
    Console.WriteLine($"    │  {label,-20} {"Sharc",-12} {"SQLite",-12} │");
    Console.WriteLine($"    │  {"Median (us)",-20} {sharcUs,-12:F1} {sqliteUs,-12:F1} │");
    Console.WriteLine($"    │  {"Winner",-20} {faster + $" {factor:F1}x",-24} │");
    Console.WriteLine($"    └──────────────────────────────────────────────┘");
    Console.WriteLine();
}
