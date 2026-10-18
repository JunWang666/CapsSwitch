using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CapsSwitch.App.Interop;
using CapsSwitch.App.Logging;

namespace CapsSwitch.App.InputSources;

public sealed class InputSourceService : IInputSourceService, IDisposable
{
	public static readonly Guid MicrosoftPinyinProfileGuid = new Guid("FA550B04-5AD7-411F-A5AC-CA038EC515D7");

	private const int VerifyPollIntervalMs = 50;

	private const int VerifyTimeoutMs = 500;

	private readonly Action<string> _log;

	private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();

	private readonly Thread _worker;

	private readonly HashSet<string> _emittedDiagLogs = new HashSet<string>(StringComparer.Ordinal);

	private volatile List<InputSourceInfo>? _cache;

	private long _cacheAtMs;

	private const long CacheTtlMs = 10000L;

	private volatile bool _disposed;

	private long _switchCount;

	private int _recoveryTaskStarted;

	private bool? _prevEnumHadTip;

	private int _enforceGeneration;

	private int _disposedFlag;

	public long SwitchCount => Interlocked.Read(in _switchCount);

	public Action? OnTipsRecovered { get; set; }

	public InputSourceService(Action<string> log)
	{
		_log = (string msg) =>
		{
			if (msg.StartsWith("[SourceListBuilder]", StringComparison.Ordinal))
			{
				Logger.Verbose(msg);
			}
			else
			{
				log(msg);
			}
		};
		_worker = new Thread(WorkerLoop)
		{
			IsBackground = true,
			Name = "CapsSwitch-InputSourceWorker"
		};
		_worker.Start();
	}

	public IReadOnlyList<InputSourceInfo> GetSources()
	{
		TaskCompletionSource<IReadOnlyList<InputSourceInfo>> tcs = new TaskCompletionSource<IReadOnlyList<InputSourceInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (_disposed)
		{
			return Array.Empty<InputSourceInfo>();
		}
		_queue.Add(() =>
		{
			tcs.SetResult(EnumSourcesCore());
		});
		return tcs.Task.Result;
	}

	public InputSourceInfo? GetCurrent()
	{
		TaskCompletionSource<InputSourceInfo?> tcs = new TaskCompletionSource<InputSourceInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (_disposed)
		{
			return null;
		}
		_queue.Add(() =>
		{
			tcs.SetResult(GetCurrentCore(null));
		});
		return tcs.Task.Result;
	}

	public bool SwitchTo(InputSourceInfo target)
	{
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (_disposed)
		{
			return false;
		}
		_queue.Add(() =>
		{
			tcs.SetResult(SwitchToCore(target));
		});
		return tcs.Task.Result;
	}

	private void WorkerLoop()
	{
		foreach (Action item in _queue.GetConsumingEnumerable())
		{
			try
			{
				item();
			}
			catch (Exception ex)
			{
				_log("[InputSourceService] 工作项异常: " + ex.GetType().Name + ": " + ex.Message);
			}
		}
	}

