# Phase 0 验证记录

> 本文档所有输出均为 2026-10-10 在本机真实运行 `CapsSwitch.Phase0` 的原始结果,未运行的实验项只列在"待人工验证"。交互验证一节为在记事本/Chrome 前台焦点下的实测。

## 验证环境

| 项目 | 实际值 |
| --- | --- |
| Windows 版本 | Microsoft Windows NT 10.0.26200.0(Windows 11,build 26200;msctf.dll 10.0.26100.8875) |
| .NET SDK | 10.0.401 |
| 目标框架 | net10.0-windows(UseWindowsForms + UseWPF,无第三方 NuGet 包) |

已安装/已启用输入源(以 `enum` 实测为准):

- `GetKeyboardLayoutList` 加载的 HKL:`0x08040804`(中文/微软拼音)、`0x04090409`(US 键盘)
- TSF `EnumProfiles` 显示已启用(`dwFlags=0x3`)的键盘类 Profile:
  - 微软拼音:`langid=0x0804 clsid={81d4e9c9-1d3b-41bc-9e6c-4b40bf79e35E} guidProfile={fa550b04-5ad7-411f-a5ac-ca038ec515d7}`(注册表 Description="Microsoft Pinyin",dwFlags=0x3 已启用+活动)
  - 日语 Microsoft IME:`langid=0x0411 clsid={03b5835f-f03c-411b-9ce2-aa23e1171e36}`(dwFlags=0,已注册未启用)
  - US 键盘:`langid=0x0409` 键盘布局伪 Profile(`hkl=0x04090409`,dwFlags=0x2)
  - 键盘布局伪 Profile `langid=0x0804 hkl=0x08040804`(中文键盘布局)

## 自动化验证结果

### 构建

```
已成功生成。
    0 个警告
    0 个错误
```

### `all`(enum + current + pinyin)

`dotnet run --project CapsSwitch.Phase0 -- all`,退出码 0。

`current` 输出:

```
=== current: 当前活动 Profile (GUID_TFCAT_TIP_KEYBOARD) ===
GetActiveProfile HRESULT=0x00000000 (S_OK)
  langid=0x0804 clsid={81d4e9c9-1d3b-41bc-9e6c-4b40bf79e35e} guidProfile={fa550b04-5ad7-411f-a5ac-ca038ec515d7}
  catid={34745c63-b2f0-4784-8b67-5e12c8701a31} hklSubstitute=0x00000000 hkl=0x00000000 dwCaps=0x5003002e dwFlags=0x00000003
  描述=Microsoft Pinyin  友好名=微软拼音 (Microsoft Pinyin)
当前线程 HKL=0x08040804
```

`enum` 要点:注册表扫描到 31 个 (clsid, langid, guidProfile) 三元组;`EnumProfiles` 逐 langid 枚举成功,键盘布局类 Profile(clsid=Empty)与 TIP Profile 均列出;`GetKeyboardLayoutList` 返回 2 个 HKL(0x08040804、0x04090409)。`IEnumTfInputProcessorProfiles` 的 Clone 在前 / Next 在后顺序验证通过。

`pinyin` 输出:

```
--- ① 注册表 HKCU\SOFTWARE\Microsoft\InputMethod\Settings\CHS 全量转储 ---
  Enable Dynamic Candidate Ranking = 1 (Int32)
  Enable Cloud Candidate = 1 (Int32)
  EnableExtraDomainType = 1 (Int32)
  ToolBarView = 190 (Int32)
  Enable self-learning = 1 (Int32)
  EnableSmartSelfLearning = 1 (Int32)
  EnableLiveSticker = 0 (Int32)
  Enable EUDP = 1 (Int32)
  English Switch Key = 6 (Int32)
  SmartFuzzyPairTriggerCount = 3 3 0 0 0 2 1 47 51 0 (String)
  AutoShowCloudSuggestion = 0 (Int32)
--- ② 前台窗口 IME 转换模式检测 ---
  hwnd=0x000200e4 pid=44376(Termius) tid=44400 HKL=0x08040804
  IME 转换模式=0x0401 (中文(IME_CMODE_NATIVE))
  IME 打开状态=开
```

注:`English Switch Key = 6` 对应微软拼音"按键"设置里的中英文切换键配置(6 = Shift,实测本机当前即按 Shift 切换)。MVP 阶段通过系统设置界面引导用户关闭,不直接改写该注册表值(未验证所有取值含义)。

### `fg`

