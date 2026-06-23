# 迭代纪要：剔除树状图中"可用空间"的绘制

**日期**：2026-06-23

## 背景
原设计中，在全盘扫描时 Treemap 会注入一个合成"Free Space"节点，在布局引擎中为可用空间分配比例块，以匹配经典 SpaceMonger 的行为。这在树状图（Treemap）中不合理——树状图应只展示实际占用的文件/文件夹空间，可用空间信息在标题栏（FreeLabel）已有呈现。

## 改动内容

### 1. TreemapViewModel.cs
- 移除 `FreeSpaceSentinel` 静态合成字段
- 简化 `RecomputeLayoutForCurrentRoot()` 方法，移除整个可用空间注入逻辑（原约 45 行代码），直接调用 `_layoutEngine.ComputeLayout()`

### 2. Strings.resx / Strings.zh-CN.resx
- 移除不再使用的本地化键：`FreeSpaceName`、`FreeSpacePath`、`FreeSpaceWithSize`

## 影响范围
- 树状图视图：全盘扫描时将不再显示"Free Space"方块
- 标题栏的"可用"信息不受影响（`FreeLabel` 保留）
- 编译通过，0 错误
