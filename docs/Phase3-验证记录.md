# Phase 3 验证记录

> 本文档所有输出均为 2026-10-11 本机真实运行结果(发布版 `dist\CapsSwitch.App.exe` / 框架依赖版)。UI 交互类验证见文末"待人工验证"。

## 验证环境

| 项目 | 值 |
| --- | --- |
| Windows | Microsoft Windows NT 10.0.26200.0(build 26200) |
| .NET SDK | 10.0.401 |
| 输出类型 | WinExe(托盘模式无控制台窗口);CLI 命令经 `AttachConsole(-1)` 挂父控制台 |

## 自动化验证结果

### 构建与测试

```
dotnet build     → 0 警告 0 错误
dotnet test      → 已通过! 失败:0,通过:73(新增 AutoStartRegistrarTests ×3)
```

### 发布(scripts/publish.ps1,真实执行)

```
C:\DevCodes\Others\CapsSwitch\dist\CapsSwitch.App.exe                      71.93 MB   (self-contained 单文件)
C:\DevCodes\Others\CapsSwitch\dist\framework-dependent\CapsSwitch.App.exe   0.26 MB   (需 .NET 10 桌面运行时)
```

### 托盘模式冒烟(CAPSSWITCH_EXIT_AFTER_MS=4000,exit 0)

日志文件 `%LocalAppData%\CapsSwitch\capswitch.log` 真实内容(节选):

```
[14:59:39.584] [App] CapsSwitch Phase 3 启动(托盘模式)
[14:59:39.7xx] [Settings] (默认配置已存在,直接加载)
[14:59:39.9xx] [App] 首选中文=fa550b04-... 首选英文=hkl:04090409 顺序=[fa550b04-..., hkl:04090409] HUD=开(1000ms)
[14:59:39.952] [App] 快捷键切换=启用
[14:59:39.952] [App] 就绪。短按 Caps=切换输入源,长按 Caps=系统 CapsLock。
[14:59:39.952] [Monitor] 前台变化: hwnd=... HKL=0x08040804 模式=中文
[14:59:4x.xxx] [FocusMonitor] UIA FocusChanged 已注册(MTA)
[14:59:4x.xxx] [App] HUD 已初始化
[14:59:43.825] [App] 正在退出…
[14:59:43.834] [FocusMonitor] UIA 线程退出
[14:59:43.944] [KeyboardHook] 已卸载
[14:59:43.947] [App] 已退出
```

全部后台线程(钩子/Monitor/FocusMonitor/HUD)启动与清理正常;日志文件可读(UTF-8 无 BOM)。

### CLI 兼容(AttachConsole,本环境 Git Bash 下真实可用)

```
$ dist\CapsSwitch.App.exe list
[14:59:39.215] [App] CLI 模式:list(已挂接父控制台)
共 2 个输入源:
  [中  ] Microsoft Pinyin  Id=fa550b04-...  HKL=0x08040804  TSF TIP
  [EN ] English (US) Keyboard  Id=hkl:04090409  HKL=0x04090409  键盘布局
  当前前台: [中] Microsoft Pinyin

$ dist\CapsSwitch.App.exe capsstate
CapsLock locked = False

$ dist\framework-dependent\CapsSwitch.App.exe capsstate   → 框架依赖版同样正常
```

注:AttachConsole 挂接的是**启动它的父控制台**;从资源管理器双击 CLI 命令无父控制台时,输出只进日志文件(已实测不崩)。

### 单实例

第二次启动同一 exe → 日志记录"已有 CapsSwitch 实例在运行,退出",返回 0。(命名 Mutex `Local\CapsSwitch.SingleInstance`)

### 开机自启(注册表 setter/getter 真实验证,未做重启)

