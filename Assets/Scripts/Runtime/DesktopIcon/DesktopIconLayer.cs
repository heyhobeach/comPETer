using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 바탕화면 아이콘들을 Unity 안에서 동일한 위치/모양으로 복제 표시합니다.
///
/// 동작:
/// 1. DesktopIconReader로 모든 아이콘의 좌표/이름/경로 읽기
/// 2. IconImageExtractor로 각 아이콘의 이미지를 Texture2D로 변환
/// 3. IconView 프리팹을 화면 좌표 위치에 배치
/// 4. refreshInterval 주기로 갱신 (사용자가 아이콘을 옮기거나 추가/삭제할 수 있으므로)
///
/// 활용 예:
///   고양이/수납장 같은 펫 오브젝트를 Sorting Layer "Middle"이나 "Dynamic"에 배치하면
///   복제된 아이콘 위/아래에 시각적으로 겹쳐 보이게 할 수 있습니다.
///   (단일 창이지만 Sorting Layer로 깊이감을 시뮬레이션)
/// </summary>
public class DesktopIconLayer : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IconView prefab;
    [SerializeField] private Camera   targetCamera;

    [Header("Settings")]
    [Tooltip("아이콘 정보 갱신 주기 (초)")]
    [SerializeField] private float  refreshInterval  = 2f;

    [Tooltip("복제 아이콘이 그려질 Sorting Layer")]
    [SerializeField] private string sortingLayerName = "IconLayer";
    [SerializeField] private int    sortingOrder     = 0;

    // ListView 인덱스별 생성된 뷰 캐시 (재생성 비용 절감)
    private readonly Dictionary<int, IconView> _views = new Dictionary<int, IconView>();

    private float _timer;
    private int   _iconSize;
    private float _ppu;

    private void Start()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        _iconSize = WindowsIconSize.GetDesktopIconSize();

        // 카메라 기준 1픽셀 = 몇 worldUnit인지 계산
        // PPU를 이 값으로 설정하면 텍스처 픽셀 수 = 화면 픽셀 수
        _ppu = Screen.height / (targetCamera.orthographicSize * 2f);

        Refresh();
    }

    private void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer >= refreshInterval)
        {
            _timer = 0f;
            Refresh();
        }
    }

    /// <summary>아이콘 목록을 가져와 화면 상태를 동기화합니다.</summary>
    private void Refresh()
    {
        var icons = DesktopIconReader.GetIcons();
        var seen  = new HashSet<int>();

        // 사용자 설정 사이즈에 가장 가까운 시스템 ImageList 사이즈 선택
        int extractSize = _iconSize <= 48 ? 48 : (_iconSize <= 96 ? 96 : 256);

        foreach (var icon in icons)
        {
            seen.Add(icon.index);

            // ListView 좌표는 셀 좌상단 — 셀 중앙으로 이동시켜야 정렬됨
            Vector2 cellCenter = icon.screenPos +
                                 new Vector2(_iconSize * 0.5f, _iconSize * 0.5f);
            Vector3 worldPos   = ScreenToWorld.Pixel(cellCenter, targetCamera);

            // 이미 존재하면 위치만 갱신 (이동된 경우 대응)
            if (_views.TryGetValue(icon.index, out var view))
            {
                view.transform.position = worldPos;
                continue;
            }

            // 신규 아이콘 — 텍스처 추출 후 뷰 생성
            var newView = Instantiate(prefab, worldPos, Quaternion.identity, transform);

            Texture2D tex = string.IsNullOrEmpty(icon.fullPath)
                ? null
                : IconImageExtractor.GetIconTextureHQ(icon.fullPath, extractSize);

            newView.Setup(tex, icon.label, _ppu, _iconSize);
            newView.ApplySorting(sortingLayerName, sortingOrder);

            _views[icon.index] = newView;
        }

        // 사라진 아이콘 정리 — 사용자가 아이콘을 삭제했거나 인덱스가 바뀐 경우
        var toRemove = new List<int>();
        foreach (var kv in _views)
            if (!seen.Contains(kv.Key)) toRemove.Add(kv.Key);

        foreach (var key in toRemove)
        {
            Destroy(_views[key].gameObject);
            _views.Remove(key);
        }
    }
}