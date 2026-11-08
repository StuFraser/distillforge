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
