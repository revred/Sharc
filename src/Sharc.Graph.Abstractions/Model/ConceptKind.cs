/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message Ã¢â‚¬â€ or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License Ã¢â‚¬â€ free for personal and commercial use.                         |
--------------------------------------------------------------------------------------------------*/

namespace Sharc.Graph.Model;

/// <summary>
/// Core ontology for code context concepts.
/// Represents the discriminator 'kind' in _concepts table.
/// </summary>
public enum ConceptKind
{
    // --- Artifacts ---
    /// <summary>A source file.</summary>
    File = 1,
    /// <summary>A directory.</summary>
    Directory = 2,
    /// <summary>A project or solution.</summary>
    Project = 3,

    // --- Symbols ---
    /// <summary>A class definition.</summary>
    Class = 4,
    /// <summary>An interface definition.</summary>
    Interface = 5,
    /// <summary>A method or function.</summary>
    Method = 6,
    /// <summary>A property.</summary>
    Property = 7,
    /// <summary>A field or member variable.</summary>
    Field = 8,
    /// <summary>A local variable or parameter.</summary>
    Variable = 9,
    
    // --- Session ---
    /// <summary>A user request.</summary>
    UserRequest = 20,
    /// <summary>An assistant response.</summary>
    AssistantResponse = 21,
    /// <summary>A plan of action.</summary>
    Plan = 22,
    /// <summary>A goal or objective.</summary>
    Goal = 23,
    
    // --- Knowledge ---
    /// <summary>A software design pattern.</summary>
    Pattern = 30,
    /// <summary>A rule or guideline.</summary>
    Rule = 31,
    /// <summary>A constraint.</summary>
    Constraint = 32,
    
    // --- General ---
    /// <summary>A prompt or instruction.</summary>
    Prompt = 100,
    /// <summary>Documentation or comment.</summary>
    Documentation = 101,
}
