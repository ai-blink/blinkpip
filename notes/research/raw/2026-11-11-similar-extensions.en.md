**PiP+, SubPIP, and SuperPiP are the closest browser-extension comparisons. ff2mpv and mpv-handler are the most useful references for connecting the extension to HybridPipPlayer’s native player.**

Ranked below by overlap with your brief, as of **November 11, 2026**. Counts are store-displayed users, not downloads or GitHub stars. Features are documented claims; no extensions were installed or playback-tested. “Potential gap” means absent from your brief, not confirmed absent from your code.

- **1. PiP+ — Picture-in-Picture Subtitles**
  
  **Users/update:** 257 Chrome users; v1.4.0, September 21, 2026. **Features:** subtitles, Bilibili danmaku, playback controls; paid window-size memory, dimming, extra speeds, volume boost, and screenshots. **Document PiP/MV3:** repository explicitly documents Document PiP with legacy video-PiP fallback; its manifest uses MV3, `activeTab`, `scripting`, and a service worker. **Potential gap:** synchronized caption/comment layers and explicit fallback behavior. **Caution:** the store calls Netflix experimental, despite broader repository claims. The inspected repository manifest is v1.3.0, so store/source parity **NEEDS VERIFICATION**. [Chrome store](https://chromewebstore.google.com/detail/ickijfnjomkjakfadhcllkfmbbelknol), [GitHub](https://github.com/paradoxie/pip-plus-public), [manifest](https://raw.githubusercontent.com/paradoxie/pip-plus-public/main/manifest.json).

- **2. SubPIP: Picture-in-Picture with Subtitles**
  
  **Users/update:** 1,000 Chrome users; v5.0.0, October 21, 2026. **Features:** caption styling, in-window controls, remembered manual caption selection, optional automatic PiP; paid translation, speech captions, external subtitles, and learning tools. **Document PiP/MV3:** its indexed developer README identifies Document PiP and Chromium 116+, but the repository returned 404 during direct verification; current implementation and MV3 architecture **NEEDS VERIFICATION**. **Potential gap:** user-assisted caption detection, imported subtitle files, translation, and automatic PiP. The README also documents an embedded-player limitation and offers opening the iframe player in its own tab. [Chrome store](https://chromewebstore.google.com/detail/subpip-picture-in-picture/cajeijlommigmipnnhemgopednbpmnjg), [indexed repository—currently unavailable](https://github.com/tmonga2208/Subpip).

- **3. SuperPiP**
  
  **Users/update:** 60,000 Chrome users; v2.14, February 25, 2026 in the directly opened listing. **Features:** subtitles for YouTube, Netflix, Disney+, and Prime Video; seek, volume, speed, next-video controls, keyboard shortcuts, and remembered window size. **Document PiP/MV3:** custom controls and caption integrations are documented; the exact API and manifest implementation **NEEDS VERIFICATION**. Its developer says the original tab must remain open. **Potential gap:** service-specific next-episode controls and caption synchronization. Its documented aspect-ratio resizing limitations provide a useful comparison for HybridPipPlayer’s aspect lock. [Chrome store](https://chromewebstore.google.com/detail/superpip-picture-in-pictu/jjjpjmbnbdjhbkclajpagjkefefnednl?hl=en), [developer site](https://superpip.cc/).

- **4. PiP Cue**
  
  **Users/update:** 176 Chrome users; v0.2.1, June 5, 2026. **Features:** standard PiP, a floating player with captions and controls, a captions-only window, A–B looping, speed control, timestamp bookmarks, optional bookmark sync, and YouTube transcripts. **Document PiP/MV3:** three modes are documented, but the API behind the enhanced modes and the manifest architecture **NEEDS VERIFICATION**. The listing says scripts are bundled without remote code. **Potential gap:** captions-only operation, A–B repeat, transcript navigation, and browser/native bookmark synchronization. [Chrome store](https://chromewebstore.google.com/detail/pip-cue-picture-in-pictur/ceheblgmggejkiggkaldojaoeomaeoin).

- **5. ff2mpv**
  
  **Users/update:** 922 Firefox users; v6.0.0, November 26, 2024. Chrome also has a linked distribution; its current count and update date **NEEDS VERIFICATION**. **Features:** send the current page or a context-menu link to mpv; Windows installation instructions and alternative native clients. **Document PiP/MV3:** no browser PiP required; the inspected MV3 manifest uses `nativeMessaging`, context menus, `activeTab`, and a Chrome service worker. **Potential gap:** a versioned native-host bridge, installation diagnostics, and clear extension/helper compatibility checks. Its README explicitly requires updating the native host alongside the extension. [Firefox store](https://addons.mozilla.org/en-US/firefox/addon/ff2mpv/), [Chrome store](https://chromewebstore.google.com/detail/ff2mpv/ephjcajbkgplkjmelpglennepbpmdpjg), [GitHub](https://github.com/woodruffw/ff2mpv), [manifest](https://raw.githubusercontent.com/woodruffw/ff2mpv/master/manifest.json).

- **6. mpv-handler + Play with MPV userscript**
  
  **Users/update:** no comparable store user count for the native handler. The release page identifies v0.4.2 as latest; its complete release date **NEEDS VERIFICATION**. **Features:** browser-to-mpv/yt-dlp launch through a registered protocol, with parameters for playback position, subtitle URL, referrer, quality, codec, profile, and cookie-file selection. **Document PiP/MV3:** uses `mpv-handler://` through a userscript rather than implementing PiP; extension behavior depends on the userscript manager. **Potential gap:** preserving timestamp, subtitles, referrer, and playback preferences during browser-to-native handoff. [GitHub and protocol documentation](https://github.com/akiirui/mpv-handler), [releases](https://github.com/akiirui/mpv-handler/releases), [userscript](https://greasyfork.org/scripts/416271-play-with-mpv).

- **7. Play with MPV — Thann**
  
  **Users/update:** current store user count and last update **NEEDS VERIFICATION**; the linked store page could not be retrieved. **Features:** Chrome extension plus a Python server; context-menu/shortcut launching into mpv. Its README supplies borderless, always-on-top, corner-positioned mpv settings. **Document PiP/MV3:** external-player architecture, with a separate local server. Current MV3 compatibility **NEEDS VERIFICATION**; the README still describes youtube-dl and older installation workflows. **Potential gap:** helper startup, connection status, and recovery when the local player service is unavailable. Treat this as an architectural reference pending maintenance verification. [GitHub](https://github.com/Thann/play-with-mpv), [linked Chrome listing](https://chromewebstore.google.com/detail/play-with-mpv/hahklcmnfgffdlchjigehabfbiigleji).

- **8. asbplayer**
  
  **Users/update:** 60,000 Chrome users; v1.22.0, October 14, 2026. **Features:** selectable subtitles, imported subtitle files, Netflix/YouTube subtitle detection, subtitle-list navigation, sentence repeat, automatic pause, and Anki cards containing text, screenshots, and audio. **Document PiP/MV3:** the examined documentation establishes subtitle overlays and browser/native integrations; current Document PiP support and Chrome manifest details **NEEDS VERIFICATION**. **Potential gap:** subtitle timing adjustment, transcript navigation, repeat-by-caption, and selectable text. Particularly valuable for subtitle architecture beyond CSS font changes. [Chrome store](https://chromewebstore.google.com/detail/asbplayer-language-learni/hkledmpjpaehamkiehglnbelcpdflcab), [GitHub](https://github.com/asbplayer/asbplayer), [settings documentation](https://docs.asbplayer.dev/docs/reference/settings/).

- **9. Substital**
  
  **Users/update:** 400,000 Chrome users; v2.10.7, August 17, 2026. **Features:** subtitle search, SRT/ZIP import, manual synchronization, size/color/background styling, and support claims covering Netflix, Disney+, Prime Video, YouTube, and others. **Document PiP/MV3:** the store explicitly records its December 2024 MV3 migration. Injection is user-triggered rather than automatic on every visited page. Document PiP support **NEEDS VERIFICATION**. **Potential gap:** subtitle import/search, delay correction, and an optional on-demand injection mode. [Chrome store](https://chromewebstore.google.com/detail/substital-add-subtitles-t/kkkbiiikppgjdiebcabomlbidfodipjg?hl=en), [Firefox store](https://addons.mozilla.org/en-US/firefox/addon/substital/).

- **10. Video Speed Controller — igrigorik**
  
  **Users/update:** 3,000,000 Chrome users; v0.11.1, August 14, 2026. **Features:** on-video controls, configurable speed increments, seek shortcuts, remappable keys, per-site defaults, remembered speed, and repositionable controls. **Document PiP/MV3:** its inspected manifest is MV3 with a service worker and storage permission; its core feature controls HTML5 media directly rather than requiring Document PiP. **Potential gap:** configurable shortcut conflicts, per-domain playback rules, remembered speed, and control positioning. This is a strong reference for making the injected playback bar adaptable across websites. [Chrome store](https://chromewebstore.google.com/detail/video-speed-controller/nffaoalbilbmmfgbnbgppjihopabppdk?hl=en-GB), [GitHub manifest](https://github.com/igrigorik/videospeed/blob/master/manifest.json).

- **11. Picture-in-Picture Extension — Google**
  
  **Users/update:** 4,000,000 Chrome users; v1.14, February 13, 2025. **Features:** toolbar/Alt+P activation of an always-on-top video window. **Document PiP/MV3:** the inspected manifest confirms MV3, a service worker, `scripting`, context menus, storage, and broad host permissions. This is the basic video-PiP comparison; the listing does not advertise custom subtitle rendering or an enhanced Document PiP interface. **Potential gap:** a lightweight baseline/fallback mode and predictable shortcut activation. Its small scope also provides a useful complexity benchmark. [Chrome store](https://chromewebstore.google.com/detail/picture-in-picture-extens/hkgfoiooedgoejojocmhlaklaeopbecg), [GitHub](https://github.com/GoogleChromeLabs/picture-in-picture-chrome-extension), [manifest](https://raw.githubusercontent.com/GoogleChromeLabs/picture-in-picture-chrome-extension/master/src/manifest.json).

- **12. Picture-in-Picture Everywhere — a.mashkovtcev**
  
  **Users/update:** 1,000 Chrome users; v1.0.3, April 9, 2026. **Features:** hover button, automatic discovery of newly added videos, largest-video selection through Alt+P, video counts, themes, and removal of `disablePictureInPicture`. **Document PiP/MV3:** the attribute handling is documented; exact PiP API and MV3 implementation **NEEDS VERIFICATION**. **Potential gap:** dynamic-page rediscovery, automatic video prioritization, and handling sites that disable ordinary PiP. Its “any website” claim was not playback-tested. [Chrome store](https://chromewebstore.google.com/detail/picture-in-picture-everyw/fdchconolfmpkjnjnmmhfklfanlnaohp).

- **13. Language Reactor**
  
  **Users/update:** 2,000,000 Chrome users; v6.0.1, October 19, 2026. **Features:** Netflix/YouTube language learning, dual-language subtitles, popup dictionaries, and precise playback controls. **Document PiP/MV3:** the listing describes integration with the original service player; Document PiP support and internal MV3 handling **NEEDS VERIFICATION**. **Potential gap:** independently styled bilingual tracks, dictionary lookup, and caption-oriented navigation. It is a subtitle interaction benchmark rather than a native-window replacement. [Chrome store](https://chromewebstore.google.com/detail/language-reactor/hoombieeljmmljlkjmnheibnpciblicm).

- **14. Dualsub — Dualsub Studio**
  
  **Users/update:** 5,316 Firefox users; v2.73.1, November 10, 2026. Current Chrome figures **NEEDS VERIFICATION**; use the Firefox figures independently. **Features:** translated dual subtitles on YouTube and other supported video websites; Firefox Android availability. **Document PiP/MV3:** the Firefox listing establishes site permissions but does not explain Document PiP or Chrome MV3 internals—both **NEEDS VERIFICATION**. **Potential gap:** simultaneous subtitle languages and separate track styling. Verify publisher identity: unrelated products use similar “DualSub” names. [Firefox store](https://addons.mozilla.org/en-US/firefox/addon/dualsub/), [Chrome listing](https://chromewebstore.google.com/detail/gnlibmlfpencglodjpgnalbdebfhpmfp).

- **15. SponsorBlock**
  
  **Users/update:** 2,000,000 Chrome users; v6.1.6, June 26, 2026. **Features:** crowdsourced sponsor/intro/outro and other segment skipping, plus submission and voting. **Document PiP/MV3:** Document PiP is unnecessary for its core timestamp-based skipping. The exact current Chrome build’s MV3 lifecycle implementation **NEEDS VERIFICATION** in this review. **Potential gap:** your brief already includes SponsorBlock in the desktop player; check whether browser playback also offers category controls, visible segment markers, manual skip/unskip, and consistent settings between engines. [Chrome store](https://chromewebstore.google.com/detail/sponsorblock-for-youtube/mnjggcdmjocbbbhaepdhchncahnbgone?hl=en), [GitHub](https://github.com/ajayyy/SponsorBlock).

The comparison suggests four implementation priorities:

- **Separate the window modes explicitly.** Document PiP supports custom HTML controls and captions, but remains tied to its opener, cannot navigate, and does not expose arbitrary window positioning. Native WPF remains relevant for your snap, click-through, and independent-window requirements. [Chrome Document PiP documentation](https://developer.chrome.com/docs/web-platform/document-picture-in-picture).
- **Treat captions as a separate integration layer.** Detecting `<video>` does not establish access to a service’s subtitle data. Compare native text tracks, site-specific caption adapters, imported subtitles, and user-assisted caption selection.
- **Make the all-frame design permission-aware.** `all_frames` injection still depends on matching URLs and frame configuration; isolated content scripts cannot directly read page JavaScript variables. Cross-frame discovery and page-player API access need deliberate messaging. [Chrome content-script documentation](https://developer.chrome.com/docs/extensions/develop/concepts/content-scripts).
- **Verify recovery after service-worker suspension.** Keep playback state recoverable through storage and reconnection rather than background globals; Chrome normally terminates idle extension workers. [Chrome service-worker lifecycle](https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle).

Only the supplied prompt and relevant skill instructions were read locally. No project implementation was inspected, and no files were modified.

Codex session ID: 01a1ca5e-54ea-7cc3-87f1-fc8e0a7d5887
Resume in Codex: codex resume 01a1ca5e-54ea-7cc3-87f1-fc8e0a7d5887
