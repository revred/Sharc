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
    /// <summary>Authored a commit.</summary>
    Authored = 40,
    /// <summary>Modified a file.</summary>
    Modified = 41,
    /// <summary>Parent commit relationship.</summary>
    ParentOf = 42,
    /// <summary>Branch points to a commit.</summary>
    PointsTo = 43,

    // --- Annotations ---
    /// <summary>Annotated a file or concept.</summary>
    Annotated = 50,
    /// <summary>Decided on a concept or approach.</summary>
    Decided = 51
}