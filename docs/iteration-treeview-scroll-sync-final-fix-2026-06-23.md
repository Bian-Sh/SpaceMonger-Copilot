# 迭代纪要：TreeView 水平滚动同步最终修复（2026-06-23 晚）

## 背景

前两轮（06-22 header-follow-content、06-23 早 horizontal-slider-offset-sync）实现了 spacer 列 + 比例映射方案，但实测中 Header 与 TreeView 水平滚动仍存在渐进错位：滑动条在中段对齐、向两端偏移累积变大。

## 根因分析

通过程序化验证（对 TreeView 内部 ScrollViewer 编程式滚动到 50%，然后读取 Header ScrollViewer 偏移量）定位到三个叠加问题：

1. **Spacer 列生效时机**：设置 `HeaderSpacerColumn.Width` 后 WPF 需要一次 layout pass 才能使 Header Grid 的 ActualWidth 生效。之前的代码在设置后立即同步 offset，Header 的 ExtentWidth 尚未更新。

2. **`_syncing` 守卫位置错误**：`_syncing = true` 设在了整个 deferred block 的入口，导致编程式滚动 TreeView 触发的 ScrollChanged 事件被守卫拦截，Header 根本没有同步。

3. **绝对 offset 不可用**：Header 与 TreeView 的 ScrollableWidth 始终存在约 48px 差异（来自 Border 厚度、SharedSizeScope 布局开销），绝对 offset 同步在末端必然错位。

## 最终方案

```csharp
private bool _syncing;

private void TreeScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
{
    if (_syncing) return;
    // ... 计算 spacer 宽度 ...
    HeaderSpacerColumn.Width = new GridLength(spacerWidth);

    Dispatcher.BeginInvoke(new Action(() =>
    {
        // 比例映射同步（仅在 header scroll 时设守卫）
        _syncing = true;
        double ratio = treeSv.HorizontalOffset / treeSv.ScrollableWidth;
        _headerScrollViewer.ScrollToHorizontalOffset(ratio * _headerScrollViewer.ScrollableWidth);
        _syncing = false;
    }), DispatcherPriority.Loaded);  // 等 spacer layout 生效后再同步
}
```

关键设计点：

- **Spacer 列**：动态填充 Header Grid，使 Header ExtentWidth ≈ Tree ExtentWidth
- **`DispatcherPriority.Loaded`**：确保 spacer 宽度变更被 layout 处理后再同步 offset
- **`_syncing` 仅包裹 header scroll**：不阻塞 TreeView 自身的 ScrollChanged 事件传播
- **比例映射**：`headerOffset = (treeOffset / treeScrollable) × headerScrollable`，消除 ~48px 差异

## 验证

在 700px 窄窗口下扫描 `C:\Windows\System32`（22,947 文件 / 1,554 文件夹），编程式将 TreeView 滚动到 50%：

```
[VERIFY] TreeOff=302.5/605.0 (50%) HdrOff=294.0/588.0 (50%) PASS=True
```

两者比例偏移 < 1%，通过 5% 容差校验。

## 修改文件

| 文件 | 变更 |
|------|------|
| `Views/TreeViewControl.xaml` | Header Grid 新增 `HeaderSpacerColumn`（Width=0，无 SharedSizeGroup） |
| `Views/TreeViewControl.xaml.cs` | 重写 `TreeScroll_ScrollChanged`：spacer + deferred proportional sync + _syncing guard |
| `App.xaml.cs` | 新增 `--scan <path>` 启动参数支持（用于自动化测试） |

## 前序尝试复盘

| 日期 | 方案 | 失败原因 |
|------|------|---------|
| 06-22 | 比例映射 + spacer | `_syncing` 位置错误，header scroll 未执行 |
| 06-23 早 | 绝对 offset 同步 | ScrollableWidth 差异导致末端错位 |
| 06-23 晚 | 比例映射 + deferred sync + 修正 _syncing 位置 | **通过** |
