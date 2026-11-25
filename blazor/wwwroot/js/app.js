window.distillforge = {
  scrollToFirstHighlight: function (containerId) {
    const el = document.querySelector('#' + containerId + ' mark');
    if (el) el.scrollIntoView({ block: 'center', behavior: 'smooth' });
  }
};

// Copies text to the clipboard. Falls back to a hidden textarea where the async clipboard API is unavailable (e.g. plain http).
window.distillforge.copyText = async function (text) {
  try {
    if (navigator.clipboard && window.isSecureContext) { await navigator.clipboard.writeText(text); return true; }
  } catch (e) { /* fall through to the legacy path */ }
  const ta = document.createElement('textarea');
  ta.value = text;
  ta.setAttribute('readonly', '');
  ta.style.position = 'fixed';
  ta.style.opacity = '0';
  document.body.appendChild(ta);
  ta.select();
  let ok = false;
  try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
  document.body.removeChild(ta);
  return ok;
};

// Opens Trello's approval page in a pop-up and reports what trello-callback.html sends back, by window.opener message or by
// BroadcastChannel (same origin only). Calls OnTrelloResult(code, state, error, reason): a code and state on success; an error
// when Trello refused; or a reason of 'blocked' (pop-up blocker), 'invalid' or 'timeout'. The caller checks `state`.
// There is deliberately no "pop-up was closed" detection: sign-in sites can sever the pop-up from this window, which makes
// `popup.closed` look true while the user is still approving. The UI offers Cancel instead (cancelTrello).
window.distillforge.connectTrello = function (authUrl, dotnetRef) {
  if (window.distillforge._trelloCancel) window.distillforge._trelloCancel();
  const w = 520, h = 720;
  const left = Math.max(0, Math.round(window.screenX + (window.outerWidth - w) / 2));
  const top = Math.max(0, Math.round(window.screenY + (window.outerHeight - h) / 2));
  const popup = window.open(authUrl, 'distillforge-trello', 'width=' + w + ',height=' + h + ',left=' + left + ',top=' + top);
  if (!popup) { dotnetRef.invokeMethodAsync('OnTrelloResult', null, null, null, 'blocked'); return; }

  let done = false;
  const channel = 'BroadcastChannel' in window ? new BroadcastChannel('distillforge-trello') : null;
  const cleanup = function () {
    done = true;
    window.removeEventListener('message', onMessage);
    if (channel) channel.close();
    clearTimeout(timeout);
    window.distillforge._trelloCancel = null;
  };
  const finish = function (code, state, error, reason) {
    if (done) return;
    cleanup();
    try { popup.close(); } catch (err) { /* already gone */ }
    dotnetRef.invokeMethodAsync('OnTrelloResult', code, state, error, reason);
  };
  const handle = function (d) {
    if (done || !d || d.type !== 'distillforge-trello') return;
    if (typeof d.error === 'string' && d.error) { finish(null, null, String(d.errorDescription || d.error).slice(0, 200), null); return; }
    if (typeof d.code === 'string' && /^[A-Za-z0-9._~-]{8,2048}$/.test(d.code) && typeof d.state === 'string' && /^[A-Za-z0-9_-]{8,200}$/.test(d.state))
      finish(d.code, d.state, null, null);
    else finish(null, null, null, 'invalid');
  };
  const onMessage = function (e) { if (e.origin === window.location.origin && e.source === popup) handle(e.data); };
  window.addEventListener('message', onMessage);
  if (channel) channel.onmessage = function (e) { handle(e.data); };
  const timeout = setTimeout(function () { finish(null, null, null, 'timeout'); }, 10 * 60 * 1000);
  window.distillforge._trelloCancel = function () { if (done) return; cleanup(); try { popup.close(); } catch (err) { /* gone */ } };
};

window.distillforge.cancelTrello = function () {
  if (window.distillforge._trelloCancel) window.distillforge._trelloCancel();
};