```
=== fg: 前台窗口线程输入法状态 ===
  hwnd=0x00020b22 pid=61964(chrome) tid=45456 HKL=0x08040804
  IME 转换模式=0x0401 (中文(IME_CMODE_NATIVE))
  IME 打开状态=开
```

`ImmGetDefaultIMEWnd` + `WM_IME_CONTROL` / `IMC_GETCONVERSIONMODE` / `IMC_GETOPENSTATUS` 链路验证通过;无 IME 的前台窗口会打印"前台线程无关联 IME 窗口"而不会崩溃。

### `activate zh`(微软拼音,自动化阶段,前台为 chrome)

```
ActivateProfile(TF_PROFILETYPE_INPUTPROCESSOR, dwFlags=0) HRESULT=0x80070057 (失败)
ActivateProfile(TF_PROFILETYPE_INPUTPROCESSOR, TF_IPPMF_FORPROCESS) HRESULT=0x00000000 (S_OK)
  [本进程视角] GetActiveProfile ... guidProfile={fa550b04-...}   ← 仅本进程视角生效
  前台 HKL=0x04090409(不变)
ActivateProfile(TF_PROFILETYPE_INPUTPROCESSOR, TF_IPPMF_FORSESSION) HRESULT=0x00000000 (S_OK)
  [本进程视角] guidProfile={fa550b04-...}
  前台 HKL=0x08040804 ← 前台线程随之切换
```

初步结论(后被记事本交互实验修正,见下节):`FORSESSION` 能改变跟随会话默认值的线程;`FORPROCESS` 只影响本进程;`dwFlags=0` 在目标 Profile 非当前活动时报 `E_INVALIDARG`。

### `activate en`(US 键盘)

```
ActivateProfile(TF_PROFILETYPE_KEYBOARDLAYOUT, dwFlags=0) HRESULT=0x80070057 (E_INVALIDARG)
ActivateProfile(TF_PROFILETYPE_KEYBOARDLAYOUT, TF_IPPMF_ENABLEPROFILE) HRESULT=0x80004005 (E_FAIL)
ActivateProfile(TF_PROFILETYPE_KEYBOARDLAYOUT, ENABLEPROFILE|FORPROCESS) HRESULT=0x80004005 (E_FAIL)
ActivateProfile(TF_PROFILETYPE_KEYBOARDLAYOUT, ENABLEPROFILE|FORSESSION) HRESULT=0x80004005 (E_FAIL)
TSF KEYBOARDLAYOUT 激活在本机返回失败,回退到 WM_INPUTLANGCHANGEREQUEST:
PostMessage(WM_INPUTLANGCHANGEREQUEST, hkl=0x04090409) → 已投递
  前台 HKL=0x04090409,IME 打开状态=关 ← 切换生效
```

结论:**本机(Windows 11 build 26200)上 `TF_PROFILETYPE_KEYBOARDLAYOUT` 类型的 ActivateProfile 不可用**(0/ENABLEPROFILE 组合均失败),切到纯键盘布局需走 `WM_INPUTLANGCHANGEREQUEST`。

### `activate-msg`

- `activate-msg 00000409`:前台 HKL 变为 `0x04090409`,IME 关闭 → 生效。
- `activate-msg E0200804`:前台 HKL 无变化。**向 TIP 的 substitute HKL(E0200804)发请求不能恢复微软拼音**;对已加载的中文键盘布局 HKL(08040804)发消息则生效(见交互验证)。

## 交互验证结果(记事本 / Chrome 前台实测)

### 跨进程激活记事本

记事本(Win32 编辑区)置于前台后:

1. `activate en`:TSF KEYBOARDLAYOUT 四种 flag 组合全部失败,自动回退 `WM_INPUTLANGCHANGEREQUEST(0x04090409)` → **记事本线程 HKL 变为 0x04090409,IME 转换模式 0x0000,生效**。
2. `activate zh`(记事本处于 US 键盘状态):FORPROCESS / FORSESSION 均返回 S_OK,本进程视角 GetActiveProfile 变为微软拼音,**但记事本线程 HKL 保持 0x04090409 不变**(等待 2 秒后复查仍不变)。

   修正结论:**FORSESSION 只改变"跟随会话默认值"的线程;被显式设置过线程级输入语言的线程(如被 WM_INPUTLANGCHANGEREQUEST 切换过的记事本)不跟随**。因此 TSF ActivateProfile 不能作为跨进程切换的主路径。

