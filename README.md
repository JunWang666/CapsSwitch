# CapsSwitch

Windows 输入法切换工具:按下 CapsLock 在中英文输入源之间切换,在光标旁悬浮显示当前输入状态(HUD),托盘常驻 + 设置窗口。Phase 0–3 全部完成。

## 系统前提

- Windows 11(普通权限即可,无需管理员;开机自启写 HKCU 注册表)。
- 目标机器需安装**微软拼音 + US 键盘**(或其他中英输入源;首选对可在设置中更改)。
- **使用前退出/禁用 AutoHotkey、PowerToys Keyboard Manager 等按键重映射工具**,否则与 CapsLock 拦截互相争抢(见"已知限制")。
- 建议从 `scripts\publish.ps1` 产出的单文件 EXE 启动(见"发布");源码运行需要 .NET 10 SDK。

## 目录结构

```
CapsSwitch.sln                  解决方案文件
CapsSwitch.App/                 Phase 1–3 核心应用(net10.0-windows,WinExe,UI 线程消息泵)
  Program.cs                    run(托盘)/ list / capsstate / hudtest / autostart 命令入口
  AppHost.cs                    运行期宿主:Hook/Monitor/FocusMonitor/状态机/切换线程/HUD 编排
  Input/
    KeyboardHook.cs             WH_KEYBOARD_LL(专用线程 + 消息泵,Caps 吞掉,可暂停放行)
    CapsKeyStateMachine.cs      短按/长按/连按/组合取消状态机(纯逻辑,时间注入,可单测)
    KeyboardSimulator.cs        SendInput 模拟 CapsLock(带魔术 dwExtraInfo,防自反馈)
  InputSources/
    InputSourceInfo.cs          输入源模型(Id/LangId/Clsid/ProfileGuid/Hkl/DisplayName/ShortLabel)
    IInputSourceService.cs      输入源服务接口 + SwitchConfig/ISwitchStrategy
    InputSourceService.cs       TSF 枚举 + 前台匹配 + WM_INPUTLANGCHANGEREQUEST 切换与读回验证
    SourceListBuilder.cs        纯函数:profile 行+HKL→源列表(未启用过滤、同 HKL 伪布局丢弃、去重)
    SwitchResult.cs             切换结果语义(HKL 成功 / 中文模式强制未确认 / 失败)
    NativeModeReenforceThrottle.cs  中文模式自动纠正节流(纯逻辑)
    InputSourceMonitor.cs       200ms 轮询前台 HKL + IME NATIVE 位,变化触发事件
    PreferredPairCycleStrategy.cs  首选中英互切 + OrderedSourceIds 循环(纯逻辑,可单测)
  Focus/
    TextInputDetector.cs        可编辑文本控件判定(纯逻辑,UIA ControlType/IsReadOnly 描述入参)
    CaretLocator.cs             光标定位三级降级(原生 UIA TextPattern2 → GetSelection/GetGUIThreadInfo → BoundingRectangle)
    FocusMonitor.cs             独立 MTA 线程:UIA FocusChanged 监听 + 500ms 去重 + 光标定位队列
  Overlay/
    HudTextMapper.cs            (源, NATIVE 位) → HUD 文本(纯逻辑:中文 TIP 中/英 → "中"/"A")
    HudMetrics.cs               HUD 逻辑像素设计值与 DPI 换算(纯逻辑:10pt / padding 8x3 / 0.78 透明度)
    HudPlacer.cs                HUD 定位(纯逻辑:光标下方 4px,底部放不下翻到上方,支持负坐标)
    ListHighlight.cs            列表高亮环形推进(纯逻辑)
    CycleSession.cs             循环会话(纯逻辑:正常/循环两模式,任意间隔推进,超时/长按回正常)
    InputHudForm.cs             无焦点置顶浮窗:UpdateLayeredWindow 逐像素 alpha 圆角(无锯齿)、徽章/列表双模式、DPI 感知尺寸
    HudController.cs            显示调度:焦点(总是显示)/短按徽章/连按列表/外部变化/⇪ 大写状态;
                                首帧不等 UIA 定位(缓存→GetGUIThreadInfo→上次位置),UI 线程零跨进程调用
  UI/
    TrayContext.cs              托盘:NotifyIcon(代码生成图标)+ 右键菜单 + 首次气球提示
    SettingsForm.cs             设置窗口(单例):首选对/循环顺序/阈值/HUD/自启,保存立即生效
  Config/
    AppSettings.cs              settings.json 模型(含 HotkeysEnabled)
    SettingsStore.cs            %LocalAppData%\CapsSwitch\settings.json 读写、默认检测、损坏备份 .bad
    AutoStartRegistrar.cs       HKCU Run 键开机自启(写/删/查)
  Logging/
    Logger.cs                   统一日志:文件(>2MB 截断)+ Debug;CLI 挂父控制台
  Interop/
    Win32Native.cs              user32 / imm32 P/Invoke(钩子/SendInput/DPI/AttachConsole)
    TsfInterop.cs               TSF COM 互操作(从 Phase 0 复制,含修正后的 vtable)
CapsSwitch.Tests/               xunit 单元测试(73 个:状态机/策略/源列表/节流/结果语义/控件判定/HUD 文本/自启引用)
CapsSwitch.Phase0/              Phase 0 技术验证控制台项目(存档,不再改动)
docs/
  Phase0-验证记录.md            Phase 0 真实运行记录与结论
  Phase1-集成测试记录.md        Phase 1 合成按键集成测试与拼音模式限制定位
  Phase2-集成验证记录.md        Phase 2 HUD 截屏验证记录(截图在 docs/shots/)
  Phase3-验证记录.md            Phase 3 构建/发布/冒烟/CLI/自启真实结果 + 人工验证清单
scripts/
  publish.ps1                   发布脚本(独立版 + 框架依赖版)
  (其余 PowerShell 验证脚本)   IME 读写、按键合成、UIA 回读、截屏
```

