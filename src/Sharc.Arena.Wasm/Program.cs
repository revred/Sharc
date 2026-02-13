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

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Sharc.Arena.Wasm;
using Sharc.Arena.Wasm.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Tier 1: Native .NET engines (Stopwatch + GC alloc tracking)
builder.Services.AddSingleton<SharcEngine>();
builder.Services.AddSingleton<SqliteEngine>();

// Tier 2: Browser API engine (JS interop + performance.now())
builder.Services.AddSingleton<IndexedDbEngine>();

// Reference engine (static base data for SurrealDB)
builder.Services.AddSingleton<ReferenceEngine>();

// Orchestrator: routes to live engines, falls back to reference for SurrealDB
builder.Services.AddSingleton<BenchmarkRunner>();
builder.Services.AddSingleton<IBenchmarkEngine>(sp => sp.GetRequiredService<BenchmarkRunner>());

await builder.Build().RunAsync();