// Firefox won't start a drag unless dataTransfer has data; Blazor can't set it, so do it here.
document.addEventListener('dragstart', function (e) {
  if (e.target && e.target.classList && e.target.classList.contains('note-wrap') && e.dataTransfer) {
    e.dataTransfer.setData('text/plain', 'note');
    e.dataTransfer.effectAllowed = 'move';
  }
});

// Reports the current text selection inside the source viewer as source-text offsets (via data-s on each run),
// so a note can be created from a passage. Calls back with nulls when there is no usable selection.
window.distillforge.watchSelection = function (containerId, dotnetRef) {
  const container = document.getElementById(containerId);
  if (!container) return null;

  const offsetOf = function (node, offset) {
    let el = node.nodeType === Node.TEXT_NODE ? node.parentElement : node;
    const run = el && el.closest ? el.closest('[data-s]') : null;
    if (!run || !container.contains(run)) return null;
    const base = parseInt(run.dataset.s, 10);
    if (node.nodeType === Node.TEXT_NODE) return base + offset;
    return base + (offset > 0 ? run.textContent.length : 0); // boundary inside the run element itself
  };

  const report = function () {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed) { dotnetRef.invokeMethodAsync('OnSelectionChanged', -1, -1, 0, 0); return; }
    const range = sel.getRangeAt(0);
    if (!container.contains(range.commonAncestorContainer)) { dotnetRef.invokeMethodAsync('OnSelectionChanged', -1, -1, 0, 0); return; }
    // Clamp ends that sit outside any run (e.g. dragged past the text) to the container's bounds.
    const runs = container.querySelectorAll('[data-s]');
    const last = runs[runs.length - 1];
    let a = offsetOf(range.startContainer, range.startOffset);
    let b = offsetOf(range.endContainer, range.endOffset);
    if (a === null) a = 0;
    if (b === null) b = last ? parseInt(last.dataset.s, 10) + last.textContent.length : 0;
    if (b <= a) { dotnetRef.invokeMethodAsync('OnSelectionChanged', -1, -1, 0, 0); return; }
    const rects = range.getClientRects();
    const r = rects.length ? rects[rects.length - 1] : range.getBoundingClientRect();
    dotnetRef.invokeMethodAsync('OnSelectionChanged', a, b, r.right, r.bottom);
  };

  const onUp = function () { setTimeout(report, 0); };
  const onKey = function (e) { if (e.shiftKey || e.key === 'Escape') setTimeout(report, 0); };
  const onScroll = function () { dotnetRef.invokeMethodAsync('OnSelectionChanged', -1, -1, 0, 0); };
  container.addEventListener('mouseup', onUp);
  container.addEventListener('keyup', onKey);
  container.addEventListener('scroll', onScroll);
  return { dispose: function () {
    container.removeEventListener('mouseup', onUp);
    container.removeEventListener('keyup', onKey);
    container.removeEventListener('scroll', onScroll);
  } };
};

// Blazor's InputFile covers the file picker. This companion gives it dropped files
// so all file types follow the same API upload path.
window.distillforge.watchFileDrop = function (element) {
  if (!element) return null;
  const prevent = function (e) { e.preventDefault(); e.stopPropagation(); };
  const enter = function (e) { prevent(e); element.classList.add('drag-over'); };
  const leave = function (e) { prevent(e); if (!element.contains(e.relatedTarget)) element.classList.remove('drag-over'); };
  const drop = function (e) {
    prevent(e);
    element.classList.remove('drag-over');
    const file = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
    if (!file) return;
    const input = element.querySelector('input[type=file]');
    if (!input) return;
    const files = new DataTransfer();
    files.items.add(file);
    input.files = files.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
  };
  element.addEventListener('dragenter', enter);
  element.addEventListener('dragover', enter);
  element.addEventListener('dragleave', leave);
  element.addEventListener('drop', drop);
  return { dispose: function () {
    element.removeEventListener('dragenter', enter);
    element.removeEventListener('dragover', enter);
    element.removeEventListener('dragleave', leave);
    element.removeEventListener('drop', drop);
  } };
};