## 构建与测试

```bash
dotnet build
dotnet test
```

## 发布

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

| 产物 | 大小 | 要求 |
| --- | --- | --- |
| `dist\CapsSwitch.App.exe` | ~72MB | 无,self-contained,拷到任何 Win11 机器直接跑 |
| `dist\framework-dependent\CapsSwitch.App.exe` | ~0.3MB | 目标机器需安装 .NET 10 桌面运行时 |

## 使用

双击 `CapsSwitch.App.exe`(或 `autostart on` 后开机自动)进入托盘模式,无控制台窗口:

- **短按 CapsLock**:首选中文 ↔ 首选英文 互切;连按(间隔 ≤300ms)沿循环顺序继续轮换。
- **长按 CapsLock(≥400ms)**:模拟系统 CapsLock(大写锁定),状态可用 `capsstate` 命令核实。
- **Caps+其他键**:组合键,不触发任何切换。
- **托盘右键菜单**:
  - 暂停/启用快捷键切换(暂停时 Caps 完全恢复系统原生行为)
  - 暂停/启用输入焦点提示(HUD)
  - 设置…(首选输入源、循环顺序、长按/连按/HUD 时长、开机自启;保存立即生效)
  - 退出(干净卸载钩子,托盘图标不残留)
- **HUD**:尺寸按 DPI 缩放的小圆角半透明标签(UpdateLayeredWindow 逐像素 alpha,淡入 90ms/淡出 120ms),出现在光标**下方**(底部放不下自动翻到上方);切换响应:按键→HUD 首帧约 100ms(中文模式强制后台化,不阻塞切换与显示):
  - 短按(首次):紧凑徽章 中 / A / EN / あ(当前源与模式);
  - 连按(第 2 次起):展开**循环列表**并进入循环会话——列表可见期(约 1.5s)内**任意间隔**继续按都沿顺序循环推进、高亮跟随;列表消失后自动回到主副互切语义;
  - 长按 CapsLock 后:显示系统真实大写状态 ⇪ 大写 / ⇪ 小写;
  - 文本控件聚焦时总是显示;外部(非 Caps)输入源变化也触发紧凑徽章。

