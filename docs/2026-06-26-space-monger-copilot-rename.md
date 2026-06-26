# 2026-06-26 SpaceMonger Copilot 改名纪要

## 目标
- 按用户要求将用户可见的 Next 版本命名切换为 Copilot 版本。

## 变更
- 标题栏、关于页、资源字符串改为 `SpaceMonger Copilot`。
- 崩溃日志、LLM/分析诊断、更新缓存等本地应用目录改为 `SpaceMonger Copilot`。
- 更新检查 User-Agent 改为 `SpaceMongerCopilot-UpdateChecker`。
- `SpaceMonger.App.csproj` 增加 `AssemblyTitle`、`Product`、`Description` 元数据。
- AI 身份回答同步为 `SpaceMonger Copilot`。

## 说明
- 未改 C# namespace、项目名、程序集文件名，避免对构建、引用、历史测试和发布脚本造成大范围破坏。


## 2026-06-26 仓库正式命名补充

- 产品正式名称确定为 `SpaceMonger Copilot`。
- GitHub 仓库已从 `Bian-Sh/spacemonger-next` 重命名为 `Bian-Sh/SpaceMonger-Copilot`。
- 本地 `origin` 已更新为 `https://github.com/Bian-Sh/SpaceMonger-Copilot.git`。
- 原始仓库仍保留为 `tribute`，地址为 `https://github.com/mrkozma/spacemonger-next.git`，且 push 被禁用。
- README、关于页 GitHub 链接、更新检查仓库名已同步到新名称。
- 命名空间、解决方案文件名、项目文件名暂不整体重命名，避免扩大变更面；它们属于内部工程标识，不影响产品正式名称。

## 验证与发布

- 已执行 `npx @colbymchenry/codegraph sync`，CodeGraph 同步完成。
- 已执行 `dotnet build src\SpaceMonger.sln -c Release`，构建通过；存在既有 NU1701、nullable 与重复资源名警告。
- 已发布 WPF 包到 `outputs\2026-06-26_164709`。

## README 乱码修复

- 修复 README 中历史遗留的 mojibake：`鈥?` 恢复为 em dash `—`。
- 修复 Project Structure 目录树中的 `鈹...` 乱码，恢复为 `├──`、`│`、`└──` 等标准 box-drawing 字符。
- 本次只处理 `README.md` 文档乱码，不修改代码逻辑。
