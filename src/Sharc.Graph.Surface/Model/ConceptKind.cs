// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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
    
    // --- Git ---
    /// <summary>A git commit.</summary>
    GitCommit = 40,
    /// <summary>A git author.</summary>
    GitAuthor = 41,
    /// <summary>A git branch.</summary>
    GitBranch = 42,
    /// <summary>A git tag.</summary>
    GitTag = 43,

    // --- Annotations ---
    /// <summary>A review annotation.</summary>
    Annotation = 50,
    /// <summary>A decision record.</summary>
    Decision = 51,

    // --- General ---
    /// <summary>A prompt or instruction.</summary>
    Prompt = 100,
    /// <summary>Documentation or comment.</summary>
    Documentation = 101,
}