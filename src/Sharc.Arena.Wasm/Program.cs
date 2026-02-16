using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Sharc.Arena.Wasm.Layout;
using Sharc.Arena.Wasm.Services;

namespace Sharc.Arena.Wasm;

public partial class Program
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MainLayout))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Pages.Arena))]
    public static async Task Main(string[] args)
    {
        // Explicit SQLitePCLRaw init — required in WASM before any SqliteConnection use.
        // Without this, the static constructor throws TypeInitializationException.
        SQLitePCL.Batteries_V2.Init();

        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        ConfigureServices(builder);
        await builder.Build().RunAsync();
    }

    static void ConfigureServices(WebAssemblyHostBuilder builder)
    {
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

        // Tier 1: Native .NET engines (Stopwatch + GC alloc tracking)
        builder.Services.AddSingleton<SharcEngine>();
        builder.Services.AddSingleton<SqliteEngine>();

        // Tier 2: Browser API engine (JS interop + performance.now())
        builder.Services.AddSingleton<IndexedDbEngine>();

        // Reference engine (static baseline data)
        builder.Services.AddSingleton<ReferenceEngine>();

        // Orchestrator: routes to live engines, falls back to reference
        builder.Services.AddSingleton<BenchmarkRunner>();
        builder.Services.AddSingleton<IBenchmarkEngine>(sp => sp.GetRequiredService<BenchmarkRunner>());

        // Data loader: fetches benchmark JSON from wwwroot/data/
        builder.Services.AddScoped<BenchmarkDataLoader>();

        // Query pipeline: reference data for 13-query comparison
        builder.Services.AddScoped<QueryPipelineEngine>();
    }
}