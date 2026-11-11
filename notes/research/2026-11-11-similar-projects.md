# 유사 레포·크롬 확장 리서치 (2026-11-11)

- 방법: `/codex-research` sol high, 3각도 병렬(데스크톱 앱·레포 / 확장 / 한계·주의). 문서·README·스토어 페이지만 읽었고 **설치·재생 실측은 하지 않았다.**
- 영어 원문(Codex 답): `raw/2026-11-11-similar-apps-repos.en.md`, `raw/2026-11-11-similar-extensions.en.md`, `raw/2026-11-11-limits-pitfalls.en.md`
- 스타·사용자 수·버전은 2026-11-11 기준 스냅샷이다. 적용 전 원문 링크로 다시 대조한다(특히 DRM·약관·라이선스).

## 결론
- 두 엔진(libmpv + WebView2) + 모든 사이트 크롬 확장 + 리모컨 창을 모두 갖춘 동일 제품은 못 찾았다. 부분 겹침만 있다.
- 가장 비슷한 제품: **uView Player**(윈도우·WebView2, 항상 위·잠금·투명도·북마크·확장 연동·`uview:` 프로토콜). 소스 비공개, 현재 Netflix 재생 여부 확인 필요.
- 구현 참고 우선순위: **mpv.net, Flyleaf, OnTopReplica, StreamHub**. 확장 쪽: **PiP+, SubPIP, SuperPiP**, 네이티브 연동은 **ff2mpv, mpv-handler**.

## 데스크톱 앱·레포
| 프로젝트 | 스택·규모 | 배울 점 |
|---|---|---|
| uView Player | WebView2, 비공개 | 채널 팔로우, 이미지 갤러리, `uview:` 실행 프로토콜 |
| mpv.net | C#/libmpv, 5,555★, GPL-2.0, 마지막 커밋 2026-02 | 설정·단축키 검색 편집기, 전역 단축키, 확장 API |
| StreamHub | Electron+외부 mpv, Linux 전용, 7★, MIT | Netflix/Disney+와 mpv 병행, 서비스 전환 시 일시정지, 세션 분리 |
| Flyleaf | WPF/FFmpeg, 924★, LGPL-3.0 | 투명 오버레이, 분리 가능한 영상 호스트 |
| LLPlayer | WPF, 4,634★, GPL-3.0 | 이중 자막, 자막 검색 사이드바, Whisper, 번역 |
| OnTopReplica | WinForms, 3,413★, MS-RL, 2023 이후 정지 | 클릭 투과, 영역 크롭 저장 |
| MPC-QT | libmpv, 1,150★ | 재생목록별 진행률, 시크바 썸네일 |
| ImPlay | libmpv, 790★ | 명령 팔레트, mpv 속성 점검기 |
| Screenbox | UWP/LibVLC, 4,683★ | PiP, 터치 제스처 |
| MPC-HC(clsid2) | 16,130★ | A-B 반복, 종료 시 동작 |
| 기타 | SMPlayer, Haruna, Mpv.NET-lib, PiP-Tool, ElectronPlayer(보관됨) | 원문 참조 |

## 크롬·파이어폭스 확장
| 확장 | 사용자 수 | 특징 |
|---|---|---|
| PiP+ | 257 | Document PiP, 자막, 컨트롤. Netflix 실험적 |
| SubPIP | 1,000 | 자막 스타일, 자동 PiP, 번역. 레포 404 |
| SuperPiP | 60,000 | 서비스별 자막, 다음 영상 버튼 |
| PiP Cue | 176 | 자막 전용 창, A-B 반복 |
| asbplayer | 60,000 | 서비스별 자막 어댑터, 자막 목록 이동, 문장 반복 |
| Substital | 400,000 | 자막 검색·가져오기, 싱크 보정 |
| Video Speed Controller | 3,000,000 | 사이트별 규칙, 컨트롤 위치 |
| ff2mpv / mpv-handler | 922(FF) / 없음 | 네이티브 호스트·`mpv-handler://`로 재생 위치·자막·리퍼러 전달 |
| 기타 | Google PiP, PiP Everywhere, Language Reactor, Dualsub, SponsorBlock | 원문 참조 |

## 한계·주의
- **DRM**: WebView2 Widevine 지원은 공식 요청(#4828)이 열린 채. 번들 런타임에서 Netflix/Disney+ 재생을 직접 확인해야 한다. 고정 버전 런타임에서 보호 영상 실패 보고(#4632). "L3=480p" 식 일률 규칙은 근거 없음.
- **Document PiP**: 사용자 제스처 필요, 열어 준 탭에 종속, 위치 지정 불가, 최상위 프레임에서만 호출 → `all_frames` 구조는 최상위 프레임 조율자 필요. 네이티브 WPF의 스냅·클릭 투과·독립 수명이 차별점.
- **MV3**: 서비스 워커 30초 유휴 종료 → 상태는 저장소에, 재연결 설계. 원격 실행 코드 다운로드 금지. 콘텐츠 스크립트는 격리 세계라 사이트 플레이어 객체 직접 접근 불가.
- **스토어 정책**: `<all_urls>`는 사유 필요 → 사이트별 선택 권한 검토. "다른 앱 실행만이 목적" 확장은 불리.
- **yt-dlp**: 2025-11 이후 유튜브 전체 지원에 외부 JS 런타임(EJS) 필요. 추출 성공해도 mpv에서 403 사례(2026-10-27). 검증된 조합 배포 + 진단 표시.
- **라이선스**: mpv 기본 GPLv2+, yt-dlp PyInstaller 실행파일은 GPLv3+. 배포 중인 `libmpv-2.dll`·`yt-dlp.exe` 출처·빌드 확인 필요.
- **약관**: Netflix(2026-04)·Disney(2025-10) 약관이 자동화·프레이밍·보호 우회를 제한. 재생된다고 허용된 것 아님.

## 차용 후보 (미결정 — 사용자 선택 대기)
1. 이중 자막·자막 목록·싱크 보정
2. 자막 단위 자동 정지·반복 연습 모드
3. 크롭 프리셋 저장
4. 검색·재매핑 단축키 편집기, 전역 단축키
5. 상태 패널(엔진·의존성 버전·실패 단계·브라우저 넘기기)
6. `uview:` 류 실행 프로토콜, 사이트별 재생 규칙, SponsorBlock 카테고리·구간 표시 연동

## 확인 필요
uView 내부와 현재 Netflix 재생 · SubPIP/SuperPiP/PiP Cue/asbplayer의 Document PiP·MV3 구현 · Play with MPV 유지보수 · PiP+ 스토어(1.4.0) vs 레포(1.3.0) · 번들 WebView2 실제 DRM 재생 · 약관·라이선스의 배포 지역별 해석.
