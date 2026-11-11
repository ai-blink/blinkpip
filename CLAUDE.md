# HybridPipPlayer

WPF(.NET 8) 하이브리드 플레이어(libmpv + WebView2) + 크롬 MV3 확장(`extension/`). 구조·파일 역할은 `README.md`, 확장은 `extension/README.md`.

## 포인터
- **유사 레포·확장 비교 리서치(2026-11-11)** — `notes/research/2026-11-11-similar-projects.md` (영어 원문 `notes/research/raw/`). 기능 추가·차별화·DRM·라이선스·스토어 정책을 정하기 전에 먼저 읽는다. 값은 스냅샷이라 적용 전 원문 링크로 재확인.
- **차용 후보 ①~⑤ 구현 계획(2026-11-11)** — `notes/research/2026-11-11-implementation-plan.md`. 상태 판 → 단축키 편집 → 화면 자르기 → 자막 연습 → 확장→앱 넘기기 순서. 새 기능은 새 파일로(`MainWindow.Playback.cs` 964줄).
- 이 저장소는 `notes/`를 제외하되 `notes/research/`만 추적한다(`.gitignore`). 핸드오프·시안은 로컬 전용.
