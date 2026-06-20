# TreeView 扫描末尾崩溃修复迭代纪要（2026-06-21）

## 背景

用户反馈在扫描快结束时偶发崩溃，完成分析后点击 TreeView 必定崩溃。排查重点集中在近期 WizTree 风格 TreeView 改造后的 ViewModel 构建路径。

## 本次修复

- 将 TreeView 的子树统计从递归 `GetStats` 改为扫描后一次性迭代填充缓存，避免大目录或深目录在 UI 构造时触发栈溢出。
- 迭代统计增加引用去重，防止异常树结构导致重复遍历或死循环。
- `TreeViewItemViewModel` 的统计 fallback 不再递归进入子节点，确保未预填缓存时也不会引发深递归。
- `SelectEntry` 改为沿 `FileEntry.Parent` 父链逐级定位并按需加载 TreeView 子节点，避免递归搜索尚未加载的 UI 树。

## 影响范围

- 主要影响 `TreeViewModel` 的 TreeView 构建、展开和右键“询问 AI”定位行为。
- 不改变扫描器的磁盘扫描逻辑，不重新扫描磁盘。
- 不改变 TreeView 现有列、占比进度条、属性列、右键菜单语义。

## 当前限制

- TreeView 首次绑定仍会为整棵已扫描树计算一次统计缓存；这是非递归实现，已规避栈溢出，但超大扫描树仍可能带来短时间 UI 开销。
- 属性列仍只在可见/已加载节点 ViewModel 构造时读取系统属性；后续可考虑把文件属性纳入扫描结果，彻底避免 UI 层再次访问磁盘。

## 验证

- `dotnet build src\\SpaceMonger.sln -v:minimal` 通过，保留既有 NuGet 兼容性警告和 nullable 警告。
- `dotnet test tests\\SpaceMonger.App.Tests\\SpaceMonger.App.Tests.csproj --no-build -v:minimal` 通过。
- `dotnet test tests\\SpaceMonger.Core.Tests\\SpaceMonger.Core.Tests.csproj --no-build -v:minimal` 通过。

## 追加修复：TreeView 不再二次读取磁盘属性

用户明确指出 TreeView 应复用 Treemap 的扫描数据，不能因为新增属性列而再次访问磁盘。针对 `mklink -j` junction 目录场景，本次追加调整：

- `FileEntry` 新增 `Attributes` 缓存字段，用于保存扫描阶段已经拿到的 `FileAttributes`。
- 普通扫描 `FileScanner` 直接复用 `FileSystemEnumerable` 返回的属性位，不增加额外系统调用。
- MFT 快扫和 USN 增量变更路径也写入属性位，确保 TreeView 后续显示只依赖内存树。
- TreeView 属性列移除 `File.GetAttributes(entry.Path)`，不再在 UI 构造节点时访问真实磁盘路径。

这意味着 TreeView 的属性列现在和大小、文件数、目录数一样，都来自 `ScanSession.RootEntry` / `FileEntry.Children` 的扫描结果；对于 junction、权限敏感目录或扫描后变化的路径，不会因为切换/点击 TreeView 再触发额外文件系统访问。

## 追加修复：崩溃日志与 dump 定位

为避免继续靠猜测定位 TreeView / 取消扫描崩溃，本次增加可复现取证链路：

- App 启动时注册 `DispatcherUnhandledException`、`AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`。
- 崩溃时写入 `%LOCALAPPDATA%\SpaceMonger.Next\Diagnostics\logs\crash-*.log`。
- 崩溃时尝试由应用自身写入 `%LOCALAPPDATA%\SpaceMonger.Next\Diagnostics\dumps\crash-*.dmp`。
- 同时配置当前用户 WER LocalDumps：`%LOCALAPPDATA%\SpaceMonger.Next\Diagnostics\wer-dumps`，保留 10 个 full dump。
- 扫描开始、扫描完成、取消扫描、TreeView SetRoot/Rebuild/LoadChildren 均写入日常日志 `%LOCALAPPDATA%\SpaceMonger.Next\Diagnostics\logs\spacemonger-YYYYMMDD.log`。
- 安装了 `dotnet-dump`，复现后可用 `dotnet-dump analyze <dump>` 读取 managed 堆栈。

同时保留两个防护修复：

- 取消扫描返回 `IsCancelled` session 时，不再设置 `CurrentSession`，也不再触发 `ScanCompleted` 推给 Treemap/TreeView。
- TreeView 的项目/文件/文件夹统计不再在 UI 层遍历整棵树，而是读取扫描阶段写入的 `FileEntry.Subtree*Count`。

## 追加修复：根据崩溃日志定位 TreeView XAML 资源缺失

复现后读取 `%LOCALAPPDATA%\SpaceMonger.Next\Diagnostics\logs\crash-20260621-013838-799.log`，真实异常为：

- `System.Windows.Markup.XamlParseException`
- 缺失资源：`BoolToVisibilityConverter`
- 触发位置：TreeView 模板首次 measure / render 时解析 `Visibility="{Binding ..., Converter={StaticResource BoolToVisibilityConverter}}"`

修复：

- 在 `TreeViewControl.xaml` 声明 `SpaceMonger.App.Converters` 命名空间。
- 在 `UserControl.Resources` 中补充 `<converters:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter" />`。

这次问题确认不是 junction 本身，也不是 TreeView 二次扫描磁盘；junction 只是在更真实的数据规模下暴露了 TreeView 首次渲染路径的 XAML 资源错误。
