# ② 자막 연습 — 구현 및 검증 (2026-11-12)

## 동작

엔진 A의 자막 메뉴/리모컨에서 연습 창을 연다. 현재 문장과 시간 목록, 문장 끝 자동 정지, 첫 재생 포함 1–5회 반복 후 정지, 현재·선택 문장 다시 듣기, 두 번째 자막 트랙 선택을 제공한다. 목록 선택은 재생 위치를 바꾸지 않고 Enter·두 번 클릭·버튼으로 재생한다. Escape로 창을 닫는다.

- `MpvPlayer.Subtitles.cs`: libmpv observe_property와 typed native ABI, 현재 위치·문장·시각·트랙·지연을 복사한 관리형 이벤트. `sub-lines` 목록과 `secondary-sid` 연결.
- `SubtitlePracticeController.cs`: GUI와 분리한 문장/반복 상태와 JSON 목록 파서. 지연 ±, 재생 위치 복귀 확인, 이전 동작의 generation 무효화, 범위를 벗어나거나 끝 시각이 없는 문장 제외. 유한한 비음수 1년 이내 시각만 연습하며 알 수 없는 값은 `?`로 표시한다. JSON 종류를 확인하고 8MB/1만 문장/문장 4천 글자로 제한한다.
- `SubtitlePracticeSettings.cs`: `%LOCALAPPDATA%\HybridPipPlayer\subtitle-practice.json`에 모드·횟수만 원자적으로 저장한다. 문장 내용과 파일별 위치는 저장하지 않는다.
- `SubtitlePracticeWindow.cs`: 키보드 접근 가능한 연습 창, 최대 높이가 있는 현재 문장 스크롤 영역, 목록, 번역 트랙 선택. 두 번째 트랙은 기존 자막을 표시하며 번역을 생성하지 않는다.
- `MainWindow.Subtitles.cs`: 엔진/파일/기본 자막 트랙/수동 탐색 변경 시 상태 취소, UI 갱신, A–B 반복 상호 배제. 기존 자막·오디오 메뉴를 이 파일로 이동하여 `MainWindow.Playback.cs`를 늘리지 않았다.

## 비 GUI 검증

- 현재 빌드: `dotnet build HybridPipPlayer.csproj --no-restore -v minimal` 성공. 오류 0, 기존 `ToastWindow.Margin` CS0108 경고 1.
- 게스트 표준 **Release** 빌드: `dotnet build HybridPipPlayer.csproj -c Release --no-restore -v minimal` 성공(오류0, 기존CS0108 경고1). DLL SHA256: `68E3B93C895C4017C0F65A7296CCD02175B15EC1F472705513289AE5DB139F5B`.
- Debug 빌드 DLL SHA256 (게스트 표준 Release 후보와 구분): `3DF5918A6D7864DE24B826E327A7724F29908B7518461753D3B5E0D999788040`.
- 순수 상태/파서/포맷 검사: `%TEMP%\HybridPipSubtitleChecks\SubtitleChecks.csproj` → **45개 통과**. NaN·overflow, 잘못된 JSON 값 종류, 정지/반복, 이전 동작 취소, 지연 ±, 실제 변경과 늦은 알림 구분을 포함한다.
- 번들 libmpv `v0.41.0-1227-ge471496c5`를 직접 호출한 비 GUI native 검사: `%TEMP%\HybridPipSubtitleNativeChecks\native_subtitles.py`, `receipt.json` → **51개 통과**. 생성한 Y4M+두 SRT로 실행했고 `vo=null`, `ao=null`, config/scripts/input 비활성 상태였다. 제품의 HWND 생성자를 사용하지 않았다.
- DOUBLE 관찰값은 `sub-start/full=1.125`, `sub-end/full=2.375`를 보존했다. `sub-lines` JSON과 한국어 secondary track 선택·해제를 확인했다. 지연 +0.5에서 player1.5에는 문장이 없고1.8에 표시되며 원래 문장 시각1.125/2.375는 유지됨을 확인했다. 음수 지연도 확인했다.
- 실제 bundled native+현재 상태 머신 검사: `%TEMP%\HybridPipSubtitleNativeController\NativeController.csproj`, `bin\Debug\net8.0\receipt.json` → 지연0.25 변경 직후 알림을 비우지 않고3회 반복 시작, **seek3회·최종pause1회(2.625초)·의도하지 않은seek reset0회**.

## 발견한 순서 문제와 수정

native 이벤트에서 늦은 `sub-delay` 알림이 replay의 SEEK20보다 먼저 올 수 있다. 알림이 왔다는 이유만으로 상태를 초기화하면 내부 seek 표시가 사라진다. 실제 현재 sid/delay를 잠금 안에서 읽고, Replay가 먼저 같은 현재 값을 저장한 뒤 seek를 보낸다. 같은 값의 늦은 알림은 상태를 지우지 않으며 실제 값 변경은 이전 동작을 취소한다. 새 파일/기본 트랙 변경 시 이전 창 목록으로 replay할 수 없도록 세대 값을 확인한다.

## 게스트 검증

제품 GUI·키 입력은 Hyper-V에서만 실행했다. 최종 Release65DF 후보 실행 `hybridpip-7ae99fafa8c74fabbf789758eb653308`: 본 검사 **29/29**, 새 프로세스 설정 복원 **1/1** 통과. 종료 코드0, 테스트 설정·클립보드 복원 및 자체 payload 정리 완료. 호스트 입력 없음.

