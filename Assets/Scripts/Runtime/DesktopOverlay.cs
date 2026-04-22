using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class DesktopOverlay : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────────────────
    // Windows API Declarations
    // ──────────────────────────────────────────────────────────────────────────

    // 창 스타일 상수
    private const int GWL_STYLE   = -16;
    private const int GWL_EXSTYLE = -20;

    private const uint WS_POPUP          = 0x80000000; // 테두리/타이틀바 없는 팝업 창
    private const uint WS_VISIBLE        = 0x10000000;
    private const uint WS_EX_LAYERED     = 0x00080000; // 레이어드 창(투명도 지원)
    private const uint WS_EX_TRANSPARENT = 0x00000020; // 마우스 클릭 관통

    // SetWindowPos 플래그
    private static readonly IntPtr HWND_BOTTOM    = new IntPtr(1);  // 항상 아래
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2); // Topmost 해제
    private const uint SWP_NOMOVE      = 0x0002;
    private const uint SWP_NOSIZE      = 0x0001;
    private const uint SWP_FRAMECHANGED = 0x0020; // 스타일 변경 후 프레임 갱신
    private const uint SWP_NOACTIVATE  = 0x0010;

    // 컬러키 — 마젠타(R=255,G=0,B=255) 픽셀을 OS가 투명으로 처리
    // COLORREF 포맷은 0x00BBGGRR 이므로 마젠타 = 0x00FF00FF
    private const uint MAGENTA_KEY  = 0x00FF00FF;
    private const uint LWA_COLORKEY = 0x00000001;

    // WinEvent 훅 — Z-order 변경 감지용
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003; // 다른 창이 포커스 받을 때
    private const uint EVENT_OBJECT_REORDER    = 0x8004; // Z-order 변경될 때
    private const uint WINEVENT_OUTOFCONTEXT   = 0x0000;

    // DWM 구조체
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    // 훅 콜백 델리게이트
    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime);

    // user32.dll
    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern int    SetWindowLong(IntPtr hwnd, int nIndex, uint dwNewLong);
    [DllImport("user32.dll")] private static extern uint   GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern bool   SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool   ShowWindow(IntPtr hwnd, int nCmdShow);

    // 마젠타 컬러키 방식의 투명 처리 — DWM과 달리 SetParent 없이도 안정적으로 동작
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    // Z-order 변경 이벤트 훅 — 폴링 없이 다른 창이 올라올 때만 HWND_BOTTOM 재적용
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax,
        IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // ──────────────────────────────────────────────────────────────────────────
    // Inspector Settings
    // ──────────────────────────────────────────────────────────────────────────

    [Header("Overlay Settings")]
    [Tooltip("true = 클릭 관통 활성화 (투명 영역 클릭이 뒤 창으로 전달됨)")]
    [SerializeField] private bool enableClickThrough = true;

    // ──────────────────────────────────────────────────────────────────────────
    // Runtime State
    // ──────────────────────────────────────────────────────────────────────────

    private IntPtr _hwnd     = IntPtr.Zero;
    private bool   _isClickThrough = false;

    // GC가 훅 델리게이트를 수집하지 못하도록 레퍼런스 유지
    private WinEventDelegate _hookProc;
    private IntPtr           _hook = IntPtr.Zero;

    // ──────────────────────────────────────────────────────────────────────────
    // Unity Lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    private void Start()
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        _hwnd = GetActiveWindow();
        if (_hwnd == IntPtr.Zero)
        {
            Debug.LogError("[DesktopOverlay] 윈도우 핸들을 가져오지 못했습니다.");
            return;
        }

        // 카메라 배경을 마젠타로 설정 — 이 색이 컬러키가 되어 OS 레벨에서 투명 처리됨
        // 검정(0,0,0)은 어두운 오브젝트와 구분이 안 되므로 마젠타 사용
        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(1f, 0f, 1f, 0f);
        }

        ApplyBorderlessWindow();
        ApplyDwmTransparency();

        // Topmost를 해제한 뒤 Z-order 최하단으로 고정
        // → 바탕화면 위, 모든 일반 창 아래 레이어에 상주
        SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetWindowPos(_hwnd, HWND_BOTTOM,    0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        if (enableClickThrough)
            SetClickThrough(true);

        // Z-order 변경 이벤트 훅 등록
        // 폴링(매 N초마다 SetWindowPos) 대신 실제로 Z-order가 바뀔 때만 재적용
        _hookProc = OnWinEvent;
        _hook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_OBJECT_REORDER,
            IntPtr.Zero, _hookProc,
            0, 0, WINEVENT_OUTOFCONTEXT);

        Debug.Log("[DesktopOverlay] 투명 오버레이 창 설정 완료.");
