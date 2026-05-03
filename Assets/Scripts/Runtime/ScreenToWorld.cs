using UnityEngine;

/// <summary>
/// 윈도우 스크린 픽셀 좌표를 Unity world 좌표로 변환하는 헬퍼.
///
/// 좌표계 차이:
/// - Windows: 좌상단 (0,0), Y축이 아래로 증가
/// - Unity  : 카메라 중심 기준, Y축이 위로 증가
/// </summary>
public static class ScreenToWorld
{
    /// <summary>스크린 픽셀 좌표를 world 좌표로 변환합니다.</summary>
    public static Vector3 Pixel(Vector2 screenPx, Camera cam)
    {
        float w = Screen.width;
        float h = Screen.height;

        // Y축 반전 — 윈도우 좌표를 Unity viewport 좌표로
        Vector3 viewport = new Vector3(
            screenPx.x / w,
            1f - screenPx.y / h,
            0f);

        Vector3 world = cam.ViewportToWorldPoint(viewport);
        world.z = 0f;
        return world;
    }
}