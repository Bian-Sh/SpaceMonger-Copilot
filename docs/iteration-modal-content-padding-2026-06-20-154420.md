# 迭代纪要：Modal 英文内容左右留白

- 时间：2026-06-20-154420
- 背景：英文模式下退出确认弹窗的 content text 接近或溢出模态窗口左右边界。
- 调整：在 src/SpaceMonger.App/Controls/AppModalHost.xaml.cs 的通用消息弹窗 content row 上增加左右 40px 外边距。
- 影响：所有通过 AppModalHost.ShowAsync 创建的消息弹窗内容区都会获得一致左右安全留白；内容文本仍保持居中与自动换行。
- 验证计划：执行 dotnet build，并发布 WPF exe 到 outputs。
