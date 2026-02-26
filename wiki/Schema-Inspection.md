# Schema Inspection

## SharcSchema

Access the database schema through `db.Schema`:

```csharp
using var db = SharcDatabase.Open("mydata.db");

foreach (var table in db.Schema.Tables)
{
    Console.WriteLine($"Table: {table.Name} ({table.Columns.Count} columns)");
    foreach (var col in table.Columns)
        Console.WriteLine($"  {col.Name}: {col.DeclaredType}");
}
```

### Schema Properties

```csharp
db.Schema.Tables    // IReadOnlyList<TableInfo>
db.Schema.Indexes   // IReadOnlyList<IndexInfo>
db.Schema.Views     // IReadOnlyList<ViewInfo>
```

### Lookup by Name

```csharp
TableInfo users = db.Schema.GetTable("users");     // Case-insensitive, throws if not found
ViewInfo? view = db.Schema.GetView("active_users"); // Returns null if not found
```

## TableInfo

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Table name |
| `RootPage` | `int` | B-tree root page number |
| `Sql` | `string` | Original `CREATE TABLE` statement |
| `Columns` | `IReadOnlyList<ColumnInfo>` | Column definitions |
| `Indexes` | `IReadOnlyList<IndexInfo>` | Indexes on this table |
| `IsWithoutRowId` | `bool` | WITHOUT ROWID table |
| `HasMergedColumns` | `bool` | Has merged 128-bit columns (`__hi`/`__lo` and/or `__dhi`/`__dlo`) |

### Column Ordinal Lookup

```csharp
int ordinal = table.GetColumnOrdinal("email");  // Case-insensitive
```

## ColumnInfo

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Column name |
| `DeclaredType` | `string` | Type affinity (`"INTEGER"`, `"TEXT"`, etc.) |
| `Ordinal` | `int` | Zero-based position |
| `IsPrimaryKey` | `bool` | Part of PRIMARY KEY |
| `IsNotNull` | `bool` | Has NOT NULL constraint |
| `IsGuidColumn` | `bool` | Declared `GUID` or `UUID` |
| `IsDecimalColumn` | `bool` | Declared `FIX128`, `DECIMAL128`, or `DECIMAL` |
| `IsMergedFix128Column` | `bool` | Any merged 128-bit logical column |
| `IsMergedGuidColumn` | `bool` | Merged GUID from `__hi`/`__lo` pair |
| `IsMergedDecimalColumn` | `bool` | Merged decimal from `__dhi`/`__dlo` pair |

## IndexInfo

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Index name |
| `TableName` | `string` | Parent table |
| `RootPage` | `int` | B-tree root page number |
| `Sql` | `string` | `CREATE INDEX` statement |
| `IsUnique` | `bool` | Unique constraint |
| `Columns` | `IReadOnlyList<IndexColumnInfo>` | Indexed columns |

## ViewInfo

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | View name |
| `Sql` | `string` | `CREATE VIEW` statement |
| `SourceTables` | `IReadOnlyList<string>` | Referenced tables |
| `Columns` | `IReadOnlyList<ViewColumnInfo>` | Projected columns |
| `IsSelectAll` | `bool` | Uses `SELECT *` |
| `HasJoin` | `bool` | Contains JOIN |
| `HasFilter` | `bool` | Contains WHERE |
| `IsSharcExecutable` | `bool` | Can be auto-promoted to executable view |

## Example: Schema Discovery

```csharp
using var db = SharcDatabase.Open("mydata.db");
var schema = db.Schema;

Console.WriteLine($"Tables: {schema.Tables.Count}");
Console.WriteLine($"Indexes: {schema.Indexes.Count}");
Console.WriteLine($"Views: {schema.Views.Count}");

foreach (var table in schema.Tables)
{
    Console.WriteLine($"\n{table.Name}:");
    foreach (var col in table.Columns)
    {
        string pk = col.IsPrimaryKey ? " [PK]" : "";
        string nn = col.IsNotNull ? " NOT NULL" : "";
        Console.WriteLine($"  {col.Name} {col.DeclaredType}{pk}{nn}");
    }

    foreach (var idx in table.Indexes)
    {
        string unique = idx.IsUnique ? "UNIQUE " : "";
        Console.WriteLine($"  INDEX {unique}{idx.Name} ({string.Join(", ", idx.Columns.Select(c => c.Name))})");
    }
}
```
