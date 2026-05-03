// GlobalInputHook.cs
// 전역 키보드/마우스 훅 (Windows Low-Level Hook)
//
// [사용 Windows API 원리]
// - SetWindowsHookEx(WH_KEYBOARD_LL, ...) :
//     시스템 전체 키보드 메시지를 가로채는 '로우레벨 훅'을 설치합니다.
//     WH_KEYBOARD_LL(13)은 별도 DLL 없이 현재 프로세스 내 콜백만으로 동작합니다.
// - SetWindowsHookEx(WH_MOUSE_LL, ...) :
//     시스템 전체 마우스 메시지(이동, 클릭, 휠 등)를 가로채는 훅을 설치합니다.
// - CallNextHookEx() :
//     훅 콜백 처리 후 반드시 호출해 훅 체인의 다음 핸들러로 메시지를 전달합니다.
//     이 호출을 생략하면 시스템 입력이 차단되어 OS가 행(hang) 상태가 될 수 있습니다.
// - UnhookWindowsHookEx() :
//     설치한 훅을 해제합니다. 앱 종료 시 반드시 호출해야 합니다.
//     해제하지 않으면 시스템 입력 지연 또는 크래시가 발생합니다.
//
// [⚠️ 에디터 충돌 방지 설계]
// 1. #if UNITY_STANDALONE_WIN && !UNITY_EDITOR 블록으로 에디터에서는 훅이 아예 설치되지 않습니다.
// 2. 훅 핸들은 GCHandle + delegate 참조를 통해 GC에 의해 수거되지 않도록 고정합니다.
// 3. OnApplicationQuit / OnDestroy / ~GlobalInputHook() (파이널라이저) 세 곳에서 중복 해제를 막는
//    _isDisposed 플래그와 함께 UnhookWindowsHookEx를 호출합니다.
// 4. 로깅 빈도를 throttle(쓰로틀링)해 GC 압박과 UI 과부하를 방지합니다.

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

public class GlobalInputHook : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────────────────
    // Windows API Declarations
    // ──────────────────────────────────────────────────────────────────────────

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL    = 14;

    // 키보드 메시지 코드
    private const int WM_KEYDOWN    = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    // 마우스 메시지 코드
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint   vkCode;    // 가상 키 코드
        public uint   scanCode;
        public uint   flags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT  pt;         // 마우스 좌표
        public uint   mouseData;
        public uint   flags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    // ──────────────────────────────────────────────────────────────────────────
    // Inspector Settings
    // ──────────────────────────────────────────────────────────────────────────

    [Header("Hook Settings")]
    [SerializeField] private bool enableKeyboardHook = true;
    [SerializeField] private bool enableMouseHook    = true;

    [Header("Logging")]
    [Tooltip("화면 텍스트에도 로그를 표시합니다.")]
    [SerializeField] private bool showOnScreenLog = true;

    [Tooltip("로그 메시지 최대 보관 수")]
    [SerializeField] private int maxLogLines = 8;

    [Tooltip("동일 이벤트 로그 최소 간격 (초) — GC 방어")]
    [SerializeField] private float logThrottleSeconds = 0.1f;

    // ──────────────────────────────────────────────────────────────────────────
    // Runtime State
    // ──────────────────────────────────────────────────────────────────────────

    private IntPtr  _kbHook    = IntPtr.Zero;
    private IntPtr  _msHook    = IntPtr.Zero;
    private HookProc _kbProc  = null; // GC 수거 방지용 참조 보존
    private HookProc _msProc  = null;

    private bool _isDisposed = false;

    // 메인 스레드 전달용 스레드 세이프 큐 (훅 콜백은 별도 스레드)
    private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();

    // 화면 로그 버퍼
    private System.Collections.Generic.Queue<string> _screenLogs;

    // 로그 쓰로틀
    private float _lastKbLogTime  = -999f;
    private float _lastMsLogTime  = -999f;

    // GUI 스타일 (OnGUI용)
    private GUIStyle _logStyle;

    // ──────────────────────────────────────────────────────────────────────────
    // Unity Lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _screenLogs = new System.Collections.Generic.Queue<string>(maxLogLines + 1);
    }

    private void Start()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        InstallHooks();
#else
        Debug.LogWarning("[GlobalInputHook] 에디터 / 비-Windows 환경 → 훅이 설치되지 않습니다.");
        PushLog("에디터 모드 — Input System 폴백 중...");
