using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class BelowIconsWindow : MonoBehaviour
{
#if UNITY_STANDALONE_WIN

    [Header("아이콘 아래에 렌더링할 카메라")]
    public Camera belowIconsCamera;

    [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string wnd);
    [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string wnd);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(
        IntPtr hwnd, uint msg, UIntPtr wp, IntPtr lp,
        uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll")] static extern IntPtr CreateWindowEx(
        uint exStyle, string cls, string title, uint style,
        int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc,
        uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int nCmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(
        IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);

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
    const uint WS_CHILD          = 0x40000000;
    const uint ULW_ALPHA         = 0x00000002;
    const uint WS_POPUP     = 0x80000000;   // WS_CHILD 대신 WS_POPUP 사용
    const uint SWP_NOMOVE   = 0x0002;
    const uint SWP_NOSIZE   = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_SHOWWINDOW = 0x0040;

    IntPtr           _hwnd;
    RenderTexture    _rt;
    Texture2D        _readback;
    WndProcDelegate  _wndProc;
    int              _w, _h;

    void Start()
    {
        _w = Screen.width;
        _h = Screen.height;

        _rt       = new RenderTexture(_w, _h, 0, RenderTextureFormat.ARGB32);
        _readback = new Texture2D(_w, _h, TextureFormat.BGRA32, false);

        if (belowIconsCamera != null)
            belowIconsCamera.targetTexture = _rt;

        Invoke(nameof(CreateWindow), 0.5f);
    }

    void CreateWindow()
    {
        IntPtr workerW = GetWorkerW();
        if (workerW == IntPtr.Zero)
        {
            Debug.LogError("[BelowIconsWindow] WorkerW 없음");
            return;
        }

        _wndProc = (hwnd, msg, wp, lp) => DefWindowProc(hwnd, msg, wp, lp);

        var wc = new WNDCLASSEX
        {
            cbSize        = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = "UnityBelowIcons"
        };
        RegisterClassEx(ref wc);

        // WorkerW를 부모로 설정 → 아이콘 아래에 위치
        _hwnd = CreateWindowEx(
            WS_EX_LAYERED | WS_EX_TRANSPARENT,
            "UnityBelowIcons", "",
            WS_CHILD,
            0, 0, _w, _h,
            workerW,           // ← WorkerW가 부모
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        Debug.Log($"[BelowIconsWindow] 아이콘 아래 창 생성 완료: {_hwnd}");
    }

    void LateUpdate()
    {
        if (_hwnd == IntPtr.Zero) return;

        RenderTexture.active = _rt;
        _readback.ReadPixels(new Rect(0, 0, _w, _h), 0, 0, false);
        _readback.Apply();
        RenderTexture.active = null;

        BlitToWindow(_readback.GetRawTextureData());
    }

    void BlitToWindow(byte[] pixels)
    {
        IntPtr screenDC = GetDC(IntPtr.Zero);
        IntPtr memDC    = CreateCompatibleDC(screenDC);

        var bmi = new BITMAPINFO
        {
            biSize     = Marshal.SizeOf(typeof(BITMAPINFO)),
            biWidth    = _w,
            biHeight   = -_h,
            biPlanes   = 1,
            biBitCount = 32
        };

        IntPtr bits;
        IntPtr hBmp = CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
        IntPtr hOld = SelectObject(memDC, hBmp);
        Marshal.Copy(pixels, 0, bits, pixels.Length);

        var ptDst = new POINT { x = 0, y = 0 };
        var ptSrc = new POINT { x = 0, y = 0 };
        var size  = new SIZE  { cx = _w, cy = _h };
        var blend = new BLENDFUNCTION { Op = 0, Flags = 0, Alpha = 255, Format = 1 };

        UpdateLayeredWindow(_hwnd, screenDC,
                            ref ptDst, ref size,
                            memDC, ref ptSrc,
                            0, ref blend, ULW_ALPHA);

        SelectObject(memDC, hOld);
        DeleteObject(hBmp);
        DeleteDC(memDC);
        ReleaseDC(IntPtr.Zero, screenDC);
    }

    IntPtr GetWorkerW()
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        SendMessageTimeout(progman, 0x052C, UIntPtr.Zero,     IntPtr.Zero,  0, 1000, out _);
        SendMessageTimeout(progman, 0x052C, new UIntPtr(0xD), new IntPtr(1), 0, 1000, out _);
        SendMessageTimeout(progman, 0x052C, UIntPtr.Zero,     IntPtr.Zero,  0, 1000, out _);

        // Windows 10
        IntPtr workerW = IntPtr.Zero;
        while (true)
        {
            workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null);
            if (workerW == IntPtr.Zero) break;
            IntPtr shelf = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shelf != IntPtr.Zero)
            {
                IntPtr found = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null);
                if (found != IntPtr.Zero) return found;
            }
        }

        // Windows 11
        IntPtr shelfUnderProgman = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (shelfUnderProgman != IntPtr.Zero)
        {
            IntPtr fallback = FindWindowEx(IntPtr.Zero, progman, "WorkerW", null);
            return fallback != IntPtr.Zero ? fallback : progman;
        }

        return IntPtr.Zero;
    }

    void OnDestroy()
    {
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        if (_rt       != null) Destroy(_rt);
        if (_readback != null) Destroy(_readback);
    }

#endif
}