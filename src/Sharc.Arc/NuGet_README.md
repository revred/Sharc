# Sharc.Arc

**Distributed arc file management for the Sharc database engine.**

Cross-arc reference resolution, multi-arc fusion, data ingestion, and validation with zero native dependencies.

## Features

- **FusedArcContext**: Mount N `.arc` files and query across all fragments with source-arc provenance.
- **ArcResolver**: Pluggable URI resolution (`arc://local/path`, `arc://https/url`) with security validation.
- **CsvArcImporter**: RFC 4180-compliant CSV ingestion with automatic schema inference and type detection.
- **ArcDiffer**: Structural diff between two arcs — schema, ledger, and data comparison.
- **ArcValidator**: Four-layer validation: format, chain integrity, trust anchors, limits. Never throws.
- **Cloud Locators**: HTTP/HTTPS arc loading with Dropbox, Google Drive, and S3 presigned URL support.

## Quick Start

```csharp
using Sharc.Arc;

// Fuse multiple arc files into a unified view
var fused = new FusedArcContext();
fused.Mount(ArcHandle.OpenLocal("conversations.arc"), "conversations");
fused.Mount(ArcHandle.OpenLocal("codebase.arc"), "codebase");

// Query across all fragments with provenance
foreach (var row in fused.Query("commits", maxRows: 100))
    Console.WriteLine($"{row.SourceAlias}: {row.Values[0]}");

// Import CSV data into a new .arc file
var options = new CsvImportOptions { TableName = "patients" };
ArcHandle arc = CsvArcImporter.ImportFile("data.csv", options);
```

[Full Documentation](https://github.com/revred/Sharc)
