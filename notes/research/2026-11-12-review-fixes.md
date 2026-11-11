# 2026-11-12 리뷰 후 최소 수정과 검증

최신 기능 리뷰에서 확인한 다섯 문제만 수정했다. 제품 소스는 아래 빌드 이후 동결했으며, 이 문서 작성 시 커밋하지 않았다.

1. `DiagInfo.cs`: URL 안의 apostrophe에서 가림 범위가 끝나 경로 뒤쪽이 노출되던 문제를 수정했다. HTTP/HTTPS/FTP 주소의 apostrophe를 URL 일부로 처리한다.
2. `MainWindow.Diagnostics.cs`, `MainWindow.Playback.cs`: 엔진 A의 기존 목록에 파일을 추가할 때 재생 중인 항목의 handoff revision을 유지한다. 영상 교체, 빈 목록, 엔진 B에서 전환하는 경로는 기존 무효화를 유지한다.
3. `extension/handoff-ui.js`: 대화상자의 `keyup` 전파를 막아 사이트 재생 단축키에 전달되지 않도록 한다. 체크박스 기본 동작은 막지 않는다.
4. 같은 파일: 중간 클릭의 `auxclick` 기본 이동을 막아 최종 클릭 시점의 미리보기 확인을 우회하지 못하게 한다.
5. 같은 파일: 소유 문서의 실제 `pagehide`에서 대화상자와 공유 active 상태를 정리한다. 대화상자를 교체할 때 이전 문서 listener도 해제한다.

## 빌드와 화면 없는 검사

- Release 빌드: 오류 0, 기존 CS0108 경고 1.
- Release DLL SHA256: `2DA148BF0852E43D548CA6B3C50F7EBF30E9E301A867DE2ADE771F7335B437C0`.
- 별도 self-contained win-x64 publish DLL SHA256: `45C920F2AFBE0D358699EE5C5A0BEDF3666DBA67AB9F83780534DA857475BDA6`.
- 진단 가림 회귀 10/10: apostrophe/복수 apostrophe/따옴표 로그/인코딩 경로/인증 값/로컬 경로/실제 Snapshot 경로. 외부 검사 프로젝트가 live `DiagInfo.cs`를 연결한다.
- 확장 입력 순수 검사 91/91, DOM double 검사 14/14, 기존 자막 검사 12/12. DOM double 결과는 실제 브라우저 GUI 결과가 아니다.
- 외부 게스트 helper 컴파일: 오류 0, 경고 0. 호스트에서 실행하지 않았다.

재확인 명령:

```powershell
dotnet build 'C:\ai\projects\HybridPipPlayer\HybridPipPlayer.csproj' -c Release --nologo -v minimal
dotnet run --project 'C:\ai\projects\background-lab\work\hpp-diag-apostrophe-review\Checks.csproj' --nologo
node 'C:\ai\projects\HybridPipPlayer\extension\test\handoff.test.js'
node 'C:\ai\projects\HybridPipPlayer\extension\test\handoff-ui.test.js'
node 'C:\ai\projects\HybridPipPlayer\extension\test\subfeed.test.js'
dotnet build 'C:\ai\projects\background-lab\work\hybridpip-key-harness\KeyHarness.csproj' --nologo -v minimal
git -c safe.directory=C:/ai/projects/HybridPipPlayer diff --check
```

진단 원시 결과: `C:\ai\projects\background-lab\work\hpp-diag-apostrophe-review\bin\Debug\net8.0\receipt.json`.

publish 명령:

```powershell
dotnet publish 'C:\ai\projects\HybridPipPlayer\HybridPipPlayer.csproj' -c Release -r win-x64 --self-contained true -p:OutputPath=C:\ai\projects\background-lab\work\hybridpip-review-publish\build\ -o 'C:\ai\projects\HybridPipPlayer\notes\validation\candidate-handoff-review' --nologo -v minimal
```

## 실제 Hyper-V 게스트 결과

고정 VM `73c28998-944e-4a95-ba07-19729c36db53`, 게스트 UUID `2E903A40-DD97-4884-9C93-B7AEABF338FE`, TestAgent 세션 1을 확인했다. 전원과 보안 설정은 변경하지 않았다. GUI 및 입력 도구는 게스트에서만 실행했다.

위 publish 빌드의 **append 회귀 2/2는 통과**했다. 실제 loopback HTTP 영상 응답을 보류하여 FILE_LOADED 전 상태를 만든 뒤 실제 앱 `OpenPaths(..., append:true)` 경로를 호출했다. 파일 선택창이나 물리 키 입력 검사는 아니다.

