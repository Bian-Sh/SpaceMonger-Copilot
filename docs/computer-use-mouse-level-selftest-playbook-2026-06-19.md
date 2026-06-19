# Computer Use 鼠标级自测成功经验沉淀（2026-06-19）

## 目标

本记录沉淀本次地址栏编辑态首击无效 bug 修复中，真正可复用、用户认可的 PC Use / Computer Use 鼠标级自测方法。核心原则：可以用诊断接口准备状态和读取状态，但用户交互链路必须由真实鼠标点击完成。

## 关键原则

1. **不要把扫描耗时误判成假死**
   - 扫描磁盘可能很慢，尤其是 C:。
   - 不要因为短时间无响应就杀进程。
   - 如需确认状态，优先截图或读取 acceptance state，确认 isScanning、窗口画面、当前路径。
   - 自测修复时优先使用小型测试目录，避免不必要地扫描整个磁盘。

2. **长路径可以先注入，但交互不能注入**
   - 用户认可：先通过 acceptance server 创建/扫描/导航到长路径，保证测试起点稳定。
   - 不认可：用 
avigate、edit、lur 等接口替代“进入编辑态、点击退出区域、首次点击面包屑”。
   - 进入编辑态、点击退出编辑态的目标区域、首次点击面包屑，必须使用 Computer Use sky.click 或 Win32 SetCursorPos + mouse_event。

3. **测试路径使用体验选定长路径**
   - 本次稳定路径：C:\tmp\sm-test\LongParent\MiddleFolder\LeafFolder
   - 目标父级：C:\tmp\sm-test\LongParent\MiddleFolder
   - 目录内放少量文件即可产生 treemap，不需要扫描 C:。

4. **测试前必须把窗口提到前台**
   - 使用 Computer Use：wait sky.activate_window({ window })
   - 或 Win32：ShowWindow(hwnd, 9) + SetForegroundWindow(hwnd)
   - 然后截图确认窗口、路径、treemap 已处于预期状态。

5. **每次测试都要截图确认，不要只看接口状态**
   - Computer Use get_window_state({ include_screenshot: true, include_text: true }) 可同时拿截图和 UIA 树。
   - 截图中应能看到长路径面包屑和 treemap。
   - 如果刚点击“扫描”，必须等待扫描结束后再继续点击，不要直接判定 OK。

## 推荐流程

### 1. 启动测试包并启用 acceptance server

``powershell
$env:SPACEMONGER_ACCEPTANCE_SERVER='1'
Start-Process 'outputs\SpaceMonger-win-x64-folder-20260619-134557\SpaceMonger.App.exe'
``

### 2. 准备小型长路径测试目录

``powershell
$base='C:\tmp\sm-test'
$leaf=Join-Path $base 'LongParent\MiddleFolder\LeafFolder'
New-Item -ItemType Directory -Force -Path $leaf | Out-Null
1..8 | ForEach-Object {
  Set-Content -Path (Join-Path $leaf "file$_.bin") -Value ('x' * (1024*$_)) -Encoding ASCII
}
Set-Content -Path (Join-Path $base 'rootfile.bin') -Value ('root' * 1024) -Encoding ASCII
``

### 3. 只用 acceptance 准备状态

允许用 acceptance 做这些事：

- 
avigate 到 C:\tmp\sm-test
- scan 小目录
- 轮询 state 等待 isScanning == false
- 
avigate 到 C:\tmp\sm-test\LongParent\MiddleFolder\LeafFolder

不允许把这些作为鼠标交互结果证明：

- edit
- lur
- 
avigate 到父目录
- click_coord 代替 Computer Use/真实鼠标矩阵

### 4. Computer Use 初始化与窗口截图

在 Node REPL 中使用插件脚本初始化，不要直接导入 @oai/sky：

``js
if (!globalThis.sky) {
  const { setupComputerUseRuntime } = await import(
    "C:/Users/BianShanghai/.codex/plugins/cache/openai-bundled/computer-use/26.616.31447/scripts/computer-use-client.mjs"
  );
  await setupComputerUseRuntime({ globals: globalThis });
}

globalThis.apps = await sky.list_apps();
const app = globalThis.apps.find(a => (a.displayName || "").includes("SpaceMonger.App"));
globalThis.smWindow = app.windows[0];
await sky.activate_window({ window: globalThis.smWindow });
const state = await sky.get_window_state({
  window: globalThis.smWindow,
  include_screenshot: true,
  include_text: true
});
``

成功经验：

- list_apps() 能看到 SpaceMonger.App，说明 Computer Use 可用。
- get_window_state() 的截图中应能看到长路径面包屑和 treemap。
- state.screenshots[0] 会带 id、originX、originY、width、height。

### 5. 鼠标级矩阵测试

本次窗口在 1120,296、尺寸 1200x800 时，成功坐标如下；使用 Computer Use 时坐标是窗口截图内相对坐标：

| 目标 | Computer Use 相对坐标 | 说明 |
| --- | --- | --- |
| 地址栏空白 | 650,58 | 点击进入编辑态 |
| TitleLeft | 180,18 | 点击标题栏左侧退出编辑态 |
| TitleCenter | 520,18 | 点击标题栏中部退出编辑态 |
| Chat | 1010,450 | 点击聊天区域退出编辑态 |
| Treemap | 450,300 | 点击 treemap 区域退出编辑态 |
| BottomPanel | 450,620 | 点击底部面板退出编辑态 |
| MiddleFolder | 456,58 | 首次点击父级面包屑 |

测试逻辑：

