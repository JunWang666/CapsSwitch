using Microsoft.Win32;

namespace CapsSwitch.App.Config;

/// <summary>
/// 开机自启:HKCU\Software\Microsoft\Windows\CurrentVersion\Run 值 "CapsSwitch" = exe 路径(带引号)。
/// 不需要管理员。
/// </summary>
public static class AutoStartRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CapsSwitch";

    /// <summary>给 Run 键用的路径引用(含空格路径必须带引号)。纯函数,可单测。</summary>
    public static string QuoteForRunKey(string exePath) => $"\"{exePath}\"";

    public static void SetAutoStart(bool enabled, string? exePath = null)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            string path = exePath ?? Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定当前 exe 路径");
            key.SetValue(ValueName, QuoteForRunKey(path));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string s && s.Length > 0;
    }
}
