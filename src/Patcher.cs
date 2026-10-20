// 补丁核心逻辑：与界面无关，供 GUI 与命令行共用。
//
// 2.99.0.0 把 ModSecret.RestrictMcDownload 置为 True 后重编译。该常量是 Const，
// 编译期即被内联，各守卫点展开成硬编码的 ldstr/throw，二进制中已无开关字段可改。
// 底层下载链路（DlClientList*、版本清单、资源补全）完整保留，故按点还原即可。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace PclDownloadFixer;

internal sealed class PatchResult
{
    public bool Recognized;          // 是否为受影响的 PCL 版本
    public bool AlreadyFixed;        // 无需修改
    public bool InUse;               // 目标文件被占用（PCL 正在运行）
    public int Applied;              // 实际修改处数
    public bool ResourceDetailSkipped; // 资源详情页未能修复
    public string? Error;
    public string? BackupPath;
    public List<string> Steps = new();
}

internal static class Patcher
{
    private const string GuardMsg = "为遵守相关法律法规";

    /// <summary>
    /// 判断文件是否被其它进程独占——PCL 运行时会对自身 exe 加锁。
    /// 不能靠进程名判断：用户可能用改名副本启动，也可能同时开着多个启动器。
    /// </summary>
    private static bool IsFileLocked(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    public static PatchResult Apply(string path, bool dryRun)
    {
        var r = new PatchResult();
        try
        {
            // 从内存加载：dnlib 若直接 Load(路径)，会以内存映射方式持有文件，
            // 导致后续回写抛「无法在使用用户映射区域打开的文件上执行」。
            byte[] bytes = File.ReadAllBytes(path);
            using var mod = ModuleDefMD.Load(bytes, new ModuleContext());

            if (!HasRecognizableGuard(mod))
            {
                r.Recognized = false;
                return r;
            }
            r.Recognized = true;

            PatchLoaderInit(mod, r);
            PatchLoaderLambda(mod, r);
            PatchModifyVisibility(mod, r);
            PatchResourceDetail(mod, r);

            if (r.Applied == 0) { r.AlreadyFixed = true; return r; }
            if (dryRun) return r;

            // 写入前先确认文件没被占用，否则会抛出一句难懂的共享冲突
            if (IsFileLocked(path))
            {
                r.InUse = true;
                r.Error = "PCL 正在运行，请先完全退出它（包括托盘图标）再重试。";
                return r;
            }

            var backup = path + ".bak";
            if (!File.Exists(backup))
            {
                File.Copy(path, backup);
                r.BackupPath = backup;
            }

            // 先写临时文件再替换，避免中途失败留下半截 exe
            var tmp = path + ".new";
            mod.Write(tmp);
            mod.Dispose();

            try
            {
                File.Move(tmp, path, overwrite: true);
            }
            catch (IOException)
            {
                // 检查与替换之间被重新占用（用户又开了 PCL）
                try { File.Delete(tmp); } catch { }
                r.InUse = true;
                r.Error = "PCL 正在运行，请先完全退出它（包括托盘图标）再重试。";
                r.Applied = 0;
                return r;
            }
            return r;
        }
        catch (Exception ex)
        {
            // 兜底：把共享冲突翻译成人话
            if (ex is IOException && IsFileLocked(path))
            {
                r.InUse = true;
                r.Error = "PCL 正在运行，请先完全退出它（包括托盘图标）再重试。";
                return r;
            }
            r.Error = ex.Message;
            return r;
        }
    }

    public static bool HasRecognizableGuard(ModuleDefMD mod)
    {
        foreach (var t in mod.GetTypes())
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                if (m.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr
                        && i.Operand is string s && s.Contains(GuardMsg)))
                    return true;
            }
        return false;
    }

    // ── 1. PageDownloadInstall.LoaderInit ────────────────────
    // 现版：PageLoaderInit(..., new LoaderTask("关闭下载", 抛异常), ...)
    // 还原：PageLoaderInit(..., ModDownload.DlClientListLoader, ...)
    private static void PatchLoaderInit(ModuleDefMD mod, PatchResult r)
    {
        var page = mod.GetTypes().FirstOrDefault(t => t.Name == "PageDownloadInstall");
        var dl = mod.GetTypes().FirstOrDefault(t => t.Name == "ModDownload");
        var loaderField = dl?.Fields.FirstOrDefault(f => f.Name == "DlClientListLoader");
        var m = page?.Methods.FirstOrDefault(x => x.Name == "LoaderInit");

        if (m is null || !m.HasBody || loaderField is null) return;

        var ins = m.Body.Instructions;
        int ctorIdx = -1, strIdx = -1;
        // 必须按声明类型精确匹配：Action`1<LoaderTask`2<…>> 的 ctor 的 FullName 里也含 "LoaderTask"，
        // 用 Contains 会误匹配到代理构造，删除后会残留孤立 dup，导致 max stack 计算失败。
        for (int i = 0; i < ins.Count; i++)
            if (ins[i].OpCode == OpCodes.Newobj && ins[i].Operand is IMethod im
                && im.Name == ".ctor"
                && im.DeclaringType?.FullName.StartsWith("PCL.ModLoader/LoaderTask") == true)
            { ctorIdx = i; break; }

        if (ctorIdx < 0) { r.Steps.Add("下载页：已是真实加载器"); return; }

        for (int i = ctorIdx - 1; i >= 0; i--)
            if (ins[i].OpCode == OpCodes.Ldstr && ins[i].Operand is string s && s == "关闭下载")
            { strIdx = i; break; }

        if (strIdx < 0) { r.Steps.Add("下载页：结构不符，跳过"); return; }

        ins[ctorIdx] = OpCodes.Ldsfld.ToInstruction(loaderField);
        for (int i = ctorIdx - 1; i >= strIdx; i--)
            ins.RemoveAt(i);

        r.Applied++;
        r.Steps.Add("下载页：版本列表已接回真实加载器");
    }

    // ── 2. 占位抛异常的 lambda → 空实现 ─────────────────────
    private static void PatchLoaderLambda(ModuleDefMD mod, PatchResult r)
    {
        var page = mod.GetTypes().FirstOrDefault(t => t.Name == "PageDownloadInstall");
        if (page is null) return;

        foreach (var nested in page.NestedTypes)
        foreach (var m in nested.Methods)
        {
            if (!m.HasBody) continue;
            if (m.MethodSig.Params.Count != 0) continue;
            var ins = m.Body.Instructions;
            if (ins.Count < 3) continue;
            if (!ins.Any(i => i.OpCode == OpCodes.Ldstr && i.Operand is string s && s.Contains(GuardMsg)))
                continue;

            ins.Clear();
            m.Body.Variables.Clear();
            if (m.Body.HasExceptionHandlers) m.Body.ExceptionHandlers.Clear();
            ins.Add(OpCodes.Ret.ToInstruction());
            m.Body.UpdateInstructionOffsets();
            r.Applied++;
            r.Steps.Add("下载页：占位异常 lambda 已置空");
            return;
        }
        r.Steps.Add("下载页：lambda 已处理");
    }

    // ── 3. PageDownloadModify.LoadInstance 折叠 MC 卡片 ─────
    // 原版：get_CardMinecraft(); ldc.i4.2 (Collapsed); set_Visibility
    // 必须按 get_CardMinecraft 精确定位——同方法内 BtnBack 等控件同样用 ldc.i4.2 折叠，
    // 泛匹配会改错控件，也会让重复运行不再幂等。
    private static void PatchModifyVisibility(ModuleDefMD mod, PatchResult r)
    {
        var t = mod.GetTypes().FirstOrDefault(x => x.Name == "PageDownloadModify");
        if (t is null) return;

        foreach (var m in t.Methods.Where(x => x.HasBody))
        {
            var ins = m.Body.Instructions;
            for (int i = 2; i < ins.Count; i++)
            {
                if (ins[i].OpCode != OpCodes.Callvirt) continue;
                if (ins[i].Operand is not IMethod sv || sv.Name != "set_Visibility") continue;
                if (ins[i - 2].Operand is not IMethod gm || gm.Name != "get_CardMinecraft") continue;

                if (ins[i - 1].OpCode == OpCodes.Ldc_I4_0) { r.Steps.Add("修改页：MC 版本卡片已显示"); return; }

                ins[i - 1].OpCode = OpCodes.Ldc_I4_0; // Collapsed → Visible
                m.Body.UpdateInstructionOffsets();
                r.Applied++;
                r.Steps.Add("修改页：MC 版本卡片已恢复显示");
                return;
            }
        }
        r.Steps.Add("修改页：未找到折叠逻辑");
    }

    // ── 4. 资源详情页安装按钮 ───────────────────────────────
    // 原版：ToListItem(Save_Click, Install_Click, bad)；现版第二参数被填 null。
    private static void PatchResourceDetail(ModuleDefMD mod, PatchResult r)
    {
        var t = mod.GetTypes().FirstOrDefault(x => x.Name == "PageDownloadResourceDetail");
        var install = t?.Methods.FirstOrDefault(x => x.Name == "Install_Click");
        var handler = mod.GetTypes().FirstOrDefault(x => x.FullName == "PCL.MyIconButton/ClickEventHandler");
        var handlerCtor = handler?.Methods.FirstOrDefault(x => x.Name == ".ctor");
        if (t is null || install is null || handlerCtor is null) { r.ResourceDetailSkipped = true; return; }

        foreach (var m in t.Methods.Where(x => x.HasBody))
        {
            var ins = m.Body.Instructions;
            for (int i = 2; i < ins.Count; i++)
            {
                if (ins[i].OpCode != OpCodes.Callvirt) continue;
                if (ins[i].Operand is not IMethod tl || tl.Name != "ToListItem") continue;

                // 形态可能是 (obj, Save_Click, null, bad) 或 (obj, null, null, bad)
                int at = -1;
                if (ins[i - 1].OpCode == OpCodes.Ldnull && ins[i - 2].OpCode == OpCodes.Ldnull) at = i - 2;
                else if (ins[i - 1].OpCode == OpCodes.Ldnull && ins[i - 2].OpCode == OpCodes.Newobj) at = i - 1;
                if (at < 0) continue;

                ins[at] = OpCodes.Ldarg_0.ToInstruction();
                ins.Insert(at + 1, OpCodes.Ldftn.ToInstruction(install));
                ins.Insert(at + 2, OpCodes.Newobj.ToInstruction(handlerCtor));
                m.Body.UpdateInstructionOffsets();
                r.Applied++;
                r.Steps.Add("资源详情页：安装按钮已恢复");
                return;
            }
        }
        r.ResourceDetailSkipped = true;
    }
}
