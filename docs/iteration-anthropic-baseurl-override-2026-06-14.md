# 迭代纪要：Anthropic base URL 可覆盖

日期：2026-06-14

## 目标

将 SpaceMonger 中硬编码的 Anthropic base URL 改为可覆盖，并输出 Windows 可执行文件。

## 变更

- 新增 `src/SpaceMonger.Core/Services/Llm/AnthropicOptions.cs`，统一解析 Anthropic base URL。
- `src/SpaceMonger.App/Views/SettingsDialog.xaml` 增加 `Anthropic Base URL` 输入框。
- `src/SpaceMonger.Core/Models/AppSettings.cs` 增加 `AnthropicBaseUrl` 设置项。
- 设置页留空时使用内置 `https://api.anthropic.com`；填写绝对 `http(s)` URL 时覆盖内置地址。
- 修复自定义 base URL 带路径时 `/v1/messages` 拼接覆盖原路径导致验证 404 的问题。
- 环境变量 `SPACEMONGER_ANTHROPIC_BASE_URL` 和 `ANTHROPIC_BASE_URL` 保留为设置为空时的兜底覆盖方式。
- `README.md` 增加配置说明。

## 验证

- `dotnet build .\src\SpaceMonger.sln -c Release` 通过。
- `dotnet publish .\src\SpaceMonger.App\SpaceMonger.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\SpaceMonger-win-x64-localized-folder-...` 成功。

## 输出

- 最新 exe 位于 `outputs` 下的本地化发布目录。

## 备注

- 单文件发布在本机多次遇到 `GenerateBundle` 写入 exe 被占用，当前采用 folder publish；需要保留同目录 DLL/资源文件。
- 构建中保留了仓库原有的 `NU1701` 警告，未额外处理。