1. 使用 acceptance 
avigate 重置到 LeafFolder。
2. sky.activate_window() 提到前台。
3. sky.click(AddressEmpty) 进入编辑态。
4. 读取 state，确认 readcrumbMode == "edit"。
5. sky.click(退出区域) 退出编辑态。
6. 读取 state，确认 readcrumbMode == "breadcrumb"。
7. sky.click(MiddleFolder)，这是关键的首次面包屑点击。
8. 读取 state，确认：
   - currentRootPath == "C:\\tmp\\sm-test\\LongParent\\MiddleFolder"
   - isExternalRoot == false

本次通过矩阵：

| 退出区域 | afterEdit | afterExit | 首击结果 | OK |
| --- | --- | --- | --- | --- |
| Chat | edit | breadcrumb | C:\tmp\sm-test\LongParent\MiddleFolder | true |
| TitleLeft | edit | breadcrumb | C:\tmp\sm-test\LongParent\MiddleFolder | true |
| TitleCenter | edit | breadcrumb | C:\tmp\sm-test\LongParent\MiddleFolder | true |
| Treemap | edit | breadcrumb | C:\tmp\sm-test\LongParent\MiddleFolder | true |
| BottomPanel | edit | breadcrumb | C:\tmp\sm-test\LongParent\MiddleFolder | true |

## 可复用 Computer Use 测试脚本骨架

``js
async function sendSm(obj) {
  const net = await import("node:net");
  return await new Promise((resolve, reject) => {
    const client = net.createConnection({ host: "127.0.0.1", port: 39187 }, () => {
      client.write(JSON.stringify(obj) + "\n");
    });
    let data = "";
    client.on("data", chunk => data += chunk.toString());
    client.on("end", () => {
      try { resolve(JSON.parse(data)); } catch (e) { reject(e); }
    });
    client.on("error", reject);
  });
}

async function smState() {
  return (await sendSm({ Command: "state" })).data;
}

async function smNav(path) {
  return await sendSm({ Command: "navigate", Path: path });
}

const leaf = "C:\\tmp\\sm-test\\LongParent\\MiddleFolder\\LeafFolder";
const want = "C:\\tmp\\sm-test\\LongParent\\MiddleFolder";

await sky.activate_window({ window: globalThis.smWindow });
const screenshotState = await sky.get_window_state({
  window: globalThis.smWindow,
  include_screenshot: true,
  include_text: true
});
const sid = screenshotState.screenshots[0].id;

const coords = {
  AddressEmpty: { x: 650, y: 58 },
  TitleLeft: { x: 180, y: 18 },
  TitleCenter: { x: 520, y: 18 },
  Chat: { x: 1010, y: 450 },
  Treemap: { x: 450, y: 300 },
  BottomPanel: { x: 450, y: 620 },
  MiddleFolder: { x: 456, y: 58 }
};

const results = [];
for (const name of ["Chat", "TitleLeft", "TitleCenter", "Treemap", "BottomPanel"]) {
  await smNav(leaf);
  await new Promise(r => setTimeout(r, 500));
  await sky.activate_window({ window: globalThis.smWindow });

  await sky.click({ window: globalThis.smWindow, screenshotId: sid, ...coords.AddressEmpty, mouse_button: "left" });
  await new Promise(r => setTimeout(r, 450));
  const afterEdit = await smState();

  await sky.click({ window: globalThis.smWindow, screenshotId: sid, ...coords[name], mouse_button: "left" });
  await new Promise(r => setTimeout(r, 450));
  const afterExit = await smState();

  await sky.click({ window: globalThis.smWindow, screenshotId: sid, ...coords.MiddleFolder, mouse_button: "left" });
  await new Promise(r => setTimeout(r, 700));
  const afterClick = await smState();

  results.push({
    case: name,
    afterEdit: afterEdit.breadcrumbMode,
    afterExit: afterExit.breadcrumbMode,
    root: afterClick.currentRootPath,
    external: afterClick.isExternalRoot,
    ok: afterClick.currentRootPath === want && afterClick.isExternalRoot === false
  });
}

nodeRepl.write(JSON.stringify(results, null, 2));
``

## Win32 真实鼠标备选方案

当 Computer Use 插件不可用时，仍可使用 Win32 真实鼠标事件，但必须明确说明这是 fallback，并仍需截图/状态验证。

``powershell
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class MouseTest {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X,int Y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint data,UIntPtr ex);
  public const uint LD=0x02, LU=0x04;
  public static void Click(int x,int y){
    SetCursorPos(x,y);
    System.Threading.Thread.Sleep(120);
    mouse_event(LD,0,0,0,UIntPtr.Zero);
    System.Threading.Thread.Sleep(80);
    mouse_event(LU,0,0,0,UIntPtr.Zero);
    System.Threading.Thread.Sleep(650);
  }
}
'@
``

Win32 坐标是屏幕绝对坐标。本次窗口位于 1120,296 时，MiddleFolder 中心约为 1576,354；等价于 Computer Use 相对坐标约 456,58。

## 常见误区

- 不要点击“扫描”后马上继续测试；必须等待扫描完成。
- 不要只用 state 判断“看起来 OK”；关键点击必须真实发生并截图确认。
- 不要用接口 
avigate 到父目录来证明面包屑首击有效。
- 不要因为窗口暂时无响应就杀进程；先截图、查 state.isScanning。
- 不要只测聊天区；标题栏、treemap、底部面板都可能触发不同焦点路径。
- 不要在修复中到处硬编码退出点；应把编辑态退出状态清理收敛到统一函数。
