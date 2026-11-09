window.distillforge = {
  scrollToFirstHighlight: function (containerId) {
    const el = document.querySelector('#' + containerId + ' mark');
    if (el) el.scrollIntoView({ block: 'center', behavior: 'smooth' });
  }
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