```
$ dist\CapsSwitch.App.exe autostart status   → autostart = off
$ dist\CapsSwitch.App.exe autostart on
  → 已写入 HKCU Run 键(值="C:\DevCodes\Others\CapsSwitch\dist\CapsSwitch.App.exe")
PowerShell 独立读回: Get-ItemProperty HKCU:\...\Run -Name CapsSwitch
  → "C:\DevCodes\Others\CapsSwitch\dist\CapsSwitch.App.exe"   ← 一致,带引号
$ dist\CapsSwitch.App.exe autostart off
PowerShell 独立确认: 值已删除
```

`autostart` 命令与设置窗口"开机自启"勾选走完全相同的 `AutoStartRegistrar.SetAutoStart` 路径。

## 待人工验证

- [ ] **托盘菜单交互**:右键托盘图标 → 暂停/启用快捷键切换(Caps 应完全恢复系统行为)、暂停/启用输入焦点提示、设置…(打开设置窗口,重复点击只激活)、退出(托盘图标不残留、进程结束)。
- [ ] **首次运行气球提示**:删除 settings.json 后启动,应弹出一次"CapsSwitch 已启动"气泡;托盘图标随输入源变化换字(中/EN)。
- [ ] **设置窗口**:修改首选中英/循环顺序(上移/下移)/三个毫秒阈值/HUD 开关/开机自启 → 保存后立即生效(阈值影响长按手感、HUD 时长变化、自启注册表写入);取消不落地。
- [ ] **开机自启真实重启**:勾选后注销/重启,确认托盘自动出现(注册表已写入,未做重启验证)。
- [ ] **多应用手工集成**:记事本/Chrome/终端间切换 + HUD 光标定位精度、候选窗避让、多显示器(负坐标)显示。
- [ ] **日志截断**:日志超 2MB 后重启应截断重来(未实测,逻辑为启动时检查)。

---

# UI/UX 整改记录(2026-10-11 第二轮)

## 问题 → 修改 → 验证

### 1. HUD 太大太丑、不在输入焦点

**问题根因(实测定位):**
- 尺寸:14pt Bold + 固定像素 Padding,150% 缩放下字高物理 28px,显笨。
- 定位:`method=TextPattern2` 从未命中的真正原因是**锚点错**——探针对所有可见顶层窗口逐一调用原生路径,逐级 HRESULT 日志(新增)显示 `GetCurrentPattern(TextPattern2) hr=0x00000000 pattern=null`:TextPattern2 挂在**聚焦的编辑控件**上,而 hudtest/旧 LocateCaretAsync 用 `FromHandle(顶层 hwnd)` 取元素,顶层窗口天然不支持 → 每次都静默降级到 BoundingRectangle(控件左上角)。`IUIAutomationElement` vtable(IID `d22108aa-…`)已对照 SDK 头文件复核,槽位无误,COM 互操作本身无 bug。

**修改:**
- `InputHudForm`:11pt 常规字、逻辑 padding(10,5)、圆角半径 8,全部按 96 DPI 逻辑值设计,`HudMetrics.Scale` 按 `DeviceDpi` 换算;`DpiChanged` 重算;Region 圆角随尺寸/DPI 重建;底色 (24,24,24)、Opacity 0.92。
- `HudPlacer`(新纯函数):光标右侧 4px、垂直中线对齐、右缘放不下放左侧、按工作区裁剪(支持负坐标)。
- `CaretLocator`/`FocusMonitor`:原生路径与托管路径统一改用**焦点元素**(`GetFocusedElement`/`AutomationElement.FocusedElement`,回退 FromHandle);每级失败打 `Logger.Verbose`(HRESULT/异常/空矩形);顺序 TextPattern2 → GetSelection → GetGUIThreadInfo → BoundingRectangle(内缩 8,8)。
- 新增纯函数测试 `HudLayoutTests`(DPI 换算 5 例 + 定位 5 例含负坐标/极窄工作区)。

