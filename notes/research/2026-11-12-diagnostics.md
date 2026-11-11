# ① 재생 진단 상태 판 — 구현 및 검증 (2026-11-12)

## 구현

- `DiagInfo.cs`: 스레드 안전 상태 집계, 최근 경고·오류 최대 12건, 엔진 선택 → 주소 풀기 → 열기 → 재생 시작 단계. URL 경로·쿼리·fragment·사용자 정보와 로컬 경로·인증 값은 저장 전에 가린다.
- `MpvPlayer.cs`: start-file, file-loaded, playback-restart, end-file, 로그를 집계기에 전달한다. file-loaded만으로 재생 성공을 표시하지 않는다. `ytdl_hook` 로그를 주소 풀기 단계의 단서로 사용하며, 오류 원인 자체를 추측하여 확정하지 않는다.
- `MainWindow.Diagnostics.cs`: 실패 상태와 진단 보기 토스트, 초기화/파일 열기/재생 목록 경로 연결, 비동기 WebView2 초기화 중 새 요청의 우선권 유지.
- `SettingsWindow.Diagnostics.cs`: 북마크·정보 탭의 `InfoText`를 1초마다 갱신하고, 가린 진단을 복사한다. 창 종료 시 타이머를 정지한다.
- yt-dlp 버전은 앱 폴더/PATH의 실행 파일에 `--version`을 비동기로 요청한다. 8초 제한, 종료 취소, 자식 프로세스 종료를 적용한다. 표시된 경로 탐색 결과가 mpv 내부의 실제 실행 경로를 입증하는 것은 아니다.
- 없는 절대 로컬 파일 경로를 웹 주소로 잘못 바꾸던 정규화를 수정했다.
- `MainWindow.Playback.cs`는 최소 호출부만 수정하고 `ShowMpv`를 partial로 옮겨 기존보다 5줄 줄었다.
- 디스크에 새로 남기는 mpv 경고·오류도 같은 가림 처리를 적용한다. 기존 로그 파일을 삭제하거나 변경하지 않았다.

## 확인한 결과

- `dotnet build HybridPipPlayer.csproj --no-restore -v minimal`: 성공, 오류 0. 기존 `ToastWindow.Margin`의 CS0108 경고 1건.
- 출력: `C:\ai\projects\HybridPipPlayer\bin\Debug\net8.0-windows\HybridPipPlayer.dll` 및 같은 폴더의 실행 파일.
- 비 GUI C# 검사: `dotnet run --project "$env:TEMP\HybridPipPlayer-DiagnosticsChecks\DiagnosticsChecks.csproj" --no-restore -v minimal` → `PASS 23 diagnostics checks`.
- 검사 범위: URL·경로·인증 정보 가림, 없는 파일 실패 단계, yt-dlp 실패 단계, 최근 로그 개수 제한, OTT 전환 뒤 이전 mpv 이벤트 무시, file-loaded와 재생 시작 구분, 일시정지 표시, 동시 로그/스냅샷 호출, yt-dlp 없음, 종료 뒤 실패 알림 억제.
- 검사 하네스는 `%TEMP%\HybridPipPlayer-DiagnosticsChecks\`에 있으며 제품 프로젝트의 재귀 컴파일 대상 밖에 있다.
- `git diff --check`: 통과. 에이전트는 커밋·스테이징하지 않았다.

## Hyper-V 검증 및 남은 범위

- 최종 수정 빌드로 Hyper-V 4개 시나리오 모두 통과: 없는 파일, 짧은 YouTube ID의 실제 `Incomplete YouTube ID` 거부, yt-dlp 부재와 실제 resolver 실패, 별도 서명·로그인 값 포함 URL의 가림. 각 사례에서 실제 실패 토스트·정보 탭의 엔진/단계/버전·클립보드 교체 및 가림을 확인했다. 단순 DNS 실패를 잘못된 ID 거부의 증거로 사용하지 않았다.
- 증거: 로컬 `notes/validation/runs/hybridpip-f5b21e5a334e461b821f0e952ac9f423/receipt.json`, `evidence.zip` 및 화면 PNG. 게스트 UUID `2E903A40-DD97-4884-9C93-B7AEABF338FE`, TestAgent 세션 1. 설정·yt-dlp·클립보드 복원, VM Running 유지, 호스트 입력 없음.
- 초기 관리 권한 제한은 사용자의 명시적 UAC 승인 후 해결했다. 검증용 복사본 누적으로 발생한 게스트 디스크 부족은 이번 작업의 앱/압축파일 복사본만 정리해 해결했고, 이후 회수된 증거를 확인한 뒤 해당 복사본을 정리한다. 기존 자료·VM 전원·보안 설정은 변경하지 않았다.
- URL 해석 단계는 mpv의 ytdl 로그를 이용한 관찰이며 별도 네트워크 진단을 수행하지 않는다.
- OTT WebView2의 페이지 탐색 완료와 보호 영상 재생 성공을 구분했다. 보호 영상 시작 감지와 자막 읽기는 **확인 필요**로 표시한다.
- 번들 libmpv·yt-dlp 라이선스/출처 검토와 배포 승인은 이 구현의 검증 범위에 포함되지 않는다.


## 가상머신 발견 사항 수정

- 첫 게스트 검사에서 URL 가림 후 `https://`의 `s:/` 부분을 드라이브 경로로 잘못 인식하여 호스트 표시가 사라지는 문제를 발견했다. 정보 노출은 없었지만 진단 정보가 불필요하게 줄었다.
- 드라이브 경로 정규식에 앞 문자 경계를 추가했다. HTTP/HTTPS 호스트 보존과 실제 로컬 경로 가림 회귀 검사 3개를 추가했고, 순수 검사 총 23개 및 빌드가 통과했다.
- 수정 빌드 DLL SHA256: `DCA1EE13151E1524BC47AA5AED74F38C20DCC95E3522305B326C2D4BB103A10F`.
- 위 SHA256의 수정 빌드로 최종 Hyper-V 검증을 통과했다. 이전 빌드 검증과 구분해 증거를 보존했다.
