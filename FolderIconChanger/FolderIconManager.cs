using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FolderIconChanger;

/// <summary>
/// 폴더 아이콘 적용 / 초기화 핵심 로직.
/// 다른 컴퓨터로 이동해도 유지되도록 ico를 폴더 내부에 복사하고
/// desktop.ini에서 상대경로(파일명만)로 참조한다.
/// </summary>
public static class FolderIconManager
{
    public const string IconFileName = "folderIcon.ico";
    public const string IconFilePrefix = "folderIcon";
    public const string IniFileName = "desktop.ini";

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    // crystal-folders와 동일한 공식 방식: 셸 API로 desktop.ini를 기록하면 즉시 반영됨
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint SHGetSetFolderCustomSettings(ref SHFOLDERCUSTOMSETTINGS pfcs, string pszPath, uint dwReadWrite);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFOLDERCUSTOMSETTINGS
    {
        public uint dwSize;
        public uint dwMask;
        public IntPtr pvid;
        [MarshalAs(UnmanagedType.LPTStr)] public string? pszWebViewTemplate;
        public uint cchWebViewTemplate;
        [MarshalAs(UnmanagedType.LPTStr)] public string? pszWebViewTemplateVersion;
        [MarshalAs(UnmanagedType.LPTStr)] public string? pszInfoTip;
        public uint cchInfoTip;
        public IntPtr pclsid;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.LPTStr)] public string? pszIconFile;
        public uint cchIconFile;
        public int iIconIndex;
        [MarshalAs(UnmanagedType.LPTStr)] public string? pszLogo;
        public uint cchLogo;
    }

    private const uint FCSM_ICONFILE = 0x00000010;
    private const uint FCS_FORCEWRITE = 0x00000002;

    private const uint SHCNE_UPDATEDIR = 0x00001000;
    private const uint SHCNE_UPDATEITEM = 0x00002000;
    private const uint SHCNF_PATHW = 0x0005;

    public static void ApplyIcon(string folderPath, string sourceIcoPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            throw new DirectoryNotFoundException("폴더 경로가 올바르지 않습니다.");
        if (string.IsNullOrWhiteSpace(sourceIcoPath) || !File.Exists(sourceIcoPath))
            throw new FileNotFoundException("ico 파일을 찾을 수 없습니다.", sourceIcoPath);
        if (!string.Equals(Path.GetExtension(sourceIcoPath), ".ico", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ico 파일만 선택할 수 있습니다.");

        // 아이콘 내용 해시로 대상 파일명을 만듦 (예: folderIcon_a1b2c3d4.ico)
        // 파일명이 같으면 탐색기가 예전 이미지를 캐시에서 계속 보여주는 문제 방지.
        // 여전히 폴더 내부 + 상대경로이므로 다른 PC로 이동해도 유지됨.
        byte[] icoBytes = File.ReadAllBytes(sourceIcoPath);
        string hash = Convert.ToHexString(SHA256.HashData(icoBytes))[..8].ToLowerInvariant();
        string destFileName = $"{IconFilePrefix}_{hash}.ico";
        string destIco = Path.Combine(folderPath, destFileName);
        string iniPath = Path.Combine(folderPath, IniFileName);

        // 1) ico를 폴더 안으로 복사
        // 첫 적용 후에는 아이콘 경로가 폴더 안 복사본을 가리키므로
        // 원본==대상이면 복사를 건너뜀 (자기 자신 복사는 IOException 발생)
        bool sameFile = string.Equals(
            Path.GetFullPath(sourceIcoPath),
            Path.GetFullPath(destIco),
            StringComparison.OrdinalIgnoreCase);
        if (!sameFile)
        {
            WriteBytesWithRetry(destIco, icoBytes);
        }
        // 숨김+시스템 (탐색기에 거슬리지 않게)
        try
        {
            File.SetAttributes(destIco,
                File.GetAttributes(destIco) | FileAttributes.Hidden | FileAttributes.System);
        }
        catch { /* 속성 실패해도 진행 */ }

        // 이전에 적용했던 다른 해시의 복사본은 삭제 (쌓이지 않게)
        try
        {
            foreach (var f in Directory.GetFiles(folderPath, $"{IconFilePrefix}*.ico", SearchOption.TopDirectoryOnly))
            {
                if (!string.Equals(Path.GetFullPath(f), Path.GetFullPath(destIco), StringComparison.OrdinalIgnoreCase))
                    TryDeleteFile(f);
            }
        }
        catch { /* 무시 */ }

        // 2) 셸 API로 desktop.ini 기록 (상대경로 파일명 → 다른 PC로 이동해도 유지, 즉시 반영)
        // crystal-folders 방식: 폴더 수정시간 +1ms 트릭과 병행하면 탐색기 캐시가 바로 갱신됨
        DateTime newMtime;
        try { newMtime = Directory.GetLastWriteTime(folderPath).AddMilliseconds(1); }
        catch { newMtime = DateTime.Now; }

        var fcs = new SHFOLDERCUSTOMSETTINGS
        {
            dwSize = (uint)Marshal.SizeOf<SHFOLDERCUSTOMSETTINGS>(),
            dwMask = FCSM_ICONFILE,
            pszIconFile = destFileName,
            iIconIndex = 0,
        };
        uint hr = SHGetSetFolderCustomSettings(ref fcs, folderPath, FCS_FORCEWRITE);
        if (hr != 0)
        {
            // API 실패 시 기존 수동 방식으로 폴백
            WriteDesktopIni(iniPath, destFileName);
        }

        // 3) 폴더에 System 속성 부여 (없으면 desktop.ini가 무시됨)
        var folderAttr = File.GetAttributes(folderPath);
        if ((folderAttr & FileAttributes.System) == 0)
            File.SetAttributes(folderPath, folderAttr | FileAttributes.System);

        // 폴더 수정시간 복원(+1ms) → 탐색기 캐시 갱신 유도
        try { Directory.SetLastWriteTime(folderPath, newMtime); } catch { }

        // 4) 탐색기에 변경 알림
        NotifyShell(folderPath);
    }

    /// <summary>수동 desktop.ini 작성 (셸 API 실패 시 폴백용, UTF-16, 상대경로)</summary>
    private static void WriteDesktopIni(string iniPath, string destFileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[.ShellClassInfo]");
        sb.AppendLine($"IconResource={destFileName},0");
        sb.AppendLine($"IconFile={destFileName}");
        sb.AppendLine("IconIndex=0");
        if (File.Exists(iniPath))
        {
            try { File.SetAttributes(iniPath, FileAttributes.Normal); } catch { }
        }
        File.WriteAllText(iniPath, sb.ToString(), Encoding.Unicode);
        File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);
    }

    public static void ResetIcon(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            throw new DirectoryNotFoundException("폴더 경로가 올바르지 않습니다.");

        // 셸 API로 아이콘 지정 해제
        try
        {
            var fcs = new SHFOLDERCUSTOMSETTINGS
            {
                dwSize = (uint)Marshal.SizeOf<SHFOLDERCUSTOMSETTINGS>(),
                dwMask = FCSM_ICONFILE,
                pszIconFile = null,
                iIconIndex = 0,
            };
            SHGetSetFolderCustomSettings(ref fcs, folderPath, FCS_FORCEWRITE);
        }
        catch { /* 실패해도 파일 삭제로 계속 */ }

        // 알려진 찌꺼기 파일명 (+ 해시형 folderIcon_*.ico 패턴)
        string[] knownFiles = { IniFileName, IconFileName, "folder.ico", "icon.ico", ".folderIcon.ico" };
        foreach (var name in knownFiles)
        {
            string p = Path.Combine(folderPath, name);
            TryDeleteFile(p);
        }
        try
        {
            foreach (var f in Directory.GetFiles(folderPath, $"{IconFilePrefix}*.ico", SearchOption.TopDirectoryOnly))
                TryDeleteFile(f);
        }
        catch { /* 무시 */ }

        // 초기화시 폴더 안에 있는 숨겨진 ico 찌꺼기 제거:
        // Hidden 또는 System 속성이 있는 *.ico 전부 삭제
        string[] leftoverIco;
        try { leftoverIco = Directory.GetFiles(folderPath, "*.ico", SearchOption.TopDirectoryOnly); }
        catch { leftoverIco = Array.Empty<string>(); }

        foreach (var f in leftoverIco)
        {
            try
            {
                var attr = File.GetAttributes(f);
                if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    TryDeleteFile(f);
            }
            catch { /* 무시 */ }
        }

        // 아이콘 지정이 사라져 내용이 빈 desktop.ini는 삭제 (다른 설정이 있으면 유지)
        RemoveDesktopIniIfEmpty(Path.Combine(folderPath, IniFileName));

        NotifyShell(folderPath);
    }

    /// <summary> 섹션 껍데기만 남은 desktop.ini는 삭제. 다른 사용자 설정이 있으면 유지. </summary>
    private static void RemoveDesktopIniIfEmpty(string iniPath)
    {
        try
        {
            if (!File.Exists(iniPath)) return;
            bool hasContent = false;
            foreach (var raw in File.ReadAllLines(iniPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']')) continue;
                if (line.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase)) continue;
                hasContent = true;
                break;
            }
            if (!hasContent)
                TryDeleteFile(iniPath);
        }
        catch { /* 무시 */ }
    }

    /// <summary>현재 폴더에 커스텀 아이콘이 적용되어 있는지 + 사용 중인 ico 경로 반환</summary>
    public static string? GetAppliedIconPath(string folderPath)
    {
        try
        {
            string iniPath = Path.Combine(folderPath, IniFileName);
            if (!File.Exists(iniPath))
                return null;
            string[] icons;
            try { icons = Directory.GetFiles(folderPath, $"{IconFilePrefix}*.ico", SearchOption.TopDirectoryOnly); }
            catch { return null; }
            if (icons.Length == 0)
                return null;
            // desktop.ini가 가리키는 파일을 우선 반환
            try
            {
                string iniText = File.ReadAllText(iniPath);
                foreach (var ic in icons)
                {
                    if (iniText.Contains(Path.GetFileName(ic), StringComparison.OrdinalIgnoreCase))
                        return ic;
                }
            }
            catch { /* 무시 */ }
            return icons[0];
        }
        catch { }
        return null;
    }

    /// <summary>탐색기가 아이콘을 잠깐 물고 있어도 실패하지 않도록 재시도 쓰기</summary>
    private static void WriteBytesWithRetry(string dest, byte[] bytes, int retries = 3)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                if (File.Exists(dest))
                {
                    try { File.SetAttributes(dest, FileAttributes.Normal); } catch { }
                }
                File.WriteAllBytes(dest, bytes);
                return;
            }
            catch (IOException) when (i < retries)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch { /* 개별 실패 무시 */ }
    }

    private static void NotifyShell(string folderPath)
    {
        // 전체 아이콘 캐시 재구축(ASSOCCHANGED)은 매 적용마다 보내면
        // 재구축 중에 기본 아이콘이 보여서 여러 번 눌러야 하는 현상이 생기므로 쓰지 않음.
        // 대상 파일/폴더 + 부모 폴더 뷰만 표적으로 갱신 (저비용·즉시 반영).
        NotifyPath(SHCNE_UPDATEITEM, Path.Combine(folderPath, IniFileName));
        NotifyPath(SHCNE_UPDATEITEM, folderPath);
        try
        {
            string? parent = Path.GetDirectoryName(
                folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                NotifyPath(SHCNE_UPDATEDIR, parent);
        }
        catch { /* 무시 */ }
    }

    private static void NotifyPath(uint eventId, string path)
    {
        try
        {
            IntPtr ptr = Marshal.StringToHGlobalUni(path);
            try { SHChangeNotify(eventId, SHCNF_PATHW, ptr, IntPtr.Zero); }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch { /* 알림 실패해도 아이콘 자체는 적용됨 */ }
    }
}
