# 2026-06-26 Agent-First Copilot 运行验收清单

## 验收目标
确认当前版本已经从“规则先行”切换为“agent-first”，并且围绕磁盘空间管理任务具备可接受的交互体验。

## 环境准备
1. 启动发布包：`outputs/SpaceMonger-win-x64-folder-20260627-010455/SpaceMonger.App.exe`
2. 准备两种状态分别验证：
   - **状态 A：未配置 API Key**
   - **状态 B：已配置可用模型服务**
3. 准备两种扫描上下文：
   - **上下文 1：未扫描任何路径**
   - **上下文 2：已扫描一个实际目录**，例如 `D:\Downloads`

## 场景清单

### 1. 无模型保护
- 前置：状态 A + 任意上下文
- 输入：`说说你自己`
- 预期：
  - 不再出现本地关键字 fallback 式胡乱回答
  - 直接提示需要先配置模型服务/API Key
  - 不出现“请先完成扫描”这类误导提示

### 2. 身份/说明类问题不要求先扫描
- 前置：状态 B + 上下文 1
- 输入：`说说你自己`
- 预期：
  - AI 直接回答身份说明
  - 不要求先扫描
  - 不弹一级确认卡片

### 3. 模块解释类问题直接回答
- 前置：状态 B + 上下文 1
- 输入：`推荐清理是什么？`
- 预期：
  - AI 解释推荐清理模块用途
  - 不直接触发分析
  - 不出现“需要先扫描才能清理”这种生硬终止式回答

### 4. 未扫描时主动提议扫描
- 前置：状态 B + 上下文 1
- 输入：`看看 D:\Downloads 有什么可清理的`
- 预期：
  - AI 不应停在“请先完成扫描”
  - AI 应给出解释文本 + 一级确认卡片
  - 卡片意图应是扫描 `D:\Downloads`
  - 卡片内文字可选中复制

### 5. 已扫描路径内的清理分析
- 前置：状态 B + 上下文 2（已扫描 `D:\Downloads`）
- 输入：`这个文件夹有啥可清理的`
- 预期：
  - AI 应基于当前扫描上下文理解这是分析请求
  - 若需要分析，给出一级确认卡片
  - 不应再先问无意义的二层确认

### 6. 已有推荐结果时的覆盖提醒
- 前置：状态 B + 上下文 2 + 已存在推荐结果
- 输入：`重新分析这个目录的推荐清理`
- 预期：
  - AI/卡片文案应体现旧推荐会被覆盖
  - 用户确认前不执行

### 7. 路径不在扫描树内时的导航体验
- 前置：状态 B + 上下文 2（扫描的是 A 路径）
- 输入：`定位 D:\OtherFolder`
- 预期：
  - 若不在当前扫描树内，不是直接死报错
  - AI 倾向于提议先扫描该路径或解释当前限制

### 8. 卡片可复制性
- 前置：状态 B + 任何会出现卡片的场景
- 操作：鼠标拖选卡片中的 `Description / Impact / StatusText`
- 预期：
  - 文本可选中
  - 可复制到剪贴板

### 9. 多语言设置优先
- 前置：状态 B
- 操作：
  - 把 APP 语言设为 `en`
  - 用中文输入：`说说都有啥功能，我该怎么用好你呢`
- 预期：
  - AI 仍以英文回答
  - 回答面向普通用户解释模块，不暴露内部工具/函数名
  - 默认卡片状态/按钮兜底文案也为英文
- 操作：
  - 把 APP 语言设为 `zh-CN`
  - 用英文输入：`What can you do?`
- 预期：
  - AI 仍以中文回答
  - 回答和默认卡片兜底文案恢复中文