#endif
    }

    private void Update()
    {
        // 훅 콜백의 메시지를 메인 스레드에서 처리 (Unity API 호출은 메인 스레드만 허용)
        while (_logQueue.TryDequeue(out string msg))
        {
            Debug.Log(msg);
            if (showOnScreenLog) PushLog(msg);
        }

#if UNITY_EDITOR
        // ── 에디터 전용 폴백: 새 Input System API 사용 ──
        float now = Time.unscaledTime;

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.anyKey.wasPressedThisFrame
            && now - _lastKbLogTime > logThrottleSeconds)
        {
            _lastKbLogTime = now;
            string msg = "[에디터] 키보드 입력됨";
            Debug.Log(msg);
            if (showOnScreenLog) PushLog(msg);
        }

        var mouse = Mouse.current;
        if (mouse != null && now - _lastMsLogTime > logThrottleSeconds)
        {
            bool left   = mouse.leftButton.wasPressedThisFrame;
            bool right  = mouse.rightButton.wasPressedThisFrame;
            bool middle = mouse.middleButton.wasPressedThisFrame;


            var pos = mouse.position.ReadValue();

            if (left || right || middle)
            {
                _lastMsLogTime = now;
                string btn = left ? "좌" : right ? "우" : "중간";
                string msg = $"[에디터] 마우스 {btn}클릭됨 @ ({pos.x:F0}, {pos.y:F0})";
                
                Debug.Log(msg);
                if (showOnScreenLog) PushLog(msg);
            }
        }
#endif
    }

    private void OnGUI()
    {
        if (!showOnScreenLog || _screenLogs == null || _screenLogs.Count == 0) return;

        if (_logStyle == null)
        {
            _logStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize  = 14,
                alignment = TextAnchor.LowerLeft,
                wordWrap  = true
            };
            _logStyle.normal.textColor = Color.white;
        }

        string display = string.Join("\n", _screenLogs);
        float w = 420f, h = _screenLogs.Count * 22f + 12f;
        GUI.Box(new Rect(10, Screen.height - h - 10, w, h), display, _logStyle);
    }

    private void OnApplicationQuit()
    {
        RemoveHooks();
    }

    private void OnDestroy()
    {
        RemoveHooks();
    }

    // 파이널라이저 — 비정상 종료 대비 최후 방어선
    ~GlobalInputHook()
    {
        RemoveHooks();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Hook Install / Remove
    // ──────────────────────────────────────────────────────────────────────────

    private void InstallHooks()
    {
        IntPtr hMod = GetModuleHandle(null);

        if (enableKeyboardHook)
        {
            _kbProc = KeyboardHookCallback; // delegate를 필드에 보존 → GC 방지
            _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);

            if (_kbHook == IntPtr.Zero)
                Debug.LogError($"[GlobalInputHook] 키보드 훅 설치 실패. LastError: {Marshal.GetLastWin32Error()}");
            else
                Debug.Log("[GlobalInputHook] 키보드 훅 설치 완료.");
        }

        if (enableMouseHook)
        {
            _msProc = MouseHookCallback;
            _msHook = SetWindowsHookEx(WH_MOUSE_LL, _msProc, hMod, 0);

            if (_msHook == IntPtr.Zero)
                Debug.LogError($"[GlobalInputHook] 마우스 훅 설치 실패. LastError: {Marshal.GetLastWin32Error()}");
            else
                Debug.Log("[GlobalInputHook] 마우스 훅 설치 완료.");
        }
    }

    /// <summary>훅을 안전하게 해제합니다. 여러 번 호출해도 안전합니다.</summary>
    private void RemoveHooks()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_kbHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_kbHook);
            _kbHook = IntPtr.Zero;
            Debug.Log("[GlobalInputHook] 키보드 훅 해제 완료.");
        }

        if (_msHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_msHook);
            _msHook = IntPtr.Zero;
            Debug.Log("[GlobalInputHook] 마우스 훅 해제 완료.");
        }

        // delegate 참조 해제 — GC가 수거해도 괜찮은 시점
        _kbProc = null;
        _msProc = null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Hook Callbacks (별도 스레드에서 호출됨 — Unity API 직접 호출 금지)
    // ──────────────────────────────────────────────────────────────────────────

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // nCode < 0이면 처리하지 말고 반드시 다음 훅으로 넘겨야 합니다.
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var kb    = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            string msg = $"[GlobalInputHook] 키보드 입력됨 — VK: {kb.vkCode} (0x{kb.vkCode:X2})";
            _logQueue.Enqueue(msg); // 메인 스레드로 전달
        }

        return CallNextHookEx(_kbHook, nCode, wParam, lParam); // 체인 유지 (필수!)
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg_int = wParam.ToInt32();
            if (msg_int == WM_LBUTTONDOWN || msg_int == WM_RBUTTONDOWN || msg_int == WM_MBUTTONDOWN)
            {
                var ms   = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                string btn = msg_int == WM_LBUTTONDOWN ? "좌" : msg_int == WM_RBUTTONDOWN ? "우" : "중간";
                string msg = $"[GlobalInputHook] 마우스 {btn}클릭됨 @ ({ms.pt.x}, {ms.pt.y})";
                _logQueue.Enqueue(msg);
            }
        }

        return CallNextHookEx(_msHook, nCode, wParam, lParam); // 체인 유지 (필수!)
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private void PushLog(string msg)
    {
        _screenLogs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {msg}");
        while (_screenLogs.Count > maxLogLines)
            _screenLogs.Dequeue();
    }
}
