using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Windows 바탕화면 아이콘의 좌표, 이름, 파일 경로를 읽어옵니다.
///
/// 동작 원리:
/// 1. Progman → SHELLDLL_DefView → SysListView32 핸들 탐색 (Win10/11 모두 대응)
/// 2. ListView 항목 개수 조회
/// 3. explorer.exe 프로세스 메모리에 버퍼 할당 후 아이콘 좌표/텍스트 읽기
///    (LVM_GETITEMPOSITION, LVM_GETITEMW 메시지가 다른 프로세스 주소를 요구하기 때문)
/// 4. 셸 가상 폴더(휴지통, 내 PC 등)는 GUID 경로로 매핑
///
/// 주의:
/// - explorer.exe와 동일 사용자 권한 필요
/// - Windows 11 24H2 이후 일부 메시지 동작 변경 가능성 있음
/// </summary>
public static class DesktopIconReader
{
    // ── 구조체 ──────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    /// <summary>ListView 항목 정보 (LVM_GETITEMW 메시지용)</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LVITEM
    {
        public uint   mask;
        public int    iItem;
        public int    iSubItem;
        public uint   state;
        public uint   stateMask;
        public IntPtr pszText;     // 텍스트를 받을 버퍼 포인터 (대상 프로세스 주소)
        public int    cchTextMax;
        public int    iImage;
        public IntPtr lParam;
        public int    iIndent;
        public int    iGroupId;
        public uint   cColumns;
        public IntPtr puColumns;
        public IntPtr piColFmt;
        public int    iGroup;
    }

    /// <summary>외부에 반환되는 아이콘 정보</summary>
    public struct DesktopIcon
    {
        public int     index;     // ListView 인덱스
        public Vector2 screenPos; // 스크린 픽셀 좌표 (좌상단 기준)
        public string  label;     // 표시 이름
        public string  fullPath;  // 전체 경로 또는 셸 GUID 경로 (가상 폴더)
    }