### 10. 普通功能介绍不暴露内部函数名
- 前置：状态 B + 上下文 1
- 输入：`说说都有啥功能，我该怎么用好你呢`
- 预期：
  - AI 介绍用户可见模块：扫描/路径输入、Treemap、TreeView、推荐清理、AI Chat、设置/API Key、白名单/保护路径、控制台/日志
  - 不出现 `find_by_path`、`find_by_name`、`list_children`、`summarize_subtree`、`find_large_files`、`propose_copilot_action`、`get_copilot_context` 等内部工具/函数名
  - 不要求先扫描
  - 不弹一级确认卡片

### 11. 中文盘符扫描直接给确认卡片
- 前置：状态 B + 上下文 1
- 输入：`扫描 G 盘，说说我买的游戏`
- 预期：
  - AI 直接给出“扫描 G:\”的一级确认卡片
  - 不追问“要我现在提出这个扫描建议吗？”
  - 用户确认前不执行扫描
  - 扫描完成前不声称已经分析出游戏占用

### 12. 产品名不硬翻译
- 前置：状态 B + 任意上下文
- 输入：`你是谁？`
- 预期：
  - 回答使用产品名 `SpaceMonger Copilot`
  - 不把 `SpaceMonger` 翻译成“太空漫游者”
  - 回答简短聚焦磁盘空间管理助手身份

## 当前自动化证据
- `tests/SpaceMonger.Core.Tests/AgentProposalTests.cs`
- `tests/SpaceMonger.Core.Tests/AiSkillRouterTests.cs`（覆盖普通功能介绍、中文盘符扫描、App 语言设置优先、产品名不翻译）
- `tests/SpaceMonger.App.Tests/ChatViewModelProposalTests.cs`

## 当前已知非阻断项
- 工程仍存在既有 `NU1701`、nullable 和重复资源名 warning。
- 这些 warning 不影响当前 agent-first Copilot 主流程的构建与发布验证。

## Computer Use 验证记录
- 验证时间：2026-06-27 00:59 左右。
- 验证包：`outputs/SpaceMonger-win-x64-folder-20260627-010455/SpaceMonger.App.exe`。
- 验证方式：使用 `computer-use` 连接 Windows 窗口 `SpaceMonger Copilot`，读取 UI Automation 树与窗口截图。
- 场景 9 抽验通过：App 语言设置为 `en` 后，界面切换到英文；用中文输入 `说说都有啥功能，我该怎么用好你呢`，AI 返回英文模块说明，未暴露内部函数名。
- 场景 11 初验发现问题：英文语言设置下，`扫描 G 盘，说说我买的游戏` 能直接生成确认卡片，但卡片文案仍为中文。
- 已修复并重新发布：本地确认卡片标题、描述、影响、按钮和解释文本改为跟随 App 当前语言。
- 场景 11 复验通过：英文语言设置下，`扫描 G 盘，说说我买的游戏` 生成英文确认卡片，包含 `Scan this path`、`Start Scan`、`Cancel`，且不会追问是否创建卡片。
- 用户截图追加反馈：点击 `Start Scan` 后，卡片执行状态仍出现中文“扫描完成，已刷新 TreeView、Treemap 和聊天上下文。”。
- 已修复并重新发布：Copilot 动作执行结果 `AiActionResult.Message/Details` 改为跟随 App 当前语言。
- 场景 11 二次复验通过：英文语言设置下点击 `Start Scan` 后，状态显示 `Scan complete. TreeView, Treemap, and chat context have been refreshed.` 和 `Calculating sizes... — 10,105 files, 594 folders`，未再出现中文状态文本。

## 2026-06-27 ???????????????

- ????`outputs/SpaceMonger-win-x64-folder-20260627-012434/SpaceMonger.App.exe`
- ??????? `??` ?? WPF ??App ???? `en`????????`?? G ?????????`?
- ????????? `Start Scan` ????????????????? Copilot ??????????????????????????
- ?????????????????????????????????????? `SteamLibrary/steamapps/common` ???????????????????????
- ?????????????????????????????????? `AiInteractionCard.FollowUpPrompt`?`ContinueAfterConfirmedScanAsync` ? `BuildPostScanFollowUpPrompt` ?????????????
