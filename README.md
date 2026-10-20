<div align="center">
  <img src="https://raw.githubusercontent.com/OMSociety/PCL-Download-Fixer/main/assets/logo.png" alt="PCL 下载修复器" width="128">
  <h1>PCL 下载修复器</h1>
  <p>恢复 PCL 2.99.0.0 的原版 Minecraft 下载入口。</p>
  <p>选好主程序，点一次按钮。</p>

  <p>
    <a href="https://github.com/OMSociety/PCL-Download-Fixer/releases/latest"><img src="https://img.shields.io/github/v/release/OMSociety/PCL-Download-Fixer?color=4f6ef7" alt="Version"></a>
    <a href="https://github.com/OMSociety/PCL-Download-Fixer"><img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows"></a>
    <a href="LICENSE"><img src="https://img.shields.io/github/license/OMSociety/PCL-Download-Fixer?color=4f6ef7" alt="License"></a>
    <a href="https://github.com/OMSociety/PCL-Download-Fixer/stargazers"><img src="https://img.shields.io/github/stars/OMSociety/PCL-Download-Fixer?color=4f6ef7" alt="Stars"></a>
    <a href="https://github.com/OMSociety/PCL-Download-Fixer/issues"><img src="https://img.shields.io/github/issues/OMSociety/PCL-Download-Fixer?color=4f6ef7" alt="Issues"></a>
  </p>

<a href="#这是什么">这是什么</a> • <a href="#核心特性">核心特性</a> • <a href="#效果">效果</a> • <a href="#快速开始">快速开始</a> • <a href="#它在文件里改了什么">它在文件里改了什么</a> • <a href="#常见问题">常见问题</a> • <a href="#许可">许可</a>
</div>

## 这是什么

PCL 2.99.0.0 的「下载」页面不再提供 Minecraft 原版游戏与加载器的安装入口，原本的安装按钮换成了「关闭下载」的提示。安装代码本身仍在程序里，被一处编译期开关挡在门外。

**PCL 下载修复器**不联网、不重装、不替换你的 PCL —— 它直接读取你电脑上那份 `Plain Craft Launcher 2.exe`，改掉其中三个被写死的判断，然后原地写回。改完启动 PCL，下载页恢复正常。

整个程序是一个 exe，选文件、点按钮、完事。

## 核心特性

| 特性 | 说明 |
|---|---|
| **一键修复** | 选中 PCL 主程序后点一次「开始修复」，全过程在本地完成，不下载任何东西 |
| **自动定位** | 启动时自动在当前目录、上级目录、桌面与桌面下的 PCL 子目录里找 `Plain Craft Launcher 2.exe`，也可以直接把 exe 拖进窗口 |
| **先备份再改** | 修改前把原文件存为 `Plain Craft Launcher 2.exe.bak`，想还原直接改名回去 |
| **可重复运行** | 已修复过的文件再次运行会直接跳过并说明原因，不会叠加改动 |
| **占用检测** | PCL 运行时锁定自身 exe，工具在动手前先探测，提示你退出 PCL，而不是写到一半失败 |
| **改完自检** | 只有三处改动全部写成功才算完成；中途出错会保留原文件不动 |
| **单文件免安装** | 自包含发布，不需要预装 .NET 运行时；不写注册表、不装服务、不留后台进程 |
| **跟随系统主题** | 深色、浅色两套界面，按 Windows 的应用主题自动切换 |

## 效果

<table>
<tr>
<td align="center"><img src="https://raw.githubusercontent.com/OMSociety/PCL-Download-Fixer/main/docs/shots/fixed.png" alt="修复完成后的界面" width="640"></td>
</tr>
<tr>
<td align="center">修复完成：备份路径、改动处数与后续操作都写在日志里</td>
</tr>
</table>

## 快速开始

### 直接使用

