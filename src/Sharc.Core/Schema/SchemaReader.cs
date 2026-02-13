/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message — or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/


namespace Sharc.Core.Schema;

/// <summary>
/// Reads the sqlite_schema table (page 1) and builds a <see cref="SharcSchema"/>.
/// </summary>
internal sealed class SchemaReader
{
    private readonly IBTreeReader _bTreeReader;
    private readonly IRecordDecoder _recordDecoder;
    private readonly ColumnValue[] _columnBuffer = new ColumnValue[5];

    public SchemaReader(IBTreeReader bTreeReader, IRecordDecoder recordDecoder)
    {
        _bTreeReader = bTreeReader;
        _recordDecoder = recordDecoder;
    }

    /// <summary>
    /// Reads the sqlite_schema from page 1 and builds a full schema.
    /// </summary>
    /// <returns>The parsed database schema.</returns>
    public SharcSchema ReadSchema()
    {
        var tables = new List<TableInfo>();
        var indexes = new List<IndexInfo>();
        var views = new List<ViewInfo>();

        // sqlite_schema is always rooted at page 1
        using var cursor = _bTreeReader.CreateCursor(1);

        while (cursor.MoveNext())
        {
            _recordDecoder.DecodeRecord(cursor.Payload, _columnBuffer);
            
            // sqlite_schema columns: type(0), name(1), tbl_name(2), rootpage(3), sql(4)
            if (_columnBuffer[0].IsNull) continue;
            
            string type = _columnBuffer[0].AsString();
            string name = _columnBuffer[1].IsNull ? "" : _columnBuffer[1].AsString();
            string tblName = _columnBuffer[2].IsNull ? "" : _columnBuffer[2].AsString();
            int rootPage = _columnBuffer[3].IsNull ? 0 : (int)_columnBuffer[3].AsInt64();
            string? sql = _columnBuffer[4].IsNull ? null : _columnBuffer[4].AsString();

            switch (type)
            {
                case "table":
                    if (sql != null)
                    {
                        var columnInfos = CreateTableParser.ParseColumns(sql);
                        tables.Add(new TableInfo
                        {
                            Name = name,
                            RootPage = rootPage,
                            Sql = sql,
                            Columns = columnInfos,
                            IsWithoutRowId = sql.Contains("WITHOUT ROWID",
                                StringComparison.OrdinalIgnoreCase)
                        });
                    }
                    break;

                case "index":
                    var indexColumns = sql != null
                        ? CreateIndexParser.ParseColumns(sql)
                        : (IReadOnlyList<IndexColumnInfo>)[];
                    indexes.Add(new IndexInfo
                    {
                        Name = name,
                        TableName = tblName,
                        RootPage = rootPage,
                        Sql = sql ?? "",
                        IsUnique = sql != null && sql.Contains("UNIQUE",
                            StringComparison.OrdinalIgnoreCase),
                        Columns = indexColumns
                    });
                    break;

                case "view":
                    views.Add(new ViewInfo
                    {
                        Name = name,
                        Sql = sql ?? ""
                    });
                    break;
            }
        }

        return new SharcSchema
        {
            Tables = tables,
            Indexes = indexes,
            Views = views
        };
    }
}
