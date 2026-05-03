# comPETer — 바탕화면 데스크톱 펫 시스템

Unity로 만든 데스크톱 펫이 **바탕화면 위, 일반 창 아래** 레이어에서 살아 움직이도록 하는 시스템입니다. Wallpaper Engine이나 망고 같은 데스크톱 펫의 동작 방식을 Unity로 구현했습니다.

---

## 핵심 컨셉

```
┌────────────────────────────────────────────┐
│  일반 앱 창 (브라우저, 메모장 등)            │  ← 가장 위
├────────────────────────────────────────────┤
│  Unity 창 (펫 + 가구 + 복제된 아이콘)        │  ← 여기에 상주
├────────────────────────────────────────────┤
│  실제 바탕화면 아이콘                       │
│  바탕화면 배경 이미지                       │  ← 가장 아래
└────────────────────────────────────────────┘
```

Unity 창은 단 하나만 띄우지만, 그 안에서 **Sorting Layer**로 깊이감을 시뮬레이션해 다양한 시각 효과를 만듭니다.

---

## 시스템 구성

| 스크립트 | 역할 |
|---|---|
| `DesktopOverlay.cs` | Unity 창을 투명·보더리스·바탕화면 위 고정 오버레이로 변환 |
| `DesktopIconReader.cs` | 실제 바탕화면 아이콘의 좌표·이름·경로 읽기 |
| `IconImageExtractor.cs` | 파일/폴더의 시스템 아이콘 → Unity Texture2D 변환 |
| `WindowsIconSize.cs` | 사용자가 설정한 아이콘 크기 (레지스트리) 읽기 |
| `ScreenToWorld.cs` | 윈도우 픽셀 좌표 → Unity world 좌표 변환 |
| `IconView.cs` | 복제 아이콘 한 개의 프리팹 컴포넌트 |
| `DesktopIconLayer.cs` | 모든 복제 아이콘을 관리·갱신하는 매니저 |

---

## 작동 원리

### 1. 창 자체 처리 — `DesktopOverlay`

| 기능 | 사용한 Win32 API | 효과 |
|---|---|---|
| 보더리스 | `WS_POPUP` 스타일 | 타이틀바·테두리 제거 |
| 투명 | `DwmExtendFrameIntoClientArea` (음수 마진) + `WS_EX_LAYERED` | 카메라 배경(α=0)이 그대로 투명 |
| 바탕화면 위 고정 | `HWND_BOTTOM` + `SetWinEventHook` | Z-order 변경 시에만 자동 재고정 (폴링 X) |
| Alt+Tab 숨김 | `WS_EX_TOOLWINDOW` 추가, `WS_EX_APPWINDOW` 제거 | 작업 전환 목록 + 작업 표시줄에서 숨김 |
| 클릭 관통 | `WS_EX_TRANSPARENT` 토글 | 투명 영역은 뒷 창으로 클릭 전달 |

**핵심 설계 결정**: `SetParent(WorkerW)` 방식이 아니라 `HWND_BOTTOM`을 사용합니다. `SetParent`로 자식 창이 되면 DWM 투명이 깨지기 때문입니다.

### 2. 바탕화면 아이콘 복제 — `DesktopIconReader` + `IconImageExtractor`

```
1. Progman → SHELLDLL_DefView → SysListView32 핸들 탐색
2. explorer.exe 프로세스 메모리에 버퍼 할당 (LVM 메시지가 외부 프로세스 주소를 요구)
3. LVM_GETITEMPOSITION / LVM_GETITEMW 로 좌표/이름 읽기
4. 라벨로 실제 파일 경로 매칭 (휴지통 등은 GUID 경로로 매핑)
5. SHGetFileInfo + 시스템 ImageList(Jumbo 256px) 로 아이콘 추출
6. HICON → BGRA 픽셀 → RGBA + Y축 반전 → Texture2D
```

윈도우 11에서는 `SHELLDLL_DefView`가 `WorkerW` 안쪽에 있을 수 있어 두 위치 모두 탐색합니다.

