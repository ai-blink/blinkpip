// 창·탭 제어(content script는 직접 못 한다).
// - focusTab: "원본" 버튼이 원래 탭을 앞으로 가져온다.
// - openControls / controlsBye: 무테 PiP 옆에 컨트롤 창을 열고 닫는다. 창 열기는 사용자 클릭 허가가 필요 없다.
'use strict';

// 서비스 워커는 잠들면 메모리가 사라지므로 열린 컨트롤 창은 세션 저장소에 둔다.
const getWins = async () => (await chrome.storage.session.get('ctrlWins')).ctrlWins || {};

async function closeControls(tabId) {
  const wins = await getWins();
  const id = wins[tabId];
  if (id == null) return;
  delete wins[tabId];
  await chrome.storage.session.set({ ctrlWins: wins });
  chrome.windows.remove(id).catch(() => {});
}

chrome.runtime.onMessage.addListener((msg, sender) => {
  if (sender.id !== chrome.runtime.id) return; // 우리 확장의 content script·페이지가 보낸 것만
  const tabId = sender.tab?.id ?? msg?.tabId;   // 컨트롤 창은 탭이 아니라서 tabId를 직접 실어 보낸다
  if (tabId == null) return;
  switch (msg?.type) {
    case 'focusTab':
      chrome.tabs.update(tabId, { active: true })
        .then(t => chrome.windows.update(t.windowId, { focused: true }))
        .catch(() => {});
      break;
    case 'openControls': {
      if (!sender.tab) break;
      closeControls(tabId).then(async () => {
        const w = await chrome.windows.create({
          url: `controls.html?tab=${tabId}&frame=${sender.frameId}`,
          type: 'popup', width: 460, height: 560, // 열린 뒤 컨트롤 창이 내용에 맞게 다시 조절한다
        });
        const wins = await getWins();
        wins[tabId] = w.id;
        await chrome.storage.session.set({ ctrlWins: wins });
      }).catch(() => {});
      break;
    }
    case 'controlsBye':
      closeControls(tabId);
      break;
  }
});

chrome.tabs.onRemoved.addListener(closeControls); // 탭이 닫히면 컨트롤 창도 닫는다
