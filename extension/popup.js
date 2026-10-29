'use strict';
document.getElementById('form-css').textContent = HPP_FORM_CSS;

chrome.storage.local.get(HPP_DEFAULTS, v => {
  const settings = { ...HPP_DEFAULTS, ...v };
  const saver = makeSettingsSaver(settings);
  const form = buildSettingsForm(document, settings, saver.change, saver.flush);
  document.getElementById('form').append(form.el);
  window.addEventListener('pagehide', saver.flush); // 팝업이 닫히기 전에 마지막 변경을 저장
});
