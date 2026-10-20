// 图形界面：单窗口、拖拽或自动定位、一键修复。
// 跟随系统深浅色；浅色为白底蓝调，深色为 PCL 风格的深灰蓝。
//
// 布局统一按 96 DPI 的设计坐标书写，运行时用 _scale 一次性换算成物理像素。
// 不用 WinForms 的 AutoScaleMode：它按 Font 尺寸推断缩放，高 DPI 下窗体与子控件
// 会采用不同倍率，表现就是控件挤在顶部、下方留出大片空白。
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PclDownloadFixer;

internal static class Gui
{
    private const string ExeName = "Plain Craft Launcher 2.exe";
    private const string ProjectUrl = "https://github.com/OMSociety/PCL-Download-Fixer";

    // 主题
    private static bool _dark;
    private static Color C_Bg, C_Card, C_TextMain, C_TextSub, C_Border, C_LogBg;
    private static readonly Color Accent = Color.FromArgb(19, 112, 243);
    private static readonly Color AccentHover = Color.FromArgb(40, 128, 250);

    private static float _scale = 1f;
    private static Form _form = null!;
    private static TextBox _txtPath = null!;
    private static Button _btnBrowse = null!;
    private static Button _btnRun = null!;
    private static Label _lblStatus = null!;
    private static TextBox _txtLog = null!;
    private static string? _pendingPath;
    private static bool _busy;

    [STAThread]
    public static int RunGui(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // 进程已在清单声明 per-monitor DPI 感知，这里读到的就是真实系统 DPI
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) _scale = g.DpiX / 96f;

        ApplyTheme();
        BuildUi();

        var argPath = args.FirstOrDefault(a => !a.StartsWith("-") && File.Exists(a));
        var auto = argPath is not null ? Path.GetFullPath(argPath) : AutoLocate();
        if (auto is not null) SelectTarget(auto);
        else Log($"未自动找到 {ExeName}。\r\n请点击「浏览…」选择，或把 PCL 主程序拖进本窗口。");

