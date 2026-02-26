using Microsoft.Data.Sqlite;
using Sharc;
using Sharc.Core;
using Sharc.Core.Query;

var dbPath = Path.Combine(Path.GetTempPath(), "sharc_sample_guid_fix128.db");
if (File.Exists(dbPath)) File.Delete(dbPath);

using (var conn = new SqliteConnection($"Data Source={dbPath}"))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE accounts (
            id INTEGER PRIMARY KEY,
            account_guid UUID NOT NULL,
            balance FIX128 NOT NULL
        );

        CREATE TABLE positions (
            id INTEGER PRIMARY KEY,
            owner__hi INTEGER NOT NULL,
            owner__lo INTEGER NOT NULL,
            notional__dhi INTEGER NOT NULL,
            notional__dlo INTEGER NOT NULL
        );
        CREATE INDEX idx_positions_owner ON positions(owner__hi, owner__lo);
        CREATE INDEX idx_positions_notional ON positions(notional__dhi, notional__dlo);
        """;
    cmd.ExecuteNonQuery();
}

using var db = SharcDatabase.Open(dbPath, new SharcOpenOptions { Writable = true });
using var writer = SharcWriter.From(db);

var accountA = Guid.NewGuid();
var accountB = Guid.NewGuid();

writer.Insert("accounts",
    ColumnValue.FromInt64(1, 1),
    ColumnValue.FromGuid(accountA),
    ColumnValue.FromDecimal(12345678901234567890.12345678m));

writer.Insert("accounts",
    ColumnValue.FromInt64(1, 2),
    ColumnValue.FromGuid(accountB),
    ColumnValue.FromDecimal(250.75m));

writer.Insert("positions",
    ColumnValue.FromInt64(1, 1),
    ColumnValue.FromGuid(accountA),
    ColumnValue.FromDecimal(999.50m));

writer.Insert("positions",
    ColumnValue.FromInt64(1, 2),
    ColumnValue.FromGuid(accountB),
    ColumnValue.FromDecimal(2500.75m));

Console.WriteLine("GUID/FIX128 typed column sample");
Console.WriteLine("--------------------------------");

Console.WriteLine("Accounts:");
using (var accountReader = db.CreateReader("accounts"))
{
    while (accountReader.Read())
    {
        long id = accountReader.GetInt64(0);
        Guid guid = accountReader.GetGuid(1);
        decimal balance = accountReader.GetDecimal(2);
        Console.WriteLine($"  [{id}] {guid} => {balance}");
    }
}

Console.WriteLine();
Console.WriteLine("Positions with notional > 1000:");
using (var highNotional = db.CreateReader("positions", FilterStar.Column("notional").Gt(1000m)))
{
    while (highNotional.Read())
    {
        long id = highNotional.GetInt64(0);
        Guid owner = highNotional.GetGuid(1);
        decimal notional = highNotional.GetDecimal(2);
        Console.WriteLine($"  [{id}] owner={owner}, notional={notional}");
    }
}

Console.WriteLine();
using (var strictReader = db.CreateReader("accounts"))
{
    strictReader.Read();
    try
    {
        _ = strictReader.GetDecimal(1); // account_guid is GUID/UUID, not decimal
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine("Strict accessor check:");
        Console.WriteLine($"  {ex.Message}");
    }
}
