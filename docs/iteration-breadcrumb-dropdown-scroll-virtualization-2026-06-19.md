# 面包屑 "›" 下拉菜单优化：滚动 + 虚拟化（对标 Windows 11）

**日期**: 2026-06-19  
**问题**: 点击面包屑路径上的 "›" chevron，弹出的 ContextMenu 条目过多时：
1. 铺满全屏，超出屏幕部分无法滚动
2. 文件夹子项多时（>100 个）卡顿明显

## 根因分析

`RebuildBreadcrumbBar()` 为每个 "›" separator 和尾部 chevron 动态创建 `ContextMenu`，`BreadcrumbDropdown_Opened` 中用 `menu.Items.Add(MenuItem)` 逐个填充条目。

三个问题叠加：
- **ContextMenu 模板**用普通 `StackPanel` 做 ItemsHost，无 `ScrollViewer` 包裹，溢出直接裁剪
- **无高度约束**，所有条目撑到 ContextMenu 自然高度，超出屏幕不可达
- **逐个创建 MenuItem 控件**（每个 MenuItem 有完整 Border+Grid+ContentPresenter 视觉树），大量条目时同步创建数千个 WPF 对象导致明显卡顿

## 修改方案

### 1. 模板层：滚动支持（`VisionProTheme.xaml`）

ContextMenu ControlTemplate 的 ItemsHost 从 `StackPanel` 改为 `VirtualizingStackPanel`，外裹 `ScrollViewer`：

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto"
              CanContentScroll="True"
              MaxHeight="{TemplateBinding MaxHeight}">
    <VirtualizingStackPanel IsItemsHost="True"
                            Grid.IsSharedSizeScope="True"
                            KeyboardNavigation.DirectionalNavigation="Cycle" />
</ScrollViewer>
```

- `CanContentScroll="True"` 启用逻辑滚动（逐条目而非逐像素）
- `MaxHeight` 通过 TemplateBinding 由代码端动态设置

### 2. 代码层：高度约束（`MainWindow.xaml.cs`）

```csharp
double itemHeight = 32;
double maxItems = 12;
double screenMax = System.Windows.SystemParameters.WorkArea.Height * 0.7;
menu.MaxHeight = Math.Min(itemHeight * maxItems, screenMax);
```

约 12 条可见，上限为屏幕工作区 70%。

### 3. 性能层：ItemsSource + 虚拟化

原实现：逐个 `menu.Items.Add(new MenuItem { ... })`，同步创建全部 UI 控件。

新实现：
- 定义 `BreadcrumbItem(string Name, string Path)` 轻量数据记录
- 改用 `menu.ItemsSource = List<BreadcrumbItem>`，配合 `VirtualizingStackPanel` 只渲染可见区域的 MenuItem 容器
- `EnsureBreadcrumbMenuTemplate()` 一次性设置 ItemTemplate + ItemContainerStyle（hover 高亮、disabled 态、click 导航）
- Click 路由通过 `EventSetter` 绑定到 `NavigatToPathOrSelect`

## 涉及文件

| 文件 | 改动 |
|---|---|
| `src/SpaceMonger.App/Themes/VisionProTheme.xaml` | ContextMenu 模板：StackPanel → ScrollViewer + VirtualizingStackPanel |
| `src/SpaceMonger.App/MainWindow.xaml.cs` | 新增 `BreadcrumbItem` record；重写 `BreadcrumbDropdown_Opened` 用 ItemsSource；新增 `EnsureBreadcrumbMenuTemplate`、`s_breadcrumbItemsPanel` |

## 验证

- `dotnet build src/SpaceMonger.sln --no-restore` → 0 errors
- TODO：实际运行，打开含大量子文件夹的目录，点击面包屑 "›"，确认下拉可滚动且不卡

## 已知局限

- 其他 ContextMenu（如 ConsoleFilter）也受模板变更影响（共用 VirtualizingStackPanel），但 `Items.Add` 方式不会触发虚拟化，行为不变
- Empty state 通过 `DataTrigger` 将占位项设为 `IsEnabled=false`
- 虚拟化依赖 `ItemsSource` + `DataTemplate`，后续如需恢复 `Items.Add` 模式需调整