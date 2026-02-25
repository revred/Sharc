// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;
using System.Text;
using System.Xml;
using Sharc.Core;

namespace Sharc.Arc;

/// <summary>
/// Imports Gantt chart data into .arc files with graph structure.
/// Tasks become rows in a "tasks" table; dependencies become rows
/// in a "dependencies" table — ready for topological sort, critical
/// path analysis, and graph traversal via Sharc.Graph.
/// <para>
/// <b>Supported formats:</b>
/// <list type="bullet">
///   <item>GanttProject (.gan) — XML with &lt;task&gt; elements and &lt;depend&gt; links</item>
///   <item>Microsoft Project XML (.xml) — XML with &lt;Task&gt; elements and predecessor links</item>
///   <item>CSV Gantt — columns: task, start, end, duration, depends_on</item>
/// </list>
/// </para>
/// </summary>
public static class GanttArcImporter
{
    /// <summary>Options for Gantt import.</summary>
    public sealed class GanttImportOptions
    {
        /// <summary>Arc file name. Default: "gantt.arc".</summary>
        public string ArcName { get; set; } = "gantt.arc";

        /// <summary>Table name for tasks. Default: "tasks".</summary>
        public string TasksTable { get; set; } = "tasks";

        /// <summary>Table name for dependencies. Default: "dependencies".</summary>
        public string DependenciesTable { get; set; } = "dependencies";
    }

    /// <summary>A parsed Gantt task.</summary>
    public sealed class GanttTask
    {
        /// <summary>Task ID.</summary>
        public long Id { get; init; }

        /// <summary>Task name/title.</summary>
        public string Name { get; init; } = "";

        /// <summary>Start date (ISO 8601 or empty).</summary>
        public string Start { get; init; } = "";

        /// <summary>End date (ISO 8601 or empty).</summary>
        public string End { get; init; } = "";

        /// <summary>Duration in days (0 if not specified).</summary>
        public int Duration { get; init; }

        /// <summary>Completion percentage (0-100).</summary>
        public int Complete { get; init; }

        /// <summary>Parent task ID for subtasks (0 = top-level).</summary>
        public long ParentId { get; init; }
    }

    /// <summary>A parsed dependency link.</summary>
    public sealed class GanttDependency
    {
        /// <summary>Source task ID (predecessor).</summary>
        public long FromTaskId { get; init; }

        /// <summary>Target task ID (successor).</summary>
        public long ToTaskId { get; init; }

        /// <summary>Dependency type: FS (finish-start), SS, FF, SF.</summary>
        public string Type { get; init; } = "FS";
    }

    /// <summary>
    /// Imports a GanttProject (.gan) file.
    /// </summary>
    public static ArcHandle ImportGanttProject(string xmlText, GanttImportOptions? options = null)
    {
        var (tasks, deps) = ParseGanttProject(xmlText);
        return BuildArc(tasks, deps, options);
    }

    /// <summary>
    /// Imports a Microsoft Project XML file.
    /// </summary>
    public static ArcHandle ImportMsProjectXml(string xmlText, GanttImportOptions? options = null)
    {
        var (tasks, deps) = ParseMsProjectXml(xmlText);
        return BuildArc(tasks, deps, options);
    }

    /// <summary>
    /// Imports a CSV-based Gantt chart. Expected columns:
    /// id, task, start, end, duration, complete, depends_on
    /// </summary>
    public static ArcHandle ImportCsv(string csvText, GanttImportOptions? options = null)
    {
        var (tasks, deps) = ParseCsvGantt(csvText);
        return BuildArc(tasks, deps, options);
    }

