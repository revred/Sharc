// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

using Xunit;

namespace Sharc.Arc.Tests;

public sealed class GanttArcImporterTests
{
    private const string GanttProjectSample = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<project name=""Test Project"">
  <tasks>
    <task id=""1"" name=""Design"" start=""2025-01-06"" duration=""5"" complete=""100"">
      <task id=""2"" name=""UX Mockups"" start=""2025-01-06"" duration=""3"" complete=""100""/>
      <task id=""3"" name=""Architecture"" start=""2025-01-09"" duration=""2"" complete=""50"">
        <depend id=""2"" type=""2""/>
      </task>
    </task>
    <task id=""4"" name=""Build"" start=""2025-01-13"" duration=""10"" complete=""0"">
      <depend id=""1"" type=""2""/>
    </task>
  </tasks>
</project>";

    private const string MsProjectSample = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Project xmlns=""http://schemas.microsoft.com/project"">
  <Tasks>
    <Task>
      <UID>1</UID>
      <Name>Planning</Name>
      <Start>2025-01-06T08:00:00</Start>
      <Finish>2025-01-10T17:00:00</Finish>
      <Duration>PT40H0M0S</Duration>
      <PercentComplete>100</PercentComplete>
    </Task>
    <Task>
      <UID>2</UID>
      <Name>Development</Name>
      <Start>2025-01-13T08:00:00</Start>
      <Finish>2025-01-24T17:00:00</Finish>
      <Duration>PT80H0M0S</Duration>
      <PercentComplete>25</PercentComplete>
      <PredecessorLink>
        <PredecessorUID>1</PredecessorUID>
        <Type>1</Type>
      </PredecessorLink>
    </Task>
  </Tasks>
</Project>";

    [Fact]
    public void ParseGanttProject_ExtractsAllTasks()
    {
        var (tasks, deps) = GanttArcImporter.ParseGanttProject(GanttProjectSample);
        Assert.Equal(4, tasks.Count);
        Assert.Equal("Design", tasks[0].Name);
        Assert.Equal(5, tasks[0].Duration);
        Assert.Equal(100, tasks[0].Complete);
    }

    [Fact]
    public void ParseGanttProject_SubtaskParents()
    {
        var (tasks, _) = GanttArcImporter.ParseGanttProject(GanttProjectSample);
        var ux = tasks.Find(t => t.Name == "UX Mockups")!;
        Assert.Equal(1, ux.ParentId); // parent is "Design"
    }

    [Fact]
    public void ParseGanttProject_Dependencies()
    {
        var (_, deps) = GanttArcImporter.ParseGanttProject(GanttProjectSample);
        Assert.Equal(2, deps.Count);

        // Architecture depends on UX Mockups
        var archDep = deps.Find(d => d.ToTaskId == 3)!;
        Assert.Equal(2, archDep.FromTaskId);
        Assert.Equal("FS", archDep.Type);

        // Build depends on Design
        var buildDep = deps.Find(d => d.ToTaskId == 4)!;
        Assert.Equal(1, buildDep.FromTaskId);
    }

    [Fact]
    public void ImportGanttProject_CreatesQueryableArc()
    {
        using var arc = GanttArcImporter.ImportGanttProject(GanttProjectSample);

        using var taskReader = arc.Database.CreateReader("tasks");
        int count = 0;
        while (taskReader.Read()) count++;
        Assert.Equal(4, count);

        using var depReader = arc.Database.CreateReader("dependencies");
        int depCount = 0;
        while (depReader.Read()) depCount++;
        Assert.Equal(2, depCount);
    }

    [Fact]
    public void ParseMsProjectXml_ExtractsTasks()
    {
        var (tasks, deps) = GanttArcImporter.ParseMsProjectXml(MsProjectSample);
        Assert.Equal(2, tasks.Count);
        Assert.Equal("Planning", tasks[0].Name);
        Assert.Equal("2025-01-06", tasks[0].Start);
        Assert.Equal(5, tasks[0].Duration); // 40h / 8h = 5 days
        Assert.Equal(100, tasks[0].Complete);
    }

    [Fact]
    public void ParseMsProjectXml_PredecessorLinks()
    {
        var (_, deps) = GanttArcImporter.ParseMsProjectXml(MsProjectSample);
        Assert.Single(deps);
        Assert.Equal(1, deps[0].FromTaskId);
        Assert.Equal(2, deps[0].ToTaskId);
        Assert.Equal("FS", deps[0].Type);
    }

    [Fact]
    public void ImportCsv_BasicGantt()
    {
        var csv = "id,task,start,end,duration,depends_on\n" +
                  "1,Design,2025-01-06,2025-01-10,5,\n" +
                  "2,Build,2025-01-13,2025-01-24,10,1\n" +
                  "3,Test,2025-01-27,2025-01-31,5,1;2";

        using var arc = GanttArcImporter.ImportCsv(csv);

        using var taskReader = arc.Database.CreateReader("tasks");
        int count = 0;
        while (taskReader.Read()) count++;
        Assert.Equal(3, count);

        using var depReader = arc.Database.CreateReader("dependencies");
        int depCount = 0;
        while (depReader.Read()) depCount++;
        Assert.Equal(3, depCount); // Build→1, Test→1, Test→2
    }

    [Fact]
    public void ImportGantt_ThenFuse_WorksTogether()
    {
        using var gantt = GanttArcImporter.ImportGanttProject(GanttProjectSample);
        using var csv = CsvArcImporter.Import("member,role\nAlice,PM\nBob,Dev",
            new CsvArcImporter.CsvImportOptions { ArcName = "team.arc" });

        using var fused = new FusedArcContext();
        fused.Mount(gantt);
        fused.Mount(csv);

        var tables = fused.DiscoverTables();
        Assert.True(tables.ContainsKey("tasks"));
        Assert.True(tables.ContainsKey("dependencies"));
        Assert.True(tables.ContainsKey("data"));
    }
}
