# Task Plan: Agent Skill Router 去硬编码化

## 目标
把 `AiSkillRouter` 从中文关键字枚举/硬编码路由，收敛成“Agent 客户端能力通过 prompt/skill 声明”的最小实现：App 内置磁盘扫描/注册表查询能力，但不把风险算法、Unity 清理规则、语言关键词写死在 App 中。

## 步骤
1. 梳理当前 Copilot/Skill 相关代码调用链。
2. 用 CodeGraph/rg 辅助定位符号与依赖。
3. 快速参考 opencode 的工具/能力抽象，不照搬复杂框架。
4. 用声明式 skill catalog 替换硬编码关键词路由。
5. 构建验证，记录后续可扩展点。

- [x] 2026-06-29 收尾：移除旧主机建议动作兼容字段，验证 skill-driven/tool_calls-only Agent host 路径。

- [x] 2026-06-29 收尾追加：移除 Unity Library 宿主侧风险降级算法，恢复全量测试绿灯并完成 CUA 后台冒烟。

- [x] 2026-06-29 追加：删除 ChatViewModel 自然语言清空短语路由和旧自动执行路径，保留 /clear 明确命令。

- [x] 2026-06-29 追加：将 skill catalog 改为从 SKILL.md 文件发现，删除 router 内置技能清单。

- [x] 2026-06-29 追加：修复无扫描上下文 app-level tool proposal，开放符号化宿主动作给 skill/model 调用。

- [x] 2026-06-29 追加：删除旧 router 兼容策略字段，收紧文件树工具 scan context 边界并完成测试/CUA 验证。

- [x] 2026-06-29 追加：修正 runtime prompt 的 app-level proposal/tool risk 语义，并完成测试/CUA 验证。

- [x] 2026-06-29 追加：修正 ChatService streaming 工具观察文案，移除 file-tree/no-scan-context 残留假设并完成测试/CodeGraph 验证。

- [x] 2026-06-29 CUA 后台冒烟：隐藏启动 WPF，后台写入 Copilot 输入框并复核成功。

- [x] 2026-06-29 追加：取消默认注入全部 skills，新增 manage_disk_skills CRUD toolcall 与磁盘管理 skill 创建守门，并完成测试/发布包/CUA 验证。

- [x] 2026-06-30 追加：修复自然语言 Unity 请求不加载 skill、通用化发现确认卡，并把日期/Hub 风险判定写回 Unity skill。


- [x] 2026-06-30 追加：确认卡点击后立即消失、语言以 app 设置优先，并加入虚拟 C/D/E 慢扫描测试。

- [x] 2026-06-30 追加：修复 wrapped proposal/snake_case kind 导致 hasProposal=True 但无确认卡/无 step 的问题，并完成 CUA 验收。

- [x] 2026-06-30 追加：AI 外部分析等待提示从系统 MessageBox 切换到 AppModalHost 通用模态窗口，并完成测试/发布。

- [x] 2026-06-30 19:05:52 +08:00 追加：修复 chat / 指令描述本地化乱码，并新增 /clear console 清空 Console。