    // ── P/Invoke ────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern IntPtr FindWindow(string cls, string wnd);
    [DllImport("user32.dll")] private static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string cls, string wnd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern uint   GetWindowThreadProcessId(IntPtr h, out uint pid);

    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool   CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr addr, uint size, uint type, uint prot);
    [DllImport("kernel32.dll")] private static extern bool   VirtualFreeEx(IntPtr h, IntPtr addr, uint size, uint type);
    [DllImport("kernel32.dll")] private static extern bool   ReadProcessMemory(IntPtr h, IntPtr addr, IntPtr buf, uint size, out uint read);
    [DllImport("kernel32.dll")] private static extern bool   WriteProcessMemory(IntPtr h, IntPtr addr, IntPtr buf, uint size, out uint written);

    // ── 상수 ────────────────────────────────────────────────────────
    // OpenProcess 권한
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_READ      = 0x0010;
    private const uint PROCESS_VM_WRITE     = 0x0020;

    // VirtualAllocEx
    private const uint MEM_COMMIT  = 0x1000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_RW     = 0x04;

    // ListView 메시지
    private const uint LVM_GETITEMCOUNT    = 0x1004;
    private const uint LVM_GETITEMPOSITION = 0x1010;
    private const uint LVM_GETITEMW        = 0x104B;
    private const uint LVIF_TEXT           = 0x0001;

    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 바탕화면 아이콘 ListView 핸들 탐색.
    /// Win10: Progman 직속 자식이 SHELLDLL_DefView
    /// Win11: 일부 환경에서 WorkerW 안에 SHELLDLL_DefView가 있음
    /// 두 경우 모두 처리.
    /// </summary>
    private static IntPtr GetIconListView()
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr shelf   = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

        if (shelf == IntPtr.Zero)
        {
            IntPtr w = IntPtr.Zero;
            while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
            {
                shelf = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shelf != IntPtr.Zero) break;
            }
        }

        return shelf == IntPtr.Zero
            ? IntPtr.Zero
            : FindWindowEx(shelf, IntPtr.Zero, "SysListView32", null);
    }

    /// <summary>현재 바탕화면의 모든 아이콘 정보를 반환합니다.</summary>
    public static List<DesktopIcon> GetIcons()
    {
        var list = new List<DesktopIcon>();
        IntPtr lv = GetIconListView();
        if (lv == IntPtr.Zero)
        {
            Debug.LogWarning("[DesktopIconReader] ListView 못 찾음");
            return list;
        }

        // explorer.exe PID 조회 후 메모리 접근 권한으로 핸들 획득
        GetWindowThreadProcessId(lv, out uint pid);
        IntPtr proc = OpenProcess(
            PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE,
            false, pid);

        if (proc == IntPtr.Zero)
        {
            Debug.LogError("[DesktopIconReader] explorer.exe OpenProcess 실패");
            return list;
        }

        try
        {
            int count = (int)SendMessage(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);

            // explorer.exe 내부에 버퍼 3개 할당
            // remotePoint : POINT (좌표 8바이트) → 16으로 여유
            // remoteText  : 아이콘 라벨 (최대 256자 × 2바이트)
            // remoteItem  : LVITEM 구조체
            IntPtr remotePoint = VirtualAllocEx(proc, IntPtr.Zero, 16,  MEM_COMMIT, PAGE_RW);
            IntPtr remoteText  = VirtualAllocEx(proc, IntPtr.Zero, 512, MEM_COMMIT, PAGE_RW);
            IntPtr remoteItem  = VirtualAllocEx(proc, IntPtr.Zero, (uint)Marshal.SizeOf<LVITEM>(), MEM_COMMIT, PAGE_RW);

            for (int i = 0; i < count; i++)
            {
                // 좌표 — explorer.exe가 remotePoint에 POINT를 기록 → 우리 프로세스로 ReadProcessMemory
                SendMessage(lv, LVM_GETITEMPOSITION, (IntPtr)i, remotePoint);
                IntPtr local = Marshal.AllocHGlobal(8);
                ReadProcessMemory(proc, remotePoint, local, 8, out _);
                POINT p = Marshal.PtrToStructure<POINT>(local);
                Marshal.FreeHGlobal(local);

                // 텍스트 — LVITEM.pszText를 explorer.exe 내부 주소로 설정해야 함
                var item = new LVITEM
                {
                    mask       = LVIF_TEXT,
                    iItem      = i,
                    pszText    = remoteText,
                    cchTextMax = 256
                };
                IntPtr localItem = Marshal.AllocHGlobal(Marshal.SizeOf<LVITEM>());
                Marshal.StructureToPtr(item, localItem, false);
                WriteProcessMemory(proc, remoteItem, localItem, (uint)Marshal.SizeOf<LVITEM>(), out _);
                Marshal.FreeHGlobal(localItem);

                SendMessage(lv, LVM_GETITEMW, (IntPtr)i, remoteItem);

                // 텍스트 결과 읽어오기
                IntPtr localText = Marshal.AllocHGlobal(512);
                ReadProcessMemory(proc, remoteText, localText, 512, out _);
                string label = Marshal.PtrToStringUni(localText);
                Marshal.FreeHGlobal(localText);

                list.Add(new DesktopIcon
                {
                    index     = i,
                    screenPos = new Vector2(p.x, p.y),
                    label     = label,
                    fullPath  = FindFullPath(label)
                });
            }

            VirtualFreeEx(proc, remotePoint, 0, MEM_RELEASE);
            VirtualFreeEx(proc, remoteText,  0, MEM_RELEASE);
            VirtualFreeEx(proc, remoteItem,  0, MEM_RELEASE);
        }
        finally
        {
            CloseHandle(proc);
        }

        Debug.Log($"[DesktopIconReader] 아이콘 {list.Count}개 읽음");
        return list;
    }

    /// <summary>
    /// 라벨로부터 실제 파일 경로 또는 셸 가상 경로를 찾아 반환.
    /// 휴지통/내 PC 같은 가상 폴더는 ::{GUID} 형태로 반환되며 IconImageExtractor가 이를 인식합니다.
    /// </summary>
    private static string FindFullPath(string label)
    {
        // 셸 가상 폴더 매핑 (한국어/영문 모두)
        switch (label)
        {
            case "휴지통":
            case "Recycle Bin":
                return "::{645FF040-5081-101B-9F08-00AA002F954E}";
            case "내 PC":
            case "This PC":
            case "내 컴퓨터":
                return "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
            case "네트워크":
            case "Network":
                return "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
            case "제어판":
            case "Control Panel":
                return "::{26EE0668-A00A-44D7-9371-BEB064C98683}";
        }

        // 사용자 / 공용 바탕화면 폴더에서 매칭 파일 찾기
        string[] desktops =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        foreach (var d in desktops)
        {
            if (!Directory.Exists(d)) continue;

            // .lnk, .url 등 확장자가 다양하므로 와일드카드 매칭
            var matches = Directory.GetFileSystemEntries(d, label + ".*");
            if (matches.Length > 0) return matches[0];

            // 확장자 없는 경우 (폴더 등)
            var exact = Path.Combine(d, label);
            if (File.Exists(exact) || Directory.Exists(exact)) return exact;
        }

        return null;
    }
}