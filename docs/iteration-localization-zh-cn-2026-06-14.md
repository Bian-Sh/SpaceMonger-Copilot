# 迭代纪要：中文本地化与同步校验

日期：2026-06-14

## 目标

为 SpaceMonger 落地可维护的中文翻译方案，并确保后续从上游拉取更新后可以快速发现新增未翻译文案。

## 方案

- 英文源文统一放在 `src/SpaceMonger.App/Localization/Strings.resx`。
- 简体中文翻译放在 `src/SpaceMonger.App/Localization/Strings.zh-CN.resx`。
- XAML 通过 `loc:Loc` markup extension 引用资源键。
- C# 代码通过 `L.Text(...)` 和 `L.Format(...)` 引用资源键。
- 可通过 `SPACEMONGER_LANGUAGE=zh-CN` 强制中文界面；未设置时跟随系统 UI culture。

## 同步流程

每次从源头拉取更新后运行：

```powershell
python .\scripts\sync-localization.py --check
```

该脚本会检查：

- `Strings.resx` 与 `Strings.zh-CN.resx` 的 key 是否一致。
- `SpaceMonger.App` 中是否出现明显的硬编码 UI 字符串。

如果脚本报出新增文案，应先把英文源文加入 `Strings.resx`，再补 `Strings.zh-CN.resx`，最后把代码或 XAML 改为资源键引用。

## 验证

- `python .\scripts\sync-localization.py --check` 通过。
- 本次后续执行 `dotnet build .\src\SpaceMonger.sln -c Release` 做编译验证。


## 后续修复

- Treemap 可用空间块的 SkiaSharp 绘制不再使用中文标签，避免默认字体缺少中文字形时显示方块；块内仅显示容量数字。
- Anthropic 非流式响应解析兼容 OpenAI-compatible proxy 的 `choices[0].message.content` / `choices[0].text` 返回格式，避免右侧流式聊天正常但“推荐清理”非流式分析报 `Unexpected response format`。
