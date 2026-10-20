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
    public bool ModpackSkipped;        // 整合包本地导入未能修复
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
            PatchModpackInstall(mod, r);
            PatchModpackEntry(mod, r);

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

                // 参数顺序是 (列表项处理器, 图标按钮处理器, Boolean)，
                // 所以要找的是紧跟在 ClickEventHandler::.ctor 之后、被置空的那个处理器，
                // 它不一定正好贴着 callvirt。
                int at = -1;
                for (int k = i - 1; k >= 0 && k >= i - 3; k--)
                {
                    if (ins[k].OpCode != OpCodes.Ldnull) continue;
                    if (k == 0 || ins[k - 1].OpCode != OpCodes.Newobj) continue;
                    if (ins[k - 1].Operand is not IMethod ctor || ctor.Name != ".ctor") continue;
                    if (ctor.DeclaringType?.Name.Contains("ClickEventHandler") != true) continue;
                    at = k;
                    break;
                }
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

    // ── 5. ModModpack.ModpackInstall(File, InstanceName, Logo) ──
    // 原版：识别出整合包种类后按 PackType 分发到七个 InstallPack* 方法。
    // 现版：种类识别完整保留，但其后的分发整段被编译器当作不可达代码删除，
    //       方法体以一条无条件 throw 收尾，因此该方法永不返回（末尾没有 ret）。
    // 还原：把这条 throw 换成按 PackType 的 switch，并补回返回路径。
    // 注意分发代码位于 try 块内，返回必须先 leave 出 try 走到 finally 之后。
    private static void PatchModpackInstall(ModuleDefMD mod, PatchResult r)
    {
        var t = mod.GetTypes().FirstOrDefault(x => x.FullName == "PCL.ModModpack");
        var m = t?.Methods.FirstOrDefault(x => x.Name == "ModpackInstall" && x.Parameters.Count == 3);
        if (m is null || !m.HasBody) { r.ModpackSkipped = true; r.Steps.Add("整合包：未找到安装方法"); return; }

        var body = m.Body;
        var ins = body.Instructions;

        // 已修过的方法以 ldloc.0 / ret 收尾；未修过的以 endfinally 收尾
        if (ins.Count > 0 && ins[^1].OpCode == OpCodes.Ret)
        { r.Steps.Add("整合包：本地导入已可用"); return; }

        if (body.Variables.Count < 5 || ins.Count < 3 || ins[^1].OpCode != OpCodes.Endfinally)
        { r.ModpackSkipped = true; r.Steps.Add("整合包：结构不符，跳过"); return; }

        // 守卫三连：ldstr(提示语) + newobj Exception::.ctor + throw
        int g = -1;
        for (int i = 0; i + 2 < ins.Count; i++)
        {
            if (ins[i].OpCode != OpCodes.Ldstr || ins[i].Operand is not string s || !s.Contains(GuardMsg)) continue;
            if (ins[i + 1].OpCode != OpCodes.Newobj || ins[i + 2].OpCode != OpCodes.Throw) continue;
            g = i; break;
        }
        if (g < 0) { r.ModpackSkipped = true; r.Steps.Add("整合包：未找到守卫"); return; }

        // 外层 try/finally 的处理器一直延伸到方法末尾（HandlerEnd 为 null）
        var outerFinally = body.ExceptionHandlers.FirstOrDefault(
            e => e.HandlerType == ExceptionHandlerType.Finally && e.HandlerEnd is null);
        if (outerFinally is null) { r.ModpackSkipped = true; r.Steps.Add("整合包：未找到外层 finally"); return; }

        IMethod? Find(string name) => t!.Methods.FirstOrDefault(x => x.Name == name && x.IsStatic);
        var fCurse    = Find("InstallPackCurseForge");
        var fModrinth = Find("InstallPackModrinth");
        var fHMCL     = Find("InstallPackHMCL");
        var fMMC      = Find("InstallPackMMC");
        var fMCBBS    = Find("InstallPackMCBBS");
        var fLauncher = Find("InstallPackLauncherPack");
        var fCompress = Find("InstallPackCompress");
        if (fCurse is null || fModrinth is null || fHMCL is null || fMMC is null
            || fMCBBS is null || fLauncher is null || fCompress is null)
        { r.ModpackSkipped = true; r.Steps.Add("整合包：安装方法缺失，跳过"); return; }

        var guardStr = ins[g];
        var retVal    = body.Variables[0]; // LoaderCombo(Of String)
        var archive   = body.Variables[1]; // ZipArchive
        var baseDir   = body.Variables[2]; // ArchiveBaseFolder
        var packType  = body.Variables[4]; // Integer

        var ldRet = Instruction.Create(OpCodes.Ldloc, retVal);
        var ret   = Instruction.Create(OpCodes.Ret);

        var block = new List<Instruction>();

        // 每个分支：压参数 → call → 存返回值 → 跳出 try
        Instruction Branch(IMethod target, bool withName, bool withLogo)
        {
            var head = Instruction.Create(OpCodes.Ldarg_0);
            block.Add(head);
            block.Add(Instruction.Create(OpCodes.Ldloc, archive));
            block.Add(Instruction.Create(OpCodes.Ldloc, baseDir));
            if (withName) block.Add(Instruction.Create(OpCodes.Ldarg_1));
            if (withLogo) block.Add(Instruction.Create(OpCodes.Ldarg_2));
            block.Add(Instruction.Create(OpCodes.Call, target));
            block.Add(Instruction.Create(OpCodes.Stloc, retVal));
            block.Add(Instruction.Create(OpCodes.Leave, ldRet));
            return head;
        }

        var cElse = Branch(fCompress, false, false);   // Case Else：未识别，按压缩包处理
        var c0 = Branch(fCurse,    true,  true);
        var c1 = Branch(fHMCL,     false, false);
        var c2 = Branch(fMMC,      false, false);
        var c3 = Branch(fMCBBS,    true,  false);
        var c4 = Branch(fModrinth, true,  true);
        var c9 = Branch(fLauncher, false, false);

        var entry = Instruction.Create(OpCodes.Ldloc, packType);
        var sw = Instruction.Create(OpCodes.Switch,
            new[] { c0, c1, c2, c3, c4, cElse, cElse, cElse, cElse, c9 });
        var brElse = Instruction.Create(OpCodes.Br, cElse);

        // 原先所有「Exit Try」都跳向守卫语句，现在改跳分发入口
        foreach (var i in ins)
        {
            if (!ReferenceEquals(i.Operand, guardStr)) continue;
            i.Operand = entry;
            if (i.OpCode == OpCodes.Leave_S) i.OpCode = OpCodes.Leave; // 目标变远，短跳转放不下
        }
        // catch 处理器的结束位置同样指向守卫语句
        foreach (var eh in body.ExceptionHandlers)
            if (eh.HandlerType == ExceptionHandlerType.Catch && ReferenceEquals(eh.HandlerEnd, guardStr))
                eh.HandlerEnd = entry;

        // 返回值要放在 finally 之后，leave 才有合法的落点
        ins.Add(ldRet);
        ins.Add(ret);
        outerFinally.HandlerEnd = ldRet;

        block.Insert(0, brElse);
        block.Insert(0, sw);
        block.Insert(0, entry);
        for (int k = 0; k < block.Count; k++) ins.Insert(g + k, block[k]);

        // 删掉已不可达的原守卫三连（此时位于插入块之后）
        for (int k = 0; k < 3; k++) ins.RemoveAt(g + block.Count);

        if (body.MaxStack < 8) body.MaxStack = 8;
        body.UpdateInstructionOffsets();

        r.Applied++;
        r.Steps.Add("整合包：本地导入已恢复");
    }

    /// <summary>
    /// 无参 ModpackInstall 原本弹出「选择整合包压缩文件」对话框，再把路径交给后台线程安装；
    /// 守卫内联后整段被编译掉，只剩一句提示。这里按原样把入口重建出来。
    /// </summary>
    private static void PatchModpackEntry(ModuleDefMD mod, PatchResult r)
    {
        var t = mod.GetTypes().FirstOrDefault(x => x.FullName == "PCL.ModModpack");
        var m = t?.Methods.FirstOrDefault(x => x.Name == "ModpackInstall" && x.Parameters.Count == 0);
        if (m is null || !m.HasBody)
        { r.Steps.Add("整合包入口：未找到无参方法"); return; }

        var ins = m.Body.Instructions;

        // 修过的版本会调用文件选择框
        if (ins.Any(i => i.Operand is IMethod im && im.Name == "SelectFile"))
        { r.Steps.Add("整合包入口：已可用"); return; }

        // 未修过的版本只剩一句提示
        if (!ins.Any(i => i.OpCode == OpCodes.Ldstr && i.Operand is string s && s.Contains(GuardMsg)))
        { r.Steps.Add("整合包入口：结构不符，跳过"); return; }

        var closure = mod.GetTypes().FirstOrDefault(x => x.FullName == "PCL.ModModpack/_Closure$__0-0");
        var ctor    = closure?.Methods.FirstOrDefault(x => x.IsConstructor);
        var field   = closure?.Fields.FirstOrDefault(x => x.Name == "$VB$Local_File");
        var lambda  = closure?.Methods.FirstOrDefault(x => x.Name == "_Lambda$__0");
        var runThread = mod.GetTypes().FirstOrDefault(x => x.FullName == "PCL.ModBase")
                           ?.Methods.FirstOrDefault(x => x.Name == "RunInThread" && x.MethodSig.Params.Count == 1);
        if (closure is null || ctor is null || field is null || lambda is null || runThread is null)
        { r.Steps.Add("整合包入口：闭包缺失，跳过"); return; }

        // 复用模块里已有的方法引用，避免自己拼 TypeSig
        IMethod? selectFile = null, first = null, isNullOrEmpty = null, actionCtor = null;
        foreach (var mt in mod.GetTypes())
            foreach (var mm in mt.Methods)
            {
                if (!mm.HasBody) continue;
                foreach (var i in mm.Body.Instructions)
                {
                    if (i.Operand is not IMethod im) continue;
                    if (selectFile is null && im.Name == "SelectFile" && im.MethodSig.Params.Count == 4)
                        selectFile = im;
                    else if (first is null && im is MethodSpec ms && ms.Name == "FirstOrDefault"
                             && ms.Method.MethodSig.Params.Count == 1
                             && ms.GenericInstMethodSig?.GenericArguments.Count == 1
                             && ms.GenericInstMethodSig.GenericArguments[0].FullName == "System.String")
                        first = ms;
                    else if (isNullOrEmpty is null && im.Name == "IsNullOrEmpty")
                        isNullOrEmpty = im;
                    else if (actionCtor is null && im.Name == ".ctor" && im.DeclaringType?.FullName == "System.Action")
                        actionCtor = im;
                }
            }
        if (selectFile is null || first is null || isNullOrEmpty is null || actionCtor is null)
        { r.Steps.Add("整合包入口：外部方法引用缺失，跳过"); return; }

        var body = m.Body;
        body.ExceptionHandlers.Clear();
        body.Variables.Clear();

        var filterType = selectFile.MethodSig.Params[3]; // Nullable(Of OneOf(...))
        var vClosure = new Local(closure.ToTypeSig());
        var vFile    = new Local(mod.CorLibTypes.String);
        var vFilter  = new Local(filterType);
        body.Variables.Add(vClosure);
        body.Variables.Add(vFile);
        body.Variables.Add(vFilter);

        // 过滤条件传空（默认值），让用户自由选择整合包文件
        var initFilter = new Instruction { OpCode = OpCodes.Initobj, Operand = filterType.ToTypeDefOrRef() };

        var ret = Instruction.Create(OpCodes.Ret);
        var list = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldstr, "选择整合包压缩文件"),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Ldloca, vFilter),
            initFilter,
            Instruction.Create(OpCodes.Ldloc, vFilter),
            Instruction.Create(OpCodes.Call, selectFile),
            Instruction.Create(OpCodes.Call, first),
            Instruction.Create(OpCodes.Stloc, vFile),
            Instruction.Create(OpCodes.Ldloc, vFile),
            Instruction.Create(OpCodes.Call, isNullOrEmpty),
            Instruction.Create(OpCodes.Brtrue, ret),
            Instruction.Create(OpCodes.Newobj, ctor),
            Instruction.Create(OpCodes.Stloc, vClosure),
            Instruction.Create(OpCodes.Ldloc, vClosure),
            Instruction.Create(OpCodes.Ldloc, vFile),
            Instruction.Create(OpCodes.Stfld, field),
            Instruction.Create(OpCodes.Ldloc, vClosure),
            Instruction.Create(OpCodes.Ldftn, lambda),
            Instruction.Create(OpCodes.Newobj, actionCtor),
            Instruction.Create(OpCodes.Call, runThread),
            ret
        };

        ins.Clear();
        foreach (var i in list) ins.Add(i);
        body.MaxStack = 3;
        body.UpdateInstructionOffsets();

        r.Applied++;
        r.Steps.Add("整合包入口：本地导入对话框已恢复");
    }
}