**验证(真实):**
- `hudtest`(发布版,exit 0):`[HUD] 显示 '中' @ (961,1774) size=62x52 method=… dpi=144` —— 62x52 物理 = 96DPI 下约 41x35,比旧版显著缩小。
- 焦点带文本选择时命中 `method=SelectionRange`(旧版只会 BoundingRectangle);当前焦点(群聊容器)不支持 Text/TP2 时如实降级并在日志说明原因。
- 屏幕截图(PMv2 物理坐标拷贝):`docs/shots/hud-after.png` —— 光标旁小圆角深底"中",自动消失正常。

### 2. 设置窗口丑陋且按钮被裁剪

**问题:**内容约 570px 而窗口 480px,保存/取消/自启勾选出窗口(UIA 树证实);AutoSize+GroupBox 嵌套布局在 150% DPI 下量错。

**修改:**改为逻辑像素绝对布局 + `AutoScaleMode.None` + 手动 `ScaleTree`(WinForms 的 AutoScaleMode 不缩放运行时设置的 ClientSize,首版窗口没跟着子控件放大正是裁剪根因);Segoe UI 9F;分组(输入源/触发参数/启动);顶部灰色说明行;扁平按钮 + 强调色保存键。

**验证(真实):**`CapsSwitch.App.exe settings` 调试入口(无钩子)+ PrintWindow 截图 `docs/shots/settings-after.png`(682x845 物理):全部控件含 保存/取消/开机自启动 完整可见。修复过程中发现并纠正了验证方法本身的偏差:非 DPI 感知进程 PrintWindow PMv2 窗口会按逻辑尺寸渲染导致"假裁剪",截图工具改 PMv2 后确认真实渲染正常。

### 3. 托盘菜单弹出不受控

**修改:**不再把 `ContextMenuStrip` 赋给 `NotifyIcon.ContextMenuStrip`;`MouseClick` 右键显式 `_menu.Show(Cursor.Position)`;新增 `FlatMenuRenderer`(白底/悬停浅灰/选中浅蓝灰/深灰文字/浅灰分隔线),菜单项 Padding 加大。

**状态:**代码路径明确;右键真实交互留人工验证。

## 本轮验收汇总

- `dotnet build` → 0 警告 0 错误;`dotnet test` → 83/83(新增 HudLayoutTests ×10)。
- `scripts/publish.ps1` → 双版本发布成功(71.93MB / 0.26MB)。
- 发布版 `hudtest` → 显示/定位/自动消失正常(exit 0);`run` 冒烟 exit 0。
- 截图证据:`docs/shots/hud-after.png`(HUD 新样式在真实光标旁)、`docs/shots/settings-after.png`(设置窗口完整可见)。

---

# HUD 五条改进记录(2026-10-11 第三轮)

## 改动

1. **位置与样式**:`HudPlacer.PlaceBelow`(光标下方 4px、水平左对齐、底部放不下翻到上方);`HudMetrics` 缩至 10pt / padding 8x3 / Opacity 0.78。
2. **大写锁定提示**:长按后 `HudController.NotifyCapsLockToggled` 延迟 100ms 在 **UI 线程** `Control.IsKeyLocked` 读真实状态(不按预期显示),显示 "⇪ 大写"/"⇪ 小写"。
3. **连按循环列表**:`InputHudForm.SetBadge/SetList/SetListActive` 双模式;列表 = OrderedSourceIds 全源竖排、活动行强调色高亮、原地推进不闪烁;隐藏时长 = max(SequenceGap+200, HudDurationMs);新增纯函数 `ListHighlight.Move/Next` + 10 个新测试。
4. **聚焦总是显示**:焦点触发路径去掉 (hwnd+源+模式) 同状态去重(`suppressIfUnchanged:false`);FocusMonitor 的 500ms UIA 事件去重保留。
5. **文案**:"首选中文输入源"→"主输入源","首选英文输入源"→"副输入源"(设置窗口截图 `docs/shots/settings-main.png` 证实)。

## 过程中修复的三个真实 bug

