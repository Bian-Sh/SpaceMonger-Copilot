# 2026-06-26 Git 仓库转正整理

## 背景

用户希望将当前项目从“奇奇怪怪的 fork 状态”整理为正式自有项目，原始仓库仅作为致敬和参考引用。

## 已执行调整

- 将个人仓库 `https://github.com/Bian-Sh/SpaceMonger-Copilot.git` 从远端名 `fork` 调整为标准远端名 `origin`。
- 将原始作者仓库 `https://github.com/mrkozma/spacemonger-next.git` 从远端名 `origin` 调整为 `tribute`，语义上仅保留为致敬/参考来源。
- 禁用了 `tribute` 的 push URL，避免误向原始仓库推送。
- 将本地分支 `codex/anthropic-baseurl-localization-fixes` 重命名为 `main`。
- 将本地 `main` 推送到个人仓库，并设置追踪 `origin/main`。
- 将 GitHub 仓库默认分支切换为 `main`。
- 删除个人仓库远端旧分支 `master`。
- 删除个人仓库远端临时分支 `codex/anthropic-baseurl-localization-fixes`。
- 更新并确认 `origin/HEAD` 指向 `origin/main`。

## 当前状态

- `origin`：个人正式仓库 `https://github.com/Bian-Sh/SpaceMonger-Copilot.git`
- `tribute`：原始致敬引用仓库 `https://github.com/mrkozma/spacemonger-next.git`
- 当前本地分支：`main`
- 当前上游追踪：`origin/main`
- CodeGraph 索引已存在且为最新状态。

## 仍需注意

GitHub API 仍显示 `Bian-Sh/SpaceMonger-Copilot` 的 `isFork=true`，说明它在 GitHub 平台层面仍被标记为 fork。Git 命令无法直接把一个 GitHub fork 原地转换为普通仓库。

若要彻底去掉 GitHub 的 fork 标记，通常需要二选一：

1. 在 GitHub 支持页面请求 detach fork network。
2. 新建一个空仓库，然后把当前仓库作为普通仓库完整推送过去，再按需替换仓库名。

本次已完成本地和 Git 远端层面的“转正”：后续日常开发、推送、默认分支均使用 `origin/main`，原始仓库只作为 `tribute` 参考。


## 2026-06-26 仓库改名补充

仓库正式名称已调整为 `SpaceMonger-Copilot`，对应产品名 `SpaceMonger Copilot`。`origin` 已同步到新仓库 URL，`tribute` 继续指向原始仓库作为致敬引用。
