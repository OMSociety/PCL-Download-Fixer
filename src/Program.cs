// 入口：默认启动图形界面；带路径或 --cli 时走命令行（便于批量与自动化）。
using System;
using System.IO;
using System.Linq;

namespace PclDownloadFixer;

internal static class Program
{
    private const string ExeName = "Plain Craft Launcher 2.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        // 显式要求命令行，或与 --dry-run 组合使用时，一律走 CLI
        if (args.Contains("--cli") || args.Contains("--dry-run")) return RunCli(args);

        // 无参数、或参数是拖入的 exe 路径 → 图形界面（路径由 GUI 预选）
        return Gui.RunGui(args);
    }

    // WinExe 不自动绑定控制台，CLI 模式需手动挂到父进程的控制台，
    // 否则 Console.Write 全部丢弃（重定向到文件也会是空文件）。
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern int GetFileType(IntPtr hFile);

    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const int FileTypeDisk = 1;    // 重定向到文件或管道
    private const int FileTypeChar = 2;    // 控制台

    private static int RunCli(string[] args)
    {
        // WinExe 不自动绑定控制台：父进程有控制台时挂上去，才能看到输出。
        // 若 stdout 已被重定向（文件/管道），句柄本就有效，直接写即可。
        var hStd = GetStdHandle(StdOutputHandle);
        bool redirected = hStd != IntPtr.Zero && GetFileType(hStd) == FileTypeDisk;

        if (!redirected)
        {
            if (!AttachConsole(AttachParentProcess)) { /* 双击启动无控制台，交给 GUI */ return Gui.RunGui(args); }
        }

        try
        {
            var writer = new System.IO.StreamWriter(Console.OpenStandardOutput(),
                new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(writer);
        }
        catch (System.IO.IOException) { }

        Console.WriteLine("PCL 下载修复器  v1.0");
        Console.WriteLine(new string('=', 44));

        bool dryRun = args.Contains("--dry-run");
        var path = ResolveTarget(args.FirstOrDefault(a => !a.StartsWith("-")));
        if (path is null) return 1;

        Console.WriteLine($"目标: {path}");
        if (dryRun) Console.WriteLine("（预览模式，不写入）");
        Console.WriteLine();

        var r = Patcher.Apply(path, dryRun);

        if (r.Error is not null) { Err("失败：" + r.Error); return 2; }
        if (!r.Recognized) { Warn("这不是受影响的 PCL 版本（未发现下载守卫），无需修复。"); return 1; }

        foreach (var s in r.Steps)
        {
            if (s.Contains("已") || s.Contains("接回")) Ok(s);
            else Note(s);
        }

        if (r.AlreadyFixed) { Ok("该文件已修复过，无需重复操作。"); return 0; }
        if (dryRun) { Console.WriteLine($"\n预览完成：{r.Applied} 处可修复。"); return 0; }

        Console.WriteLine();
        if (r.BackupPath is not null) Console.WriteLine($"已备份原文件 → {Path.GetFileName(r.BackupPath)}");
        Ok($"完成，共修改 {r.Applied} 处。重启 PCL 即可下载原版游戏。");
        return 0;
    }

    private static string? ResolveTarget(string? argPath)
    {
        if (!string.IsNullOrEmpty(argPath))
        {
            if (!File.Exists(argPath)) { Err($"找不到文件：{argPath}"); return null; }
            return Path.GetFullPath(argPath);
        }

        var baseDir = AppContext.BaseDirectory;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var candidates = new[]
        {
            Path.Combine(baseDir, ExeName),
            Path.GetFullPath(Path.Combine(baseDir, "..", ExeName)),
            Path.Combine(desktop, ExeName),
            Path.Combine(desktop, "PCL", ExeName),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        Err($"未找到 {ExeName}。\n请把本程序放进 PCL 所在文件夹，或把 PCL 的 exe 拖到本程序图标上。");
        return null;
    }

    private static void Ok(string m)   { Console.ForegroundColor = ConsoleColor.Green;    Console.WriteLine("  ✓ " + m); Console.ResetColor(); }
    private static void Note(string m) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine("  - " + m); Console.ResetColor(); }
    private static void Warn(string m) { Console.ForegroundColor = ConsoleColor.Yellow;   Console.WriteLine("  ! " + m); Console.ResetColor(); }
    private static void Err(string m)  { Console.ForegroundColor = ConsoleColor.Red;      Console.WriteLine("  × " + m); Console.ResetColor(); }
}
