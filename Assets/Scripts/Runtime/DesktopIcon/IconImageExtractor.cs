using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 파일 / 폴더 / 셸 가상 폴더의 시스템 아이콘을 Unity Texture2D로 변환합니다.
///
/// 두 가지 모드 제공:
/// 1. GetIconTexture     : 32×32 또는 16×16 (SHGetFileInfo 단순 호출)
/// 2. GetIconTextureHQ   : 16/32/48/256 px 고해상도 (시스템 ImageList 직접 접근)
///
/// 가상 폴더 처리:
/// - "::{GUID}" 형식 경로는 SHParseDisplayName으로 PIDL 변환 후 처리
/// - 휴지통, 내 PC 등 파일 시스템 경로가 없는 셸 객체 지원
/// </summary>
public static class IconImageExtractor
{
    // ── 구조체 ──────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int    iIcon;
        public uint   dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool   fIcon;
        public int    xHotspot;
        public int    yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int    bmType;
        public int    bmWidth;
        public int    bmHeight;
        public int    bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint   biSize;
        public int    biWidth;
        public int    biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint   biCompression;
        public uint   biSizeImage;
        public int    biXPelsPerMeter;
        public int    biYPelsPerMeter;
        public uint   biClrUsed;
        public uint   biClrImportant;
    }

    /// <summary>시스템 ImageList COM 인터페이스 (jumbo 256px 아이콘 추출용)</summary>
    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add();
        [PreserveSig] int ReplaceIcon();
        [PreserveSig] int SetOverlayImage();
        [PreserveSig] int Replace();
        [PreserveSig] int AddMasked();
        [PreserveSig] int Draw();
        [PreserveSig] int Remove();
        [PreserveSig] int GetIcon(int i, uint flags, out IntPtr picon);
    }

    // ── P/Invoke ────────────────────────────────────────────────────
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("shell32.dll")]
    private static extern IntPtr SHGetFileInfo(IntPtr pidl, uint attr, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr ptr);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);

    [DllImport("gdi32.dll")] private static extern bool   DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern int    GetObject(IntPtr h, int size, out BITMAP bm);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool   DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int    GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, IntPtr bits, ref BITMAPINFOHEADER bi, uint usage);

    // ── 상수 ────────────────────────────────────────────────────────
    private const uint SHGFI_ICON          = 0x000000100;
    private const uint SHGFI_LARGEICON     = 0x000000000;
    private const uint SHGFI_SMALLICON     = 0x000000001;
    private const uint SHGFI_USEFILEATTR   = 0x000000010;
    private const uint SHGFI_PIDL          = 0x000000008;
    private const uint SHGFI_SYSICONINDEX  = 0x000004000;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    // 시스템 ImageList 사이즈 인덱스
    private const int SHIL_LARGE      = 0; // 32×32
    private const int SHIL_SMALL      = 1; // 16×16
    private const int SHIL_EXTRALARGE = 2; // 48×48
    private const int SHIL_JUMBO      = 4; // 256×256

    private const uint BI_RGB         = 0;
    private const uint DIB_RGB_COLORS = 0;
    private const uint ILD_TRANSPARENT = 0x00000001;

    // ──────────────────────────────────────────────────────────────────

    /// <summary>표준 32×32 또는 16×16 아이콘을 가져옵니다.</summary>
    public static Texture2D GetIconTexture(string path, bool largeIcon = true)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var info  = new SHFILEINFO();
        uint flags = SHGFI_ICON | (largeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON);

        IntPtr res;
        if (path.StartsWith("::{"))
        {
            // 셸 가상 경로 (휴지통 등) — PIDL 변환 후 SHGetFileInfo
            if (SHParseDisplayName(path, IntPtr.Zero, out IntPtr pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
            {
                Debug.LogWarning($"[IconImageExtractor] PIDL 파싱 실패: {path}");
                return null;
            }
            res = SHGetFileInfo(pidl, 0, ref info,
                                (uint)Marshal.SizeOf<SHFILEINFO>(), flags | SHGFI_PIDL);
            CoTaskMemFree(pidl);
        }
        else
        {
            // 일반 파일 경로 — USEFILEATTR로 실제 파일 존재 여부 무시 (속도)
            res = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref info,
                                (uint)Marshal.SizeOf<SHFILEINFO>(), flags | SHGFI_USEFILEATTR);
        }

        if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            Debug.LogWarning($"[IconImageExtractor] 아이콘 없음: {path}");
            return null;
        }

        Texture2D tex = HIconToTexture2D(info.hIcon);
        DestroyIcon(info.hIcon);
        return tex;
    }

    /// <summary>
    /// 고해상도 아이콘 (size: 16/32/48/256).
    /// SHGetFileInfo는 32×32 고정이라 더 큰 아이콘은 시스템 ImageList에서 직접 추출해야 합니다.
    /// </summary>
    public static Texture2D GetIconTextureHQ(string path, int size = 256)
    {
        if (string.IsNullOrEmpty(path)) return null;

        // 요청 사이즈 → SHIL 인덱스 매핑
        int shil =
            size <= 16 ? SHIL_SMALL :
            size <= 32 ? SHIL_LARGE :
            size <= 48 ? SHIL_EXTRALARGE :
                         SHIL_JUMBO;

        // 1. 시스템 이미지 리스트의 아이콘 인덱스 얻기 (SHGFI_SYSICONINDEX)
        var info  = new SHFILEINFO();
        uint flags = SHGFI_SYSICONINDEX;

        IntPtr res;
        if (path.StartsWith("::{"))
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out IntPtr pidl, 0, out _) != 0)
                return null;
            res = SHGetFileInfo(pidl, 0, ref info,
                                (uint)Marshal.SizeOf<SHFILEINFO>(), flags | SHGFI_PIDL);
            CoTaskMemFree(pidl);
        }
        else
        {
            res = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref info,
                                (uint)Marshal.SizeOf<SHFILEINFO>(), flags | SHGFI_USEFILEATTR);
        }
        if (res == IntPtr.Zero) return null;

        // 2. 해당 사이즈의 시스템 ImageList에서 HICON 추출
        var iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
        if (SHGetImageList(shil, ref iidImageList, out IImageList list) != 0 || list == null)
            return null;

        list.GetIcon(info.iIcon, ILD_TRANSPARENT, out IntPtr hIcon);
        if (hIcon == IntPtr.Zero) return null;

        Texture2D tex = HIconToTexture2D(hIcon);
        DestroyIcon(hIcon);
        return tex;
    }

    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// HICON 핸들을 Texture2D로 변환.
    /// 1. GetIconInfo로 컬러 비트맵(hbmColor) 추출
    /// 2. GetDIBits로 픽셀 데이터(BGRA) 가져오기
    /// 3. BGRA → RGBA + Y축 반전 (Unity는 bottom-up 좌표계)
    /// 4. Texture2D 생성
    /// </summary>
    private static Texture2D HIconToTexture2D(IntPtr hIcon)
    {
        if (!GetIconInfo(hIcon, out ICONINFO ii)) return null;

        try
        {
            GetObject(ii.hbmColor, Marshal.SizeOf<BITMAP>(), out BITMAP bm);
            int w = bm.bmWidth;
            int h = bm.bmHeight;

            // biHeight가 음수면 top-down DIB (윗줄부터 저장됨)
            var bi = new BITMAPINFOHEADER
            {
                biSize        = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth       = w,
                biHeight      = -h,
                biPlanes      = 1,
                biBitCount    = 32,
                biCompression = BI_RGB
            };

            int    byteCount = w * h * 4;
            IntPtr buf       = Marshal.AllocHGlobal(byteCount);
            IntPtr hdc       = CreateCompatibleDC(IntPtr.Zero);

            GetDIBits(hdc, ii.hbmColor, 0, (uint)h, buf, ref bi, DIB_RGB_COLORS);

            byte[] data = new byte[byteCount];
            Marshal.Copy(buf, data, 0, byteCount);
            Marshal.FreeHGlobal(buf);
            DeleteDC(hdc);

            // Windows BGRA + top-down → Unity RGBA + bottom-up
            byte[] rgba = new byte[byteCount];
            for (int y = 0; y < h; y++)
            {
                int srcRow = y * w * 4;
                int dstRow = (h - 1 - y) * w * 4;
                for (int x = 0; x < w; x++)
                {
                    int s = srcRow + x * 4;
                    int d = dstRow + x * 4;
                    rgba[d + 0] = data[s + 2]; // R ← B
                    rgba[d + 1] = data[s + 1]; // G ← G
                    rgba[d + 2] = data[s + 0]; // B ← R
                    rgba[d + 3] = data[s + 3]; // A
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(rgba);
            tex.Apply();
            tex.filterMode = FilterMode.Point; // 픽셀 아트 선명도 유지
            return tex;
        }
        finally
        {
            // GDI 비트맵 누수 방지
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
            if (ii.hbmMask  != IntPtr.Zero) DeleteObject(ii.hbmMask);
        }
    }
}