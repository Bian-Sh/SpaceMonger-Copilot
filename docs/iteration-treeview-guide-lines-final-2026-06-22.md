# TreeView 连接线最终修复迭代纪要（2026-06-22）

## 本轮问题

用户指出前一版已经接近正确，但仍有两个关键问题：

1. 深度为 2 的文件夹没有缩进，后续层级都少缩进一级。
2. 无 foldout 三角按钮的 cell 横线长度合适，但有 foldout 三角按钮的 cell 横线明显过长。

## 修复方式

- 在远端已提交的 Header/content 水平滚动同步修复基础上继续修改，未覆盖该同步逻辑。
- 新增 `TreeGuideControl`，将祖先竖线、当前分支竖线、当前横线放在一个绘制坐标系内。
- 将视觉定位拆成两类：
  - `TreeGuideSegments` 只负责祖先竖线是否延续。
  - `Depth` 负责三角按钮和图标的真实缩进位置。
- 对有子节点的行，横线只画到当前缩进槽末端，即 foldout 三角槽之前。
- 对无子节点的行，横线继续延伸到图标前，保持用户认可的长度。
- 调试阶段使用加粗彩色线识别几何，最终恢复为白色细线。

## 验证

- 使用 `C:\tmp\sm-nav-path-test` 构造测试树：`A/A1/A1Leaf`、`file-one.txt`、`file-two.txt`、`A2`、`B/b-file.txt`。
- 通过 PC 控制运行 exe，切换 TreeView，展开 `A` 与 `A1` 后视觉确认：
  - `A1` 相对 `A` 正确右移一级；`A1Leaf` 与文件继续顺延缩进。
  - 有 foldout 的 `A`、`A1` 横线不再穿过三角区域。
  - 无 foldout 的文件/叶子文件夹横线仍延伸到图标前。
  - 最终版本已恢复白色细线，没有调试色残留。

## 构建

- `dotnet build src/SpaceMonger.App/SpaceMonger.App.csproj -c Release` 通过。
- 已发布 win-x64 framework-dependent 包到 `outputs`。
