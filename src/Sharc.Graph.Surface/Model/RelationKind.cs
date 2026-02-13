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

  Licensed under the MIT License — free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

namespace Sharc.Graph.Model;

/// <summary>
/// Core ontology for graph edge relationships.
/// Represents 'kind' in _relations edge table.
/// </summary>
public enum RelationKind
{
    // --- Structural ---
    /// <summary>Is a container for.</summary>
    Contains = 10,
    /// <summary>Defines a symbol.</summary>
    Defines = 11,
    
    // --- Dependency ---
    /// <summary>Imports a module or namespace.</summary>
    Imports = 12,
    /// <summary>Inherits from a class.</summary>
    Inherits = 13,
    /// <summary>Implements an interface.</summary>
    Implements = 14,
    
    // --- Flow ---
    /// <summary>Calls a method.</summary>
    Calls = 15,
    /// <summary>Instantiates a class.</summary>
    Instantiates = 16,
    /// <summary>Reads a variable.</summary>
    Reads = 17,
    /// <summary>Writes to a variable.</summary>
    Writes = 18,
    
    // --- Contextual ---
    /// <summary>Addresses a goal or issue.</summary>
    Addresses = 19,
    /// <summary>Explains a concept or code.</summary>
    Explains = 20,
    /// <summary>Is mentioned in documentation.</summary>
    MentionedIn = 21,
    
    // --- Session ---
    /// <summary>Refers to an artifact.</summary>
    RefersTo = 30, // User request -> File
    /// <summary>Follows another step.</summary>
    Follows = 31   // Step -> Step
}
