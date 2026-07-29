using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace GuoBuZiLiaoGuanLi;

public partial class App : Application
{
    private const string MutexName = "Global\\GuoBuZiLiaoGuanLi_SingleInstance_Mutex";
    private static Mutex? _mutex;
    private const string MainWindowTitle = "国补资料管理系统";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string lpWindowName);

    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 创建全局互斥量，检测是否已有实例在运行
        _mutex = new Mutex(true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            // 已有实例运行，激活已有窗口并退出
            ActivateExistingWindow();
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    private static void ActivateExistingWindow()
    {
        try
        {
            // 通过窗口标题查找已存在的窗口并激活
            IntPtr hWnd = FindWindowW(null, MainWindowTitle);
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
        }
        catch
        {
            // 激活失败忽略
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
