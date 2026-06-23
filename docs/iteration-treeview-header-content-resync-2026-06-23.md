# TreeView 表头与内容列最右端同步修复纪要

时间：2026-06-23

## 背景

用户反馈 TreeView 的 Header 与内容列再次不同步，尤其横向滚动条拖到最右端时，Header splitter 与内容列分割线出现错位。上一次专门修复该问题的提交为 `40705054191d89ff1c2295b9e395a9b357594ed9`。

## 调试过程

- 先临时把 Header splitter 线加粗为红色，把内容列分割线加粗为青色，用发布包实际扫描项目目录后做视觉对照。
- 复现关键条件是横向滚动条拖到最右端；普通位置不容易看出错位。
- 红/青粗线确认：最右端时 Header 末端没有扣除 TreeView 右侧垂直滚动条占用宽度，导致 Header 可视区域比内容可视区域多出一段，末端列线错位。
- 同时确认 Header splitter 模板原先用 `Margin="0,0,-2,0"` 和居中线条，线本身不稳定贴合列右边界。

## 修复

- Header splitter 的视觉线改为 `HorizontalAlignment="Right"`，并移除负 `Margin`，让分割线固定画在当前列右边界。
- TreeView 内容滚动同步时，根据 `treeSv.ActualWidth - treeSv.ViewportWidth` 计算右侧 viewport inset；垂直滚动条可见时，把同等右侧 margin 应用到 HeaderScrollViewer。
- Header 横向偏移继续按 TreeView `HorizontalOffset` 的真实像素同步，避免比例映射在末端放大误差。
- 保留 splitter 拖拽过程中和拖拽结束后的同步触发。

## 验证

- `dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o outputs/spacemonger-next-treeview-final-20260623-091137`：通过，只有既有警告。
- 启动最终发布包，输入 `D:\AppData\Visual Studio\Projects\spacemonger-next` 扫描，切换到“树形列表”。
- 将底部横向滚动条拖到最右端后，Header 与内容列末端对齐，未再出现右侧垂直滚动条宽度导致的尾部错位。

## 输出

- 最终发布目录：`outputs/spacemonger-next-treeview-final-20260623-091137`

## 右上角色块修正

- 现象：Header 为了扣除 TreeView 垂直滚动条宽度后，右上角露出一块与表头不一致的背景色。
- 修复：不再用 `HeaderScrollViewer.Margin.Right` 直接缩进；改为在 Header 外层 Grid 增加 `HeaderScrollbarGutterColumn` 占位列，并用同一套 `VP.SurfaceHoverBrush` / `VP.BorderLightBrush` 绘制占位背景和边框。
- 验证：发布 `outputs/spacemonger-next-treeview-gutter-20260623-091744` 后扫描项目目录并拖到最右侧，右上角占位区已与表头视觉一致。
