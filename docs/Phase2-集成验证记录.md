# Phase 2 集成验证记录

> 2026-10-11 本机实测。方法:应用后台运行(`run`),记事本前台,截屏工具为自写 BitBlt(SRCCOPY|CAPTUREBLT,普通 CopyFromScreen 抓不到 WS_EX_LAYERED 窗口)。截图存档于 `docs/shots/`。

## 结果

| 场景 | 期望 | 实测 |
| --- | --- | --- |
| 文本控件获得焦点 | 光标旁显示当前输入源 | ✅ `[HUD] 显示 '中' @ (80,170)`,光标定位 method=GuiThreadInfo rect=(71,174 1x28) |
| 外部切换输入源(WM_INPUTLANGCHANGEREQUEST) | HUD 更新为新状态 | ✅ `[HUD] 显示 'EN' @ (80,169)`,截图 `hud-b-en.png` 可见深色浮窗位于光标右侧 |
| 自动消失 | 1000ms 后隐藏 | ✅ 日志显示/隐藏时间差 1.001s |
| 不抢焦点 | 记事本保持前台、插入符正常 | ✅ 截屏中记事本焦点与光标正常 |
| 定位降级 | TextPattern2→退化区间/GetGUIThreadInfo→BoundingRectangle | ✅ 记事本走 GuiThreadInfo;不支持控件落到 BoundingRectangle(日志实测) |

## 环境备注

- 测试中观察到本机某后台窗口(0x000627c6)偶发抢占前台焦点,会中断 HUD 更新链;非本项目问题。
- EN→拼音切换后拼音停留在英文模式(已知 Phase 1 限制,记事本 TSF 原生应用),HUD 如实显示真实模式。
- 截屏工具须知:`Graphics.CopyFromScreen` 默认不含 layered 窗口,必须带 CAPTUREBLT;且该方法对组合 rop 枚举值有校验,直接用 P/Invoke BitBlt 更稳(见 `docs/scripts/test-hud-shots3.ps1`)。

## 单元测试

70/70 全绿(新增 TextInputDetector 9 例、HudTextMapper 7 例等)。
