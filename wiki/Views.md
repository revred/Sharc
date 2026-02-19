# Views

Sharc supports programmatic views — named, reusable queries with optional projection and filtering.

## Creating a View

```csharp
using Sharc;
using Sharc.Views;

using var db = SharcDatabase.Open("mydata.db");

// Build a view with projection and filter
var adultsView = ViewBuilder
    .FromTable("users")
    .WithProjection("id", "name", "email")
    .WithFilter(row => row.GetInt64(0) > 18)
    .Build("adults");

db.RegisterView(adultsView);
```

## Reading from a View

```csharp
using var cursor = db.OpenView("adults");

while (cursor.MoveNext())
{
    long id = cursor.GetInt64(0);
    string name = cursor.GetString(1);
    string email = cursor.GetString(2);
}
```

## View Management

```csharp
// List all registered views
IReadOnlyCollection<string> viewNames = db.ListRegisteredViews();

// Unregister a view
bool removed = db.UnregisterView("adults");
```

## IViewCursor

`IViewCursor` extends `IRowAccessor` with cursor semantics:

| Method | Description |
|--------|-------------|
| `MoveNext()` | Advance to the next matching row |
| `RowsRead` | Total rows read so far |
| `GetInt64(ordinal)` | Typed accessors (same as `SharcDataReader`) |
| `GetString(ordinal)` | |
| `GetDouble(ordinal)` | |
| `IsNull(ordinal)` | |
| `FieldCount` | Number of projected columns |
| `GetColumnName(ordinal)` | Column name by position |

## SQL-Defined Views

SQLite `CREATE VIEW` statements are parsed from the schema and available via `db.Schema.Views`. Views marked as `IsSharcExecutable` can be auto-promoted to executable programmatic views.

```csharp
foreach (var view in db.Schema.Views)
{
    Console.WriteLine($"{view.Name}: {view.SourceTables.Count} source tables");
    if (view.IsSharcExecutable)
        Console.WriteLine("  -> Can be auto-promoted");
}
```
