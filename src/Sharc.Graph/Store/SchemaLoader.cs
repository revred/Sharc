/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

using Sharc.Core;
using Sharc.Core.Records;
using Sharc.Core.Schema;
using Sharc.Schema;

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
