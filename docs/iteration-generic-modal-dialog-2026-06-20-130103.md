# 通用模态弹窗迭代纪要

时间：2026-06-20 13:01:03

## 本次变更

- 删除旧的 CleanupConfirmDialog 清理确认控件，清理前确认改用通用模态弹窗。
- 在 AppModalHost 增加 ShowAsync(title, content, messageType, buttonFlags)，返回值约定为 positive 按钮 0、negative 按钮 1。
- 通用弹窗标题、正文、按钮组均居中；正文右侧根据 ModalMessageType 显示提示、警告或错误图标。
- 分析按钮在无扫描数据时、标题栏关闭按钮点击时，均接入本次通用弹窗。
- 清理确认使用设置页当前默认删除模式，并在确认文案中展示待清理数量、预计释放空间和删除模式。

## 验证

- 已运行 dotnet build src/SpaceMonger.sln，构建通过；仍存在项目原有 NU1701 包兼容警告与重复资源键警告。
- 已运行 dotnet publish src/SpaceMonger.App/SpaceMonger.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o outputs/modal-dialog-2026-06-20-130055。
- 已运行 npx @colbymchenry/codegraph sync 更新代码索引。

## 输出

- 发布目录：outputs/modal-dialog-2026-06-20-130055
