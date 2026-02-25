// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


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
    Follows = 31,  // Step -> Step

    // --- Git ---
    /// <summary>Authored a commit or artifact.</summary>
    Authored = 40,
    /// <summary>Modified a file or artifact.</summary>
    Modified = 41,
    /// <summary>Is the parent of (commit lineage).</summary>
    ParentOf = 42,
    /// <summary>Co-modified in the same commit.</summary>
    CoModified = 43,
    /// <summary>Blamed for a line or region.</summary>
    BlamedFor = 44,

    // --- Annotation ---
    /// <summary>Produced by an agent or process.</summary>
    ProducedBy = 45,
    /// <summary>Annotates a specific location.</summary>
    AnnotatesAt = 46,
    /// <summary>Reverts to a previous state.</summary>
    RevertsTo = 47,
    /// <summary>Branches from a commit or node.</summary>
    BranchesFrom = 48,
    /// <summary>Owned by an agent or author.</summary>
    OwnedBy = 49,
    /// <summary>Is a snapshot of an artifact.</summary>
    SnapshotOf = 50,
    /// <summary>Contains an annotation.</summary>
    ContainsAnnotation = 51
}