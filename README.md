# blinkpip

**버전 0.6.1** (2026-11-13) · 변경 기록 [CHANGELOG.md](CHANGELOG.md) · 라이선스 [GPL-3.0](LICENSE) · 외부 구성 요소 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

WPF(.NET 8) 하이브리드 미디어 플레이어. 한 개의 보더리스 창 안에서 두 엔진을 URL로 자동 전환한다.

## 내려받기

[릴리스](../../releases) 에서 `blinkpip-<버전>-win-x64.zip`을 받아 원하는 폴더에 풀고 `blinkpip.exe`를 실행한다. (0.6.0 릴리스 파일은 예전 이름 `HybridPipPlayer-0.6.0-win-x64.zip`·`HybridPipPlayer.exe`다)

- 윈도우 10/11 64비트. .NET 런타임이 들어 있어 따로 설치하지 않는다(자체 포함 배포).
- libmpv, yt-dlp, SponsorBlock 스크립트가 함께 들어 있다(라이선스는 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)).
- 넷플릭스·디즈니+ 등은 WebView2 런타임이 필요하다(윈도우 11에는 기본으로 있다).
- SponsorBlock(유튜브 광고 구간 건너뛰기)은 `PATH`에 Python이 있어야 동작한다. 없어도 재생은 된다.
- 코드 서명이 없어 처음 실행할 때 SmartScreen 경고가 뜰 수 있다("추가 정보 → 실행").

| 엔진 | 대상 | 기술 |
| --- | --- | --- |
| A: libmpv | 로컬 영상, 유튜브 등 | `libmpv-2.dll` + `yt-dlp` + SponsorBlock(Lua) |
| B: WebView2 | 넷플릭스, 디즈니+ 등 DRM OTT | Edge WebView2, 영구 프로필(`blinkpip_WebView2`)로 로그인 유지 |

`MainWindow.xaml.cs`의 `WebEngineHosts`(넷플릭스·디즈니+·티빙·Apple TV(`tv.apple.com`)·왓챠·쿠팡플레이·웨이브·라프텔·프라임 비디오, 서브도메인 포함)만 엔진 B로 가고, 나머지 URL·파일 경로는 엔진 A로 간다. 사이트 안 링크 이동은 웹뷰 안에 머문다. 자막 크기·창 아래 자막 줄은 디즈니+·쿠팡플레이·웨이브, PIP 비율 맞춤은 여기에 넷플릭스를 더한 네 곳에 맞춰져 있다(사이트별 자막 구조는 아래 "자막 크기"). 웨이브는 자막이 영상에 박힌 작품(외국 드라마 등)이 많고, 그런 작품의 자막은 바꾸거나 읽을 수 없다.

**치지직**(`ChzzkRoute.cs`, 2026-11-13 실측): yt-dlp는 생방송 `/live/<채널>`과 다시보기 `/video/<번호>`만 알아본다. 그래서 채널 홈 `/<채널 32자리>`은 생방송 주소로 바꿔 엔진 A로 열고, 클립 등 나머지 치지직 페이지는 엔진 B(웹 화면, DRM 없음)로 연다. 웹 화면에서 둘러보다가 생방송·다시보기 페이지에서 PIP를 켜면 그 주소를 엔진 A로 넘겨 영상만 담는다(웹 페이지째 줄어들지 않게). PIP를 나오면 그 페이지를 웹 화면으로 다시 연다(새로 불러오므로 방송·채팅은 새로 연결). PIP 중 다른 영상을 열었으면 돌아가지 않는다. 19세 방송은 로그인 정보가 엔진 A로 넘어가지 않아 열리지 않을 수 있다.

**웹 화면 브라우징**(`MainWindow.Browse.cs`): 주소창 왼쪽 뒤로·앞으로·새로 고침 버튼(웹뷰가 보일 때만 켜짐), 주소창은 웹뷰의 현재 주소로 따라 바뀐다(입력 중에는 덮어쓰지 않음). 북마크 줄은 넘치면 휠로 가로 스크롤하고 **내 파일 · 추가 · 편집** 칩은 오른쪽에 고정된다. **편집**은 설정 → 북마크·정보를 열고, 거기서 이름·주소 추가·수정, 순서, 삭제, **기본 사이트 다시 넣기**(없는 기본 사이트만 끝에 추가)를 한다(`SettingsWindow.Bookmarks.cs`).

