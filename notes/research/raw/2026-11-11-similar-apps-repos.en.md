**uView Player is the closest product match; mpv.net is the strongest Windows/.NET/libmpv reference.** StreamHub provides an unusually relevant browser-plus-mpv architecture comparison, although it targets Linux.

Ranked by similarity to your description. Stars were checked on **2026-11-11**; commit dates refer to the latest default-branch commit returned by GitHub, in UTC. “Learn from” identifies features absent from your brief, rather than confirmed gaps in your implementation.

1. **[uView Player](https://www.idruf.com/products/uviewplayer)** — **Closest overall product**
   - **Stars / last commit:** N/A; no public repository identified. **Stack:** Windows app using WebView2; other internals **NEEDS VERIFICATION**. **License:** source/license availability **NEEDS VERIFICATION**.
   - **Overlap:** floating always-on-top playback, local media and web streams, browser-extension handoff, bookmarks, playlists, window locking, adjustable transparency.
   - **Learn from:** followed-channel management, image galleries, and a published `uview:` launch protocol. Netflix is advertised; current Netflix/Disney+ DRM playback **NEEDS VERIFICATION**.

2. **[mpv.net](https://github.com/mpvnet-player/mpv.net)** — **Closest playback-engine and Windows stack**
   - **Stars:** 5,555 · **[Last commit](https://github.com/mpvnet-player/mpv.net/commits/main):** 2026-02-09 · **Stack:** C#/.NET, Windows WPF/WinForms UI, libmpv · **License:** GPL-2.0.
   - **Overlap:** mpv playback, yt-dlp streaming, browser launching, subtitle controls, external control through JSON IPC.
   - **Learn from:** searchable configuration and shortcut editors, global shortcuts, .NET extension API, Lua/JavaScript scripts, and advanced HDR/color-management options.

3. **[StreamHub](https://github.com/pl0xuee/StreamHub)** — **Closest hybrid architecture**
   - **Stars:** 7 · **[Last commit](https://github.com/pl0xuee/StreamHub/commits/main):** 2026-10-11 · **Stack:** JavaScript, castLabs Electron/Chromium, external mpv · **License:** MIT; runtime redistribution has separate terms.
   - **Overlap:** official Netflix/Disney+ websites, persistent isolated service sessions, PiP, and mpv playback for Jellyfin.
   - **Learn from:** pause-on-service-switch, session management, four-pane viewing, and browser/mpv coordination. **Linux-only**; DRM and service compatibility are README claims and **NEEDS VERIFICATION**.

4. **[Flyleaf](https://github.com/SuRGeoNix/Flyleaf)** — **Strongest WPF hosting/overlay reference**
   - **Stars:** 924 · **[Last commit](https://github.com/SuRGeoNix/Flyleaf/commits/master):** 2026-11-10 · **Stack:** C#/.NET, WPF/WinUI/WinForms, FFmpeg, DirectX · **License:** LGPL-3.0.
   - **Overlap:** native embedded playback, transparent UI overlays, detachable video hosts, aspect-preserving resize, yt-dlp integration.
   - **Learn from:** attach/detach lifecycle, rendering-surface composition, recording, reverse playback, subtitle downloading, and capture inputs. Its engine is FFmpeg/DirectX rather than libmpv.

5. **[LLPlayer](https://github.com/umlx5h/LLPlayer)** — **Strongest subtitle-feature comparison**
   - **Stars:** 4,634 · **[Last commit](https://github.com/umlx5h/LLPlayer/commits/main):** 2026-07-19 · **Stack:** C#/WPF, Flyleaf, FFmpeg/DirectX · **License:** GPL-3.0.
   - **Overlap:** Windows playback, yt-dlp, flexible subtitle size/placement, browser-extension integration workflows.
   - **Learn from:** dual subtitles, searchable subtitle sidebar, local Whisper transcription, bitmap-subtitle OCR, translation, and word lookup. Its documented extension workflow uses clipboard/browser integration rather than your injected playback bar.

6. **[Screenbox](https://github.com/huynhsontung/Screenbox)** — **Modern Windows PiP benchmark**
   - **Stars:** 4,683 · **[Last commit](https://github.com/huynhsontung/Screenbox/commits/main):** 2026-11-08 · **Stack:** C#, UWP, LibVLCSharp/libVLC · **License:** GPL-3.0.
   - **Overlap:** Windows media playback, PiP, resizing shortcuts, screenshots.
   - **Learn from:** touch gestures, network-media browsing, Chromecast, Xbox support, and Fluent UI. Useful for interaction design and device support; its playback stack differs.

7. **[MPC-QT](https://github.com/mpc-qt/mpc-qt)** — **Feature-rich libmpv desktop comparison**
   - **Stars:** 1,150 · **[Last commit](https://github.com/mpc-qt/mpc-qt/commits/master):** 2026-11-09 · **Stack:** C++, Qt 6, libmpv · **License:** GPL-2.0.
   - **Overlap:** Windows builds, native playback, yt-dlp online video, conventional player controls.
   - **Learn from:** multiple playlists with independent progress, seekbar thumbnails, quick queuing, threaded playlist search, and configurable screenshot filenames. Some familiar MPC options remain explicitly unimplemented.

8. **[ImPlay](https://github.com/tsl0922/ImPlay)** — **Compact libmpv interface reference**
   - **Stars:** 790 · **[Last commit](https://github.com/tsl0922/ImPlay/commits/main):** 2026-02-23 · **Stack:** C++, Dear ImGui, GLFW, mpv · **License:** GPL-2.0.
   - **Overlap:** Windows playback, subtitle settings, playlists, mpv scripts and configuration.
   - **Learn from:** command palette, live mpv-property inspector, filtered logs, completion-enabled console, and support for thumbnail-preview OSC scripts.

9. **[OnTopReplica](https://github.com/LorenzCK/OnTopReplica)** — **Best click-through overlay reference**
   - **Stars:** 3,413 · **[Last commit](https://github.com/LorenzCK/OnTopReplica/commits/master):** 2023-07-05 · **Stack:** C#/.NET Framework, WinForms, Windows DWM thumbnails · **License:** Microsoft Reciprocal License, confirmed in its LICENSE file.
   - **Overlap:** always-on-top, click-through, corner positioning, transparency.
   - **Learn from:** click forwarding, saved crop regions, relative crop coordinates, and automatic switching between source windows. It mirrors existing windows rather than decoding media.

10. **[PiP-Tool](https://github.com/LionelJouin/PiP-Tool)** — **WPF floating-window reference**
    - **Stars:** 1,043 · **[Last commit](https://github.com/LionelJouin/PiP-Tool/commits/master):** 2019-08-11 · **Stack:** C#/WPF, .NET Framework 4.7, ML.NET · **License:** MIT.
    - **Overlap:** selected-window regions displayed in an always-on-top thumbnail.
    - **Learn from:** locally learned region selection. The README explicitly states that minimizing the source window stops the PiP update; it is a historical UI reference.

11. **[ElectronPlayer](https://github.com/oscartbeaumont/ElectronPlayer)** — **Historical streaming-wrapper comparison**
    - **Stars:** 505 · **[Last commit](https://github.com/oscartbeaumont/ElectronPlayer/commits/master):** 2021-10-09 · **Stack:** JavaScript, Electron, Widevine · **License:** MIT · **Archived**.
    - **Overlap:** streaming-service launcher, frameless always-on-top window, remembered startup page.
    - **Learn from:** configurable service definitions and DRM maintenance failure modes. Its README explicitly documents partial Windows support and Widevine problems. Current streaming playback **NEEDS VERIFICATION**.

12. **[SMPlayer](https://github.com/smplayer-dev/smplayer)** — **Mature playback/settings benchmark**
    - **Stars:** 1,085 · **[Last commit](https://github.com/smplayer-dev/smplayer/commits/master):** 2026-10-25 · **Stack:** C++/Qt, MPlayer or mpv backend · **License:** GPL-2.0.
    - **Overlap:** Windows playback, YouTube, subtitles, resume.
    - **Learn from:** restoring per-file audio/subtitle/volume settings, subtitle downloading, filters, and optional YouTube search. These features are documented on the [official feature page](https://www.smplayer.info/en/info).

13. **[Mpv.NET-lib](https://github.com/mysteryx93/Mpv.NET-lib)** — **Direct WPF/libmpv integration reference**
    - **Stars:** 0 · **[Last commit](https://github.com/mysteryx93/Mpv.NET-lib/commits/master):** 2020-04-01 · **Stack:** C#, libmpv C API, WPF/WinForms hosting.
    - **License:** MIT wrapper; WPF/WinForms example license files contain GPL-2.0.
    - **Overlap:** HWND embedding, event loop, playlists, seeking, frame stepping, legacy youtube-dl support.
    - **Learn from:** managed/native integration patterns. This is an old fork; modern .NET 8/libmpv compatibility **NEEDS VERIFICATION**.

14. **[Haruna](https://github.com/KDE/haruna)** — **Qt/QML/libmpv interaction reference**
    - **Stars:** 331 · **[Last commit, GitHub mirror](https://github.com/KDE/haruna/commits/master):** 2026-11-11 · **Stack:** C++, Qt/QML, KDE, libmpv · **License:** GPL-3.0-or-later for application code, with additional per-file licenses.
    - **Overlap:** native playback, online video, configurable controls.
    - **Learn from:** overlay playlists, seekbar previews, custom mpv commands, and keyword-based chapter skipping. These are documented by [KDE](https://haruna.kde.org/); Windows distribution **NEEDS VERIFICATION**.

15. **[MPC-HC — clsid2 fork](https://github.com/clsid2/mpc-hc)** — **Mature Windows feature benchmark**
    - **Stars:** 16,130 · **[Last commit](https://github.com/clsid2/mpc-hc/commits/develop):** 2026-11-10 · **Stack:** C++/MFC, DirectShow, LAV Filters · **License:** GPL-3.0.
    - **Overlap:** Windows playback, yt-dlp URLs, resume, extensive subtitle controls.
    - **Learn from:** seekbar previews, A–B repeat, subtitle search, HDR renderer selection, customizable input, and end-of-playback actions.

**Start with mpv.net, Flyleaf, OnTopReplica, and StreamHub** for implementation study; compare uView’s complete user workflow against yours. None of the reviewed documentation established a complete equivalent combining your two engines, separate remote-control window, SponsorBlock, and all-frame Chrome subtitle/playback overlay.

This was a read-only review of public repository metadata, READMEs, selected source/license files, and official product pages. Apps and DRM playback were not executed; no files were modified.

Codex session ID: 01a1ca5e-4720-75a3-a256-14f74b176bc5
Resume in Codex: codex resume 01a1ca5e-4720-75a3-a256-14f74b176bc5
