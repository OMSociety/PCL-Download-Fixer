# AGENTS.md

写给要改这个仓库的开发者，以及接手的 AI 会话。只记录工具管不住、但照做才不会改坏东西的部分。

## 这个仓库是什么

Windows 单文件图形工具：读入用户本机的 `Plain Craft Launcher 2.exe`，用 dnlib 改 IL，把 PCL 2.99.0.0 里写死的下载拦截还原回去，同时恢复整合包本地导入。

它不下载、不重装、不携带 PCL，只就地修改用户自己那份 exe，改之前备份成 `.bak`。

## 目录

| 路径 | 职责 |
|---|---|
| `src/Program.cs` | 入口。带 `--cli` 或 `--dry-run` 走控制台，否则开窗口 |
| `src/Gui.cs` | 单窗口界面。按 96 DPI 设计坐标书写，运行时用 `_scale` 换算物理像素 |
| `src/Patcher.cs` | 六个 `PatchXxx` 与写盘流程，与界面零耦合 |
| `src/app.manifest` | per-monitor DPI 感知、`asInvoker`（不申请管理员） |
| `assets/app.ico` | 编译进 exe 的图标 |
| `assets/logo.png` | 内嵌资源，界面左上角显示 |
| `tools/MakeIco` | 一次性工具，从 `logo.png` 生成多尺寸 `app.ico` |
| `docs/shots/` | README 引用的界面截图 |

## 构建

```bash
dotnet build src/Fixer.csproj -c Release
```

## 发布

```bash
dotnet publish src/Fixer.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o dist
```

产物是 `dist/PCL 下载修复器.exe`，单个文件约 65 MB，自包含运行库，目标机器不需要装 .NET。

`dist/` 已在 `.gitignore` 中。发布前先关掉正在运行的修复器，否则 exe 被占用、publish 会失败。

## 发版

1. 在 `CHANGELOG.md` 追加条目。
2. 按上面的命令发布，把产物改名为 `PCL-Download-Fixer-1.0.1-win-x64.exe` 这种形式。
3. tag 打在最终提交上，与提交一起推：

```bash
git tag v1.0.1
git push origin main v1.0.1
gh release create v1.0.1 "dist/PCL-Download-Fixer-1.0.1-win-x64.exe" \
  --title "PCL 下载修复器 1.0.1" --notes-file release-notes.md
```

已发布的 tag 不覆盖。要重发同一版本，先 `gh release delete v1.0.0 --yes --cleanup-tag`，再重打 tag、重建 Release。只改源码归档不换资产时，用 `gh api` 把 tag ref 指到新提交即可，不必重传 exe。

## 六个补丁

| 函数 | 改的 PCL 位置 | 做了什么 |
|---|---|---|
| `PatchLoaderInit` | `PageDownloadInstall.LoaderInit` | 把占位的 `LoaderTask` 换成 `ModDownload.DlClientListLoader`，版本列表接回真实加载链路 |
| `PatchLoaderLambda` | `PageDownloadInstall/_Closure$__._Lambda$__3-0` | 方法体只剩守卫抛错，整体清空为 `ret` |
| `PatchModifyVisibility` | MC 版本卡片的 `set_Visibility` | 实参 `ldc.i4.2` 改 `ldc.i4.0`，卡片不再隐藏 |
| `PatchResourceDetail` | `InitializeStackResourceVersions` 里 `ToListItem` 的图标按钮处理器 | 把置空的 `ldnull` 换成 `Install_Click` |
| `PatchModpackInstall` | `ModModpack.ModpackInstall(File, InstanceName, Logo)` | 守卫三连换成按 `PackType` 的 `switch`，分发到七个 `InstallPack*` |
| `PatchModpackEntry` | `ModModpack.ModpackInstall()` | 重建文件选择框与后台线程入口 |

## 改补丁时必须遵守

1. **每个补丁都要能重复跑。** 先识别「已修过」的形态并直接返回，让 `Applied` 保持 0，交给 `Apply` 统一报「已修复过」。识别方式是回读被改过的那几条指令，不要只看方法是否存在。
2. **dnlib 只能用 `ModuleDefMD.Load(bytes)`。** 传路径会以内存映射持有文件，回写时抛「无法在使用用户映射区域打开的文件上执行」。
3. **写盘顺序不可换**：探测占用 → 备份 → 写 `.new` → `File.Move(overwrite)`。占用的判断用 `FileShare.None` 打开，不要靠进程名。
4. **`.bak` 只在不存在时创建。** 它是用户第一次那份原件的唯一副本。
5. **若干 IL 偏移与 `ExceptionHandler.HandlerEnd` 是按当前 PCL 版本的实际布局算的**，`PatchModpackInstall` 尤其依赖「外层 finally 的处理器一直延伸到方法末尾」这一形态。改任何补丁后，必须把打完补丁的文件回读 dump，逐条确认 `switch` 目标、`leave` 落点与 EH 边界，再提交。
6. **按名字找目标时要带签名过滤。** 例如 `LoaderTask`2::.ctor`、`FirstOrDefault`，要同时校验参数个数与泛型实参，否则会匹配到泛型定义或其他重载，留下非法 IL。
7. **新增或删除补丁要同步文档。** README 里「六处写死的拦截」与「修复完成（6 处）」两个数字跟着实际补丁数走。
8. **`Gui.cs` 不要开 `AutoScaleMode`。** 它按字体尺寸推断缩放，高 DPI 下窗体与子控件会用不同倍率，表现为控件挤在顶部。新控件一律用 `S()` 换算。

## 验收

改了 `Patcher.cs` 就必须实机跑一遍：

1. 拿一份未经修改的 `Plain Craft Launcher 2.exe` 放进空目录。
2. 跑 `PCL 下载修复器.exe --cli "D:\pcl\Plain Craft Launcher 2.exe"`（换成你那份的完整路径），六行都打勾，末行为「完成，共修改 6 处。」
3. 再跑一次，应报「该文件已修复过，无需重复操作。」
4. 启动 PCL：下载页有版本列表，资源详情页安装按钮可用，整合包能本地导入。

`.bak` 与 `.new` 是运行期产物，验收目录要先清干净，否则会干扰「已修复」的判断。

## 不要做的事

- 不要提交 `dist/`、`.bak`、`.new`。
- 不要把 `assets/logo.png` 换成未压缩的原始大图，仓库里这份已经是 512×512 的压缩版。
- 不要在 manifest 里申请管理员权限。写不进去是安装目录权限问题，让用户自己以管理员身份运行。
- 不要改 `.gitattributes` 的行尾规则：`.cs` 与 `.md` 强制 LF，`.csproj` 保持 CRLF。
- 不要在仓库里放密钥或内网地址，本仓库是公开的。
