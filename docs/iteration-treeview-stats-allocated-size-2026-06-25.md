# 迭代纪要：TreeView 统计与分配大小修复（2026-06-25）

## 背景

用户指出 TreeView 中“分配”“项目”“文件”“文件夹”列数据不对，部分数据没有统计。

## 变更

- `FileEntry` 新增 `AllocatedSize`，用于记录文件实际磁盘分配大小，并在目录重算时汇总子项分配大小。
- 普通扫描、MFT 快速扫描后的并行尺寸收集、增量扫描的新建/修改路径都写入 `AllocatedSize`。
- Windows 下通过 `GetCompressedFileSizeW` 获取文件实际分配大小；失败或非 Windows 环境回退到逻辑大小。
- TreeView 的“分配”列改为显示 `AllocatedSize`，无值时回退 `Size`。
- TreeView 的“项目”“文件”“文件夹”列改为使用扫描阶段已有的子树统计；对于旧数据或测试构造数据，保留递归兜底统计。
- 新增 App 层单元测试，覆盖子树项目/文件/文件夹计数和分配大小展示。

## 验证

- `dotnet test tests\SpaceMonger.App.Tests\SpaceMonger.App.Tests.csproj -c Release` 通过，4 个测试通过。
- `dotnet build src\SpaceMonger.sln -c Release` 通过。
- `dotnet publish src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -o outputs\spacemonger-treeview-stats-20260625-202430` 通过。

## 输出

- 发布包：`outputs\spacemonger-treeview-stats-20260625-202430\SpaceMonger.App.exe`

## 备注

- 构建仍保留项目既有警告：`NU1701` 包兼容警告、`NoItemsSelectedForCleanupToolTip` 重复资源名警告，以及部分 nullable 警告。
