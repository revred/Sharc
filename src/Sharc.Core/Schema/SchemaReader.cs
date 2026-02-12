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
            var columns = _recordDecoder.DecodeRecord(cursor.Payload);
            if (columns.Length < 5) continue;

            // sqlite_schema columns: type(0), name(1), tbl_name(2), rootpage(3), sql(4)
            string type = columns[0].IsNull ? "" : columns[0].AsString();
            string name = columns[1].IsNull ? "" : columns[1].AsString();
            string tblName = columns[2].IsNull ? "" : columns[2].AsString();
            int rootPage = columns[3].IsNull ? 0 : (int)columns[3].AsInt64();
            string? sql = columns[4].IsNull ? null : columns[4].AsString();

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
