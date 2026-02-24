// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Sharc.Archive.Sync;

namespace Sharc.Archive;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "capture" => RunCapture(args[1..]),
                "annotate" => RunAnnotate(args[1..]),
                "review" => RunReview(args[1..]),
                "checkpoint" => RunCheckpoint(args[1..]),
                "sync" => RunSync(args[1..]),
                _ => Error($"Unknown command: {args[0]}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    // ── capture ──────────────────────────────────────────────────────

    private static int RunCapture(string[] args)
    {
        string? inputPath = null, outputPath = null, agentId = null, source = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input" when i + 1 < args.Length: inputPath = args[++i]; break;
                case "--output" when i + 1 < args.Length: outputPath = args[++i]; break;
                case "--agent" when i + 1 < args.Length: agentId = args[++i]; break;
                case "--source" when i + 1 < args.Length: source = args[++i]; break;
                case "--help": PrintCaptureHelp(); return 0;
            }
        }

        if (inputPath == null || outputPath == null)
        {
            Console.Error.WriteLine("Error: --input and --output are required.");
            PrintCaptureHelp();
            return 1;
        }

        var (conversation, turns) = InputParser.ParseJsonLines(inputPath, agentId, source);
        using var db = ArchiveSchemaBuilder.CreateSchema(outputPath);
        using var writer = new ArchiveWriter(db);

        writer.WriteConversation(conversation);
        writer.WriteTurns(turns);

        Console.WriteLine($"Captured: {turns.Count} turns → {outputPath}");
        return 0;
    }

    // ── annotate ─────────────────────────────────────────────────────

    private static int RunAnnotate(string[] args)
    {
        string? archivePath = null, target = null, annotationType = null;
        string? verdict = null, content = null, annotatorId = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--archive" when i + 1 < args.Length: archivePath = args[++i]; break;
                case "--target" when i + 1 < args.Length: target = args[++i]; break;
                case "--type" when i + 1 < args.Length: annotationType = args[++i]; break;
                case "--verdict" when i + 1 < args.Length: verdict = args[++i]; break;
                case "--content" when i + 1 < args.Length: content = args[++i]; break;
                case "--annotator" when i + 1 < args.Length: annotatorId = args[++i]; break;
                case "--help": PrintAnnotateHelp(); return 0;
            }
        }

        if (archivePath == null || target == null || annotationType == null || annotatorId == null)
        {
            Console.Error.WriteLine("Error: --archive, --target, --type, and --annotator are required.");
            PrintAnnotateHelp();
            return 1;
        }

        // Parse target: "turn:42" → targetType="turn", targetId=42
        var parts = target.Split(':');
        if (parts.Length != 2 || !long.TryParse(parts[1], out long targetId))
        {
            Console.Error.WriteLine("Error: --target must be 'type:id' (e.g., 'turn:42').");
            return 1;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var db = ArchiveSchemaBuilder.CreateSchema(archivePath);
        using var writer = new ArchiveWriter(db);
        var record = new AnnotationRecord(parts[0], targetId, annotationType, verdict, content, annotatorId, now, null);
        long rowId = writer.WriteAnnotation(record);

        Console.WriteLine($"Annotation {rowId} created: {parts[0]}:{targetId} → {annotationType} ({verdict ?? "no verdict"})");
        return 0;
    }

    // ── review ───────────────────────────────────────────────────────

    internal static int RunReview(string[] args)
    {
        string? archivePath = null, conversationId = null, format = "table";
        string section = "all";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--archive" when i + 1 < args.Length: archivePath = args[++i]; break;
                case "--conversation" when i + 1 < args.Length: conversationId = args[++i]; break;
                case "--format" when i + 1 < args.Length: format = args[++i]; break;
                case "--section" when i + 1 < args.Length: section = args[++i]; break;
                case "--help": PrintReviewHelp(); return 0;
            }
        }

        if (archivePath == null)
        {
            Console.Error.WriteLine("Error: --archive is required.");
            PrintReviewHelp();
            return 1;
        }

        using var db = SharcDatabase.Open(archivePath, new SharcOpenOptions { Writable = false });
        var reader = new ArchiveReader(db);

        if (section == "all" || section == "conversations")
        {
            var convs = reader.ReadConversations(conversationId);
            Console.Write(CliFormatter.FormatConversations(convs, format));
        }

        if (section == "all" || section == "turns")
        {
            var turns = reader.ReadTurns(conversationId);
            Console.Write(CliFormatter.FormatTurns(turns, format));
        }

        if (section == "all" || section == "annotations")
        {
            var anns = reader.ReadAnnotations();
            Console.Write(CliFormatter.FormatAnnotations(anns, format));
        }

        if (section == "all" || section == "decisions")
        {
            var decs = reader.ReadDecisions(conversationId);
            Console.Write(CliFormatter.FormatDecisions(decs, format));
        }

        if (section == "all" || section == "checkpoints")
        {
            var cps = reader.ReadCheckpoints();
            Console.Write(CliFormatter.FormatCheckpoints(cps, format));
        }

        return 0;
    }

    // ── checkpoint ───────────────────────────────────────────────────

    private static int RunCheckpoint(string[] args)
    {
        string? archivePath = null, label = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--archive" when i + 1 < args.Length: archivePath = args[++i]; break;
                case "--label" when i + 1 < args.Length: label = args[++i]; break;
                case "--help": PrintCheckpointHelp(); return 0;
            }
        }

        if (archivePath == null || label == null)
        {
            Console.Error.WriteLine("Error: --archive and --label are required.");
            PrintCheckpointHelp();
            return 1;
        }

        using var db = ArchiveSchemaBuilder.CreateSchema(archivePath);
        var reader = new ArchiveReader(db);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        int convCount = reader.ReadConversations().Count;
        int turnCount = reader.ReadTurns().Count;
        int annCount = reader.ReadAnnotations().Count;

        var cpId = $"cp-{Guid.NewGuid().ToString("N")[..8]}";
        using var writer = new ArchiveWriter(db);
        var cp = new CheckpointRecord(cpId, label, now, convCount, turnCount, annCount, 0, null);
        writer.WriteCheckpoint(cp);

        Console.WriteLine($"Checkpoint '{label}' ({cpId}): {convCount} convs, {turnCount} turns, {annCount} annotations");
        return 0;
    }

    // ── sync ─────────────────────────────────────────────────────────

    private static int RunSync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintSyncHelp();
            return 1;
        }

        return args[0] switch
        {
            "export" => RunSyncExport(args[1..]),
            "import" => RunSyncImport(args[1..]),
            "status" => RunSyncStatus(args[1..]),
            "--help" => SyncHelp(),
            _ => Error($"Unknown sync subcommand: {args[0]}")
        };
    }

    private static int RunSyncExport(string[] args)
    {
        string? archivePath = null, outputPath = null;
        long fromSequence = 1;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--archive" when i + 1 < args.Length: archivePath = args[++i]; break;
                case "--from-sequence" when i + 1 < args.Length: fromSequence = long.Parse(args[++i]); break;
                case "--output" when i + 1 < args.Length: outputPath = args[++i]; break;
            }
        }

        if (archivePath == null || outputPath == null)
        {
            Console.Error.WriteLine("Error: --archive and --output are required.");
            return 1;
        }

        using var db = SharcDatabase.Open(archivePath, new SharcOpenOptions { Writable = false });
        var result = FragmentSyncProtocol.Export(db, fromSequence);
        File.WriteAllBytes(outputPath, result.Payload);

        Console.WriteLine($"Exported {result.DeltaCount} deltas from sequence {fromSequence} → {outputPath}");
        return 0;
    }

    private static int RunSyncImport(string[] args)
    {
        string? archivePath = null, inputPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--archive" when i + 1 < args.Length: archivePath = args[++i]; break;
                case "--input" when i + 1 < args.Length: inputPath = args[++i]; break;
            }
        }

        if (archivePath == null || inputPath == null)
        {
            Console.Error.WriteLine("Error: --archive and --input are required.");
            return 1;
        }

        using var db = ArchiveSchemaBuilder.CreateSchema(archivePath);
        var payload = File.ReadAllBytes(inputPath);
        var result = FragmentSyncProtocol.Import(db, payload);

        if (result.Success)
            Console.WriteLine($"Imported {result.ImportedCount} deltas. Ledger sequence: {result.NewLedgerSequence}");
        else
            Console.Error.WriteLine($"Import failed: {result.ErrorMessage}");

        return result.Success ? 0 : 1;
    }

    private static int RunSyncStatus(string[] args)
    {
        string? archivePath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--archive" when i + 1 < args.Length: archivePath = args[++i]; break;
            }
        }

        if (archivePath == null)
        {
            Console.Error.WriteLine("Error: --archive is required.");
            return 1;
        }

        using var db = SharcDatabase.Open(archivePath, new SharcOpenOptions { Writable = false });
        using var mw = new ManifestWriter(db);
        var entries = mw.ReadAll();

        if (entries.Count == 0)
        {
            Console.WriteLine("No sync fragments recorded.");
            return 0;
        }

        Console.WriteLine("| Fragment | Version | Entries | Ledger Seq | Last Sync |");
        Console.WriteLine("|----------|---------|---------|------------|-----------|");
        foreach (var e in entries)
        {
            Console.WriteLine($"| {e.FragmentId} | {e.Version} | {e.EntryCount} | {e.LedgerSequence} | {e.LastSyncAt} |");
        }
        return 0;
    }

    // ── Help ─────────────────────────────────────────────────────────

    private static void PrintUsage()
    {
        Console.WriteLine("sharc-archive: Conversation archive with audit trail");
        Console.WriteLine();
        Console.WriteLine("Usage: sharc-archive <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  capture      Capture conversation turns from JSON-lines input");
        Console.WriteLine("  annotate     Add annotation/verdict to a turn or file");
        Console.WriteLine("  review       Query and display archived data");
        Console.WriteLine("  checkpoint   Save a milestone state snapshot");
        Console.WriteLine("  sync         Export/import ledger deltas for cross-arc sync");
        Console.WriteLine("  --help       Show this help message");
    }

    private static void PrintCaptureHelp()
    {
        Console.WriteLine("Usage: sharc-archive capture --input <file.jsonl> --output <archive.arc> [--agent <id>] [--source <name>]");
    }

    private static void PrintAnnotateHelp()
    {
        Console.WriteLine("Usage: sharc-archive annotate --archive <path> --target turn:<rowid> --type <type> --annotator <id> [--verdict <v>] [--content <text>]");
    }

    private static void PrintReviewHelp()
    {
        Console.WriteLine("Usage: sharc-archive review --archive <path> [--conversation <id>] [--section all|conversations|turns|annotations|decisions|checkpoints] [--format table|json]");
    }

    private static void PrintCheckpointHelp()
    {
        Console.WriteLine("Usage: sharc-archive checkpoint --archive <path> --label <label>");
    }

    private static void PrintSyncHelp()
    {
        Console.WriteLine("Usage: sharc-archive sync <subcommand>");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  export   --archive <path> --from-sequence <N> --output <deltas.bin>");
        Console.WriteLine("  import   --archive <path> --input <deltas.bin>");
        Console.WriteLine("  status   --archive <path>");
    }

    private static int SyncHelp() { PrintSyncHelp(); return 0; }

    private static int Error(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 1;
    }
}
