# TreeView WizTree 风格表格迭代纪要

日期：2026-06-20  
主题：TreeView 对齐 WizTree 的树表格展示

## 本轮完成

- TreeView 表头扩展为 WizTree 风格列：文件夹、父级百分比、大小、分配、项目、文件、文件夹、修改时间、属性。
- 新增父级百分比进度条效果，显示当前节点相对于父节点的占比。
- TreeView 条目新增统计字段：项目总数、文件数、文件夹数。
- 新增属性列，尽量对齐 Windows/WizTree 风格属性标记：`R` 只读、`H` 隐藏、`S` 系统、`A` 存档、`C` 压缩、`E` 加密、`L` 重解析点，并保留访问拒绝/云占位提示。
- 重做 TreeViewItem 模板：去掉默认 WPF 缩进对后续列的影响，只在首列内部做树形缩进。
- 新增首列 foldout 按钮和缩进参考线，避免整行 cell 右移导致列与 Header 不对齐。

## 当前限制

- `分配` 暂时沿用扫描树的 `Size`，因为当前 `FileEntry` 没有独立 allocated size 字段。
- 属性列会在创建可见 VM 时读取文件属性；访问失败会降级使用扫描阶段已有标记。
- 由于工作区已有未提交的 `MainWindow.xaml` / `VersionDisplay` 重名改动，当前 App 构建被该既有问题阻塞，本轮 TreeView 文件已完成但未能跑完整 App 构建。

## 追加修复：TreeView 点击崩溃

- 修复 `TreeViewItem_Selected`：点击条目时不再调用 `SelectEntry` 做递归查找和反向设置选择，改为直接记录当前 `TreeViewItemViewModel`，避免选择事件重入。
- 修复 TreeView 右键选择：右键时同样直接记录当前 VM，不再触发递归选择链。
- 移除旧的真实子项 dummy 懒加载占位，避免未展开时点击占位项或递归预加载整棵树。现在 foldout 按钮由 `HasChildren` 控制，展开时再加载真实 children。
