# Phase 1 集成测试记录

> 2026-10-10 本机实测。方法:应用后台运行(`run`),用 PowerShell `keybd_event` 合成 Caps Lock 按键(本进程模拟输入以魔术 dwExtraInfo 过滤,外部合成按键视为真实输入),前台为记事本,通过 `GetKeyboardLayout(前台tid)` + `WM_IME_CONTROL/IMC_GETCONVERSIONMODE` + UIA 文本回读三重验证。

## 环境前提

- 测试前确认退出/暂停了 AutoHotkey 与 PowerToys Keyboard Manager 的按键映射(二者此前把 CapsLock 重映射为"切换输入语言",是测试初期一切异常翻转的来源;且以管理员权限运行,非提升进程杀不掉)。
- 已安装输入源:微软拼音(TIP)+ English (US) Keyboard。

## 功能结果(全部通过)

| 场景 | 期望 | 实测 |
| --- | --- | --- |
| 短按 Caps(拼音→EN) | HKL 0x08040804→0x04090409 | ✅,日志 `[Switch] 当前=Microsoft Pinyin → 目标 English (US) Keyboard: 成功` |
| 短按 Caps(EN→拼音) | 切回拼音 | ✅,HKL 读回一致 |
| 切回拼音强制中文模式 | IME NATIVE 位置位 | ✅ 生效(首次会话上下文);⚠️ 往返后失效,见下文限制 |
| 连按(间隔 150ms) | seq=0 首选切换,seq=1 循环下一源 | ✅(日志 sequence=0/1 正确,目标按 OrderedSourceIds 推进) |
| 长按 600ms ×1 | 系统 CapsLock ON,不切输入源 | ✅ 打字验证:输入 'a' 得 'A';HKL 不变 |
| 长按 600ms ×2 | CapsLock OFF | ✅ 打字验证:得 'a' |
| Caps+A 组合 | 不切换、不锁大写,'a' 正常输入 | ✅ text 追加 'a',HKL/CapsLock 均不变 |
| 枚举稳定性 | 始终 2 源(拼音+EN) | ✅ SourceListBuilder 丢弃同 HKL 伪布局;丢失 TIP 时延迟重试;Monitor 发现未知 HKL 时缓存自愈 |
| 退出清理 | Hook 卸载、进程干净退出 | ✅ |

单元测试 47/47 全绿(状态机 17、策略 11、SourceListBuilder 9、SwitchResult 4、节流 6)。

## 拼音中文模式:限制定位过程(TSF 原生应用)

记事本为 TSF 原生应用,实测结论:

1. **决定打字行为的是顶层窗口 IMM 上下文**:modeTop=0x0000→打字直接提交英文;0x0001→进入拼音候选。焦点子控件上下文是"幻影"(读回 0x0401,打字仍英文),读写一律走顶层窗口路径。
2. **全新上下文**:顶层 IMC_SETCONVERSIONMODE 置 NATIVE **一次即生效**(打字验证进入候选)。
3. **经"拼音→EN 键盘→拼音"往返后**:拼音以英文模式(0x0000)重新激活,且此后该上下文生命周期内一切外部手段均失效:
   - IMC_SETCONVERSIONMODE(0x1/0x401/0x409 各种掩码,重复 10 次/1.5s)❌
   - IMC_SETOPENSTATUS ❌
   - 等待 5s / 切走再切回焦点 ❌
   - TSF ActivateProfile 六种 flag 组合(含 DONTCARECURRENTINPUTLANGUAGE)全部 S_OK 但不改变目标线程 ❌
   - 注入 Shift 单击 ❌
4. IMM 桥接类应用(Chrome)无此问题:读写模式、强制中文均正常。
5. Win+Space 注入实验:未能复现系统切换器行为(HKL 未变),且发现 IMM 模式读数在 TSF 原生应用里可能与实际打字行为脱节——**以行为(打字是否进候选)为最终判据**。

## 产品化处理

- 切换后照常尝试 IMC 强制(对 Chrome 类有效),失败降级为警告日志,不再误报整体切换失败。
- Monitor 自愈:发现"前台=首选中文 TIP 且英文模式"时自动补一次强制;同一窗口只纠一次(之后视为用户手动 Shift 切英文,不抢回)。
- 系统层缓解:引导用户确认微软拼音"默认模式=中文"(设置→时间和语言→输入→微软拼音→常规),对新窗口上下文有效。
- 该限制写入 README 已知限制。
