# ⑤ 확장→앱 넘기기 (2026-11-12)

사용자가 hybridpip:// 주소 방식을 선택했다. 새 HandoffParser, App/MainWindow/MpvPlayer/SettingsWindow partial 및 FileAssociation.Protocol을 추가했다. SingleInstance는 현재 사용자 파이프를 유지하며 UTF-8 16KiB·연결/수신 제한·양쪽 입력 검증을 적용한다. 주소를 셸 명령문으로 만들지 않고 등록된 실제 실행 파일의 한 인자로 받는다. 로그인 정보 전달은 지원하지 않는다.

## 구현 범위

- 확장0.5.0은 현재 영상/iframe의 앱으로 버튼, 보낼 항목 체크박스, 미리보기와 신뢰된 최종 링크 클릭을 제공한다. 위치만 기본 선택하며 외부 자막URL/속도/PIP는 별도 선택이다. 페이지의 쿠키·로그인 저장소·헤더를 읽지 않는다. 실제 선택된 외부 track src만 자막 후보로 쓴다.
- 직접 주소를 보낼 수 없으면 안전한 페이지 주소를 사용하고 제한을 표시한다. YouTube는 검증한 영상 ID의 watch 주소로 정리한다. 서명·인증 주소를 잘라서 작동할 것처럼 보내지 않는다. 원래 영상/PIP는 자동 종료하지 않는다.
- protocol은 v=1과 url 필수, t/sub/speed/pip 선택이며 정확한 필드만 받는다. URL별 정규화 후4096 UTF-8 bytes, 전체16384 bytes, t0..31536000초, speed0.25..4, pip1/0. 중복·이상 escape·제어문자·userinfo·fragment·알려진 인증/서명 값은 거절한다. JS와 C#은 공통 fixture를 대조한다.
- HKCU 주소 등록/해제는 설정의 명시적 버튼에서만 수행한다. 다른 프로그램 소유 등록은 덮어쓰지 않으며 현재 실행 파일의 등록만 해제한다. 호스트 등록은 수행하지 않았다.
- startup과 pipe 모두 같은 OpenExternal 경로를 사용한다. 명시적 t=0도 이어 보기보다 우선한다. PIP는 토글 요청이 아닌 원하는 상태이며, native playlist entry ID·route revision·path를 함께 확인하여 오래된 FILE_LOADED가 새 요청 정보를 적용하지 않게 한다. END_FILE은 일치하는 요청만 취소한다.
- 엔진 B는 페이지 열기/PIP만 적용하고 시간·속도·자막 미지원 상태를 표시한다. OTT 보호 영상 시작 감지/자막 읽기: 확인 필요.

## 비 GUI 검증

- 실제 Release 빌드 오류0, 기존 ToastWindow.Margin CS0108 경고1. DLL SHA256 630FDE03B2C6F04128AD4AEEEEAD00D5601EC07C72928A8D43D8000D04D8A5BC.
- C# 입력 모델91개 통과(공통83+추가길이/UTF-8/중복/NaN/명시적0·false8개). 임시 검사 프로젝트 `%TEMP%/HybridPipHandoffChecks`, 호스트 GUI 없음.
- 번들 libmpv headless identity20개 검사 통과. loadfile replace 직후 playlist/0/id와 실제 START_FILE eventdata int64를 대조했고 같은 URL3회에 서로 다른1/2/3 ID를 확인했다. vo/ao=null, 설정/스크립트/입력 비활성.
- 확장 순수 입력/주소 선택91개, DOM 동작10개, 기존 자막12개 검사 및 JS 구문 검사가 통과했다. 최종 브라우저 결과는 아래에 따로 기록한다.

재확인 명령 `node --test extension/test/handoff.test.js extension/test/handoff-ui.test.js`: 종료 코드0, 입력91개·DOM10개 통과. `git -c safe.directory=C:/ai/projects/HybridPipPlayer diff --cached --check`: 종료 코드0.

## 게스트 검증

게스트 DNS 제약을 피해 게스트 내부 loopback HTTP 영상/자막 fixture를 사용했다. 전역 runtime이 없는 게스트에서도 실제 exe 주소 실행을 검증하도록 .NET/WindowsDesktop8.0.33을 포함한 별도 self-contained win-x64 publish를 사용했다. 아래 세 실행의 제품 DLL SHA256은 모두 `FC0C00AB1D3134C61469011BB22BF7E78005E4A269CB9B777BC5062DDBD3082C`이며, 최종 Release 원본 `630FDE03...`에서 생성했다. 이전 A774 빌드의 결과는 최종 집계에서 제외했다.