3. `activate-msg 08040804` → **记事本线程 HKL 变为 0x08040804,微软拼音恢复**,但 IME 转换模式=0x0000(**拼音英文模式**,需要额外恢复中文模式)。

结论:**主切换路径 = 向前台窗口 PostMessage `WM_INPUTLANGCHANGEREQUEST` + 目标输入源的已加载 HKL**(键盘布局和 TSF TIP 均适用),切换后必须读回验证。

### 微软拼音中英文模式控制(IMC_SETCONVERSIONMODE 跨进程)

Chrome 前台、微软拼音状态下(`mode=0x0401`,NATIVE|SYMBOL,中文模式):

| 操作 | 立即读回 | 700ms 后 |
| --- | --- | --- |
| `IMC_SETCONVERSIONMODE lParam=0x0001`(中文) | 0x0001 | 0x0001 |
| `IMC_SETCONVERSIONMODE lParam=0x0000`(英文) | 0x0000 | 0x0000 |
| `IMC_SETCONVERSIONMODE lParam=0x0001`(恢复) | 0x0401 | 0x0401 |

结论:**`WM_IME_CONTROL` / `IMC_SETCONVERSIONMODE` 可跨进程控制微软拼音的中英文模式**(NATIVE 位生效;SYMBOL 等附加位由拼音自身配置决定,设置时会被拼音按用户配置合并,恢复 0x0001 后拼音自行补回 0x0400 位)。记事本下首次 SET 实验结果不一致(读回延迟/焦点抖动),MVP 中每次设置后需轮询读回确认,失败时记录降级。

### watch 观察外部切换

`watch 12` 运行期间由另一进程执行 `activate-msg 04090409` 再 `activate-msg 08040804`,watch 两次均捕获得到:

```
现在: activeProfile ... guidProfile={fa550b04-...} | 前台 HKL=0x04090409 | 前台 IME 转换模式=0x0001
之前: activeProfile ... guidProfile={fa550b04-...} | 前台 HKL=0x08040804 | 前台 IME 转换模式=0x0401
```

结论:**200ms 轮询"前台线程 HKL + IME 转换模式"即可观测外部切换**,无需依赖 `ITfInputProcessorProfileActivationSink`(其只反映本进程 TSF 视角,对外部进程切换不敏感)。另外观察到微软拼音会偶发自行切换 SYMBOL 位(0x0401↔0x0001),监控逻辑应按 NATIVE 位判断中英文,忽略其他位抖动。

### focus 光标定位(记事本)

`focus 15` 运行期间在 Chrome 与记事本之间切换焦点,捕获到:

```
[13:25:07.663] 文本控件: ControlType=ControlType.Document Name="文本编辑器" FrameworkId=Win32 进程=Notepad
  TextPattern 可用,使用 GetSelection() 退化区间(等效插入符):
  caret(退化区间) 矩形=(71,150 1x52)
```

结论:UIA 全局焦点监听在独立 MTA 线程工作正常;记事本(Win32)暴露 `ControlType.Document` + TextPattern,**退化区间给出可用插入符矩形(71,150 1x52)**;同名事件会重复触发(需按 spec 合并去重);对无法识别的元素(Name 为空、无 TextPattern)能优雅降级不崩溃。

## 与预期不符的行为(重要)

