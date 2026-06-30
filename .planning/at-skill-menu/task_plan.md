# @ skill 菜单与 step 展示器计划

## 目标
- 输入 `@` 时展示本 app skills 菜单。
- 选择 skill 后把“应调用哪个 skill”明确注入给 AI。
- 菜单支持鼠标、上下键、Tab/Enter 确认，并让 `/` 菜单也支持 Tab 确认、上下切换。
- 用 cua 后台测试确认 step 展示器显示与交互。
- 若是 WPF/桌面 app 且有实质修改，发布 exe 到 outputs，使用可读时间命名。

## 步骤
1. 梳理项目结构和现有 slash/step 菜单实现。
2. 找到 skills 数据来源与 AI 消息发送路径。
3. 复用现有菜单组件实现 `@` skill picker。
4. 统一补齐 `/` 与 `@` 的键盘行为。
5. 构建并用 cua 验证 UI 与 step 展示器。
6. 发布 exe 到 outputs。

## 追加修复（2026-06-27）
- 修复 hover/step 中 selected skill 文案乱码。
- 调整 step 展示器与 `@` 菜单圆点样式。
- 为 Unity 项目 `Library` 加入推荐清理兜底：仅在父目录包含 `Assets` 和 `ProjectSettings` 时触发。
- 使用 cua 跑通发布包中的 Unity skill 流程，确认推荐列表出现 `Library`。
