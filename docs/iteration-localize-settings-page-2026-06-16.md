# 迭代纪要：设置页文案本地化修复（2026-06-16）

## 问题

- 设置页仍有大量 XAML 硬编码中文，切换语言时不会跟随本地化资源变化。

## 修复

- `src/SpaceMonger.App/Localization/Strings.resx`
  - 新增设置页英文资源键，覆盖返回按钮、搜索占位、左侧导航、AI 接口、模型、思考、语言和默认清理模式文案。

- `src/SpaceMonger.App/Localization/Strings.zh-CN.resx`
  - 新增对应简体中文资源键。

- `src/SpaceMonger.App/Views/SettingsPage.xaml`
  - 将设置页硬编码中文全部改成 `{loc:Loc ...}` 绑定。
  - 语言下拉项的“简体中文”、“English”、“自动检测”也改为资源绑定。

## 验证

```powershell
dotnet build .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Debug --no-restore
dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-win-x64-folder-20260616-014012
```

- 构建通过。
- 新版 exe 已发布到 `outputs\SpaceMonger-win-x64-folder-20260616-014012`。
- 仍只有既有 `NU1701` 包兼容警告。