        Application.Run(_form);
        return 0;
    }

    /// <summary>把 96 DPI 设计坐标换算成当前 DPI 的物理像素。</summary>
    private static int S(int v) => (int)Math.Round(v * _scale);

    private static void ApplyTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            _dark = key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { _dark = false; }

        if (_dark)
        {
            C_Bg = Color.FromArgb(32, 32, 32);
            C_Card = Color.FromArgb(45, 45, 45);
            C_LogBg = Color.FromArgb(26, 26, 26);
            C_TextMain = Color.FromArgb(240, 240, 240);
            C_TextSub = Color.FromArgb(150, 150, 150);
            C_Border = Color.FromArgb(64, 64, 64);
        }
        else
        {
            C_Bg = Color.White;
            C_Card = Color.FromArgb(245, 245, 245);
            C_LogBg = Color.FromArgb(250, 250, 250);
            C_TextMain = Color.FromArgb(52, 61, 74);
            C_TextSub = Color.FromArgb(140, 140, 140);
            C_Border = Color.FromArgb(222, 226, 230);
        }
    }

    // 深色模式下让系统绘制深色标题栏，避免白条突兀
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeOld = 19;

    private static void BuildUi()
    {
        _form = new Form
        {
            Text = "PCL 下载修复器",
            ClientSize = new Size(S(540), S(382)),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedSingle,
            MaximizeBox = false,
            BackColor = C_Bg,
            ForeColor = C_TextMain,
            Font = new Font("Microsoft YaHei UI", 9F),
            AllowDrop = true,
            Icon = LoadIcon(),
            AutoScaleMode = AutoScaleMode.None,
        };

        if (_dark)
        {
            _form.HandleCreated += (_, _) =>
            {
                int on = 1;
                if (DwmSetWindowAttribute(_form.Handle, DwmwaUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(_form.Handle, DwmwaUseImmersiveDarkModeOld, ref on, sizeof(int));
            };
        }

        // 头部图标：PictureBox 的 BackColor=Transparent 在 WinForms 下不会真正
        // 透过父容器重绘，透明底 PNG 会露出控件的默认白块，故直接铺窗体底色。
        var logo = new PictureBox
        {
            Location = new Point(S(20), S(16)),
            Size = new Size(S(38), S(38)),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = LoadLogo(),
            BackColor = C_Bg,
        };

        var lblTitle = new Label
        {
            Text = "PCL 下载修复器",
            Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
            ForeColor = C_TextMain,
            Location = new Point(S(68), S(14)),
            Size = new Size(S(300), S(26)),
            AutoSize = false,
            BackColor = C_Bg,
        };

        var lblSub = new Label
        {
            Text = "恢复原版 Minecraft 的下载入口",
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = C_TextSub,
            Location = new Point(S(70), S(41)),
            Size = new Size(S(300), S(18)),
            AutoSize = false,
            BackColor = C_Bg,
        };

        var lblPath = new Label
        {
            Text = "PCL 主程序",
            ForeColor = C_TextSub,
            Location = new Point(S(20), S(72)),
            Size = new Size(S(200), S(18)),
            AutoSize = false,
            BackColor = C_Bg,
        };

        _txtPath = new TextBox
        {
            Location = new Point(S(20), S(93)),
            Size = new Size(S(400), S(23)),
            ReadOnly = true,
            BackColor = C_Card,
            ForeColor = C_TextMain,
            BorderStyle = BorderStyle.FixedSingle,
            Text = "未选择",
        };

        _btnBrowse = MakeButton("浏览…", 428, 91, 92, 27, secondary: true);
        _btnBrowse.Click += (_, _) => BrowseTarget();

        _btnRun = MakeButton("开始修复", 20, 130, 500, 38, secondary: false);
        _btnRun.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
        _btnRun.Enabled = false;
        _btnRun.Click += async (_, _) => await DoPatchAsync();

        _lblStatus = new Label
        {
            Text = "",
            Location = new Point(S(20), S(177)),
            Size = new Size(S(500), S(18)),
            ForeColor = C_TextSub,
            BackColor = C_Bg,
            AutoSize = false,
        };

        _txtLog = new TextBox
        {
            Location = new Point(S(20), S(200)),
            Size = new Size(S(500), S(142)),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = C_LogBg,
            ForeColor = C_TextMain,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9F),
        };

        // 页脚：程序免费，附项目地址
        var link = new LinkLabel
        {
            Text = "免费软件 · 付费获取即被骗 · " + ProjectUrl,
            Location = new Point(S(20), S(350)),
            Size = new Size(S(500), S(18)),
            AutoSize = false,
            LinkColor = _dark ? Color.FromArgb(120, 170, 240) : Color.FromArgb(60, 110, 190),
            ActiveLinkColor = Accent,
            VisitedLinkColor = _dark ? Color.FromArgb(120, 170, 240) : Color.FromArgb(60, 110, 190),
            LinkBehavior = LinkBehavior.HoverUnderline,
            BackColor = C_Bg,
            Font = new Font("Microsoft YaHei UI", 8.5F),
        };
        link.LinkClicked += (_, _) => OpenProjectPage();

        _form.Controls.AddRange(new Control[]
        {
            logo, lblTitle, lblSub, lblPath, _txtPath, _btnBrowse, _btnRun, _lblStatus, _txtLog, link
        });

        _form.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        _form.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            var exe = files.FirstOrDefault(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (exe is null) { SetStatus("请拖入 .exe 文件", warn: true); return; }
            SelectTarget(exe);
        };
    }

    private static Button MakeButton(string text, int x, int y, int w, int h, bool secondary)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(S(x), S(y)),
            Size = new Size(S(w), S(h)),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 9F),
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 1;
        if (secondary)
        {
            b.BackColor = C_Card;
            b.ForeColor = C_TextMain;
            b.FlatAppearance.BorderColor = C_Border;
        }
        else
        {
            b.BackColor = Accent;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Accent;
            b.FlatAppearance.MouseOverBackColor = AccentHover;
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(15, 95, 210);
        }
        return b;
    }

    // ── 资源 ────────────────────────────────────────────────
    private static Icon? LoadIcon()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream("logo.png");
            if (s is null) return null;
            using var bmp = new Bitmap(s);
            return Icon.FromHandle(bmp.GetHicon());
        }
        catch { return null; }
    }

    private static Image? LoadLogo()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream("logo.png");
            if (s is null) return null;
            // 复制一份再释放流，避免 GDI+ 依赖已关闭的流
            using var src = Image.FromStream(s);
            return new Bitmap(src);
        }
        catch { return null; }
    }

    // ── 目标定位 ─────────────────────────────────────────────
    private static string? AutoLocate()
    {
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
            if (File.Exists(c)) return Path.GetFullPath(c);

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(baseDir).Take(30))
            {
                var c = Path.Combine(dir, ExeName);
                if (File.Exists(c)) return Path.GetFullPath(c);
            }
        }
        catch { /* 目录不可读时忽略，交给手动选择 */ }

        return null;
    }

    private static void BrowseTarget()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择 PCL 主程序",
            Filter = "PCL 主程序|Plain Craft Launcher 2.exe|可执行文件|*.exe",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() == DialogResult.OK) SelectTarget(dlg.FileName);
    }

    private static void SelectTarget(string path)
    {
        _pendingPath = path;
        _txtPath.Text = path;
        _btnRun.Enabled = true;

        var name = Path.GetFileName(path);
        if (!name.Equals(ExeName, StringComparison.OrdinalIgnoreCase))
            SetStatus($"注意：文件名不是 {ExeName}，仍将尝试处理", warn: true);
        else
            SetStatus("已就绪，点击「开始修复」");
    }

    // ── 执行 ────────────────────────────────────────────────
    private static async Task DoPatchAsync()
    {
        if (_pendingPath is null || _busy) return;
        var path = _pendingPath;

        _busy = true;
        _btnRun.Enabled = false;
        _btnBrowse.Enabled = false;
        _btnRun.Text = "正在处理……";
        _txtLog.Clear();
        SetStatus("正在处理，请稍候……");

        var result = await Task.Run(() => Patcher.Apply(path, dryRun: false));

        _busy = false;
        _btnBrowse.Enabled = true;
        _btnRun.Text = "开始修复";

        if (result.Error is not null)
        {
            Log("× " + result.Error);
            if (result.InUse)
            {
                Log("");
                Log("PCL 运行时会锁定自身的 exe，无法覆盖。");
                Log("请从托盘图标退出 PCL 后，再点一次「开始修复」。");
                SetStatus("PCL 正在运行，请先退出", warn: true);
            }
            else
            {
                SetStatus("修复失败，原文件未改动", warn: true);
            }
            _btnRun.Enabled = true;
            return;
        }

        if (!result.Recognized)
        {
            Log("× 这不是受影响的 PCL 版本（未发现下载守卫）。");
            SetStatus("无需修复", warn: true);
            _btnRun.Enabled = true;
            return;
        }

        foreach (var s in result.Steps) Log("· " + s);

        if (result.AlreadyFixed)
        {
            Log("");
            Log("该文件已修复过，无需重复操作。");
            SetStatus("无需修复");
            _btnRun.Enabled = true;
            return;
        }

        Log("");
        if (result.BackupPath is not null) Log("已备份原文件：" + Path.GetFileName(result.BackupPath));
        Log($"完成，共修改 {result.Applied} 处。");
        if (result.ResourceDetailSkipped)
            Log("资源详情页的安装按钮未能恢复。");
        if (result.ModpackSkipped)
            Log("整合包本地导入未能恢复（该版本的分发代码已被删除）。");
        Log("");
        Log("请关闭 PCL 后重新启动，即可正常下载原版游戏。");
        Log("");
        Log("本程序开源免费。如果你是付费得到的，说明你被诈骗了喵。");
        Log("项目主页：" + ProjectUrl);

        SetStatus($"修复完成（{result.Applied} 处）", ok: true);
        _btnRun.Enabled = true;
    }

    private static void OpenProjectPage()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ProjectUrl) { UseShellExecute = true }); }
        catch { /* 无默认浏览器时忽略 */ }
    }

    private static void Log(string text) => _txtLog.AppendText(text + "\r\n");

    private static void SetStatus(string text, bool warn = false, bool ok = false)
    {
        _lblStatus.Text = text;
        _lblStatus.ForeColor = warn ? Color.FromArgb(220, 90, 90)
                            : ok ? Color.FromArgb(46, 164, 96)
                            : C_TextSub;
    }
}
