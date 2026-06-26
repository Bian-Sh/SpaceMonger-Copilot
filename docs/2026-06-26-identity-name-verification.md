# 2026-06-26 身份名称验证补充

## 目标
确保用户用中英文、口语化方式询问“你是谁”时，Copilot 返回的名称稳定为 `SpaceMonger Copilot`。

## 新增测试思路
### 中文口语化问法
- 你是谁
- 你叫什么
- 说说你自己
- 介绍一下你自己
- 你是哪个 Copilot

### 英文口语化问法
- Who are you?
- What's your name?
- Tell me about yourself
- Introduce yourself
- Are you SpaceMonger Copilot?

## 预期
- 路由结果包含 `AiIntent.Identity`
- 本地身份回答包含 `SpaceMonger Copilot`
- 不应再出现 `Disk Space Analysis Copilot` 等旧名称

## 说明
这组测试主要验证身份识别与本地身份兜底名称是否统一；真实联调时，模型侧回答也应在 `app-guide` / identity skill 的约束下保持同名。