- 추가 전후 pending entry 2와 revision 2가 같고 playlist-count가 2였다.
- 응답 후 저장된 재개 위치 60초보다 명시적 t=0이 우선했다(관측 위치 0.5초). speed 1.75, PIP true, 자막 `HANDOFF SUBTITLE VERIFIED`, 같은 entry 2, 목록 2개를 확인했다.

**브라우저 이벤트 3개는 실제 통과를 확보하지 못했다.** 최신 `handoff.js`/`handoff-ui.js`의 정확한 payload 바이트를 게스트 loopback fixture에서 로드하는 검사였으며 확장을 다시 설치하는 검사가 아니다. keyup, 중간 클릭, 실제 DocumentPiP 문서 pagehide 검사에 도달하기 전 준비 단계에서 실패했다. 이를 제품 결함이나 GUI 통과로 단정하지 않는다. DOM double 14개 통과와 별개다.

| 실행 ID | 확보한 결과 | 브라우저 준비 실패 |
| --- | --- | --- |
| `hybridpip-b77510ae7f0d4022acb4375931e4ff26` | append 2/2 | 시험 WebM metadata timeout. 이후 이벤트 회귀와 무관한 재생 준비 조건을 제거했다. |
| `hybridpip-7cc0ae78453343b5a2f8690cf72cbc76` | append 2/2 | Edge가 DevToolsActivePort를 쓰는 도중 읽어 sharing violation. 정확한 소유 프로필 파일의 readable/port 준비를 최대 12초 기다리도록 helper만 보완했다. |
| `hybridpip-683b8bfed73541a389cf5696fa1faf69` | append 2/2 | fixture 열기 버튼의 CDP 입력 뒤 대화상자 checkbox가 관측되지 않았다. 제품 JS, 입력, 화면/DOM 중 원인은 확정하지 못했다. |

반복한 append는 고유 검사 **2개**로 계산한다. 각 receipt의 `checkCount:3`은 append 두 결과와 브라우저 suite 준비 실패 한 건이며, 세 브라우저 회귀가 수행됐다는 뜻이 아니다. 세 실행 모두 전체 `ok:false`, exitCode 1이다. 추가 실행은 중단했다. 기존 기능의 49개 검증은 이전 FC0C 빌드에 대한 별도 기록이며 이번 후보의 브라우저 이벤트 검증으로 대체하지 않는다.

세 실행 모두 `protocolRegistrationRestored:true`, `restored:true`, `payloadCopiesRemoved:true`, `hostInput:false`, `vmPowerChanged:false`, 최종 VM `running`을 기록했다. 마지막 freeDiskBytes는 4472979456이었다. 자체 앱·시험 브라우저 프로필만 정리했고 기존 HKCU 프로토콜 등록과 설정을 복원했다. 다른 작업과 VM은 종료하지 않았으며 세션 복구·재부팅·보안 변경도 하지 않았다.

실행 명령(기존 사용자 승인 범위의 관리자 자식 실행, 비밀 인자 없음):

```powershell
Start-Process -FilePath 'C:\ai\projects\background-lab\work\venv\Scripts\python.exe' -Verb RunAs -WindowStyle Hidden -ArgumentList @('"C:\ai\projects\HybridPipPlayer\notes\validation\run_hyperv.py"','--stage','handoff','--app-dir','"C:\ai\projects\HybridPipPlayer\notes\validation\candidate-handoff-review"')
```

payload 모드는 `harness-mode.txt`의 `handoff-review`다. runner는 실행마다 기존 VM 상태·권한·UUID를 검사하고 lease를 얻은 뒤 순차 실행한다.

원시 결과는 `C:\ai\projects\HybridPipPlayer\notes\validation\runs\<실행 ID>\receipt.json`, 같은 폴더의 `extracted\evidence\handoff.json`에 있다. 마지막 실행의 화면:

- `C:\ai\projects\HybridPipPlayer\notes\validation\runs\hybridpip-683b8bfed73541a389cf5696fa1faf69\extracted\evidence\handoff-review-append.png`
- `C:\ai\projects\HybridPipPlayer\notes\validation\runs\hybridpip-683b8bfed73541a389cf5696fa1faf69\extracted\evidence\handoff-ui-review-failure.png`

스크린샷은 관측 증거이며, 브라우저 이벤트나 앱 재생 줄 표시와 native 상태의 일치를 입증하는 자료로 사용하지 않는다. ignored 도구·원시 로그·임시 payload는 커밋하지 않는다.
