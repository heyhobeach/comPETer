using UnityEngine;
using TMPro;

/// <summary>
/// 바탕화면 아이콘 하나를 화면에 그리는 뷰 컴포넌트.
///
/// 프리팹 구조 예시:
///   IconView (이 스크립트)
///   ├── Icon  (SpriteRenderer)
///   └── Label (TextMeshPro 3D Text)
///
/// DesktopIconLayer가 Refresh 주기에 맞춰 Setup / ApplySorting을 호출합니다.
/// </summary>
public class IconView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer iconRenderer;
    [SerializeField] private TMP_Text       labelText;

    /// <summary>
    /// 아이콘 텍스처와 라벨을 적용합니다.
    /// </summary>
    /// <param name="tex">아이콘 텍스처 (null이면 무시)</param>
    /// <param name="label">아이콘 아래 표시될 텍스트</param>
    /// <param name="ppu">Pixels Per Unit — 1px = 1px가 되도록 카메라에 맞춰 계산된 값</param>
    /// <param name="targetPx">실제 화면에 표시될 목표 픽셀 크기</param>
    public void Setup(Texture2D tex, string label, float ppu, int targetPx)
    {
        if (tex != null && iconRenderer != null)
        {
            // PPU를 카메라 매핑값으로 두면 텍스처의 픽셀 수가 그대로 화면 픽셀 수가 됨
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                ppu);
            iconRenderer.sprite = sprite;

            // 텍스처가 targetPx보다 크거나 작으면 스케일로 보정
            float scale = (float)targetPx / tex.width;
            iconRenderer.transform.localScale = Vector3.one * scale;
        }

        if (labelText != null)
        {
            labelText.text = label;

            // 아이콘 크기에 맞춰 라벨 위치/크기 조정
            float iconWorldSize = targetPx / ppu;

            labelText.fontSize = 12f;
            float labelScale   = iconWorldSize * 0.15f;
            labelText.transform.localScale    = Vector3.one * labelScale;
            labelText.transform.localPosition = new Vector3(0, -iconWorldSize * 0.7f, 0);

            var rect = labelText.rectTransform;
            rect.sizeDelta  = new Vector2(20f, 5f);
            labelText.alignment = TextAlignmentOptions.Top;
        }
    }

    /// <summary>
    /// 이 아이콘 뷰를 특정 Sorting Layer에 배치합니다.
    /// 텍스트는 아이콘보다 한 단계 위(order+1)에 그려서 글자가 안 가려지게 함.
    /// </summary>
    public void ApplySorting(string layerName, int order)
    {
        foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
        {
            sr.sortingLayerName = layerName;
            sr.sortingOrder     = order;
        }

        foreach (var tmp in GetComponentsInChildren<TMP_Text>(true))
        {
            // TMP는 MeshRenderer를 통해 렌더되므로 그쪽에 sorting 지정
            var mr = tmp.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sortingLayerName = layerName;
                mr.sortingOrder     = order + 1;
            }
        }
    }
}