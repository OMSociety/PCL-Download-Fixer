# Changelog

本项目的所有重要变更都记录在此文件中。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循[语义化版本](https://semver.org/lang/zh-CN/)。

## [1.0.0] - 2026-10-20

首个版本。

### 新增

- 图形界面：选择或拖入 PCL 主程序后一键修复，自动备份原文件为 `.bak`。
- 覆盖 PCL 2.99.0.0 的三处下载入口拦截，恢复下载页的 Minecraft 版本清单拉取与资源详情页的版本卡片。
- 启动时自动在常见位置查找 `Plain Craft Launcher 2.exe`。
- 修改前探测文件占用，PCL 正在运行时给出明确提示而非写入失败。
- 目标特征与预期不符时拒绝修改，不产生部分写入的文件。
- 深色、浅色两套界面，跟随 Windows 应用主题。
- 单文件自包含发布，无需预装 .NET 运行时。