최종 기준은 세 실행에서 확보한 **49개 통과 검사**다. 브라우저 링크→프로토콜과 실제 설치된 확장 버튼→앱 흐름을 구분했다. 검증 준비·입력 대상 확인 검사도 이 수에 포함된다.

| 실행 ID | 통과한 범위 | 결과 |
| --- | --- | --- |
| `hybridpip-7b9d5cee8f0a40db97058c47f41bf8af` | 실제 exe 새 실행 10개 + 실행 중 App/파이프 10개 | 20/20 |
| `hybridpip-00895be5bcb146d7910093125e717547` | 잘못된 IPC·요청 수명 14개 + 느린 자막 3개 | 17/17 |
| `hybridpip-e2658654b576458b8191d477b9381d94` | 실제 Edge 확장 설치·영상 버튼·앱 전달 | 12/12 |

첫 실행은 cold t42초/speed1.5/자막과 같은 PID의 PIP 전환을 확인했다. 실행 중 경로는 t30→30.1초/speed1.75/PIP/자막, 같은 URL 최신 요청70.2초, 명시적0초가 저장된60초보다 우선함, 생략한 옵션 보존, PIP true 반복/false를 확인했다. 코어20개 뒤 브라우저 fixture 준비 오류가 발생했으므로 그 실행의 전체 receipt는 `ok:false`다.

두 번째 실행은 잘못된 주소·17KB 프레임·잘못된 UTF-8·미완성 프레임 거절과 매회 정상 복구, 열린 채 방치된 pipe의2350ms 내 복구를 확인했다. 이전 END_FILE은 새 요청을 지우지 않았고 일치하는 중지/실패는 요청을 해제했다. 자막 HTTP 응답을 보류한 동안 Dispatcher가7회 동작하고 실제 볼륨 컨트롤17이 적용됐으며, 응답 후 자막 표시와 소스 전환 후 이전 자막 누출 없음을 확인했다. 별도 확장 설치 helper 실패1개가 있어 전체 receipt는 `ok:false`; 해당 실패는17개 통과 범위에 포함하지 않았다.

마지막 실행은 게스트 Edge156의 새 시험 프로필에서 Developer mode→압축 풀린 파일 로드→정확한 시험 확장 폴더 선택으로 실제 확장을 설치했다. 영상 위 ‘앱으로’ 버튼과 닫힌 shadow dialog의 실제 클릭으로 링크를 만들고, 새 탭의 특정 HybridPipPlayer 확인창을 UIA Invoke로 허용했다. ‘항상 허용’은 변경하지 않았다. 앱 native 상태는 target0.5291초→0.591초, speed1.25, PIP=true, 올바른 WebM URL 및 외부VTT였다. 원래 브라우저 영상은 재생 상태를 유지하며1.7335초 진행했다. 전체 receipt `ok:true`, exitCode0이다.

세 실행 모두 `hostElevated:true`, `hostInput:false`, `vmPowerChanged:false`, `protocolRegistrationRestored:true`, `restored:true`, `payloadCopiesRemoved:true`, 최종 VM `running`을 기록했다. 고정 VM은 `73c28998-944e-4a95-ba07-19729c36db53`, 게스트 UUID는 `2E903A40-DD97-4884-9C93-B7AEABF338FE`, GUI 세션은 TestAgent/1이다. 기존 HKCU 등록·설정은 복원했고 시험 소유 앱/브라우저 프로필만 정리했다.

원시 증거는 `notes/validation/runs/<실행 ID>/receipt.json`과 `extracted/evidence/handoff.json`이다. 첫 실행의 새 프로세스 결과는 `handoff-before-restart.json`에 있다. 마지막 실행의 `handoff-extension-installed.png`, `handoff-extension-preview.png`, `handoff-extension-protocol-prompt.png`, `handoff-extension-result.png`에 실제 화면을 보존했다. ignored 검증 도구와 원시 로그는 커밋하지 않는다.

