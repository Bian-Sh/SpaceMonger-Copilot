# 2026-06-26 App Guide Skill 来源对齐纪要

## 背景
用户指出一个关键问题：当前 Copilot 虽然已经引入 `skills/app-guide/SKILL.md` 与 `skills/disk-management/SKILL.md`，但并不能直接证明 AI 回答来自这些 skill 文件，因为 `AiSkillRouter` 内仍保留了较多硬编码说明文本。

## 本轮调整
- 新增 `ISkillPromptProvider` 与 `FileSkillPromptProvider`。
- 运行时优先从 `skills` 目录读取 `SKILL.md` 内容作为 active skill prompt。
- `AiSkillRouter` 改为通过 provider 构建 `AiSkill`，不再把 `app-guide` / `disk-management` 的主说明写死在 router 常量里。
- 新增 `GetSkillSource(string skillId)`，便于测试与调试时直接验证当前 skill 文本来源。
- 同步修正 `AgentRuntime` 身份提示词为 `SpaceMonger Copilot`，避免继续出现旧的 `disk space analysis assistant` 品牌残留。

## 当前可验证方式
1. 打开 skill 文件：
   - `D:\AppData\Visual Studio\Projects\spacemonger-next\skills\app-guide\SKILL.md`
   - `D:\AppData\Visual Studio\Projects\spacemonger-next\skills\disk-management\SKILL.md`
2. 运行单元测试，确认 `AiSkillRouter` 能读取 `app-guide` 原文。
3. 修改 `app-guide` 中某个独特措辞后重启应用，再问对应模块问题；若回答风格/提示词行为随之变化，即说明已接到 skill 文件而非硬编码副本。

## 仍保留的最小本地兜底
- 无模型时的身份/模块说明本地答复仍保留“最小可用”兜底，用于避免完全无响应。
- 但模型侧 active skill prompt 已改为优先从 `skills` 文件加载。

## 结论
这次调整后，`skills` 不再只是“随包附带的文档”，而是已经进入 Copilot 的实际 prompt 组装链路。