### 3. 좌표·크기 동기화 — `DesktopIconLayer`

- 사용자가 윈도우에서 설정한 아이콘 크기(레지스트리 `IconSize`)를 그대로 사용
- 카메라 기준 `Screen.height / (orthographicSize * 2)` 로 PPU 계산 → 1px = 1px
- 2초마다 갱신해 사용자가 아이콘을 옮기거나 추가/삭제해도 동기화

---

## 설치 및 설정

### 1. Unity 프로젝트 설정

**Player Settings → Resolution and Presentation**
- Fullscreen Mode: Windowed
- Run In Background: ✅
- Display Resolution Dialog: Disabled

**Player Settings → Other Settings**
- Active Input Handling: Input System Package 또는 Both

### 2. 카메라 설정

```
Main Camera
├── Projection      : Orthographic
├── Clear Flags     : Solid Color
└── Background      : (0, 0, 0, 0)   ← α=0 필수
```

### 3. Sorting Layer 추가

**Project Settings → Tags and Layers → Sorting Layers**

위에서부터 아래 순서로 추가합니다 (위가 먼저 그려짐 = 더 뒤에 있음).

```
Background     ← 수납장 뒷면 등 가장 뒤 오브젝트
IconLayer      ← 복제된 바탕화면 아이콘
Middle         ← 수납장 옆면 등 아이콘 위 오브젝트
Dynamic        ← 고양이 등 동적 오브젝트 (런타임에 레이어 변경 가능)
```

### 4. 씬 구성

```
Scene
├── Main Camera
├── DesktopOverlay  (DesktopOverlay.cs)
├── DesktopIconLayer (DesktopIconLayer.cs)
│   ├── prefab: IconView 프리팹
│   └── targetCamera: Main Camera
└── 펫/가구 오브젝트들
```

### 5. IconView 프리팹

```
IconView (IconView.cs)
├── Icon  (SpriteRenderer)
└── Label (TextMeshPro - Text)
        └── Font Asset: 한글 글리프 포함 폰트
```

한글 라벨을 표시하려면 **Window → TextMeshPro → Font Asset Creator**에서 한글 글리프(`가-힣` 범위)를 포함한 폰트 에셋을 미리 생성해 두세요.

---

## 사용 예 — 펫 동작 시 레이어 변경

```csharp
public class CatBehavior : MonoBehaviour
{
    SpriteRenderer _sr;

    void Awake() => _sr = GetComponent<SpriteRenderer>();

    public void HideBehindFurniture() => _sr.sortingLayerName = "Background";
    public void ShowMiddle()          => _sr.sortingLayerName = "Middle";
    public void JumpToFront()         => _sr.sortingLayerName = "Dynamic";
}
```

펫이 가구 뒤로 숨었다가 나타나는 효과 등을 한 줄로 구현할 수 있습니다.

---

## 알려진 제약

| 항목 | 내용 |
|---|---|
| OS | Windows 전용 (10/11). Editor에서는 동작 안 함 |
| 그래픽 API | DirectX 권장 (Vulkan/OpenGL은 DWM 투명 동작이 다를 수 있음) |
| 다중 모니터 | 현재는 메인 모니터 기준 좌표만 처리 |
| 권한 | explorer.exe와 동일 사용자 권한 필요 (관리자 권한 불필요) |
| Windows 11 24H2+ | explorer 내부 구조 변경 시 일부 메시지가 동작 안 할 수 있음 |

---

## 빌드 시 주의

- **IL2CPP**: 일부 P/Invoke가 stripping될 수 있음 → `link.xml`에 `System.Runtime.InteropServices` 보존 권장
- **Mono**: 별도 설정 없이 동작
- 빌드 후 첫 실행 시 **Run In Background**가 꺼져 있으면 펫 창이 다른 창에 가려진 채로 멈출 수 있음

---

## 라이선스 / 참고

- 사용한 Win32 API는 모두 Microsoft 공개 문서에 있는 표준 API입니다.
- Wallpaper Engine, ShareX 등 기존 도구의 일반적인 접근 방식을 참고했습니다.
