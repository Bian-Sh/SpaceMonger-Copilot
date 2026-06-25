# 迭代纪要：TreeView Header 多列排序与指示器（2026-06-25）

## 背景

用户指出 TreeView 的 header 排序功能不合理：整个 header 只响应一个文件夹的排序，且排序状态缺乏视觉反馈。

## 变更

- `TreeViewModel` 新增 `SortColumn` 属性，记录当前排序列名；初始值为空字符串，表示未排序状态。
- 为所有可排序列（文件夹、父级百分比、大小、分配、项目、文件、文件夹、修改时间）新增独立的排序命令，替换原先共用命令的逻辑。
- 排序逻辑统一：点击已排序列切换升降序，点击其他列则切换排序目标并默认降序（名称列默认升序）。
- `GetSortedChildren` 方法扩展，支持按分配大小、项目数、文件数、子文件夹数排序。
- Header 中所有可排序列改为 `Button`，并在列名左侧添加三角形指示器：`▲` 表示升序，`▼` 表示降序；未排序列不显示任何指示器。
- 使用 `MultiDataTrigger` 绑定 `SortColumn` 与 `SortDescending` 控制三角形可见性。

## 验证

- `dotnet build src/SpaceMonger.sln` 通过，无新增错误。
- `dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o outputs/exe` 通过。
- 运行应用，点击各列 header 验证排序切换与三角形显示正常。

## 输出

- 单文件发布包：`outputs/exe/SpaceMonger.App.exe`

## 备注

- 仍保留项目既有警告（`NU1701`、`NoItemsSelectedForCleanupToolTip`、部分 nullable 警告）。