1. **`ITfInputProcessorProfileMgr` vtable 与任务书给定签名不一致**。本机 Windows SDK(10.0.26100)`msctf.h` 实测:`ActivateProfile` 第一个参数是 `dwProfileType`(INPUTPROCESSOR=1 / KEYBOARDLAYOUT=2)、**最后一个**参数才是 `dwFlags`(共 6 参);`GetActiveProfile` 排在第 8 位(前面还有 `GetProfile`/`ReleaseInputProcessor`/`RegisterProfile`/`UnregisterProfile`)。按任务书顺序调用会直接调进 `GetProfile` 产生原生 0xC0000005 崩溃。代码已按 SDK 头文件修正。
2. **`GUID_TFCAT_TIP_KEYBOARD` 任务书值有误**。任务书给 `34745C63-B2F9-4784-AE17-10F52D336E95`,本机 `EnumProfiles` 实测键盘类 catid 为 `34745C63-B2F0-4784-8B67-5E12C8701A31`(用错误值调用 `GetActiveProfile` 返回 E_INVALIDARG)。
3. **`GetActiveProfile` 调用前必须自行填 `pProfile.dwSize`**(MSDN 要求),否则原生崩溃。
4. **COM interop 默认把失败 HRESULT 转成异常**,接口方法已全部加 `[PreserveSig]` 以获得可打印的原始 HRESULT。
5. **`TF_PROFILETYPE_KEYBOARDLAYOUT` 的 ActivateProfile 在本机完全不可用**(见上)。
6. **TSF `ActivateProfile(FORSESSION)` 不能切换已显式设置线程级输入语言的目标线程**(记事本实测不变),TSF 激活不能作为跨进程切换主路径。
7. **WPF 版 UIAutomationClient(.NET 10)没有 `TextPattern2`/`TextPatternRange`**(已对照 ref 包确认),无法直接 `GetCaretRange()`;`focus` 改用 `TextPattern.GetSelection()` 的退化区间(等效插入符位置)+ `GetGUIThreadInfo` fallback。Phase 2 若需精确 `GetCaretRange`,需走原生 UIA COM 互操作。
8. 注册表 Profile 描述路径正确(`HKLM\SOFTWARE\Microsoft\CTF\TIP\{clsid:B}\LanguageProfile\0x{langid:x8}\{guidProfile:B}` 的 `Description`),注意 C# 中 `Guid.ToString("B")` 自带花括号,不要重复包 `{{}}`。
9. `.NET 10 的 dotnet new sln` 默认生成 `.slnx`;本仓库按要求使用 `--format sln` 生成传统 `.sln`。
10. `LibraryImport` 源生成需要 `AllowUnsafeBlocks`;`UIAutomationClient` 需要 `UseWPF=true`(仅 `UseWindowsForms` 不够)。

## Phase 0 结论:MVP 可实施方案

基于以上实测,正式 MVP 的技术选型确定如下:

1. **输入源枚举**:TSF `ITfInputProcessorProfileMgr.EnumProfiles`(逐 langid)+ 注册表描述;输入源标识用 (langid, clsid, guidProfile) 三元组 + 其对应已加载 HKL。
2. **输入源切换(主路径)**:向前台窗口 `PostMessage(WM_INPUTLANGCHANGEREQUEST, 0, targetHkl)`;targetHkl 通过 `GetKeyboardLayoutList` / 注册表 preload/substitute 与 TSF Profile 的映射解析。TSF `ActivateProfile(FORSESSION)` 仅作为辅助手段(影响新线程默认值),不作为切换成功判据。
3. **切换验证(必做)**:切换后 100~500ms 内轮询读回 `GetKeyboardLayout(前台tid)`(+ IME 转换模式),与目标比对;不一致时重试一次后报告失败,绝不用"请求已发出"冒充"切换成功"。
4. **微软拼音中文模式**:激活拼音后用 `WM_IME_CONTROL / IMC_SETCONVERSIONMODE` 置 NATIVE 位并读回确认;Shift 切换键通过系统设置引导用户关闭(`HKCU\...\Settings\CHS` 的 `English Switch Key`,MVP 只检测提示,不改写)。
5. **外部切换观测**:后台线程 200ms 轮询前台线程 HKL + IME 转换模式(NATIVE 位),驱动 HUD 更新;同时监听前台窗口变化事件。
6. **焦点检测与光标定位**:UIA FocusChanged(独立 MTA 线程)+ TextPattern 退化区间 / `GetGUIThreadInfo` fallback,去重合并后交 UI 线程显示 HUD。
7. **键盘 Hook 与状态机**:`WH_KEYBOARD_LL` + 纯内存状态机(Hook 回调零 COM/UIA 调用),符合 spec,无需进一步验证。
8. **已知受限场景(记录在 README)**:管理员权限窗口/UAC 桌面/锁屏下普通权限进程的消息投递与 Hook 会被系统限制;个别应用(终端类)的 UIA/HKL 行为待 Phase 2 手工验证。

## 待人工验证(留给 Phase 1/2 集成测试)

- [ ] Win+Space 等系统快捷键切换时 watch/HUD 的反映(本次以跨进程 activate-msg 模拟外部切换,未按真实 Win+Space)。
- [ ] 浏览器地址栏 / 网页输入框 / VS Code 的 TextPattern 光标矩形准确性。
- [ ] 多显示器与 DPI 缩放下的坐标换算(Phase 2)。
- [ ] 记事本中 `IMC_SETCONVERSIONMODE` 读回延迟的稳定性(需要在 MVP 中以轮询确认逻辑覆盖)。
