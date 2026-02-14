// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.


namespace Sharc.Arena.Wasm.Models;

/// <summary>
/// The current state of the benchmark runner.
/// </summary>
public enum BenchmarkState
{
    Idle,
    Running,
    Done
}