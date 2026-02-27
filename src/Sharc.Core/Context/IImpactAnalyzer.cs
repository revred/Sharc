#pragma warning disable CS1591

// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Core.Context;

/// <summary>
/// Calculates downstream impact for a path/symbol change.
/// </summary>
public interface IImpactAnalyzer
{
    ImpactReport CalculateImpact(string targetPath, ImpactOptions? options = null);
}

#pragma warning restore CS1591