## 파일

저장소 루트가 곧 프로젝트 폴더다(`blinkpip.csproj`가 루트에 있다).

| 파일 | 역할 |
| --- | --- |
| `MainWindow.xaml(.cs)` | 창, 라우팅, PIP, 클릭 투과, 배경화면, 스냅, 비율 유지, 자막 크기, OTT 키 브릿지 |
| `MainWindow.Browse.cs`, `SettingsWindow.Bookmarks.cs` | 웹 화면 뒤로·앞으로·새로 고침과 주소 따라가기, 북마크 줄 스크롤·편집 칩, 설정의 북마크 추가·수정·기본 사이트 다시 넣기 |
| `MainWindow.Playback.cs` | 재생 줄(재생·탐색·건너뛰기 간격·속도·볼륨), 열기, 재생 목록 패널, 자막·소리 메뉴, 스크린샷과 토스트, 전체 화면, 이어 보기, 설정 연결 |
| `ShortcutCatalog.cs`, `ShortcutStore.cs`, `MainWindow.Shortcuts.cs`, `MpvPlayer.Shortcuts.cs`, `SettingsWindow.Shortcuts.cs` | 단일 기본 키 표, 변경분 저장·충돌 검사, WPF/IME/OSK/mpv 연결, 검색·키 입력·복원 편집기 |
| `MainWindow.Remote.cs` | 리모컨(4×3 창)과 기능 id를 기존 핸들러에 잇는 부분, 재생 줄에 올린 기능 아이콘 |
| `ToastWindow.xaml(.cs)` | 영상 위에 잠깐 뜨는 알림 창(포커스를 가져가지 않음). 스크린샷 저장 알림에 쓴다 |
| `SettingsWindow.xaml(.cs)` | 설정 창(재생, 모양, PIP, 오디오·자막, 화면·소리 조절, 북마크·정보) |
| `RemoteWindow.xaml(.cs)`, `FeatureCatalog.cs` | 리모컨 창과 올릴 수 있는 기능 목록 |
| `PipControls.xaml(.cs)`, `LockOverlay.cs` | PIP 컨트롤 줄, PIP 왼쪽 위 자물쇠·PIP 나가기 버튼(별도 최상위 창) |
| `MpvPlayer.Subtitles.cs`, `SubtitlePracticeController.cs`, `SubtitlePracticeSettings.cs`, `SubtitlePracticeWindow.cs`, `MainWindow.Subtitles.cs` | 자막 속성 관찰, 문장 반복 상태, 연습 설정·창·메뉴 연결 |
| `MpvPlayer.cs` | libmpv P/Invoke 래퍼(옵션, 이벤트 스레드, 로그, 앰비언트 필터, SponsorBlock 설정, 재생 목록·트랙) |
| `AppSettings.cs`, `Bookmark.cs`, `RecentStore.cs`, `ResumeStore.cs` | 설정, 북마크, 최근 재생, 이어 보기 저장소(`%LocalAppData%\blinkpip\` 아래 json) |
| `MediaFiles.cs`, `MediaSession.cs`, `FileAssociation.cs`, `SingleInstance.cs` | 미디어 확장자·폴더 목록·m3u, 윈도우 미디어 컨트롤 연동, 연결 프로그램, 단일 실행 |
| `ThemeManager.cs`, `Themes/Dark.xaml` | 색 테마(A/B/C)와 어두운 테마 스타일 |
| `NativeMethods.cs`, `app.manifest` | Win32(핫키, WorkerW/Progman 탐색, 마우스 훅, DWM), PerMonitorV2 DPI 선언 |
| `Assets/app.ico`, `Assets/app-icon-1024.png` | 앱 아이콘(실행 파일·창·작업 표시줄, 16~256px 9개 크기)과 그 원본(투명 배경 1024px). ChatGPT 웹 시안 4번(유리 창 + 재생)을 정사각형으로 잘라 만들었다 |
| `Assets/icons/*.png` | 재생 줄 아이콘 15개. **임시로 만든 모양**이다(원본 PNG는 복구하지 못했다). 같은 이름으로 바꿔 넣으면 된다 |

## 필요한 바이너리 (저장소에 없음)

프로젝트 루트에 직접 둔다(`.gitignore`로 제외). 빌드 때 출력 폴더로 복사된다. 없으면 빌드 경고가 난다.

- `libmpv-2.dll` — 64비트. [shinchiro/mpv-winbuild-cmake](https://github.com/shinchiro/mpv-winbuild-cmake/releases) 의 `mpv-dev-x86_64-*.7z` 안에 있다. (실측: 20261021 빌드, 115 MB. 7-Zip이 없으면 윈도우 기본 `tar -xf`로도 풀린다)
- `yt-dlp.exe` — [yt-dlp/yt-dlp](https://github.com/yt-dlp/yt-dlp/releases) 릴리스. (실측: 2026.10.26, 릴리스의 `SHA2-256SUMS`와 대조해 받는다)
- WebView2 런타임(윈도우 11 기본 포함), SponsorBlock용 Python(`PATH` 또는 `C:\Python3xx`).
- `scripts/` — [po5/mpv_sponsorblock](https://github.com/po5/mpv_sponsorblock) 사본(GPL-3.0, 커밋 `7785c14`, 저장소에 포함). 출처는 `scripts/SOURCE.txt`, 라이선스는 `scripts/LICENSE-sponsorblock.txt`. 빌드 때 출력 폴더로 복사된다.

## 빌드·실행

```powershell
dotnet build C:\ai\projects\blinkpip\blinkpip.csproj
dotnet run --project C:\ai\projects\blinkpip
```

대상 프레임워크는 `net8.0-windows10.0.19041.0`다(`MediaSession.cs`가 WinRT API를 쓴다). 실행 중인 앱이 `bin\`의 exe를 잡고 있으면 빌드가 실패하니 먼저 종료한다.

### 버전 올리기와 릴리스

1. `blinkpip.csproj`의 `<Version>`을 올리고, `CHANGELOG.md` 맨 위에 그 버전의 추가·바뀜·고침을 적는다(README 첫 줄의 버전도 같이).
2. 커밋한 뒤 태그를 단다: `git tag -a v<버전> -m "<버전>"`.
3. 자체 포함 배포본을 만든다(필요한 바이너리 3종이 루트에 있어야 함께 들어간다):
   ```powershell
   dotnet publish C:\ai\projects\blinkpip\blinkpip.csproj -c Release -r win-x64 --self-contained true -o <출력 폴더>\blinkpip
   ```
   출력 폴더에 `README.md`, `CHANGELOG.md`, `THIRD_PARTY_NOTICES.md`를 넣고 `blinkpip-<버전>-win-x64.zip`으로 묶는다.
4. 푸시 후 `gh release create v<버전> <zip> --title "blinkpip <버전>" --notes-file <해당 버전 패치 노트>`.

## 재생 줄과 부가 기능

재생 줄은 엔진 A(mpv)에만 연결된다. 엔진 B(Netflix/Disney+)는 사이트 자체 UI를 쓴다. 설정에서 영상 아래(기본)나 주소 줄 아래로 옮길 수 있다.

- **열기**: 파일, 폴더, 폴더(하위 포함), 주소, 최근 재생. 파일 하나를 열면 같은 폴더의 영상으로 재생 목록을 만든다(설정에서 끌 수 있음). 끌어다 놓기와 `.m3u`/`.m3u8`도 받는다.
- **재생 목록 패널**: 재생 줄의 목록 버튼(또는 리모컨의 "재생 목록")을 누르면 오른쪽에서 300px 패널이 밀려 나온다. 영상 HWND 위에는 WPF 요소를 겹칠 수 없어서 영상 영역을 옆으로 밀어내는 방식이다. 더블클릭·Enter로 재생, ✕·Delete로 빼기, 반복·섞기, 파일·폴더 추가, 저장, 비우기. PIP·전체 화면에 들어가면 자동으로 닫힌다.
- **캡처 버튼과 스크린샷 토스트**: 재생 줄의 카메라 버튼은 mpv 화면이면 원본 해상도로, 웹뷰 화면이면 웹뷰 화면을 PNG로 `내 사진\blinkpip\`에 저장한다(웹뷰의 DRM 구간은 검게 찍힐 수 있음). 저장하면 플레이어 창 오른쪽 아래에 토스트가 뜨고 썸네일, 파일 이름, "폴더 열기"(저장한 파일을 선택한 채로 탐색기)가 있다. 표시 시간(기본 8초)과 투명도(기본 90%)는 설정의 "모양"에서 바꾸고 "알림 미리 보기"로 확인한다. 마우스를 올려 두면 사라지지 않는다. 토스트를 화면 구석이 아니라 플레이어 창 안에 띄우는 이유는, 접근성 도구 같은 다른 프로그램의 패널이 더 높은 층으로 구석을 가리는 일이 있기 때문이다.
- **리모컨**: 자주 쓰는 기능 6칸(즐겨찾기)과 재생 줄에 올릴 기능을 고른다. 기능 목록은 `FeatureCatalog.cs`.
- **설정**: 재생(컨트롤 위치, 기본 볼륨·속도, 전체 화면 컨트롤 숨김, 이어 보기, 자동 목록), 모양(테마, 북마크 줄, 알림), PIP(너비, 컨트롤 숨김 시간), 오디오·자막(장치, 글자 크기, 선호 언어), 화면·소리 조절(밝기·대비·채도·감마, 5밴드 이퀄라이저), 북마크·정보.

## 화면 자르기

엔진 A 영상 재생 중 더보기 → **영역 · 화면 자르기** 또는 리모컨의 **영역**에서 1~4배 확대와 보는 위치를 조절한다. 전체·중앙 2배·오른쪽 위 버튼과 이름별 저장/불러오기/삭제를 지원한다. `%LOCALAPPDATA%\blinkpip\crops.json`에 해상도와 무관한 비율로 최대 50개를 저장한다. OTT에서는 사용할 수 없다.

## 자막 연습

엔진 A의 자막 메뉴 → **자막 연습** 또는 리모컨의 **자막 연습**에서 텍스트 자막으로 연습한다. 문장 끝 자동 정지, 첫 재생을 포함한 1~5회 반복 후 정지, 현재/선택 문장 다시 듣기를 지원한다. 목록은 클릭으로 선택하고 Enter·두 번 클릭·다시 듣기 버튼으로 이동한다. 두 번째 자막은 기존 자막 트랙을 함께 표시하며 번역을 생성하지 않는다.

자막 지연과 재생 속도를 반영하고, 일반 A–B 반복과 동시에 켜지지 않는다. 파일·기본 자막 트랙·수동 탐색이 바뀌면 이전 문장 반복을 취소한다. 모드와 횟수는 `%LOCALAPPDATA%\blinkpip\subtitle-practice.json`에 저장한다. 목록은 메모리에 읽힌 자막 범위이며 웹 영상은 일부만 표시될 수 있다. 이미지 자막·끝 시각을 알 수 없는 문장·OTT 자막은 연습 대상에서 제외한다. 자막 자체의 속도/프레임 변환(`sub-speed`, `sub-fps`)을 별도로 변경한 경우 시간 제어를 끄고 안내한다. OTT 보호 영상 시작 감지·자막 읽기는 **확인 필요**다.

## 확장에서 앱으로 보내기

앱의 **설정 → 북마크·정보 → 확장에서 앱으로 열기 → 확장 연결 등록**을 한 번 누른다. 확장에서 영상 위의 **앱으로**를 누르고 주소와 선택 항목을 확인한 뒤 **앱에서 열기**를 누른다. 기본 선택은 보던 위치만이며, 외부 자막 파일 주소·속도·PIP 시작은 따로 선택한다. 앱을 옮기면 다시 등록한다. 등록은 현재 사용자에게만 적용한다.

`hybridpip://play?v=1&url=…&t=…&sub=…&speed=…&pip=1` 요청은 새 실행과 실행 중 창에서 같은 검증을 거친다. HTTP(S) 주소만 받고 인증·서명 값, 잘못된 형식, 중복·알 수 없는 항목, 길이 초과 요청은 거절한다. 엔진 A는 받은 위치를 이어 보기보다 우선 적용하고 속도·외부 자막·원하는 PIP 상태를 반영한다. 같은 주소를 연속 요청해도 재생 항목 ID로 해당 요청의 정보만 연결한다.

브라우저 로그인·쿠키를 공유하지 않으며 원래 영상을 자동으로 멈추지 않는다. 앱이 설치·등록되지 않은 경우 브라우저 안내와 앱 등록을 확인한다. 확장은 앱 실행 성공을 확인할 수 없어 **열기 요청**으로 표시한다. blob/보호 영상은 안전한 페이지 주소로 넘길 수 있지만 재생을 보장하지 않는다. OTT 경로는 페이지 열기/PIP만 지원하며 위치·속도·자막 적용과 보호 영상 시작 감지·자막 읽기는 **확인 필요**다.

## 단축키

전역 단축키(창이 투과 상태여도 동작). 등록에 실패하면(다른 앱과 충돌) 상단 바에 주황색으로 표시된다.

| 키 | 동작 |
| --- | --- |
| `Ctrl+Alt+P` | PIP 토글(우하단, 너비는 설정의 PIP 창 너비 — 가장자리를 끌어 바꾸면 그 너비로 저장, 항상 위, 상단 바·재생 줄을 접어 영상이 꽉 차게, 크기 조절용 6px 여백은 검은색) |
| `Ctrl+Alt+C` | 클릭 투과 + 반투명 토글(PIP 자동 진입). 커서가 창 위에 오면 반대쪽 모서리로 피한다 |
| `Ctrl+Alt+B` | 바탕화면 아이콘 뒤 라이브 배경화면 토글. 다른 앱이 전체화면이면 자동 일시정지 |
| `Ctrl+Alt+G` | 앰비언트 글로우(영상 비율 밖 여백에 블러 배경) 토글 |
| `Ctrl+Alt+↑/↓` | 자막 크게/작게 (mpv: ±0.1, 웹뷰: ±0.2, 범위 0.8~4.0) |
| `Ctrl+Alt+L` | PIP 잠금 토글(PIP에서만 동작, 자물쇠 버튼과 같음) |

**기본 재생 키**(창에 포커스가 있을 때. `ShortcutCatalog`의 같은 표를 영상 창의 mpv 바인딩과 WPF의 `ShortcutKeyBridge`가 참조한다. 주소창·목록·콤보에 포커스가 있으면 가로채지 않음)

설정 → 북마크·정보 → 단축키에서 기능을 검색하고 키를 바꿀 수 있다. 편집 칸에 `Ctrl+K`처럼 입력하거나, 칸을 선택한 뒤 **키 입력** 버튼을 눌러 실제 키를 누른다. **저장**으로 적용하며 키 입력 중 Esc는 취소다. 별칭은 `F11 | F`처럼 `|`로 구분하고 빈 칸은 해당 기능 해제다. 겹치는 키와 예약된 전역 키는 거절한다. 행별 **복원** 또는 **단축키 모두 기본값으로**를 지원한다. `%LOCALAPPDATA%\blinkpip\shortcuts.json`에는 기본값과 다른 기능만 저장한다. 전역 단축키 7개와 OTT 사이트 키는 편집 범위 밖이다. 설정 열기·자막 크기 두 기능은 기존처럼 영상 창 포커스에서만 동작한다.

| 키 | 동작 |
| --- | --- |
| `Space` | 재생 / 일시정지 |
| `←` / `→` | 설정의 건너뛰기 간격(기본 5초)만큼 뒤로 / 앞으로(`relative+exact`). `Shift`는 6배 |
| `↑` / `↓` | 볼륨 ±5 |
| `m` | 음소거 |
| `PageUp` / `PageDown` | 이전 / 다음 파일 |
| `[` / `]` / `Backspace` | 속도 -0.25 / +0.25 / 1× |
| `,` / `.` | 한 프레임 뒤 / 앞 |
| `s` / `l` | 스크린샷 / 재생 목록 패널 |
| `f` · `F11` / `Esc` | 전체 화면 / 해제 |

**글자 단축키는 저수준 키보드 훅(`TextKeyHookProc`)도 받는다.** 화상 키보드(OSK) 영문 모드는 글자를 유니코드(`VK_PACKET`)로 보내고, 한글 입력 상태의 물리 키는 창에 `VK_PROCESSKEY`로 전달되기 때문이다. 훅은 이 창이 맨 앞이고 주소창·목록·콤보·메뉴에 포커스가 없을 때 현재 키 표의 일치하는 조합만 처리하며, 처리한 KeyUp도 소비한다. 기본 키의 대소문자·Shift 허용과 한글 자모 위치 별칭(`ㅡ`→M, `ㄴ`→S, `ㅣ`→L, `ㄹ`→F)을 보존한다. 사용자 지정 키는 수정키를 정확히 구분한다. 키를 옮기면 문자·한글 위치 별칭도 새 배정을 따르며, 기존 기본 키가 mpv 내장 기능으로 되살아나지 않게 한다. `Space`·방향키·`PageUp/Down`·`Backspace`·`Esc`는 mpv 바인딩과 `ShortcutKeyBridge`가 처리한다.

재생 줄에는 뒤로·시간·앞으로·간격 변경을 한 묶음으로 둔다: `[ ‹ N | 0:32 / 1:00 | N › | ▾ ]`. 화살표 방향이 이동 방향이고, N은 건너뛰기 간격(초)이다. 간격은 `▾` 버튼(또는 방향 버튼 우클릭)의 팝업에서 `-5 -1 [N초] +1 +5`로 바로 바꾸거나(가운데 `N초`는 5초로 되돌림), 설정(재생 > 건너뛰기 간격)에서 바꾼다. 범위는 1~60초이고 `settings.json`의 `SeekSeconds`에 저장된다.

PIP에서는 영상 위 좌클릭 드래그로 이동, 우클릭 또는 PIP 나가기 버튼(아래 "PIP 잠금"의 자물쇠 오른쪽, ⧉ 모양)으로 복귀. 창 가장자리 드래그로 크기 조절(영상 실제 비율 유지).
PIP는 테두리 여백 없이 영상이 창을 꽉 채운다. 그래서 가장자리 크기 조절도 마우스 훅이 직접 한다: 창 가장자리 8px 안을 누르면 누른 변의 반대쪽을 고정하고 영상 비율대로 크기를 바꾼다(그 누름은 영상으로 넘기지 않음). 자식 창 위라 크기 조절 화살표 커서는 나오지 않는다.
mpv 화면에서는 `Ctrl+↑/↓`(영상 창 포커스)로도 자막 크기를 바꿀 수 있다.

**PIP 재생 컨트롤**: PIP에서는 아래 재생 줄을 접는다. 엔진 A(로컬·mpv)는 커서가 PIP 위에 있을 때만 영상 아래쪽 안에 재생·탐색·음소거 컨트롤(`PipControls`, 별도 최상위 창)이 뜨고, 커서가 벗어난 뒤 설정의 "컨트롤 사라지는 시간"(기본 2초)이 지나면 자물쇠와 함께 사라진다. 웹뷰(넷플릭스·디즈니+)는 사이트 자체 컨트롤이 같은 자리에 떠서 띄우지 않는다. 잠그면 컨트롤도 뜨지 않는다.

**PIP 아래 자막 줄**: 설정 → PIP → "자막을 창 아래에 표시"(기본 켬)이면 PIP에서 영상 안 자막을 숨기고 창 바로 아래(공간이 없으면 바로 위) 줄에 글자로 보여 준다(`SubtitleStrip`, 마우스 통과). mpv는 `sub-text`를, 웹뷰는 자막 요소 글자를 200ms마다 읽어 `postMessage("hppsub:…")`로 받는다(`MainWindow.PipExtras.cs`의 `SubtitleReadHosts` = 디즈니+·쿠팡플레이·웨이브 출처만 받음, 500자 제한). PIP에 들어갈 때 우하단 기본 위치면 줄이 들어갈 만큼 창을 올린다. 이때 `Ctrl+Alt+↑/↓`는 줄 글자 크기(10~40, ±2)를 바꾼다. 이미지 자막은 글자가 없어 줄에 보이지 않는다. 넷플릭스는 아직 대상이 아니다.

**웹 영상 PIP 비율 맞춤**: 넷플릭스·디즈니+·쿠팡플레이·웨이브(`AspectHosts`) 페이지가 가장 큰 `video`의 원본 크기(`videoWidth×videoHeight`)를 1초마다 확인해 바뀔 때만 `postMessage("hppaspect:WxH")`로 보낸다. PIP에서는 이 비율로 너비를 유지한 채 창 높이를 맞춰 위아래 검은 띠를 없앤다(가장자리 끌기도 같은 비율 유지). 일반 창은 사이트 목록 화면도 보여 주므로 맞추지 않는다. 재생 화면을 벗어나면 `0x0`을 보내 맞춤을 멈춘다.

**자막 글꼴·색**: 설정 → 오디오·자막. mpv(`sub-font`, `sub-color`), 웹뷰 자막(`color`·`font-family` `!important`), PIP 아래 자막 줄에 함께 적용한다.

**자막 크기(웹뷰)**: `OttViewportScript`의 `SUBS` 표가 사이트별 자막 요소를 정한다(2026-11-13 실측) — 디즈니+ = `timed-text-override-region` shadow root 안 `.hive-subtitle-renderer-cue-window`(기본 ×2.0), 쿠팡플레이 = video.js `.vjs-text-track-display .vjs-text-track-cue`(×1.0), 웨이브 = `.subtitle-wrap .subtitle-box`(×1.0, 웨이브 주소에서만). 자막 상자를 아래 가운데 기준 `transform:scale`로 키운다. 배율은 사이트 `localStorage`에 따로 저장되고, `Ctrl+Alt+↑/↓`는 페이지의 `__hppSubStep(±1)`을 불러 0.2씩(0.6~4.0) 바꾼다. ASS 자막은 `sub-ass-override=scale`이라 원래 스타일을 유지한다.

**PIP 잠금·나가기 버튼**: 커서가 PIP 위에 있으면 PIP 창 **바깥** 왼쪽 위 모서리 바로 위에 자물쇠 버튼(🔓/🔒)과 그 오른쪽 PIP 나가기 버튼(⧉, `Ctrl+Alt+P`와 같음, 잠겨 있어도 동작)이 뜨고(영상·자막을 가리지 않음),
커서가 PIP와 버튼을 모두 벗어난 뒤 설정의 "컨트롤 사라지는 시간"(기본 2초)이 지나면 사라진다. 위쪽 화면 공간이 모자라면 아래쪽, 둘 다 없으면 창 안쪽 왼쪽 위에 뜬다.
잠그면 영상·웹뷰 창을 비활성화해 마우스가 닿지 않게 하고(마우스를 스칠 때마다 플레이어 UI가 뜨는 것을 막음),
드래그 이동·크기 조절·우클릭 복귀도 막는다. `Ctrl+Alt+P`로 PIP를 나가면 자동으로 풀린다.
해제하려면 PIP 위에 커서를 올려 자물쇠를 누르거나 `Ctrl+Alt+L`.

## 데이터 위치

`%LocalAppData%\blinkpip\` — `mpv.log`(경고 이상 로그), `mpvcfg\`(SponsorBlock 설정·스크립트 사본), `settings.json`(설정), `bookmarks.json`, 최근 재생·이어 보기 기록.
`내 사진\blinkpip\` — 스크린샷 저장 폴더(OneDrive로 리디렉션된 PC에서는 OneDrive 사진 폴더 아래).
`%LocalAppData%\blinkpip_WebView2\` — WebView2 프로필(로그인 유지).

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

**2026-10-28 추가 실측** (UI Automation, 60초 시험 영상과 공개 유튜브 영상)
- 북마크 추가(웹 주소만 받음, 로컬 경로는 안내 문구). 칩 우클릭이 PIP를 전환하던 문제는 상단 바 핸들러가 버튼 위 우클릭을 무시하게 고침(마우스로는 미확인).
- 자막·소리·더보기 메뉴, 속도 팝업(+0.5 → 1.50×, 초기화 → 1.00×), 최근 재생 메뉴.
- 이어 보기: 저장 조건이 `pos > 5`이고 `pos < 길이 - 10`이라 15초 이하 영상은 저장되지 않는다. 60초 영상에서 30초 저장, 다시 열면 그 위치부터 재생.
- 화면 조절 밝기: 화면이 밝아지고 `settings.json`에 저장. 이퀄라이저는 저장만 확인(소리 변화는 미확인).
- 리모컨 칸 편집: 칸을 바꾸면 `RemoteFavorites`에 저장되고 리모컨에 반영.
- 유튜브 재생: 주소창에 넣으면 `yt-dlp`를 거쳐 재생(길이 표시 확인).
- SponsorBlock: `scripts/`가 출력 폴더와 `mpvcfg\scripts`로 복사되고 `sponsorblock.conf`가 만들어짐. 서버 조회 스크립트는 `error` 없이 응답.

**아직 확인하지 못함**
- 끌어다 놓기, 북마크 칩 우클릭 삭제 메뉴(안구 마우스가 커서를 옮겨 합성 마우스 시험이 안 됨), 이퀄라이저 소리, 웹뷰 캡처(로그인이 필요).
- SponsorBlock이 실제로 구간을 건너뛰는지(건너뛸 구간이 등록된 영상으로 시험 필요).
- 앰비언트(`Ctrl+Alt+G`)의 화면·소리, mpv 자막 크기(90, `sub-ass-override=scale`).
- PIP 영상 위 드래그의 이동 계산(합성 마우스 입력이 이 환경에서 훅에 도착하지 않아 시험 불가).
- 영상 비율 맞춤과 `WM_SIZING` 비율 유지 동작.
- 수정 후 앱 안에서의 Disney+ 자막 확대(주입 코드), 넷플릭스 전반.
- 자물쇠 버튼을 마우스로 누르는 동작, 잠금 중 실제로 플레이어 UI(Disney+/mpv)가 안 뜨는지, 잠금 중 드래그 이동·크기 조절 차단(합성 마우스 입력 시험 불가).
- 클릭 투과 중 자식 HWND(mpv/WebView2)에 알파가 실제로 적용되는지, 배경화면이 아이콘 뒤에 실제로 그려지는지.
- 실제 OSK 한글 모드에서의 글자 단축키, 포커스가 WPF 쪽에 있을 때의 단축키 경로(`ShortcutKeyBridge`), 한글 상태에서 m/s/f 직접 입력.

## 알려진 한계

- Disney+ 내부 클래스명(`hive-subtitle-renderer-*`)에 의존한다. 사이트가 바꾸면 자막 확대가 다시 어긋난다.
- 웹뷰 자막은 `transform`이라 줄바꿈을 다시 하지 않는다. 작은 창에서 큰 배율이면 긴 줄이 좌우로 넘칠 수 있다.
- 앰비언트의 오디오 유무는 켜는 시점의 파일 기준이다. 오디오 없는 파일은 비율 맞춤도 하지 않는다.
- 앰비언트 캔버스는 1920×1080 고정이다.
- 넷플릭스 키 브릿지(`seek`)의 자체 API 경로는 미검증이다.

## 다음 작업 (예정)

- 위 "아직 확인하지 못함" 항목을 실제 영상으로 하나씩 확인한다. 어색하거나 원본과 다른 동작은 고친다.
- SponsorBlock 구간 건너뛰기를 구간이 등록된 영상으로 확인한다.
- 임시 아이콘(`Assets/icons`)을 원하는 그림으로 교체한다.
- 재생 목록 패널의 드래그로 순서 바꾸기(아직 없음). 토스트의 배경만 투명하게 하는 방식(지금은 창 전체가 투명해져 글자도 옅어짐).
- 제약: mpv/WebView2는 자식 HWND라 그 위에 WPF 요소를 겹칠 수 없다(Airspace). 새 UI는 영상 영역 밖(바·옆 패널)에 두거나 `LockOverlay`·`ToastWindow`처럼 별도 최상위 창으로 띄운다.

## 디버깅 팁

웹뷰 DOM을 직접 보려면 환경 변수로 디버깅 포트를 켜고 실행한다(코드 변경 없음, 로컬 주소에만 열림).

```powershell
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS='--remote-debugging-port=9223 --remote-debugging-address=127.0.0.1'
Start-Process C:\ai\projects\blinkpip\bin\Debug\net8.0-windows\blinkpip.exe
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
- `scripts/`(po5/mpv_sponsorblock 사본)와 `libmpv-2.dll`, `yt-dlp.exe` 같은 바이너리는 기록에 없어서 다시 받았다(`scripts/`는 upstream 커밋 `7785c14`를 받아 둠).
- 화면·소리 조절의 mpv 쪽 연결(`ApplyPlayerSettings`)과 메뉴 문구, 세부 동작은 원본과 다를 수 있다.

이후 추가(복원 범위 밖): 재생 목록 슬라이딩 패널, 재생 줄의 캡처 버튼, 스크린샷 토스트(`ToastWindow`)와 그 설정(투명도·표시 시간).