	private List<InputSourceInfo> EnumSourcesCore()
	{
		long tickCount = Environment.TickCount64;
		if (_cache != null && tickCount - _cacheAtMs < 10000)
		{
			return _cache;
		}
		List<InputSourceInfo> list = EnumerateSources();
		if (_cache != null)
		{
			HashSet<string> hashSet = (from c in _cache
				where c.IsTsfTip
				select c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
			hashSet.ExceptWith(list.Select((InputSourceInfo r) => r.Id));
			if (hashSet.Count > 0)
			{
				_log("[InputSourceService] 枚举疑似不完整,丢失已启用 TIP: [" + string.Join(", ", hashSet) + "],200ms 后重试");
				Thread.Sleep(200);
				List<InputSourceInfo> list2 = EnumerateSources();
				List<string> list3 = hashSet.Except(list2.Select((InputSourceInfo r) => r.Id), StringComparer.OrdinalIgnoreCase).ToList();
				if (list3.Count < hashSet.Count)
				{
					_log("[InputSourceService] 重试恢复 TIP: [" + string.Join(", ", hashSet.Except(list3)) + "]");
				}
				if (list3.Count > 0)
				{
					_log("[InputSourceService] 重试后仍丢失 TIP: [" + string.Join(", ", list3) + "]");
				}
				list = list2;
			}
		}
		_cache = list;
		_cacheAtMs = Environment.TickCount64;
		return list;
	}

	public IReadOnlyList<InputSourceInfo> GetSourcesSnapshot()
	{
		List<InputSourceInfo>? cache = _cache;
		if (cache != null && Environment.TickCount64 - Volatile.Read(in _cacheAtMs) < 10000)
		{
			return cache;
		}
		if (!_disposed)
		{
			_queue.Add(() =>
			{
				EnumSourcesCore();
			});
		}
		return cache ?? new List<InputSourceInfo>();
	}

	public void InvalidateCache()
	{
		if (_disposed)
		{
			return;
		}
		_queue.Add(() =>
		{
			if (_cache != null)
			{
				_log("[InputSourceService] 缓存已失效,下次访问将重新枚举");
			}
			_cache = null;
		});
	}

	private List<InputSourceInfo> EnumerateSources()
	{
		List<InputSourceInfo> list = Collect();
		if (list.All((InputSourceInfo s) => !s.IsTsfTip))
		{
			Thread.Sleep(200);
			List<InputSourceInfo> list2 = Collect();
			if (list2.Any((InputSourceInfo s) => s.IsTsfTip))
			{
				_log("[InputSourceService] 冷启动枚举未见 TIP,200ms 重试恢复");
				list = list2;
			}
			else
			{
				Thread.Sleep(1000);
				List<InputSourceInfo> list3 = Collect();
				if (list3.Any((InputSourceInfo s) => s.IsTsfTip))
				{
					_log("[InputSourceService] 冷启动枚举未见 TIP,1s 二次重试恢复");
					list = list3;
				}
			}
		}
		TrackTipRecovery(list);
		if (!list.Any((InputSourceInfo s) => s.IsTsfTip))
		{
			EnsureTipRecoveryTask();
		}
		return list;
		List<InputSourceInfo> Collect()
		{
			nint[] loadedLayouts = GetLoadedLayouts();
			List<TF_INPUTPROCESSORPROFILE> list4 = new List<TF_INPUTPROCESSORPROFILE>();
			ITfInputProcessorProfileMgr? tfInputProcessorProfileMgr = null;
			try
			{
				tfInputProcessorProfileMgr = TsfInterop.CreateProfileMgr();
				foreach (short item in from x in (from p in TsfInterop.EnumRegistryProfiles()
						select p.langid).Distinct()
					orderby x
					select x)
				{
					list4.AddRange(TsfInterop.EnumProfiles(tfInputProcessorProfileMgr, item));
				}
			}
			catch (Exception ex)
			{
				_log("[InputSourceService] 枚举输入源异常: " + ex.GetType().Name + ": " + ex.Message);
			}
			finally
			{
				if (tfInputProcessorProfileMgr != null)
				{
					Marshal.ReleaseComObject(tfInputProcessorProfileMgr);
				}
			}
			return SourceListBuilder.Build(list4, loadedLayouts, LogDiagOnce);
		}
	}

	private void EnsureTipRecoveryTask()
	{
		if (Interlocked.Exchange(ref _recoveryTaskStarted, 1) != 0)
		{
			return;
		}
		Task.Run(async () =>
		{
			while (!_disposed)
			{
				await Task.Delay(5000);
				if (_disposed || _prevEnumHadTip == true)
				{
					break;
				}
				_queue.Add(() =>
				{
					_cache = null;
					EnumSourcesCore();
				});
			}
		});
	}

	private void TrackTipRecovery(List<InputSourceInfo> result)
	{
		bool flag = result.Any((InputSourceInfo s) => s.IsTsfTip);
		if ((_prevEnumHadTip == false) & flag)
		{
			_log("[InputSourceService] 枚举恢复出 TIP,触发配置归一化重跑");
			try
			{
				OnTipsRecovered?.Invoke();
			}
			catch (Exception ex)
			{
				_log("[InputSourceService] 归一化重跑回调异常: " + ex.GetType().Name);
			}
		}
		_prevEnumHadTip = flag;
	}

	private void LogDiagOnce(string message)
	{
		if (_emittedDiagLogs.Add(message))
		{
			_log(message);
		}
	}

	private static nint[] GetLoadedLayouts()
	{
		int keyboardLayoutList = Win32Native.GetKeyboardLayoutList(0, null);
		nint[] array = new nint[keyboardLayoutList];
		Win32Native.GetKeyboardLayoutList(keyboardLayoutList, array);
		return array;
	}

	private InputSourceInfo? GetCurrentCore(IReadOnlyList<InputSourceInfo>? knownSources)
	{
		IReadOnlyList<InputSourceInfo> sources = knownSources ?? EnumSourcesCore();
		nint foregroundWindow = Win32Native.GetForegroundWindow();
		if (foregroundWindow == IntPtr.Zero)
		{
			return null;
		}
		uint windowThreadProcessId = Win32Native.GetWindowThreadProcessId(foregroundWindow, out var _);
		if (windowThreadProcessId == 0)
		{
			return null;
		}
		nint keyboardLayout = Win32Native.GetKeyboardLayout(windowThreadProcessId);
		return MatchByHkl(sources, keyboardLayout);
	}

	public static InputSourceInfo? MatchByHkl(IReadOnlyList<InputSourceInfo> sources, nint hkl)
	{
		// 同 HKL 可能同时对应 TIP 和键盘布局伪项,优先取 TIP(信息更具体)
		InputSourceInfo? layoutFallback = null;
		foreach (var s in sources)
		{
			if (s.Hkl != hkl)
				continue;
			if (s.IsTsfTip)
				return s;
			layoutFallback ??= s;
		}
		if (layoutFallback is not null)
			return layoutFallback;
		// 兜底:低 16 位 langid 匹配(同一布局的不同 HKL 表示)
		long lang = ((IntPtr)hkl).ToInt64() & 0xFFFF;
		foreach (var s in sources)
			if ((s.Hkl.ToInt64() & 0xFFFF) == lang)
				return s;
		return null;
	}

	private bool SwitchToCore(InputSourceInfo target)
	{
		Interlocked.Increment(ref _switchCount);
		nint hwnd = Win32Native.GetForegroundWindow();
		if (hwnd == IntPtr.Zero)
		{
			_log("[InputSourceService] 切换→" + target.DisplayName + ": 无前台窗口,失败");
			return false;
		}
		if (!Win32Native.PostMessageW(hwnd, 80u, IntPtr.Zero, target.Hkl))
		{
			_log($"[InputSourceService] 切换→{target.DisplayName}: PostMessage 失败 LastError=0x{Marshal.GetLastWin32Error():x8}");
			return false;
		}
		uint windowThreadProcessId = Win32Native.GetWindowThreadProcessId(hwnd, out var _);
		bool flag = false;
		long num = Environment.TickCount64 + 500;
		while (Environment.TickCount64 <= num)
		{
			Thread.Sleep(50);
			if (Win32Native.GetKeyboardLayout(windowThreadProcessId) == target.Hkl)
			{
				flag = true;
				break;
			}
		}
		if (!flag)
		{
			_log($"[InputSourceService] 切换→{target.DisplayName}: 500ms 内未验证到 HKL=0x{((IntPtr)target.Hkl).ToInt64():x8},失败");
			return false;
		}
		if (target.IsTsfTip || (target.LangId & 0xFFFF) == 2052)
		{
			int gen = Interlocked.Increment(ref _enforceGeneration);
			Task.Run(() =>
			{
				EnforceNativeQuick(hwnd, target.DisplayName, gen);
			});
		}
		_log("[InputSourceService] " + new SwitchResult(HklVerified: true, null).Describe(target.DisplayName) + "(中文模式强制后台化)");
		return true;
	}

	private void EnforceNativeQuick(nint hwnd, string targetName, int generation)
	{
		try
		{
			Thread.Sleep(50);
			long num = Environment.TickCount64 + 300;
			for (int i = 1; i <= 4; i++)
			{
				if (generation != Volatile.Read(in _enforceGeneration))
				{
					return;
				}
				nint num2 = Win32Native.ImmGetDefaultIMEWnd(hwnd);
				if (num2 == IntPtr.Zero)
				{
					_log("[InputSourceService] " + targetName + ": 前台无 IME 窗口,跳过后台强制");
					return;
				}
				if (!ImeModeIO.TryGetConversionMode(num2, out var mode))
				{
					Logger.Verbose("[InputSourceService] " + targetName + ": 转换模式读不到(超时/繁忙),后台强制中止");
					return;
				}
				if ((mode & 1) != 0)
				{
					_log($"[InputSourceService] {targetName}: 后台强制读回 0x{mode:x4},NATIVE 已置位");
					return;
				}
				ImeModeIO.TrySetConversionMode(num2, mode | 1);
				if (i < 4 && Environment.TickCount64 + 75 <= num)
				{
					Thread.Sleep(75);
				}
			}
			_log("[InputSourceService] " + targetName + ": 后台强制 300ms 未确认,转交 Monitor 自愈");
		}
		catch (Exception ex)
		{
			_log("[InputSourceService] 后台强制异常(忽略): " + ex.GetType().Name);
		}
	}

	public (InputSourceInfo? source, bool nativeMode)? GetForegroundState()
	{
		TaskCompletionSource<(InputSourceInfo?, bool)?> tcs = new TaskCompletionSource<(InputSourceInfo?, bool)?>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (_disposed)
		{
			return null;
		}
		_queue.Add(() =>
		{
			tcs.SetResult(SampleForegroundCore());
		});
		return tcs.Task.Result;
	}

	private (InputSourceInfo?, bool)? SampleForegroundCore()
	{
		nint foregroundWindow = Win32Native.GetForegroundWindow();
		if (foregroundWindow == IntPtr.Zero)
		{
			return null;
		}
		uint windowThreadProcessId = Win32Native.GetWindowThreadProcessId(foregroundWindow, out var _);
		if (windowThreadProcessId == 0)
		{
			return null;
		}
		nint keyboardLayout = Win32Native.GetKeyboardLayout(windowThreadProcessId);
		InputSourceInfo? item = MatchByHkl(EnumSourcesCore(), keyboardLayout);
		bool item2 = false;
		nint num = Win32Native.ImmGetDefaultIMEWnd(foregroundWindow);
		if (num != IntPtr.Zero && ImeModeIO.TryGetConversionMode(num, out var mode))
		{
			item2 = (mode & 1) != 0;
		}
		return (item, item2);
	}

	public void ReenforceNativeMode()
	{
		if (_disposed)
		{
			return;
		}
		int gen = Interlocked.Increment(ref _healGeneration);
		_ = Task.Run(() =>
		{
			try
			{
				nint foregroundWindow = Win32Native.GetForegroundWindow();
				if (foregroundWindow == IntPtr.Zero)
				{
					return;
				}
				uint tid = Win32Native.GetWindowThreadProcessId(foregroundWindow, out _);
				if (tid == 0)
				{
					return;
				}
				nint hkl = Win32Native.GetKeyboardLayout(tid);
				string targetName = MatchByHkl(GetSourcesSnapshot(), hkl)?.DisplayName ?? "前台";
				EnsureNativeConversionMode(foregroundWindow, targetName, gen);
			}
			catch (Exception ex)
			{
				_log($"[InputSourceService] 自愈强制异常(忽略): {ex.GetType().Name}");
			}
		});
	}

	private int _healGeneration;

	private bool? EnsureNativeConversionMode(nint hwnd, string targetName, int generation = -1)
	{
		nint num = Win32Native.ImmGetDefaultIMEWnd(hwnd);
		if (num == IntPtr.Zero)
		{
			_log("[InputSourceService] " + targetName + ": 前台无 IME 窗口,跳过转换模式设置");
			return null;
		}
		long num2 = Environment.TickCount64 + 1500;
		int mode;
		while (true)
		{
			if (generation >= 0 && generation != Volatile.Read(ref _healGeneration))
				return null; // 已有新的自愈/切换,本轮作废
			if (!ImeModeIO.TryGetConversionMode(num, out mode))
			{
				_log("[InputSourceService] " + targetName + ": 转换模式读不到(超时/繁忙),本轮自愈跳过");
				return false;
			}
			if ((mode & 1) != 0)
			{
				_log($"[InputSourceService] {targetName}: 转换模式读回 0x{mode:x4},NATIVE 已置位");
				return true;
			}
			ImeModeIO.TrySetConversionMode(num, mode | 1);
			_log($"[InputSourceService] {targetName}: SET NATIVE 后读回 0x{mode:x4},{150}ms 后重试");
			if (Environment.TickCount64 >= num2)
			{
				break;
			}
			Thread.Sleep(150);
		}
		bool flag = ImeModeIO.TryGetConversionMode(num, out mode) && (mode & 1) != 0;
		_log($"[InputSourceService] {targetName}: 转换模式最终读回 0x{mode:x4} {(flag ? "NATIVE 已置位" : "NATIVE 未置位")}");
		return flag;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposedFlag, 1) == 0)
		{
			_disposed = true;
			_queue.CompleteAdding();
			if (!_worker.Join(2000))
			{
				_log("[InputSourceService] 工作线程未在 2 秒内退出");
			}
			_queue.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
