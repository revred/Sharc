// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


using Sharc.Core;
using Sharc.Core.Records;
using Sharc.Core.Schema;

namespace Sharc.Graph.Store;

/// <summary>
/// Loads the database schema using Sharc.Core primitives.
/// </summary>
internal sealed class SchemaLoader
{
    private readonly IBTreeReader _reader;

    public SchemaLoader(IBTreeReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <summary>
    /// Reads the current schema from the sqlite_schema table.
    /// </summary>
    public SharcSchema Load()
    {
        // RecordDecoder is internal in Sharc.Core but exposed via InternalsVisibleTo
        var decoder = new RecordDecoder();
        
        // SchemaReader is internal in Sharc.Core
        var schemaReader = new SchemaReader(_reader, decoder);
        
        return schemaReader.ReadSchema();
    }
}