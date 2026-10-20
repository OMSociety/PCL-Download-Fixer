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

**PCL 下载修复器**不联网、不重装、不替换你的 PCL —— 它直接改掉你电脑上那份 `Plain Craft Launcher 2.exe` 中三个被写死的判断。

整个程序是一个带操作界面的 exe，选文件、点按钮、完事。

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

1. 到 [Releases](https://github.com/OMSociety/PCL-Download-Fixer/releases/latest) 下载 `PCL-Download-Fixer-<版本>-win-x64.exe`。
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
