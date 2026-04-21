using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 아이콘 위에 렌더링할 카메라를 받아
/// 별도 Win32 창에 그려주는 컴포넌트
/// </summary>
public class AboveIconsWindow : MonoBehaviour
{
#if UNITY_STANDALONE_WIN

    [Header("아이콘 위에 렌더링할 카메라")]
    public Camera aboveIconsCamera;

    // ── Win32 ───────────────────────────────────────────────────────
    delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);

    [DllImport("user32.dll")] static extern IntPtr CreateWindowEx(
        uint exStyle, string cls, string title, uint style,
        int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("gdi32.dll")] static extern bool BitBlt(
        IntPtr hdcDst, int xDst, int yDst, int w, int h,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc,
        uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string lpszMenuName, lpszClassName;
        public IntPtr hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE  { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)]
    struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO
    {
        public int    biSize, biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint   biCompression, biSizeImage;
        public int    biXPelsPerMeter, biYPelsPerMeter;
        public uint   biClrUsed, biClrImportant;
    }

    const uint WS_EX_LAYERED     = 0x00080000;
    const uint WS_EX_TRANSPARENT = 0x00000020;
    const uint WS_EX_TOPMOST     = 0x00000008; // 아이콘 위에 있어야 하므로 사용
    const uint WS_POPUP          = 0x80000000;
    const uint ULW_ALPHA         = 0x00000002;
    const uint SWP_NOMOVE        = 0x0002;
    const uint SWP_NOSIZE        = 0x0001;
    const uint SWP_NOACTIVATE    = 0x0010;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    // ── 런타임 상태 ─────────────────────────────────────────────────
    IntPtr          _overlayHwnd = IntPtr.Zero;
    RenderTexture   _rt;
    Texture2D       _readback;
    WndProcDelegate _wndProc;   // GC 방지
    int             _w, _h;

    // ── Unity 생명주기 ──────────────────────────────────────────────

    void Start()
    {
        _w = Screen.width;
        _h = Screen.height;

        _rt       = new RenderTexture(_w, _h, 0, RenderTextureFormat.ARGB32);
        _readback = new Texture2D(_w, _h, TextureFormat.BGRA32, false);

        if (aboveIconsCamera != null)
            aboveIconsCamera.targetTexture = _rt;//카메라에 renderTexture 할당

        CreateOverlayWindow();
    }

    // RenderTexture → Win32 창으로 매 프레임 복사
    //유니티의 랜더링이 끝난 후 실행 됨
    void LateUpdate()
    {
        if (_overlayHwnd == IntPtr.Zero) return;

        // GPU → CPU 픽셀 읽기 (비용이 있으므로 카메라를 꼭 분리할 것)
        RenderTexture.active = _rt;
        _readback.ReadPixels(new Rect(0, 0, _w, _h), 0, 0, false);
        _readback.Apply();
        RenderTexture.active = null;

        BlitToOverlay(_readback.GetRawTextureData());
    }

    void OnDestroy()
    {
        if (_overlayHwnd != IntPtr.Zero) DestroyWindow(_overlayHwnd);
        if (_rt       != null) Destroy(_rt);
        if (_readback != null) Destroy(_readback);
    }

    // ── Win32 창 생성 ───────────────────────────────────────────────

    void CreateOverlayWindow()
    {
        _wndProc = (hwnd, msg, wp, lp) => DefWindowProc(hwnd, msg, wp, lp);

        var wc = new WNDCLASSEX//일종의 핸들생성
        {
            cbSize       = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
            lpfnWndProc  = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = "UnityAboveIcons"
        };
        RegisterClassEx(ref wc);//핸들 등록?

        _overlayHwnd = CreateWindowEx(
            WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST,//투명, 클릭 관통, 아이콘 위 설정
            "UnityAboveIcons", "",//클래스 이름 및 창 제목 (보이지 않으므로 빈 문자열)
            WS_POPUP,//보더리스 팝업 스타일
            0, 0, _w, _h,//화면 전체 크기로 생성
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);//부모 창 없음, 메뉴 없음, 인스턴스 핸들 없음, 추가 매개변수 없음

        // 아이콘 위 레이어에 고정,SetWindowPos 함수는 창 크기 및 위치를 설정하는데 사용됩니다. 여기서는 SWP_NOMOVE와 SWP_NOSIZE 플래그를 사용하여 창의 위치와 크기를 변경하지 않고, SWP_NOACTIVATE 플래그로 창이 활성화되지 않도록 합니다.
        SetWindowPos(_overlayHwnd, HWND_TOPMOST, 0, 0, _w, _h,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        Debug.Log($"[AboveIconsWindow] 오버레이 창 생성 완료: {_overlayHwnd}");
    }

    // ── 픽셀 데이터 → Win32 레이어드 창 ────────────────────────────

    void BlitToOverlay(byte[] pixels)
    {
        //getDC로 화면 DC 얻고, CreateCompatibleDC로 메모리 DC 생성
        IntPtr screenDC = GetDC(IntPtr.Zero);
        IntPtr memDC    = CreateCompatibleDC(screenDC);

        var bmi = new BITMAPINFO
        {
            biSize     = Marshal.SizeOf(typeof(BITMAPINFO)),
            biWidth    = _w,
            biHeight   = -_h,  // 음수 = top-down
            biPlanes   = 1,
            biBitCount = 32
        };

        IntPtr bits;
        IntPtr hBmp = CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
        IntPtr hOld = SelectObject(memDC, hBmp);

        Marshal.Copy(pixels, 0, bits, pixels.Length);

        var ptDst   = new POINT { x = 0, y = 0 };
        var ptSrc   = new POINT { x = 0, y = 0 };
        var size    = new SIZE  { cx = _w, cy = _h };
        var blend   = new BLENDFUNCTION { Op = 0, Flags = 0, Alpha = 255, Format = 1 };

        UpdateLayeredWindow(_overlayHwnd, screenDC,
                            ref ptDst, ref size,
                            memDC, ref ptSrc,
                            0, ref blend, ULW_ALPHA);

        SelectObject(memDC, hOld);
        DeleteObject(hBmp);
        DeleteDC(memDC);
        ReleaseDC(IntPtr.Zero, screenDC);
    }

#endif
}