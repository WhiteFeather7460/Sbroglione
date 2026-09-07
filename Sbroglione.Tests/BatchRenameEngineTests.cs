using System;
using System.Collections.Generic;
using Sbroglione.Services;
using Xunit;

namespace Sbroglione.Tests;

public class BatchRenameEngineTests
{
    private static readonly DateTime FixedDate = new(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BuildPlan_LiteralFindReplace_ReplacesAllOccurrences()
    {
        var files = new List<(string Path, DateTime LastModified)>
        {
            ("/tmp/src/vacation_2020.jpg", FixedDate),
        };
        var options = new RenameOptions(UseRegex: false, FindPattern: "vacation", ReplacePattern: "trip", Template: "", CounterStart: 1, CounterStep: 1);

        IReadOnlyList<RenamePlanItem> plan = BatchRenameEngine.BuildPlan(files, options);

        Assert.Equal("trip_2020.jpg", plan[0].NewName);
        Assert.False(plan[0].HasConflict);
        Assert.Null(plan[0].Error);
    }

    [Fact]
    public void BuildPlan_RegexFindReplace_SupportsBackreferences()
    {
        var files = new List<(string Path, DateTime LastModified)>
        {
            ("/tmp/src/IMG_0001.jpg", FixedDate),
        };
        var options = new RenameOptions(UseRegex: true, FindPattern: @"IMG_(\d+)", ReplacePattern: "Photo-$1", Template: "", CounterStart: 1, CounterStep: 1);

        IReadOnlyList<RenamePlanItem> plan = BatchRenameEngine.BuildPlan(files, options);

        Assert.Equal("Photo-0001.jpg", plan[0].NewName);
    }

    [Fact]
    public void BuildPlan_InvalidRegex_ReportsErrorWithoutThrowing()
    {
        var files = new List<(string Path, DateTime LastModified)>
        {
            ("/tmp/src/a.jpg", FixedDate),
        };
        var options = new RenameOptions(UseRegex: true, FindPattern: "(unclosed", ReplacePattern: "x", Template: "", CounterStart: 1, CounterStep: 1);

        IReadOnlyList<RenamePlanItem> plan = BatchRenameEngine.BuildPlan(files, options);

        Assert.NotNull(plan[0].Error);
        Assert.Equal("a.jpg", plan[0].NewName);
    }

    [Fact]
    public void BuildPlan_TemplateWithCounterAndPadding_AppliesFormatPerFile()
    {
        var files = new List<(string Path, DateTime LastModified)>
        {
            ("/tmp/src/a.jpg", FixedDate),
            ("/tmp/src/b.jpg", FixedDate),
        };
        var options = new RenameOptions(UseRegex: false, FindPattern: "", ReplacePattern: "", Template: "photo-{counter:000}.{ext}", CounterStart: 1, CounterStep: 1);

        IReadOnlyList<RenamePlanItem> plan = BatchRenameEngine.BuildPlan(files, options);

        Assert.Equal("photo-001.jpg", plan[0].NewName);
        Assert.Equal("photo-002.jpg", plan[1].NewName);
    }

    [Fact]
    public void BuildPlan_TemplateWithDateToken_FormatsFileLastModified()
    {
        var files = new List<(string Path, DateTime LastModified)>
        {
            ("/tmp/src/a.jpg", FixedDate),
        };
        var options = new RenameOptions(UseRegex: false, FindPattern: "", ReplacePattern: "", Template: "{date:yyyy-MM-dd}_{name}.{ext}", CounterStart: 1, CounterStep: 1);

        IReadOnlyList<RenamePlanItem> plan = BatchRenameEngine.BuildPlan(files, options);

        Assert.Equal("2026-03-05_a.jpg", plan[0].NewName);
    }

    [Fact]
    public void BuildPlan_TwoFilesResolveToSameName_SecondFlaggedAsConflict()
    {
        var files = new List<(string Path, DateTime LastModified)>
        {
            ("/tmp/src/a.jpg", FixedDate),
            ("/tmp/src/b.jpg", FixedDate),
        };
        var options = new RenameOptions(UseRegex: false, FindPattern: "", ReplacePattern: "", Template: "same.jpg", CounterStart: 1, CounterStep: 1);

        IReadOnlyList<RenamePlanItem> plan = BatchRenameEngine.BuildPlan(files, options);

        Assert.False(plan[0].HasConflict);
        Assert.True(plan[1].HasConflict);
    }
}