현재 문장/목록, 자동정지, 1–5회 반복·최종정지, 배속2, 지연±0.4, 수동탐색 취소, primary/secondary 변경, Enter 이동, A–B 상호배제, 비지원 자막 속도 설정, 다음 파일에서 오래된 문장 제거, 자막 없음 안내, 엔진B 제어 차단, 설정을 새 프로세스에서 다시 읽기, Escape 닫기를 확인했다. 엔진B 검사는 UI 기능 차단만 확인했으며 OTT 재생 검사가 아니다.

최소540px 창에서 4줄 문장의 목록 높이118.90px를 확인했다. 실제1000문장 SRT에서는 목록184.96px, 실체화된 항목13개, 푸터 하단481px로 목록과 버튼이 보인다. `subtitle-long-list-minimum.png`를 시각 확인했다. A→A 전환 및 오래된 열린 드롭다운/선택 콜백 차단도 통과했다.

초기68E3 후보는25+1개 검사를 통과했지만 최소420px 목록 소실과 코드 리뷰 문제를 수정했다. 이후7DD/65DF 중간 실행 두 건은 다른 작업의 VM 잠금 대기 제한으로 VM 접근 전에 끝났으며 GUI 결과로 계산하지 않는다. 다른 작업/잠금을 종료하거나 제거하지 않고 순서를 기다린 최종 실행이 통과했다.

증거: 위 실행의 `receipt.json`, `extracted/evidence/subtitles-before-restart.json`, `subtitles.json`, `subtitle-minimum-height.png`, `subtitle-long-list-minimum.png`. 외부 하네스는 `C:\ai\projects\background-lab\work\hybridpip-key-harness\SubtitleChecks.cs`.

## 범위와 한계

텍스트 자막과 알려진 문장 끝 시각이 필요하다. 메모리 내 목록이므로 웹/방송 자막은 일부만 나타날 수 있다. 이미지 자막, 끝 시각 불명 문장, 별도의 `sub-speed`/`sub-fps` 변환은 시간 제어 대상에서 제외한다. OTT 보호 영상 시작 감지·자막 읽기는 **확인 필요**이며 이 연습 창은 엔진 A 전용이다.

참고: [mpv manual](https://mpv.io/manual/master/), [client ABI](https://raw.githubusercontent.com/mpv-player/mpv/master/include/mpv/client.h), [subtitle timestamp implementation](https://raw.githubusercontent.com/mpv-player/mpv/master/sub/dec_sub.c).


## 추가 리뷰 수정과 최종 후보

- 내부 탐색 완료를 위치 `start+0.3` 범위로 추측하지 않고, 해당 SEEK20 이후 PLAYBACK_RESTART21을 확인한 뒤 문장 종료 판단을 재개한다. 늦게 도착한 첫 관찰값·높은 배속에서도 종료가 누락되지 않는다. 일반 사용자 탐색은 동기적으로 이전 연습 상태를 취소한다.
- 자막 지연 후 전체 구간이0초 이전이면 Replay를 거절한다. 일부만0초 이전인 문장은0초부터 남은 구간을 재생한다.
- 실제 새 주소/파일을 요청할 때 로드 상태를 즉시 닫고 native source serial로 이전 샘플/FILE_LOADED를 걸러낸다. 단순 목록 추가는 불필요하게 로드 상태를 닫지 않는다.
- 파일/기본 트랙 변경 시 열려 있던 두 번째 자막 메뉴를 닫고 비운다. 늦은 선택 콜백도 표시 세대·로드 상태·현재 트랙 이름/ID 목록과 대조한다.
- 최소 높이를540px, 목록 최소 높이를96px로 바꾸고 바깥 스크롤 영역을 제공한다. 게스트 하네스는 목록 최소 높이와 푸터 위치를 함께 측정한다.
- 순수 검사45개 및 독립 late-seek 회귀7/7 통과. 실제 bundled native+controller 3회 재생 검사도 새 완료 확인 방식으로 재실행하여 seek3·pause1·불필요 reset0을 확인했다.
- **수정 Release DLL SHA256:** `7DD0AE8E488B2998B3F7AE530BF91081B6E3585456785B5F4C97E3834B9CD58B`. 빌드 오류0, 기존CS0108 경고1. 최종 게스트 검증은 위65DF 실행으로 완료했다.


## 긴 목록 레이아웃 보강

바깥 ScrollViewer의 무한 높이 측정이 star 행의 크기를 내용에 따라 늘리지 않도록, root Grid 높이를 실제 viewport에 맞춰 명시적으로 제한했다(최소 내용 높이460px). 문장 목록은 최소96/최대240px이고 목록 내부에서 스크롤한다. 게스트 회귀는1000개 SRT 문장을 실제로 불러와 최소 창 크기에서 목록 높이·푸터 위치·실현된 항목 수(<100)를 확인하고 화면을 남긴다. 수정 Release DLL SHA256은 `65DF7B16E859A5FFC9F1DDAD26414DD0C3B94F7CC6E77C2DEE8DC5EE3011FA3B`이며 빌드 오류0이다. 이 후보의 게스트 검증은 대기 중이다.

MainWindow.Playback.cs는795→777줄로 감소했다.