- **列表高亮不生效**:`TableLayoutPanel.Controls.Add(ctrl,col,row)` 不会自动增长 `RowCount`(实测 RowCount=0),显式设置后恢复;探针像素级验证。
- **列表→徽章尺寸残留**:`SetBadge` 早退条件未考虑模式切换,修复。
- **列表模式被 Monitor 竞争抑制**:切换后 200ms Monitor 的"外部变化"触发会 bump 请求序号,把在途列表丢弃 → `NotifyExternalChange` 在切换完成 600ms 内跳过。
- **冷启动枚举漏拼音 TIP**(TSF 不稳定的老问题在新形态复现):无 TIP 的结果 200ms 重采一次,重试发现 TIP 则采用;防止错误默认值被写进 settings.json。

## 验证证据(合成 keybd_event 按键,记事本聚焦)

脚本:`docs/scripts/test-hud5.ps1` / `test-hud5-list.ps1`(ALT 敲击绕过前台锁 + 前台校验重试)。

| 项 | 证据 | 观察 |
| --- | --- | --- |
| a 徽章在光标下方 | `docs/shots/crop6-a.png`(全图 `hud5-a-badge.png`) | 记事本光标正下方小圆角半透明"中",53x43@144dpi |
| b 大写状态 | `docs/shots/crop6-b.png`("⇪ 大写");16:42 轮日志 "⇪ 小写" | 双向都按**真实状态**显示(IsKeyLocked),非预期值 |
| c 连按两次列表 | `docs/shots/crop5-c.png`(全图 `hud5-c-list2.png`) | 三行列表,RU 行强调色高亮(active=2/3,日志 `列表模式 active=2/3`) |
| d 连按三次循环 | `docs/shots/crop5-d.png`(全图 `hud5-d-list3.png`);日志 active 随按键推进 | 高亮推进/循环机制验证;记事本下光标定位走 `method=UiAutomationTextPattern2`(GetCaretRange 真实命中) |
| 设置文案 | `docs/shots/settings-main.png` | "主输入源"/"副输入源" |

日志摘录(16:53 轮):`[HUD] 列表模式 active=2/3 @ (61,130) size=278x121 method=UiAutomationTextPattern2`;(16:55 轮)`[HUD] 显示 '⇪ 大写'…`。

- build 0 警告 0 错误;dotnet test **90/90**(新增 HudPlacer 下方定位/翻转 5 例 + ListHighlight 5 例);publish 双版本成功。
- 注:验证期间用户正在使用机器,部分轮次前台被抢占已重试;CapsLock 最终恢复为关。

---

# 循环会话 + ULW 抗锯齿记录(2026-10-15 第四轮)

## 问题 1:列表展开后继续按键退回主副切换

**根因**:序列生命周期由状态机的 SequenceGapMs(300ms)决定,列表 HUD 还在显示(隐藏计时 max(HudDurationMs,1500ms))时,超过 300ms 的按键被当作 seq=0(主副互切),语义与可见 UI 矛盾。

**修复**:新增纯逻辑 `InputSources/CycleSession`(8 个单测覆盖全转换):正常模式 seq=0=主副互切,seq≥1=进入循环;**循环模式中任意间隔短按都循环推进**;长按或列表隐藏(HudController 新增 `ListHidden` 事件:隐藏计时器超时/被徽章替换时触发)→ 回正常模式。AppHost 的 OnCapsAction 把状态机输出翻译为 PairToggle/CycleNext。列表显示时长改为 max(HudDurationMs, 1500ms)。

## 问题 2:圆角锯齿

**根因**:`Form.Region` 是 1-bit 裁剪。

**修复**:`InputHudForm` 重写为 GDI+ 绘制 32bppArgb 位图(SmoothingMode.AntiAlias + TextRenderingHint.AntiAlias,整体 0.78 透明度折进背景 alpha)+ `UpdateLayeredWindow`(ULW_ALPHA, CreateDIBSection 负高度自顶向下 BGRA),徽章/列表两模式统一走该路径;去掉 Region/Opacity;WM_ERASEBKGND/OnPaintBackground 置空防闪帧;失败仅 Logger.Verbose。