#else
        Debug.LogWarning("[DesktopOverlay] 이 기능은 Windows 빌드에서만 동작합니다. (에디터 무시)");
#endif
    }

    private void OnDestroy()
    {
        // 앱 종료 시 훅 해제 — 해제하지 않으면 시스템 전역 훅이 남아 메모리 누수 발생
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Win32 Helper Methods
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>WS_POPUP 스타일로 테두리·타이틀바를 제거합니다.</summary>
    private void ApplyBorderlessWindow()
    {
        SetWindowLong(_hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);
        // 화면 전체를 커버 (해상도에 맞게 조정)
        SetWindowPos(_hwnd, IntPtr.Zero,
            0, 0,
            Screen.currentResolution.width,
            Screen.currentResolution.height,
            SWP_FRAMECHANGED);
        Debug.Log("[DesktopOverlay] 보더리스 창 적용 완료.");
    }

    /// <summary>
    /// WS_EX_LAYERED + 마젠타 컬러키로 투명 처리합니다.
    /// DwmExtendFrameIntoClientArea는 최상위 창에서만 동작하므로 사용하지 않습니다.
    /// </summary>
    private void ApplyDwmTransparency()
    {
        // WS_EX_LAYERED 추가 — 레이어드 창 활성화
        uint exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);

        // 마젠타 픽셀을 OS가 투명으로 처리
        // Unity 카메라 배경(마젠타)이 그대로 비치면 그 픽셀이 뚫려 바탕화면이 보임
        SetLayeredWindowAttributes(_hwnd, MAGENTA_KEY, 255, LWA_COLORKEY);

        Debug.Log("[DesktopOverlay] DWM 투명 처리 완료.");
    }

    /// <summary>
    /// WinEvent 훅 콜백 — 다른 창의 Z-order가 변경될 때 호출됩니다.
    /// 우리 창이 아닌 창이 올라오면 즉시 HWND_BOTTOM으로 재고정합니다.
    /// </summary>
    private void OnWinEvent(IntPtr hWinEventHook, uint eventType,
                            IntPtr hwnd, int idObject, int idChild,
                            uint dwEventThread, uint dwmsEventTime)
    {
        // 우리 창 자신의 이벤트는 무시 (무한 루프 방지)
        if (hwnd == _hwnd || _hwnd == IntPtr.Zero) return;

        SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>클릭 관통(WS_EX_TRANSPARENT) 플래그를 토글합니다.</summary>
    public void SetClickThrough(bool enable)
    {
        if (_hwnd == IntPtr.Zero) return;

        uint exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);

        if (enable)
            exStyle |= WS_EX_TRANSPARENT;   // 클릭 관통 ON
        else
            exStyle &= ~WS_EX_TRANSPARENT;  // 클릭 관통 OFF (캐릭터/UI 클릭 가능)

        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
        _isClickThrough = enable;
        Debug.Log($"[DesktopOverlay] 클릭 관통: {(enable ? "활성화" : "비활성화")}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Public API — 캐릭터/UI 스크립트에서 호출
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 캐릭터나 UI 위로 마우스가 올라왔을 때 호출 → 클릭 관통 OFF
    /// (예: OnMouseEnter 이벤트에서 DesktopOverlay.Instance.EnableInteraction(true) 호출)
    /// </summary>
    public void EnableInteraction(bool canInteract)
    {
        SetClickThrough(!canInteract);
    }

    // 간단한 싱글턴 (씬 내 단일 인스턴스 보장)
    public static DesktopOverlay Instance { get; private set; }
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}