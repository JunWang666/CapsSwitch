using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace CapsSwitch.App.Logging;

/// <summary>
/// 统一日志:%LocalAppData%\CapsSwitch\capswitch.log 追加写(超过 2MB 截断重来)+ Debug 通道。
/// 文件 IO 走独立写线程(阻塞队列),调用线程(尤其钩子事件消费线程)不碰磁盘。
/// CLI 模式 AttachConsole 成功后同时写父控制台。任何 IO 失败只吞掉写 Debug,绝不抛出。
/// </summary>
public static class Logger
{
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly BlockingCollection<string> FileQueue = new();
    private static readonly Thread WriterThread;
    private static string? _path;
    private static bool _consoleAttached;

    static Logger()
    {
        WriterThread = new Thread(WriterLoop)
        { IsBackground = true, Name = "CapsSwitch-LogWriter" };
        WriterThread.Start();
    }

    public static bool ConsoleAttached
    {
        get => _consoleAttached;
        set => _consoleAttached = value;
    }

    public static string? FilePath => _path;

    public static void Initialize()
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CapsSwitch");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "capswitch.log");
            var file = new FileInfo(_path);
            if (file.Exists && file.Length > MaxLogBytes)
                File.WriteAllText(_path, string.Empty); // 截断重来
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Logger] 初始化失败: {ex.Message}");
        }
    }

    public static void Info(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Debug.WriteLine(line);
        WriteConsole(line);
        FileQueue.TryAdd(line);
    }

    /// <summary>详细诊断:只写文件,不上控制台(枚举过滤类日志,避免刷屏 CLI)。</summary>
    public static void Verbose(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Debug.WriteLine(line);
        FileQueue.TryAdd(line);
    }

    private static void WriteConsole(string line)
    {
        if (!_consoleAttached)
            return;
        try { Console.WriteLine(line); }
        catch (Exception ex) { Debug.WriteLine($"[Logger] 控制台写入失败: {ex.Message}"); }
    }

    private static void WriterLoop()
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        foreach (var line in FileQueue.GetConsumingEnumerable())
        {
            if (_path is null)
                continue;
            lock (Gate)
            {
                try
                {
                    File.AppendAllText(_path, line + Environment.NewLine, encoding);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Logger] 文件写入失败: {ex.Message}");
                }
            }
        }
    }
}
