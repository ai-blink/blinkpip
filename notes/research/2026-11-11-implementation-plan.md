# 차용 후보 ①~⑤ 구현 계획 (2026-11-11 작성, 2026-11-12 전체 구현 승인)

근거: `2026-11-11-similar-projects.md` · 시안: `notes/mocks/2026-11-11_borrow-candidates/borrow-candidates-mocks.html`(저장소 제외, 로컬 전용)

## 공통 원칙
- 새 기능은 새 partial/파일로. `MainWindow.Playback.cs`는 964줄이라 더 키우지 않는다.
- 저장소는 기존 방식(`%LocalAppData%\HybridPipPlayer\` json). 단계마다 빌드 성공 + 검증 + 커밋 1개.
- GUI·키 입력 검증은 Hyper-V 가상머신 안에서(호스트에 입력 주입 금지).
- 확인 못 한 것은 "확인 필요"로 남긴다(OTT 보호 영상 시작 감지, 자막 읽기 가능 범위 등).

## 순서
1. **① 상태 판** — `MpvPlayer`의 end-file 이유·로그 → 신규 `DiagInfo.cs`, 단계(엔진 선택→주소 풀기→열기→재생 시작) 매핑, `yt-dlp --version` 비동기 확인, 설정 "북마크·정보" 탭(`InfoText`)과 재생 실패 토스트에 표시, 진단 복사 시 서명·로그인 값 가림. 검증: 없는 파일·잘못된 유튜브 주소·yt-dlp 경로 제거 3종 재현.
2. **④ 단축키 편집** — `MpvPlayer.HostKeys` 고정 표와 `ShortcutKeyBridge`를 "기능 id→키" 한 표로 합치고, 바꾼 키만 json 저장, 겹침 검사·기본값 복원. 전역 단축키(Ctrl+Alt+P)는 제외. 검증: 기본 키 전부 변경 전후 동일 동작표 대조(회귀 위험 가장 큼).
3. **③ 화면 자르기** — `video-zoom`/`video-pan-x/y`, 비율로 저장(신규 `CropStore.cs`), `FeatureCatalog`에 "영역" 기능. 엔진 A 전용, OTT 미보장 표시.
4. **② 자막 연습** — 선행: `MpvPlayer`에 `observe_property`(없음) 추가 → `sub-start`/`sub-end`, 줄 끝 자동 멈춤, 줄 반복, `secondary-sid` 번역 줄. 기존 `ab-loop`과 겹치지 않게 줄 단위. OTT 제외.
5. **⑤ 확장→앱 넘기기** — `hybridpip://play?url=&t=&sub=&speed=&pip=` 등록(`FileAssociation.cs`), `SingleInstance.cs`로 실행 중 앱에 전달, 확장 `background.js`·`manifest.json`. 외부 입력이라 길이·형식 검증, 로그인 값 미수신. 착수 시 방식(사용자 정의 주소 vs 네이티브 메시징)을 짧게 확인.

## 알려진 위험
DRM·약관·라이선스(mpv GPL, yt-dlp 실행파일 GPLv3+), 스토어 정책(`<all_urls>`·"앱 실행만이 목적") — 리서치 문서 §한계 참조.
