using System;
using System.Runtime.InteropServices;

/// <summary>
/// 사용자가 설정한 바탕화면 아이콘 크기를 레지스트리에서 읽어옵니다.
///
/// 위치: HKCU\Software\Microsoft\Windows\Shell\Bags\1\Desktop\IconSize
///
/// 표준 값:
///   32  → 작은 아이콘
///   48  → 보통 아이콘 (기본값)
///   96  → 큰 아이콘
///   256 → 아주 큰 아이콘
///
/// Microsoft.Win32.Registry 클래스 의존성을 피하기 위해
/// advapi32.dll을 직접 P/Invoke로 호출합니다.
/// </summary>
public static class WindowsIconSize
{
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegOpenKeyEx(IntPtr hKey, string subKey, int options, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegQueryValueEx(IntPtr hKey, string valueName, IntPtr reserved,
                                              out int type, byte[] data, ref int dataSize);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr hKey);

    private static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));
    private const int KEY_READ = 0x20019;

    /// <summary>현재 바탕화면 아이콘 크기(px)를 반환합니다. 실패 시 기본값 48.</summary>
    public static int GetDesktopIconSize()
    {
        try
        {
            if (RegOpenKeyEx(HKEY_CURRENT_USER,
                @"Software\Microsoft\Windows\Shell\Bags\1\Desktop",
                0, KEY_READ, out IntPtr hKey) != 0)
                return 48;

            try
            {
                byte[] buf = new byte[4]; // DWORD (4바이트)
                int size = 4;
                if (RegQueryValueEx(hKey, "IconSize", IntPtr.Zero,
                                    out _, buf, ref size) == 0)
                {
                    return BitConverter.ToInt32(buf, 0);
                }
            }
            finally
            {
                RegCloseKey(hKey);
            }
        }
        catch { /* 레지스트리 접근 실패 시 기본값 사용 */ }

        return 48;
    }
}