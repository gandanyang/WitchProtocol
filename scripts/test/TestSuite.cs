using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace MagicThunder.Test;

/// <summary>
/// 地基测试套件：headless 运行方式（见 tools/run_tests.ps1）
///   godot --headless --path . res://tests/TestSuite.tscn -- --probes=boot,event,save,pool,pattern
/// 读取命令行 --probes=a,b,c，逐跑 DevTestHub 探针，写 tests/reports/，退出码=失败数（0 通过）。
/// 探针是只读检查，无副作用；报告目录不入库（见 .gitignore）。
/// </summary>
public partial class TestSuite : Node
{
    private const string ReportDir = "user://test_reports";

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        var raw = ArgValue(args, "probes");

        var ids = (raw ?? "").Split(',', System.StringSplitOptions.RemoveEmptyEntries).ToList();
        if (ids.Count == 0)
        {
            GD.Print("[TestSuite] 未指定 --probes=...，退出（0）");
            GetTree().Quit(0);
            return;
        }

        var results = Autoload.DevTestHub.I.RunMany(ids);
        int failed = results.Count(kv => !kv.Value);

        foreach (var kv in results)
            GD.Print($"  [{(kv.Value ? "PASS" : "FAIL")}] {kv.Key}");
        GD.Print($"== TestSuite 完成：{results.Count - failed}/{results.Count} 通过 ==");

        WriteReport(ids, results);
        GetTree().Quit(failed == 0 ? 0 : 1);
    }

    private static string? ArgValue(string[] args, string name)
    {
        var prefix = "--" + name + "=";
        foreach (var a in args)
            if (a.StartsWith(prefix, System.StringComparison.Ordinal))
                return a[prefix.Length..];
        return null;
    }

    private void WriteReport(List<string> ids, System.Collections.Generic.Dictionary<string, bool> results)
    {
        DirAccess.MakeDirRecursiveAbsolute(ReportDir);
        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] probes={string.Join(',', ids)}");
        foreach (var kv in results)
            lines.AppendLine($"{kv.Key}={(kv.Value ? "PASS" : "FAIL")}");
        var path = ProjectSettings.GlobalizePath(ReportDir) + "/latest.txt";
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file != null) file.StoreString(lines.ToString());
    }
}