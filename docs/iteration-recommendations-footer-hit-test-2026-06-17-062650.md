# 迭代纪要：推荐面板底部遮挡与命中修复（2026-06-17 06-26）

## 背景

用户截图中红框标出的推荐面板底部空白区域错误向外延展，视觉上遮挡更多 cell，并且即便没有明显遮挡时也会拦截鼠标点击。该问题类似之前修过的“透明区域仍参与 hit test”的布局异常。

## 改动

- `src/SpaceMonger.App/Views/RecommendationsPanel.xaml`
  - 将分析 overlay 和空/错误 overlay 限制在内容行 `Grid.Row=1`，不再 `Grid.RowSpan=2` 覆盖底部 summary bar。
  - 移除 `RecommendationsList` 底部 `Padding=0,0,0,36`，避免列表在底部制造额外空白命中区域。

## 预期效果

- 推荐列表底部不再向外延展遮挡下方区域。
- 底部空白区域不再异常拦截 Treemap/cell 鼠标点击。
- 清理按钮和底部统计栏仍保持独立可点击。

## 验证

- `dotnet build src\SpaceMonger.sln -c Debug` 通过，`0` errors。
- 已发布新的 Windows x64 folder publish：
  - `outputs\SpaceMonger-win-x64-folder-20260617-062650`
  - `outputs\SpaceMonger-win-x64-folder-20260617-062650\SpaceMonger.App.exe`
- 仍存在既有 `NU1701` 兼容性警告。
