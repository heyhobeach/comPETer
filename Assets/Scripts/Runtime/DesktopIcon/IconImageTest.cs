using UnityEngine;

public class IconImageTest : MonoBehaviour
{
    [SerializeField] IconView prefab;

    void Start()
    {
        var icons = DesktopIconReader.GetIcons();
        if (icons.Count == 0) { Debug.LogError("아이콘 없음"); return; }

        int iconSize    = WindowsIconSize.GetDesktopIconSize();
        int extractSize = iconSize <= 48 ? 48 : (iconSize <= 96 ? 96 : 256);
        float ppu       = Screen.height / (Camera.main.orthographicSize * 2f);

        Debug.Log($"[Test] iconSize={iconSize}, extractSize={extractSize}, ppu={ppu}, screenH={Screen.height}, orthoSize={Camera.main.orthographicSize}");

        var first = icons[0];
        Debug.Log($"[Test] 대상: {first.label} / {first.fullPath}");

        var tex = IconImageExtractor.GetIconTextureHQ(first.fullPath, extractSize);
        Debug.Log($"[Test] tex={(tex != null ? $"{tex.width}x{tex.height}" : "NULL")}");

        if (tex == null) return;

        var view = Instantiate(prefab);
        view.transform.position = Vector3.zero;
        view.Setup(tex, first.label, ppu, iconSize);

        Debug.Log($"[Test] view 생성됨 @ {view.transform.position}, scale={view.transform.localScale}");
    }
}