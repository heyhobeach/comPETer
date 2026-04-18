using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class DesktopPin : MonoBehaviour
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern int    GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int    SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern bool   SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    // WinEvent 훅 API
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread,
        uint dwFlags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime);

    // Z-order 변경 이벤트만 구독
    const uint EVENT_SYSTEM_FOREGROUND   = 0x0003;
    const uint EVENT_OBJECT_REORDER      = 0x8004;
    const uint WINEVENT_OUTOFCONTEXT     = 0x0000;

    const int  GWL_EXSTYLE      = -20;
    const int  WS_EX_TOOLWINDOW = 0x00000080;
    const int  WS_EX_APPWINDOW  = 0x00040000;
    const int  WS_EX_TOPMOST    = 0x00000008;
    const uint SWP_NOMOVE       = 0x0002;
    const uint SWP_NOSIZE       = 0x0001;
    const uint SWP_NOACTIVATE   = 0x0010;

    static readonly IntPtr HWND_BOTTOM    = new IntPtr(1);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    [Header("고정 옵션")]
    public bool hideFromAltTab = true;

    IntPtr           _hwnd      = IntPtr.Zero;
    IntPtr           _hook1     = IntPtr.Zero;
    IntPtr           _hook2     = IntPtr.Zero;
    WinEventDelegate _hookProc;   // GC 방지용 레퍼런스 보관

    // ── Unity 생명주기 ──────────────────────────────────────────────

    void Start() => Invoke(nameof(Apply), 0.5f);

    void Apply()
    {
        _hwnd = GetActiveWindow();
        if (_hwnd == IntPtr.Zero) { Debug.LogError("[DesktopPin] 핸들 없음"); return; }

        RemoveTopmost(_hwnd);
        if (hideFromAltTab) HideFromAltTab(_hwnd);
        PinToBottom(_hwnd);

        // 훅 등록 — 델리게이트를 필드에 보관해야 GC에 수집되지 않음
        _hookProc = OnWinEvent;

        _hook1 = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _hookProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        _hook2 = SetWinEventHook(
            EVENT_OBJECT_REORDER, EVENT_OBJECT_REORDER,
            IntPtr.Zero, _hookProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        Debug.Log($"[DesktopPin] 고정 완료 + WinEvent 훅 등록 / hwnd={_hwnd}");
    }

    void OnDestroy()
    {
        if (_hook1 != IntPtr.Zero) UnhookWinEvent(_hook1);
        if (_hook2 != IntPtr.Zero) UnhookWinEvent(_hook2);
    }

    // ── WinEvent 콜백 — Z-order 변경 시에만 호출됨 ────────────────

    void OnWinEvent(
        IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (_hwnd == IntPtr.Zero) return;
        PinToBottom(_hwnd);
    }

    // ── Win32 헬퍼 ─────────────────────────────────────────────────

    void RemoveTopmost(IntPtr hwnd)
    {
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex &= ~WS_EX_TOPMOST;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    void HideFromAltTab(IntPtr hwnd)
    {
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }

    void PinToBottom(IntPtr hwnd)
    {
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

#else
    void Start() => Debug.LogWarning("[DesktopPin] Windows 전용");
#endif
}