1. 到 [Releases](https://github.com/OMSociety/PCL-Download-Fixer/releases/latest) 下载 `PCL 下载修复器.exe`。
2. **先完全退出 PCL**（右键任务栏托盘里的 PCL 图标退出，只关窗口不算）。
3. 双击运行修复器。它通常会自动找到 PCL；没找到就点「浏览」选中 `Plain Craft Launcher 2.exe`，或直接把 exe 拖进窗口。
4. 点「开始修复」，等状态栏变成「修复完成」。
5. 重新启动 PCL，进入「下载」页即可。

> **提示：** 如果你的 PCL 装在 `C:\Program Files` 一类需要管理员权限的位置，请右键修复器选择「以管理员身份运行」，否则写不进原文件。

> **提示：** 官方发布的 PCL 本身没有数字签名，Windows SmartScreen 可能对修复器弹出提醒。程序不联网、不改系统设置，源码就在本仓库，可自行编译核对。

### 从源码构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```bash
git clone https://github.com/OMSociety/PCL-Download-Fixer.git
cd PCL-Download-Fixer

dotnet publish src/Fixer.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o dist
```

产物是 `dist/PCL 下载修复器.exe`，单个文件，约 69 MB。

`assets/app.ico` 由 `tools/` 下的 MakeIco 工具从 `assets/logo.png` 生成，仅在更换图标时需要重新运行。

## 它在文件里改了什么

改动全部落在 IL 层面，用 [dnlib](https://github.com/0xd4d/dnlib) 直接读写元数据，不依赖源码、不重新编译 PCL。

PCL 里控制下载入口的开关叫 `ModSecret.RestrictMcDownload`，声明为 `Public Const Boolean = True`。因为是 `Const`，值在编译时就被内联进每一个引用点，程序里那个字段早已不起作用；真正拦住下载的是五处被展开成硬编码 `throw` 的守卫。所以改字段值无效，必须逐个改调用点。

本工具改三处：

| 位置 | 改动 | 效果 |
|---|---|---|
| `PageDownloadInstall.LoaderInit` | 把硬编码的空回调换成真实的 `ModDownload.DlClientListLoader` | 下载页恢复拉取 Minecraft 版本清单 |
| 下载页的默认页签 lambda | 清空方法体，直接返回 | 默认页签不再被强制指向「关闭下载」的提示页 |
| 资源详情页的版本卡片 | 把隐藏卡片的判断改为显示 | 资源详情页的 MC 版本卡片恢复显示 |

改动前会逐个确认目标特征是否匹配得上；任何一处认不出来就整体放弃，不会写出半成品。目标文件已被改过时直接判定为「无需修复」。

**整合包本地导入不在修复范围内。** 该版本把整合包安装的分发代码整段编译掉了，方法体里只剩一条抛出语句，并且紧挨 `try` 块的 `finally`，无法用等长的字节替换恢复。日志里会明确写出这一条。

**版本兼容性。** 三处特征都按 2.99.0.0 的具体 IL 形态定位。其他版本的 PCL 若特征对不上，工具会拒绝修改并说明，不会盲改。

## 常见问题

**修复器说「PCL 正在运行」怎么办？**
PCL 启动后会独占自己的 exe 文件。请从托盘图标彻底退出 PCL —— 只点窗口右上角的关闭按钮通常只是最小化到托盘 —— 再点一次「开始修复」。

**修复后 PCL 打不开了，怎么还原？**
把 PCL 目录里的 `Plain Craft Launcher 2.exe.bak` 改名回 `Plain Craft Launcher 2.exe`，覆盖现有的 exe 即可。备份是原始文件的完整副本。

**重启 PCL 后下载页还是空的？**
先确认修复器日志里是「修复完成（3 处）」而不是「无需修复」或错误提示。若三处都成功但页面仍不正常，说明你的 PCL 版本与 2.99.0.0 不同，请提 [Issue](https://github.com/OMSociety/PCL-Download-Fixer/issues) 并附上日志。

**PCL 更新后需要重新修复吗？**
需要。更新会覆盖 exe，把改动全部冲掉，更新完再跑一次修复器即可。

**修复器会收集信息或联网吗？**
不会。程序没有任何网络请求，不读写注册表，唯一改动的文件就是你指定的那个 PCL 主程序及其 `.bak` 备份。

## 许可

本仓库以 [MIT 许可证](LICENSE) 发布。

本工具不包含、不重分发 PCL 的任何代码或二进制文件，只是在用户本机上就地修改用户自己那份 PCL 主程序。PCL 本身遵循其自身的《PCL 分发有限许可》。