    /// <summary>
    /// Auto-detects format by file extension and imports.
    /// </summary>
    public static ArcHandle ImportFile(string path, GanttImportOptions? options = null)
    {
        string text = File.ReadAllText(path);
        options ??= new GanttImportOptions();
        if (options.ArcName == "gantt.arc")
            options.ArcName = Path.GetFileNameWithoutExtension(path) + ".arc";

        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".gan" => ImportGanttProject(text, options),
            ".xml" => ImportMsProjectXml(text, options),
            ".csv" => ImportCsv(text, options),
            _ => throw new ArgumentException(
                $"Unsupported Gantt format: {ext}. Use .gan (GanttProject), .xml (MS Project), or .csv.")
        };
    }

    // ── GanttProject (.gan) parser ──

    /// <summary>Parses GanttProject XML.</summary>
    public static (List<GanttTask> Tasks, List<GanttDependency> Dependencies) ParseGanttProject(string xmlText)
    {
        var tasks = new List<GanttTask>();
        var deps = new List<GanttDependency>();
        var parentStack = new Stack<long>();
        parentStack.Push(0);

        using var reader = XmlReader.Create(new StringReader(xmlText));

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "task")
            {
                long id = long.Parse(reader.GetAttribute("id") ?? "0", CultureInfo.InvariantCulture);
                string name = reader.GetAttribute("name") ?? "";
                string start = reader.GetAttribute("start") ?? "";
                int duration = int.TryParse(reader.GetAttribute("duration"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int d) ? d : 0;
                int complete = int.TryParse(reader.GetAttribute("complete"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int c) ? c : 0;

                tasks.Add(new GanttTask
                {
                    Id = id,
                    Name = name,
                    Start = start,
                    Duration = duration,
                    Complete = complete,
                    ParentId = parentStack.Peek()
                });

                if (!reader.IsEmptyElement)
                    parentStack.Push(id);
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "task")
            {
                if (parentStack.Count > 1) parentStack.Pop();
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "depend")
            {
                // <depend id="X" type="Y" .../>
                long depId = long.Parse(reader.GetAttribute("id") ?? "0", CultureInfo.InvariantCulture);
                string depType = reader.GetAttribute("type") switch
                {
                    "1" => "SS",
                    "2" => "FS",
                    "3" => "SF",
                    "4" => "FF",
                    _ => "FS"
                };

                // The depend element appears inside a task — the current task depends on depId
                if (tasks.Count > 0)
                {
                    deps.Add(new GanttDependency
                    {
                        FromTaskId = depId,
                        ToTaskId = tasks[^1].Id,
                        Type = depType
                    });
                }
            }
        }

        return (tasks, deps);
    }

    // ── MS Project XML parser ──

    /// <summary>Parses Microsoft Project XML.</summary>
    public static (List<GanttTask> Tasks, List<GanttDependency> Dependencies) ParseMsProjectXml(string xmlText)
    {
        var tasks = new List<GanttTask>();
        var deps = new List<GanttDependency>();

        var doc = new XmlDocument();
        doc.LoadXml(xmlText);

        // MS Project namespace
        var nsmgr = new XmlNamespaceManager(doc.NameTable);
        var root = doc.DocumentElement;
        if (root?.NamespaceURI is string ns && !string.IsNullOrEmpty(ns))
            nsmgr.AddNamespace("p", ns);

        string prefix = nsmgr.HasNamespace("p") ? "p:" : "";

        var taskNodes = doc.SelectNodes($"//{prefix}Task", nsmgr);
        if (taskNodes == null) return (tasks, deps);

        foreach (XmlNode taskNode in taskNodes)
        {
            long uid = ParseLong(taskNode, $"{prefix}UID", nsmgr);
            string name = ParseString(taskNode, $"{prefix}Name", nsmgr);
            string start = ParseString(taskNode, $"{prefix}Start", nsmgr);
            string finish = ParseString(taskNode, $"{prefix}Finish", nsmgr);
            int duration = ParseDuration(ParseString(taskNode, $"{prefix}Duration", nsmgr));
            int complete = (int)ParseLong(taskNode, $"{prefix}PercentComplete", nsmgr);

            if (uid == 0 && name == "") continue; // skip summary task 0

            tasks.Add(new GanttTask
            {
                Id = uid,
                Name = name,
                Start = start.Length > 10 ? start[..10] : start,
                End = finish.Length > 10 ? finish[..10] : finish,
                Duration = duration,
                Complete = complete
            });

            // Predecessors
            var predNodes = taskNode.SelectNodes($"{prefix}PredecessorLink", nsmgr);
            if (predNodes != null)
            {
                foreach (XmlNode pred in predNodes)
                {
                    long predUid = ParseLong(pred, $"{prefix}PredecessorUID", nsmgr);
                    int type = (int)ParseLong(pred, $"{prefix}Type", nsmgr);
                    deps.Add(new GanttDependency
                    {
                        FromTaskId = predUid,
                        ToTaskId = uid,
                        Type = type switch { 0 => "FF", 1 => "FS", 2 => "SF", 3 => "SS", _ => "FS" }
                    });
                }
            }
        }

        return (tasks, deps);
    }

    // ── CSV Gantt parser ──

    private static (List<GanttTask> Tasks, List<GanttDependency> Dependencies) ParseCsvGantt(string csvText)
    {
        var tasks = new List<GanttTask>();
        var deps = new List<GanttDependency>();
        var lines = csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) throw new ArgumentException("CSV Gantt must have header + data rows.");

        var header = CsvArcImporter.ParseRow(lines[0].TrimEnd('\r'), ',');
        int idCol = FindCol(header, "id");
        int nameCol = FindCol(header, "task", "name");
        int startCol = FindCol(header, "start", "start_date");
        int endCol = FindCol(header, "end", "end_date", "finish");
        int durCol = FindCol(header, "duration", "days");
        int compCol = FindCol(header, "complete", "percent", "progress");
        int depCol = FindCol(header, "depends_on", "dependencies", "predecessors");

        long autoId = 1;
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = CsvArcImporter.ParseRow(line, ',');

            long id = idCol >= 0 && idCol < fields.Length &&
                long.TryParse(fields[idCol], NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed : autoId;
            autoId = Math.Max(autoId, id + 1);

            string name = nameCol >= 0 && nameCol < fields.Length ? fields[nameCol] : "";
            string start = startCol >= 0 && startCol < fields.Length ? fields[startCol] : "";
            string end = endCol >= 0 && endCol < fields.Length ? fields[endCol] : "";
            int dur = durCol >= 0 && durCol < fields.Length &&
                int.TryParse(fields[durCol], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dv) ? dv : 0;
            int comp = compCol >= 0 && compCol < fields.Length &&
                int.TryParse(fields[compCol], NumberStyles.Integer, CultureInfo.InvariantCulture, out int cv) ? cv : 0;

            tasks.Add(new GanttTask { Id = id, Name = name, Start = start, End = end, Duration = dur, Complete = comp });

            // Parse dependencies (comma-separated task IDs)
            if (depCol >= 0 && depCol < fields.Length && !string.IsNullOrWhiteSpace(fields[depCol]))
            {
                foreach (var depStr in fields[depCol].Split(';', ','))
                {
                    if (long.TryParse(depStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long depId))
                    {
                        deps.Add(new GanttDependency { FromTaskId = depId, ToTaskId = id, Type = "FS" });
                    }
                }
            }
        }

        return (tasks, deps);
    }

    // ── Arc builder ──

    private static ArcHandle BuildArc(List<GanttTask> tasks, List<GanttDependency> deps,
        GanttImportOptions? options)
    {
        options ??= new GanttImportOptions();
        if (tasks.Count == 0)
            throw new ArgumentException("Gantt chart contains no tasks.");

        var handle = ArcHandle.CreateInMemory(options.ArcName);
        var db = handle.Database;

        using (var tx = db.BeginTransaction())
        {
            tx.Execute($"CREATE TABLE [{options.TasksTable}] " +
                "(id INTEGER PRIMARY KEY, name TEXT, start_date TEXT, end_date TEXT, " +
                "duration INTEGER, complete INTEGER, parent_id INTEGER)");
            tx.Execute($"CREATE TABLE [{options.DependenciesTable}] " +
                "(from_task INTEGER, to_task INTEGER, type TEXT)");
            tx.Commit();
        }

        using var writer = SharcWriter.From(db);

        foreach (var task in tasks)
        {
            writer.Insert(options.TasksTable,
                ColumnValue.FromInt64(4, task.Id),
                TextVal(task.Name),
                TextVal(task.Start),
                TextVal(task.End),
                ColumnValue.FromInt64(4, task.Duration),
                ColumnValue.FromInt64(1, task.Complete),
                ColumnValue.FromInt64(4, task.ParentId));
        }

        foreach (var dep in deps)
        {
            writer.Insert(options.DependenciesTable,
                ColumnValue.FromInt64(4, dep.FromTaskId),
                ColumnValue.FromInt64(4, dep.ToTaskId),
                TextVal(dep.Type));
        }

        return handle;
    }

    // ── Helpers ──

    private static ColumnValue TextVal(string value)
    {
        if (string.IsNullOrEmpty(value)) return ColumnValue.Null();
        var bytes = Encoding.UTF8.GetBytes(value);
        return ColumnValue.Text(bytes.Length * 2 + 13, bytes);
    }

    private static int FindCol(string[] header, params string[] names)
    {
        for (int i = 0; i < header.Length; i++)
        {
            string h = header[i].Trim().ToLowerInvariant();
            foreach (var name in names)
                if (h == name || h.Contains(name)) return i;
        }
        return -1;
    }

    private static long ParseLong(XmlNode parent, string xpath, XmlNamespaceManager nsmgr)
    {
        var node = parent.SelectSingleNode(xpath, nsmgr);
        if (node?.InnerText is string text &&
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long val))
            return val;
        return 0;
    }

    private static string ParseString(XmlNode parent, string xpath, XmlNamespaceManager nsmgr)
    {
        return parent.SelectSingleNode(xpath, nsmgr)?.InnerText ?? "";
    }

    /// <summary>Parses ISO 8601 duration (PT8H0M0S → days).</summary>
    private static int ParseDuration(string isoDuration)
    {
        if (string.IsNullOrEmpty(isoDuration)) return 0;
        // Simple parse: PT{hours}H{min}M{sec}S → hours/8 = days (8h workday)
        int hours = 0;
        var span = isoDuration.AsSpan();
        int idx = span.IndexOf('H');
        if (idx > 0)
        {
            // Find start of hours number
            int start = span.IndexOf('T') + 1;
            if (start > 0 && int.TryParse(span[start..idx], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int h))
                hours = h;
        }
        return hours > 0 ? Math.Max(1, hours / 8) : 0;
    }
}