配置项 `HudFocusCooldownSeconds`(默认 10,0=关闭):聚焦触发的 HUD 在"同输入源+同中英文模式"状态下 10 秒内不重复弹出;切换输入源/模式后立即弹出。切换、连按列表、长按提示不受冷却限制。

日志:`%LocalAppData%\CapsSwitch\capswitch.log`(>2MB 自动截断)。

## CLI 调试命令

```bash
CapsSwitch.App.exe list                 # 列出输入源(AttachConsole 挂父终端)
CapsSwitch.App.exe capsstate            # 真实 CapsLock 锁定状态
CapsSwitch.App.exe hudtest              # 强制显示 HUD "中" 3 秒
CapsSwitch.App.exe autostart on|off|status  # 开机自启(等价设置窗口勾选)
CapsSwitch.App.exe settings             # 设置窗口调试入口(不安装键盘钩子)
```

## 已知限制

1. **TSF 原生应用(如 Windows 11 记事本)中,拼音经"英文键盘→拼音"往返后会以英文模式重新激活,且外部无法恢复**:IMC_SETCONVERSIONMODE / IMC_SETOPENSTATUS / 等待 / 重新聚焦 / TSF ActivateProfile 全部 flag 组合 / 注入 Shift,全部无效(实测记录见 `docs/Phase1-集成测试记录.md`)。IMM 桥接类应用(Chrome 等)不受影响,强制中文模式有效。缓解:微软拼音"默认模式=中文"设置对新窗口有效;本工具会持续尝试并在 HUD 如实反映实际模式。
2. **与 AutoHotkey / PowerToys Keyboard Manager 等按键工具不兼容**:它们若重映射了 CapsLock,会与本工具互相争抢(实测 Caps 被 AHK 映射为切换输入语言)。使用本工具前请退出/禁用此类映射。若此类工具以管理员权限运行,本工具(普通权限)无法覆盖其行为。
3. **管理员权限窗口 / UAC 桌面 / 锁屏**:普通权限进程的键盘钩子与消息投递在这些场景被系统限制,功能降级为不响应(不影响系统本身)。
4. 记事本等 TSF 原生应用的 IMM 上下文有"幻影"现象:焦点子控件的 IME 上下文读数与真实打字行为不一致,读写一律以顶层窗口上下文为准(代码内已统一)。
5. 单实例运行:重复启动会直接退出(命名 Mutex);CLI 命令(list/capsstate/hudtest/autostart)不受单实例限制。
6. CLI 命令从资源管理器启动(无父控制台)时输出只进日志文件,不打印窗口(AttachConsole 挂接失败属预期)。

## Phase 0 用法(验证存档)

```bash
dotnet run --project CapsSwitch.Phase0 -- <命令> [参数]
```

| 命令 | 说明 |
| --- | --- |
| `enum` | 枚举全部已启用输入源(注册表扫描 + TSF EnumProfiles + HKL 列表) |
| `current` | 显示当前活动 Profile(GetActiveProfile) |
| `fg` | 显示前台窗口线程的输入法状态(HKL、IME 转换模式、打开状态) |
| `activate <zh\|en>` | 通过 TSF ActivateProfile 激活指定输入源并逐步验证各 flag 行为 |
| `activate-msg <hklHex>` | 向前台窗口 PostMessage `WM_INPUTLANGCHANGEREQUEST` 并验证 |
| `watch [秒数=30]` | 轮询观察输入源变化(200ms 采样,变化即打印快照) |
| `focus [秒数=30]` | UIA 焦点监听 + 光标定位实验(TextPattern / GetGUIThreadInfo fallback) |
| `pinyin` | 微软拼音实验:注册表设置转储 + 前台 IME 模式检测 |
| `all` | 依次自动跑 `enum`、`current`、`pinyin` |
