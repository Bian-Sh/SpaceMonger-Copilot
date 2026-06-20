# 2026-06-19 应用内模态布局细化

## 目标

统一应用内模态弹窗视觉结构，确保弹窗包含清晰的 `title`、`content`、`button group` 三段，并让按钮组在其区域内垂直居中，同时增强面板层次感。

## 改动

- `AppModalHost` 面板改为无内边距宿主，由内部内容控制 title/content/button group 间距。
- `AppModalHost` 卡片增加柔和 `DropShadowEffect`，提升遮罩上的浮层感。
- 消息弹窗拆分为 title、content、button group 三段布局。
- 清理确认弹窗与清理总结弹窗同步改为三段式布局。
- Treemap 属性弹窗从按钮嵌入内容 Grid 改为独立 button group。

## 验证

- `dotnet build src\SpaceMonger.App\SpaceMonger.App.csproj -c Release` 成功。
- 已发布到 `outputs\SpaceMonger-win-x64-folder-20260619-165402`。
