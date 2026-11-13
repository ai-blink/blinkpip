# 외부 구성 요소 고지

릴리스 압축 파일(`blinkpip-<버전>-win-x64.zip`, 0.6.0은 `HybridPipPlayer-0.6.0-win-x64.zip`)에는 아래 외부 구성 요소가 함께 들어 있다. 각 구성 요소의 저작권과 라이선스는 원저작자에게 있다.

| 구성 요소 | 넣은 판(0.6.0~0.6.1) | 라이선스 | 출처·소스 코드 |
| --- | --- | --- | --- |
| libmpv (`libmpv-2.dll`) | `v0.41.0-1227-ge471496c5` (shinchiro 빌드 20261021) | GPL-2.0-or-later / 이 빌드는 GPL 구성 요소를 포함해 GPL-3.0 조건 적용 | mpv 소스: <https://github.com/mpv-player/mpv> · 빌드 스크립트와 받은 파일: <https://github.com/shinchiro/mpv-winbuild-cmake/releases> |
| yt-dlp (`yt-dlp.exe`) | `2026.10.26` | Unlicense(퍼블릭 도메인), 동봉된 Python 런타임 등은 각자의 라이선스 | <https://github.com/yt-dlp/yt-dlp> |
| mpv_sponsorblock (`scripts/`) | 커밋 `7785c14` | GPL-3.0 (`scripts/LICENSE-sponsorblock.txt`) | <https://github.com/po5/mpv_sponsorblock> |
| Microsoft Edge WebView2 SDK (`Microsoft.Web.WebView2.*.dll`, `WebView2Loader.dll`) | 1.0.2792.45 | Microsoft WebView2 SDK 라이선스(BSD 형식) | <https://www.nuget.org/packages/Microsoft.Web.WebView2> |
| .NET 8 런타임(자체 포함 배포) | 8.x | MIT 및 .NET 고지 | <https://github.com/dotnet/runtime> |
| Tabler Icons (재생 줄 아이콘 15개, `Assets/icons/*.png` — 실행 파일 안에 포함) | `@tabler/icons` 3.51.0 (0.6.1 다음 판부터) | MIT (아래 원문) | <https://github.com/tabler/tabler-icons> |

## GPL 구성 요소의 소스 코드

libmpv와 mpv_sponsorblock은 GPL 조건으로 배포된다. 위 표의 링크에서 해당 판의 전체 소스 코드를 받을 수 있다. 링크가 사라졌거나 정확한 판의 소스가 필요하면 이 저장소의 이슈로 요청하면 같은 판의 소스를 제공한다.

## Tabler Icons 라이선스 원문

SVG 원본을 흰 선 64×64 PNG로 바꿔 쓴다(모양은 바꾸지 않음).

```
MIT License

Copyright (c) 2020-2026 Paweł Kuna

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 서비스 이용 약관

넷플릭스·디즈니+ 등 OTT는 각 서비스의 웹 페이지를 그대로 띄워 재생한다. 복제 방지(DRM)를 우회하지 않는다. 각 서비스의 이용 약관은 이용자가 따로 확인해야 한다.
