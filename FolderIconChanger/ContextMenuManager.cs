using System;
using Microsoft.Win32;

namespace FolderIconChanger;

/// <summary>
/// 탐색기 폴더 우클릭 메뉴 등록/해제 (HKCU 이므로 관리자 권한 불필요)
/// 등록되면 폴더 우클릭 > "폴더 아이콘 변경" 클릭 시 이 프로그램이 폴더 경로를 인자로 받아 실행됨.
/// </summary>
public static class ContextMenuManager
{
    private const string MenuKeyPath = @"Software\Classes\Directory\shell\FolderIconChanger";
    private const string MenuText = "폴더 아이콘 변경";

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MenuKeyPath);
            return key != null;
        }
        catch { return false; }
    }

    public static void Register()
    {
        string exePath = Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("실행 파일 경로를 확인할 수 없습니다.");

        using var key = Registry.CurrentUser.CreateSubKey(MenuKeyPath);
        key.SetValue("", MenuText);
        key.SetValue("Icon", $"\"{exePath}\",0");

        using var cmd = Registry.CurrentUser.CreateSubKey(MenuKeyPath + @"\command");
        cmd.SetValue("", $"\"{exePath}\" \"%1\"");
    }

    public static void Unregister()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(MenuKeyPath, throwOnMissingSubKey: false); }
        catch { /* 무시 */ }
    }
}
