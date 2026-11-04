window.distillforge = {
  scrollToFirstHighlight: function (containerId) {
    const el = document.querySelector('#' + containerId + ' mark');
    if (el) el.scrollIntoView({ block: 'center', behavior: 'smooth' });
  }
};