**표시 한계:** e265의 native 상태 검사는 위와 같이 통과했지만, 즉시 캡처한 result.png의 재생 줄은 이전 시간·속도를 표시했다. 그 화면 표시와 native 상태의 일치는 확인하지 못했다. 후속 eeb2/9c442 실행은 Edge 웹 UIA/화면 갱신 또는 전달 준비 단계에서 실패해 이 차이를 해소하지 못했으며 성공 집계에서 제외했다. 이전 AVI의 UI/native 일치만 나온 후속 결과를 handoff 표시 통과로 사용하지 않는다. 세션 복구·재로그인·재부팅·보안 변경은 수행하지 않았다.

초기 helper 실패는 반응형 메뉴 준비, 한국어 버튼 이름, Edge 자식 프로세스의 폴더창 소유권, 반복 WMI 오류, `_blank` 새 탭 대신 원래 탭을 앞으로 가져온 동작이었다. 관측된 UI/DOM과 검증된 프로세스 계보를 사용하도록 helper를 보완했다. 별도 두 실행의 PowerShell Direct 사전검사 실패는 GUI 진입 전 종료됐고 이후 읽기 전용 probe에서 연결이 복구된 것을 확인했다. 이 실패들을 제품 통과로 계산하지 않았다.

최종 실행 명령(기존 사용자 승인에 따른 관리자 자식 실행, 비밀 인자 없음):

```powershell
Start-Process -FilePath 'C:\ai\projects\background-lab\work\venv\Scripts\python.exe' -Verb RunAs -WindowStyle Hidden -ArgumentList @('"C:\ai\projects\HybridPipPlayer\notes\validation\run_hyperv.py"','--stage','handoff','--app-dir','"C:\ai\projects\HybridPipPlayer\notes\validation\candidate-handoff-final"')
```

모드는 payload의 `harness-mode.txt`에 기록됐다. 위 세 최종 근거 실행은 각각 `handoff-final`, `handoff-extension-only`, `handoff-extension-ui-only`다. helper 컴파일 명령 `dotnet build C:\ai\projects\background-lab\work\hybridpip-key-harness\KeyHarness.csproj --nologo -v minimal`은 오류0·경고0이었다. 호스트에서는 컴파일만 수행했다.

API 근거: [mpv client.h START_FILE/END_FILE entry identity](https://raw.githubusercontent.com/mpv-player/mpv/master/include/mpv/client.h).

## 실제 경로에서 보완한 점

.NET Uri.AbsoluteUri가 비어 있는 authority 경로에 `/`를 붙이는 것을 확인해 `hybridpip://play?`와 `hybridpip://play/?`를 모두 받는다. 다른 경로는 계속 거절하며 공통 회귀 사례에 포함했다. 초기 브라우저 검증의 실제 수신 형식과 최종 결과는 게스트 증거에 따로 남긴다.


## 느린 자막과 페이지 전환 보완

- 검증 서버의 HTTP 처리와 WPF UIA 조회가 같은 dispatcher를 써서 생긴 교착을 분리했다. 게스트 서버는 작업 스레드에서 응답한다. 지연된 위치/컨트롤 조회 실패를 제품 성공으로 계산하지 않았으며, 분리 후 위20개 코어 검사가 통과했다.
- 제품도 원격 sub-add를 mpv_command_async로 전송한다. 명령별 ID와 재생 항목을 대조하며 새 주소·파일 종료·Dispose에서 취소한다. 이전 요청의 취소 완료를 새 영상의 실패로 표시하지 않는다. 실제 live partial을 연결한 headless native 검사6/6 통과: 지연 응답, 빠른 속성 조회, 직접 취소, 소스 변경·즉시 변경 후 자막 누출 없음. 증거 `C:/ai/projects/background-lab/work/hpp-async-sub-probe/bin-product/net8.0/receipt.json`.
- SPA가 같은 video/blob을 재사용해도 소유 프레임의 현재 주소를 매번 읽는다. 마지막 클릭에서 대상 페이지가 바뀌면 미리보기를 갱신하고 다시 확인하도록 한다. 원래 프레임의 URL provider를 사용하므로 PiP 문서의 about:blank를 대신 보내지 않는다.
- 확장은 정규 형태 hybridpip://play/?를 생성해 길이 검사가 Windows 표준화 뒤에도 유지되게 했다. 앱은 기존 슬래시 없는 형식도 지원한다.

최종 비동기 자막 source Release630F의 self-contained publish SHA256: `FC0C00AB1D3134C61469011BB22BF7E78005E4A269CB9B777BC5062DDBD3082C`. 위 최종49개 검사에 사용한 동일 제품 빌드다.
