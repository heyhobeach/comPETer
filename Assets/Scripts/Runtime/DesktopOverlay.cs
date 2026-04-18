using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class DesktopOverlay : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────────────────
    // Windows API Declarations
    // ──────────────────────────────────────────────────────────────────────────

    // 창 스타일 상수
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    private const uint WS_POPUP = 0x80000000; // 테두리/타이틀바 없는 팝업 창
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_EX_LAYERED = 0x00080000; // 레이어드 창(투명도 지원)
    private const uint WS_EX_TRANSPARENT = 0x00000020; // 마우스 클릭 관통

    // SetWindowPos 플래그
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1); // 항상 위
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_FRAMECHANGED = 0x0020; // 스타일 변경 후 프레임 갱신

    // DWM 구조체
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    // user32.dll
    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, uint dwNewLong);
    [DllImport("user32.dll")] private static extern uint GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    // dwmapi.dll — 창 전체를 투명하게 처리
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    // ──────────────────────────────────────────────────────────────────────────
    // Inspector Settings
    // ──────────────────────────────────────────────────────────────────────────

    [Header("Overlay Settings")]
    [Tooltip("true = 클릭 관통 활성화 (투명 영역 클릭이 뒤 창으로 전달됨)")]
    [SerializeField] private bool enableClickThrough = true;

    [Tooltip("항상 위에 표시")]
    [SerializeField] private bool alwaysOnTop = true;

    // ──────────────────────────────────────────────────────────────────────────
    // Runtime State
    // ──────────────────────────────────────────────────────────────────────────

    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isClickThrough = false;

    // ──────────────────────────────────────────────────────────────────────────
    // Unity Lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    private void Start()
    {
    #if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        _hwnd = GetActiveWindow();
        
        // 창 숨기기 (렌더링은 RenderTexture로 분리)
        ShowWindow(_hwnd, 0); // SW_HIDE = 0
    #endif
    }

    private void Update()
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        // 동적 클릭 관통 토글 (런타임 테스트용 — 필요 없으면 제거 가능)
        if (Input.GetKeyDown(KeyCode.F1))
            SetClickThrough(!_isClickThrough);
#endif
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

    /// <summary>DWM을 이용해 창 전체를 투명하게 처리합니다.</summary>
    private void ApplyDwmTransparency()
    {
        // 음수 마진(-1)은 전체 클라이언트 영역을 DWM 프레임으로 확장 → 유리처럼 투명
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        int hr = DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        if (hr != 0)
            Debug.LogError($"[DesktopOverlay] DwmExtendFrameIntoClientArea 실패. HRESULT: {hr}");

        // WS_EX_LAYERED 추가 — 레이어드 창 활성화
        uint exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
        Debug.Log("[DesktopOverlay] DWM 투명 처리 완료.");
    }

    /// <summary>창을 항상 최상위(Topmost)로 고정합니다.</summary>
    private void ApplyAlwaysOnTop()
    {
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        Debug.Log("[DesktopOverlay] 항상 위 설정 완료.");
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