## 验证(真实执行,脚本 docs/scripts/test-cycle-ulw.ps1)

日志摘录(17:18 轮,记事本聚焦,keybd_event 合成按键):

```
[17:18:32.415] [Caps] 短按 sequence=0 模式=Normal → 主副互切
[17:18:32.706] [Caps] 短按 sequence=1 模式=Cycling → 循环推进
[17:18:33.357] [Caps] 短按 sequence=0 模式=Cycling → 循环推进   ← 距上一按 650ms>300ms,仍循环
[17:18:35.143] [Cycle] 列表隐藏,循环会话结束,回到正常模式      ← 1500ms 超时
[17:18:35.932] [Caps] 短按 sequence=0 模式=Normal → 主副互切     ← 回到主副语义
```

- **a** 截图 `docs/shots/crop-cycle-a.png`(全图 `cycle-a-continue.png`):600ms 后再按,列表 RU 行高亮(继续循环)。
- **b** 超时后短按=主副切换:日志 + `cycle-b-pair.png`。
- **c** 圆角 4 倍放大 `docs/shots/corner-zoom-list.png`:角部 alpha 平滑渐变,无 1-bit 锯齿;文字抗锯齿。
- build 0 警告 0 错误;dotnet test **98/98**(新增 CycleSessionTests ×8);publish 双版本成功(75.4MB / 0.29MB)。

## 过程中修复

