# HybridPipPlayer

WPF(.NET 8) 하이브리드 미디어 플레이어. 한 개의 보더리스 창 안에서 두 엔진을 URL로 자동 전환한다.

| 엔진 | 대상 | 기술 |
| --- | --- | --- |
| A: libmpv | 로컬 영상, 유튜브 등 | `libmpv-2.dll` + `yt-dlp` + SponsorBlock(Lua) |
| B: WebView2 | 넷플릭스, 디즈니+ (DRM) | Edge WebView2, 영구 프로필(`HybridPipPlayer_WebView2`)로 로그인 유지 |

`netflix.com` / `disneyplus.com`(서브도메인 포함)만 엔진 B로 가고, 나머지 URL·파일 경로는 엔진 A로 간다.

## 파일

저장소 루트가 곧 프로젝트 폴더다(`HybridPipPlayer.csproj`가 루트에 있다).

| 파일 | 역할 |
| --- | --- |
| `MainWindow.xaml(.cs)` | 창, 라우팅, PIP, 클릭 투과, 배경화면, 스냅, 비율 유지, 자막 크기, OTT 키 브릿지 |
| `MainWindow.Playback.cs` | 재생 줄(재생·탐색·속도·볼륨), 열기, 재생 목록 패널, 자막·소리 메뉴, 스크린샷과 토스트, 전체 화면, 이어 보기, 설정 연결 |
| `MainWindow.Remote.cs` | 리모컨(4×3 창)과 기능 id를 기존 핸들러에 잇는 부분, 재생 줄에 올린 기능 아이콘 |
| `ToastWindow.xaml(.cs)` | 영상 위에 잠깐 뜨는 알림 창(포커스를 가져가지 않음). 스크린샷 저장 알림에 쓴다 |
| `SettingsWindow.xaml(.cs)` | 설정 창(재생, 모양, PIP, 오디오·자막, 화면·소리 조절, 북마크·정보) |
| `RemoteWindow.xaml(.cs)`, `FeatureCatalog.cs` | 리모컨 창과 올릴 수 있는 기능 목록 |
| `PipControls.xaml(.cs)`, `LockOverlay.cs` | PIP 컨트롤 줄, PIP 왼쪽 위 자물쇠 버튼(별도 최상위 창) |
| `MpvPlayer.cs` | libmpv P/Invoke 래퍼(옵션, 이벤트 스레드, 로그, 앰비언트 필터, SponsorBlock 설정, 재생 목록·트랙) |
| `AppSettings.cs`, `Bookmark.cs`, `RecentStore.cs`, `ResumeStore.cs` | 설정, 북마크, 최근 재생, 이어 보기 저장소(`%LocalAppData%\HybridPipPlayer\` 아래 json) |
| `MediaFiles.cs`, `MediaSession.cs`, `FileAssociation.cs`, `SingleInstance.cs` | 미디어 확장자·폴더 목록·m3u, 윈도우 미디어 컨트롤 연동, 연결 프로그램, 단일 실행 |
| `ThemeManager.cs`, `Themes/Dark.xaml` | 색 테마(A/B/C)와 어두운 테마 스타일 |
| `NativeMethods.cs`, `app.manifest` | Win32(핫키, WorkerW/Progman 탐색, 마우스 훅, DWM), PerMonitorV2 DPI 선언 |
| `Assets/icons/*.png` | 재생 줄 아이콘 15개. **임시로 만든 모양**이다(원본 PNG는 복구하지 못했다). 같은 이름으로 바꿔 넣으면 된다 |

## 필요한 바이너리 (저장소에 없음)

프로젝트 루트에 직접 둔다(`.gitignore`로 제외). 빌드 때 출력 폴더로 복사된다. 없으면 빌드 경고가 난다.

- `libmpv-2.dll` — 64비트. [shinchiro/mpv-winbuild-cmake](https://github.com/shinchiro/mpv-winbuild-cmake/releases) 의 `mpv-dev-x86_64-*.7z` 안에 있다. (실측: 20261021 빌드, 115 MB. 7-Zip이 없으면 윈도우 기본 `tar -xf`로도 풀린다)
- `yt-dlp.exe` — [yt-dlp/yt-dlp](https://github.com/yt-dlp/yt-dlp/releases) 릴리스. (실측: 2026.10.26, 릴리스의 `SHA2-256SUMS`와 대조해 받는다)
- WebView2 런타임(윈도우 11 기본 포함), SponsorBlock용 Python(`PATH` 또는 `C:\Python3xx`).
- `scripts/` — [po5/mpv_sponsorblock](https://github.com/po5/mpv_sponsorblock) 사본(GPL-3.0). **지금은 없다**(복원 기록 참고). 다시 받아 `scripts/`에 두면 빌드 때 복사된다.

## 빌드·실행

```powershell
dotnet build C:\ai\projects\HybridPipPlayer\HybridPipPlayer.csproj
dotnet run --project C:\ai\projects\HybridPipPlayer
```

대상 프레임워크는 `net8.0-windows10.0.19041.0`다(`MediaSession.cs`가 WinRT API를 쓴다). 실행 중인 앱이 `bin\`의 exe를 잡고 있으면 빌드가 실패하니 먼저 종료한다.

## 재생 줄과 부가 기능

재생 줄은 엔진 A(mpv)에만 연결된다. 엔진 B(Netflix/Disney+)는 사이트 자체 UI를 쓴다. 설정에서 영상 아래(기본)나 주소 줄 아래로 옮길 수 있다.

- **열기**: 파일, 폴더, 폴더(하위 포함), 주소, 최근 재생. 파일 하나를 열면 같은 폴더의 영상으로 재생 목록을 만든다(설정에서 끌 수 있음). 끌어다 놓기와 `.m3u`/`.m3u8`도 받는다.
- **재생 목록 패널**: 재생 줄의 목록 버튼(또는 리모컨의 "재생 목록")을 누르면 오른쪽에서 300px 패널이 밀려 나온다. 영상 HWND 위에는 WPF 요소를 겹칠 수 없어서 영상 영역을 옆으로 밀어내는 방식이다. 더블클릭·Enter로 재생, ✕·Delete로 빼기, 반복·섞기, 파일·폴더 추가, 저장, 비우기. PIP·전체 화면에 들어가면 자동으로 닫힌다.
- **캡처 버튼과 스크린샷 토스트**: 재생 줄의 카메라 버튼은 mpv 화면이면 원본 해상도로, 웹뷰 화면이면 웹뷰 화면을 PNG로 `내 사진\HybridPipPlayer\`에 저장한다(웹뷰의 DRM 구간은 검게 찍힐 수 있음). 저장하면 플레이어 창 오른쪽 아래에 토스트가 뜨고 썸네일, 파일 이름, "폴더 열기"(저장한 파일을 선택한 채로 탐색기)가 있다. 표시 시간(기본 8초)과 투명도(기본 90%)는 설정의 "모양"에서 바꾸고 "알림 미리 보기"로 확인한다. 마우스를 올려 두면 사라지지 않는다. 토스트를 화면 구석이 아니라 플레이어 창 안에 띄우는 이유는, 접근성 도구 같은 다른 프로그램의 패널이 더 높은 층으로 구석을 가리는 일이 있기 때문이다.
- **리모컨**: 자주 쓰는 기능 6칸(즐겨찾기)과 재생 줄에 올릴 기능을 고른다. 기능 목록은 `FeatureCatalog.cs`.
- **설정**: 재생(컨트롤 위치, 기본 볼륨·속도, 전체 화면 컨트롤 숨김, 이어 보기, 자동 목록), 모양(테마, 북마크 줄, 알림), PIP(너비, 컨트롤 숨김 시간), 오디오·자막(장치, 글자 크기, 선호 언어), 화면·소리 조절(밝기·대비·채도·감마, 5밴드 이퀄라이저), 북마크·정보.

## 단축키

전역 단축키(창이 투과 상태여도 동작). 등록에 실패하면(다른 앱과 충돌) 상단 바에 주황색으로 표시된다.

| 키 | 동작 |
| --- | --- |
| `Ctrl+Alt+P` | PIP 토글(우하단 480 너비, 항상 위, 상단 바는 16px 띠로 축소) |
| `Ctrl+Alt+C` | 클릭 투과 + 반투명 토글(PIP 자동 진입). 커서가 창 위에 오면 반대쪽 모서리로 피한다 |
| `Ctrl+Alt+B` | 바탕화면 아이콘 뒤 라이브 배경화면 토글. 다른 앱이 전체화면이면 자동 일시정지 |
| `Ctrl+Alt+G` | 앰비언트 글로우(영상 비율 밖 여백에 블러 배경) 토글 |
| `Ctrl+Alt+↑/↓` | 자막 크게/작게 (mpv: ±0.1, 웹뷰: ±0.2, 범위 0.8~4.0) |
| `Ctrl+Alt+L` | PIP 잠금 토글(PIP에서만 동작, 자물쇠 버튼과 같음) |

PIP에서는 영상 위 좌클릭 드래그로 이동, 우클릭으로 복귀. 창 가장자리 드래그로 크기 조절(영상 실제 비율 유지).
mpv 화면에서는 `Ctrl+↑/↓`(영상 창 포커스)로도 자막 크기를 바꿀 수 있다.

**PIP 잠금**: 커서가 PIP 위에 있으면 PIP 창 **바깥** 왼쪽 위 모서리 바로 위에 자물쇠 버튼(🔓/🔒)이 뜨고(영상·자막을 가리지 않음),
커서가 PIP와 버튼을 모두 벗어난 뒤 2초 지나면 사라진다. 위쪽 화면 공간이 모자라면 아래쪽, 둘 다 없으면 창 안쪽 왼쪽 위에 뜬다.
잠그면 영상·웹뷰 창을 비활성화해 마우스가 닿지 않게 하고(마우스를 스칠 때마다 플레이어 UI가 뜨는 것을 막음),
드래그 이동·크기 조절·우클릭 복귀도 막는다. `Ctrl+Alt+P`로 PIP를 나가면 자동으로 풀린다.
해제하려면 PIP 위에 커서를 올려 자물쇠를 누르거나 `Ctrl+Alt+L`.

## 데이터 위치

`%LocalAppData%\HybridPipPlayer\` — `mpv.log`(경고 이상 로그), `mpvcfg\`(SponsorBlock 설정·스크립트 사본), `settings.json`(설정), `bookmarks.json`, 최근 재생·이어 보기 기록.
`내 사진\HybridPipPlayer\` — 스크린샷 저장 폴더(OneDrive로 리디렉션된 PC에서는 OneDrive 사진 폴더 아래).
`%LocalAppData%\HybridPipPlayer_WebView2\` — WebView2 프로필(로그인 유지).

## 설계 결정과 실측 기록

- **클릭 투과 키가 `Ctrl+Alt+T`가 아닌 이유**: 이 PC에서 다른 프로그램이 이미 선점(RegisterHotKey 오류 1409). `Ctrl+Alt+C`로 변경.
- **배경화면(Windows 11 24H2+)**: `SHELLDLL_DefView`와 `WorkerW`가 둘 다 `Progman`의 자식이다. 옛 방식(최상위 `WorkerW` 탐색)은 엉뚱한 창을 돌려준다. 그래서 `Progman`에 붙이고 `DefView` 바로 아래에 두며, `Maximize` 대신 모니터 영역으로 `SetWindowPos`한다. 24H2 이전은 옛 방식으로 폴백.
- **mpv 자식 창이 마우스를 가져간다**: wid로 임베드한 mpv/WebView2 HWND가 입력을 받아 WPF 이벤트가 오지 않는다. PIP 이동/복귀는 저수준 마우스 훅(`WH_MOUSE_LL`)에서 직접 처리한다(시스템 이동 루프는 mpv의 마우스 캡처로 끊김). 가장자리 크기 조절은 자식 창이 가리지 않도록 창 안쪽에 6px 여백을 둔다.
- **자물쇠 버튼은 별도 창**: mpv/WebView2가 자식 HWND라 그 위에 WPF 요소를 겹칠 수 없다(Airspace). 활성화되지 않는 도구 창(`WS_EX_NOACTIVATE`)을 PIP 소유 창으로 띄우고 픽셀 단위로 PIP 바깥(위 → 아래 → 안쪽 순으로 공간이 있는 곳)에 배치한다. PIP에서 버튼으로 커서를 옮기는 동안 사라지지 않도록 호버는 "PIP 위 또는 버튼 위"로 판정한다. 호버는 150ms 타이머가 커서 위치와 `WindowFromPoint`로 판정한다(자식 창이 마우스를 가져가 WPF 이벤트가 오지 않기 때문).
- **잠금 = 자식 창 비활성화**: `EnableWindow(false)`를 모든 자식 HWND에 건다. 비활성 창에는 마우스 입력이 가지 않아 웹/mpv 플레이어 UI가 뜨지 않고, `WindowFromPoint`도 비활성 창을 건너뛰어 호버 판정은 그대로 동작한다. 크기 조절은 `WM_NCHITTEST`/`WM_SIZING`으로, 이동은 마우스 훅·상단 띠에서 막는다.
- **WebView2는 지연 초기화**: Collapsed 상태에서는 HWND가 만들어지지 않아 초기화가 끝나지 않을 수 있다. 처음 OTT 요청 때 화면을 보이게 한 뒤 초기화한다.
- **앰비언트**: `lavfi-complex` 필터(작게 줄여 블러 후 확대, 1920×1080 캔버스). 그래프에 연결되지 않은 스트림은 재생되지 않아 오디오가 있으면 `[ao]`로 패스스루하고, 켜는 동안 `hwdec=auto-copy`로 바꾼다. 켜면 창은 16:9로 맞춘다.
- **SponsorBlock**: 스크립트가 `mp.find_config_file("scripts")`로 `sponsorblock_shared`를 찾으므로 스크립트를 mpv config-dir 안으로 복사해 자동 로드한다. 카테고리 목록의 쉼표 때문에 `script-opts`가 아닌 `sponsorblock.conf` 파일로 넘긴다.
- **Disney+ 자막**: 자막은 `<timed-text-override-region>`의 shadow root 안 `.hive-subtitle-renderer-cue-window`에 그려지고 글자 크기는 줄마다 인라인 px(플레이어 폭에 비례)라 바깥 CSS가 닿지 않는다. shadow root 안에 `<style>`을 넣어 창 단위로 `transform: scale()` 한다(하단 중앙 기준). 라이브 페이지에서 1/2/3배 시험으로 크기 증가와 하단 고정을 확인했다.
- **창 비율**: mpv의 `video-params/aspect`를 받아 창 비율을 영상에 맞추고, 크기 조절은 `WM_SIZING`에서 유지한다. 웹뷰 화면이나 비율을 모르면 조절 시작 시점의 창 비율을 쓴다.
- **종료**: `mpv_terminate_destroy`는 백그라운드에서 호출하고 최대 1.5초만 기다린다(VO 스레드와 얽혀 UI 스레드가 막히는 것을 방지).

## 검증 현황

**실측으로 확인됨**
- 빌드(경고·오류 0), 앱 기동·종료, `libmpv-2.dll` 로드 후 `mpv.log`에 오류 없음.
- `Ctrl+Alt+P/C/B` 동작(창 위치·크기·확장 스타일 변화, 배경화면 부모=`Progman`, 복귀).
- PIP 가장자리·모서리의 크기 조절 판정(`HTLEFT`/`HTTOPLEFT`).
- 마우스 훅이 실제 사용자 클릭에 호출됨.
- Disney+ 자막 요소 구조 및 shadow DOM 안 스타일 주입 효과(라이브 페이지).
- 자물쇠 버튼: 커서가 PIP 안이면 표시, 밖으로 나간 뒤 1초 시점엔 보이고 3초 시점엔 숨김, 위치는 PIP 왼쪽 위 모서리 바로 위 바깥(PIP와 6px 간격, 왼쪽 끝 정렬).
- 잠금(`Ctrl+Alt+L`): PIP에서만 동작, 자식 창 전부 비활성화/해제, 잠긴 채 PIP를 나가면 자동 해제.

**2026-10-28 복원·추가 후 실측** (임시 시험 영상 사용, UI Automation으로 구동)
- 빌드 오류 0, 기동, 로컬 영상 재생(시간이 흐름), 재생/일시정지, 음소거·전체 화면 토글에서 종료되지 않음.
- 재생 목록 패널: 영상 3개 폴더에서 3곡 표시, 다음 곡으로 넘어가면 현재 표시 이동, 패널이 열고 닫힐 때 영상 영역이 밀리고 돌아옴(화면 캡처로 확인).
- 캡처 버튼: 스크린샷 파일 생성, 토스트 표시(썸네일·파일 이름·폴더 열기), 약 8초 뒤 사라짐, "폴더 열기"가 저장 폴더로 탐색기를 열고 토스트를 닫음.
- 알림 설정: 슬라이더 값이 `settings.json`에 저장되고, 3초·50%로 바꿔 미리 보기를 띄웠을 때 약 3초 뒤 사라지고 뒤 영상이 비쳐 보임.

**아직 확인하지 못함**
- 2026-10-28에 새로 쓴 부분 중 위에 적지 않은 것: 북마크, 최근 재생 메뉴, 자막·소리 메뉴, 이어 보기, 끌어다 놓기, 속도 팝업, 화면·소리 조절(이퀄라이저), 리모컨 칸 편집, 웹뷰 캡처(로그인이 필요해 시험 못 함).
- 유튜브 재생(`yt-dlp`는 받아 두었지만 재생은 시험하지 않음), SponsorBlock이 실제로 구간을 건너뛰는지(`scripts/` 없음).
- 앰비언트(`Ctrl+Alt+G`)의 화면·소리, mpv 자막 크기(90, `sub-ass-override=scale`).
- PIP 영상 위 드래그의 이동 계산(합성 마우스 입력이 이 환경에서 훅에 도착하지 않아 시험 불가).
- 영상 비율 맞춤과 `WM_SIZING` 비율 유지 동작.
- 수정 후 앱 안에서의 Disney+ 자막 확대(주입 코드), 넷플릭스 전반.
- 자물쇠 버튼을 마우스로 누르는 동작, 잠금 중 실제로 플레이어 UI(Disney+/mpv)가 안 뜨는지, 잠금 중 드래그 이동·크기 조절 차단(합성 마우스 입력 시험 불가).
- 클릭 투과 중 자식 HWND(mpv/WebView2)에 알파가 실제로 적용되는지, 배경화면이 아이콘 뒤에 실제로 그려지는지.

## 알려진 한계

- Disney+ 내부 클래스명(`hive-subtitle-renderer-*`)에 의존한다. 사이트가 바꾸면 자막 확대가 다시 어긋난다.
- 웹뷰 자막은 `transform`이라 줄바꿈을 다시 하지 않는다. 작은 창에서 큰 배율이면 긴 줄이 좌우로 넘칠 수 있다.
- 앰비언트의 오디오 유무는 켜는 시점의 파일 기준이다. 오디오 없는 파일은 비율 맞춤도 하지 않는다.
- 앰비언트 캔버스는 1920×1080 고정이다.
- 넷플릭스 키 브릿지(`seek`)의 자체 API 경로는 미검증이다.

## 다음 작업 (예정)

- 위 "아직 확인하지 못함" 항목을 실제 영상으로 하나씩 확인한다. 어색하거나 원본과 다른 동작은 고친다.
- `scripts/`(SponsorBlock)를 upstream에서 다시 받아 넣고 구간 건너뛰기를 확인한다.
- 임시 아이콘(`Assets/icons`)을 원하는 그림으로 교체한다.
- 재생 목록 패널의 드래그로 순서 바꾸기(아직 없음). 토스트의 배경만 투명하게 하는 방식(지금은 창 전체가 투명해져 글자도 옅어짐).
- 제약: mpv/WebView2는 자식 HWND라 그 위에 WPF 요소를 겹칠 수 없다(Airspace). 새 UI는 영상 영역 밖(바·옆 패널)에 두거나 `LockOverlay`·`ToastWindow`처럼 별도 최상위 창으로 띄운다.

## 디버깅 팁

웹뷰 DOM을 직접 보려면 환경 변수로 디버깅 포트를 켜고 실행한다(코드 변경 없음, 로컬 주소에만 열림).

```powershell
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS='--remote-debugging-port=9223 --remote-debugging-address=127.0.0.1'
Start-Process C:\ai\projects\HybridPipPlayer\bin\Debug\net8.0-windows\HybridPipPlayer.exe
# 웹뷰는 OTT 버튼을 처음 누를 때 초기화되므로 그 뒤에 http://127.0.0.1:9223/json 으로 접속된다
```

## 복원 기록 (2026-10-28)

이 프로젝트는 원래 `C:\mpv`에 독립 git 저장소로 있었는데, 윈도우 포맷으로 폴더와 `.git`이 통째로 사라졌다. 로컬 git 저장소 285곳(커밋 메시지·경로 검색)과 디스크 검색에서도 사본을 찾지 못했다. 남아 있던 것은 Claude Code 세션 기록(`D:\ClaudeCode-Backup-20261024\claude\projects\C--mpv\*.jsonl`)뿐이라, 거기에 기록된 파일 생성·수정 호출과 파이썬 패치 실행을 순서대로 다시 적용해 소스를 되살렸다. 그 뒤 빠진 부분을 새로 썼다.

**기록에서 되살린 것**: `App`, `AppSettings`(일부), `Bookmark`, `FeatureCatalog`, `FileAssociation`, `LockOverlay`, `MainWindow.xaml(.cs)`(일부), `MainWindow.Remote.cs`, `MediaFiles`, `MediaSession`, `MpvPlayer`, `NativeMethods`, `PipControls`, `RecentStore`, `RemoteWindow`, `ResumeStore`, `SettingsWindow`, `SingleInstance`, `ThemeManager`, `Themes/Dark.xaml`.

**새로 쓴 것(원본과 같다고 보증하지 않음)**
- `MainWindow.Playback.cs` 전체: 이 파일 안의 모든 메서드와, 이를 부르던 `MainWindow.xaml.cs`의 빠진 필드·핸들러. 원본의 시작 내용이 되는 전체 작성 기록이 없어 그 위에 쌓인 패치가 적용되지 않았다.
- `AppSettings`의 `VideoAdjust`, `EqBands`, `RemoteFavorites`, `MainExtras`(복원본에 선언이 없었다), `SettingsWindow.xaml`의 "화면 · 소리 조절" 페이지, `MainWindow.xaml`의 `ExtraPanel`.
- `Assets/icons`의 아이콘 PNG 전부(원본은 복구 불가, 임시 모양).

**복구하지 못한 것**
- 원래의 git 기록(첫 커밋 `feat: WPF 하이브리드 PIP 플레이어`가 있었다).
- `scripts/`(po5/mpv_sponsorblock 사본)와 `libmpv-2.dll`, `yt-dlp.exe` 같은 바이너리는 기록에 없어서 `libmpv`와 `yt-dlp`는 다시 받았고 SponsorBlock 스크립트는 아직 없다.
- 화면·소리 조절의 mpv 쪽 연결(`ApplyPlayerSettings`)과 메뉴 문구, 세부 동작은 원본과 다를 수 있다.

이후 추가(복원 범위 밖): 재생 목록 슬라이딩 패널, 재생 줄의 캡처 버튼, 스크린샷 토스트(`ToastWindow`)와 그 설정(투명도·표시 시간).