- `ListHidden` 订阅补丁首次未打上(文本未匹配),实测发现"隐藏发生但会话未结束"后补订阅复验。
- 测试方法名以数字开头(C# 非法标识符)导致测试项目编译失败,已改名。

---

# 配置污染加固记录(2026-10-15 第五轮)

## 背景

冷启动 TSF 枚举丢失拼音 TIP 时,源列表只剩 "hkl:08040804" 伪布局项,SettingsStore 曾把它当主输入源写盘;TIP 恢复后(同 HKL 伪布局被 SourceListBuilder 丢弃,拼音 Id=guid)配置里的 hkl:08040804 变成悬空 Id——不等于拼音 guid 也不等于 hkl:00000409,失效重检测匹配不上,导致 HUD 循环列表出现"键盘布局 0x08040804"、切中文不走 TIP 中文模式强制。用户手工修复后要求代码加固。

## 加固(三层)

1. **`SourceIdResolver`(新纯函数)**:`Normalize(configuredId, sources)` — 精确匹配 → 原样;"hkl:XXXXXXXX" 形式 → 解析 HKL → 按 HKL 等价匹配(多命中优先 TIP)→ 归一化为真实 Id;否则 null。`SettingsStore.Sanitize` 对 PreferredChineseId/PreferredEnglishId/OrderedSourceIds 全部套用,命中记日志"配置 Id X 已归一化为 Y",归一化不了走现有剔除/重检测回退。
2. **冷启动重采加固**:无 TIP 首次枚举 200ms 重采后再加 1s 二次重采,仍无才接受。
3. **TIP 恢复热修复**:`InputSourceService` 检测枚举"无 TIP→有 TIP"转变(防抖,仅在转变时),回调 AppHost(经 Task.Run 脱离工作线程防自死锁)→ `SettingsStore.NormalizeInPlace`(与当前比对,不同才写盘)→ Save + 热生效(策略/Monitor/HUD,不动自启注册表)。
   Load 路径同样"归一化有变化即写盘",杜绝悬空 Id 反复出现。

## 验证(真实执行)

- build 0 警告 0 错误;dotnet test **112/112**(新增 SourceIdResolverTests ×9 + SettingsNormalizationTests ×3:污染修复并报告变化、幂等、干净配置无变化);publish 双版本成功。
- 污染配置实测:写入 `PreferredChineseId=hkl:08040804, OrderedSourceIds=[hkl:08040804, hkl:04090409]` → 启动 dist 版,日志:

  ```
  [Settings] 配置 Id hkl:08040804 已归一化为 fa550b04-5ad7-411f-a5ac-ca038ec515d7
  [Settings] 配置归一化有变化,将写盘并热生效
  [Settings] 归一化结果已写盘
  [App] 首选中文=fa550b04-5ad7-411f-a5ac-ca038ec515d7 顺序=[fa550b04-..., hkl:04090409]
  ```

  写回的 settings.json 已为拼音 guid;`list` 确认主输入源 = Microsoft Pinyin(TSF TIP)。

## 说明

- "TIP 恢复热修复"路径依赖 TSF 枚举真实抖动触发,本机稳定复现困难;转变检测/写盘防抖/热生效逻辑经代码审查,归一化正确性由 12 个单测覆盖。

---

# 性能/流畅度优化记录(2026-10-17 第六轮)

## 卡顿来源与修复

1. **HUD 被中文模式强制阻塞(主因)**:SwitchTo 原流程 PostMessage → HKL 读回 → 200ms 稳定 → 1500ms 强制循环,HUD 最后才更新(记事本强制必败 = 白等 ~1.7s)。
   修复:HKL 读回验证成功即返回(HUD 立即更新,主中文 TIP 乐观按中文模式显示,Monitor 对账);强制改后台 Task(50ms 稳定 + 前 300ms 最多 4 次 ×75ms,失败转 Monitor 自愈),带代际计数,新切换使旧强制作废;不占切换队列。
2. **列表高亮等切换完成**:新增 `CycleSession.PreviewNext/CommitIndex` 与 `HudController.NotifyCyclePreview` —— 按键到达即推进高亮(列表已显示时 UI 线程直改,~0ms),切换异步执行;完成按实际目标对账索引,失败 `NotifySwitchFailed` 回真实状态徽章。
3. **跨进程 SendMessage 阻塞风险**:新增 `ImeModeIO`(SendMessageTimeout 100ms + ABORTIFHUNG),Service/Monitor 全部 IME 读写改走它,超时按"读不到"降级。
4. **过渡动画**:`InputHudForm.FadeIn(90ms)/FadeOut(120ms)` —— ULW 的 SourceConstantAlpha 动画(位图缓存复用,不重绘),淡出完成后才 Hide 并触发 ListHidden;淡入可被新显示取消。
5. **可观测性**:`[Perf] 按键→切换返回` 与 `按键→HUD首帧` 计时日志(AppHost 记按键时间戳,贯穿通知链)。

## 实测数据(dist 版,记事本前台,keybd_event 合成按键)

```
[Perf] 按键→切换返回 62~63ms 成功            ← 原 ~1700ms(记事本强制必败场景)
[HUD] 显示 '中' … [Perf] 按键→HUD首帧 93~94ms   ← 徽章路径(含光标定位)
[HUD] 列表模式 active=1/3 … [Perf] 按键→HUD首帧 15ms / 0ms  ← 列表路径(预览即时)
[InputSourceService] … 后台强制读回 0x0001,NATIVE 已置位   ← 强制后台化且成功
```

- **3a**:切中文按键→HUD 首帧 93ms(原 1.7s 量级)。
- **3b**:连按列表高亮即时推进:第二按后列表首帧 15ms/0ms(先于切换完成的 ~62ms);截图 `docs/shots/crop-perf-b.png`(EN 行高亮)。
- **3c**:淡入淡出期间多次截屏无残影/无直角闪帧(90/120ms 窗口过快难以稳定捕捉中间帧;边缘平滑,ULW alpha 正确)。
- **3d**:强制进行期间(+150ms)再按,第二次切换立即执行(连续两次 `按键→切换返回 62ms`,无排队)。
- build 0 警告 0 错误;dotnet test **116/116**(新增 CycleSessionIndexTests ×4);publish 双版本成功。

## 附带发现(重要)

本轮实测期间微软拼音 TIP 从系统消失:`Get-WinUserLanguageList` 返回空条目;Phase0 原始枚举显示拼音 profile `dwFlags=0x2`(ENABLED 位被清除,原 0x3)。非本工具原因(枚举器如实反映)。影响:
- 路径"TIP 恢复后自动归一化"无法端到端复验(无 TIP 可恢复);
- 已加保底:`EnsureTipRecoveryTask` —— 冷启动重采(200ms/1s)仍无 TIP 时,后台每 5s 强制重枚举一次直至恢复(转变检测触发归一化);
- 当前 settings.json 的 `hkl:08040804` 在 TIP 缺席时是合法主源;TIP 重新启用后会被自动归一化为拼音 guid(第五轮机制)。
- **待人工**:确认微软拼音是否被有意移除;如需恢复,Windows 设置 → 语言 → 中文 → 添加键盘 → 微软拼音。

---

# 流畅度二轮(根因 A/B/C 修复)记录(2026-10-18 第七轮)

## 根因 → 方案

- **根因 A(HUD 首帧被光标定位拖住,实测 1313ms)**:显示前 await UIA 线程定位,该线程同时处理焦点事件的跨进程 UIA 查询(浏览器偶发数百 ms~1s)。→ 显示请求到达后**立即用最佳已知位置显示**:① 同 hwnd 光标缓存(FocusMonitor 维护)② GetGUIThreadInfo(同步微秒级)③ 该 hwnd 上次 HUD 位置;④ 都无才等异步定位(500ms 上限)。异步精修返回后差异 >8px 才移动(`CaretPlacement.NearEqual` 纯函数 + 测试)。
- **根因 B(UI 线程跨进程调用)**:ShowForCaret 同步调 `service.GetForegroundState()`(SendMessageTimeout 100ms+,与 service 锁竞争更久)→ 连按预览被堵。→ 改读 `InputSourceMonitor.LatestState`(200ms 采样缓存)+ `GetSourcesSnapshot()`(volatile 无锁快照,过期返回旧值并异步刷新);HUD UI 线程路径零跨进程调用。
- **根因 C(时间戳失真,连按序列被吃掉)**:状态机时间戳用事件**处理**时刻。→ HookEvent 全系列携带回调时刻时间戳(AtMs),状态机喂原始时间;日志文件 IO 改独立写线程(BlockingCollection),消费线程不碰磁盘。
- **附带发现**:Monitor 自愈强制(ReenforceNativeMode)走 service 工作队列,1500ms 循环实测阻塞一次切换 ~600ms(切换返回 687ms)。→ 自愈改后台 Task + 代际作废。

## 实测数据(dist 版,合成按键;统计脚本见 docs/scripts/test-perf2.ps1)

| 指标 | n | min | p50 | p90 | p100 |
| --- | --- | --- | --- | --- | --- |
| 徽章首帧(记事本 20 + Chrome 地址栏 10 + 其余) | 40 | 62 | 63 | 79 | **94ms** |
| 列表首帧 | 10 | 0 | 15 | 16 | 16ms |
| 预览帧(列表可见时高亮推进) | 8 | 0 | 0 | 15 | 16ms |
| 切换返回 | 60 | 47 | 63 | 63 | 78ms |

- 位置来源分布:cache 39/40(剩 1 次 uia,94ms);上一轮的 1313ms/687ms 离群值消失。
- 连按 10 组全部进入列表(10 次"列表模式"首显);自愈强制的 11 轮 SET 全部在后台,队列无阻塞。

## 工程事故与恢复(如实记录)

本轮中一次补丁脚本误调用把 `InputSourceService.cs` 截断为 0 字节(打开即截断、未写回)。恢复方式:用 ilspycmd 反编译上次成功构建的 `bin/Debug` DLL 还原该文件,补回可空注解(0 警告),重新打上"自愈后台化"补丁;以 119 个单测 + 上方全量实测确认行为无损。建议后续纳入版本控制(git)避免单点风险。

- build 0 警告 0 错误;dotnet test **119/119**(新增 CaretPlacementTests ×3);publish 双版本成功。
