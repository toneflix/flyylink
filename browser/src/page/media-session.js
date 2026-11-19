// Runs in the page itself, where the site's media session handlers live. It notes which buttons the site handles
// (next, previous, seek) on the page's root element, and presses them when the add-on asks with a
// "flyylink:media-action" event.
(function () {
  if (window.__flyylinkMediaSession || !('mediaSession' in navigator)) return;
  window.__flyylinkMediaSession = true;

  const handlers = new Map();
  const original = MediaSession.prototype.setActionHandler;

  function publish() {
    document.documentElement.dataset.flyylinkActions = Array.from(handlers.keys()).join(' ');
  }

  MediaSession.prototype.setActionHandler = function (action, handler) {
    if (handler) handlers.set(action, handler);
    else handlers.delete(action);
    publish();

    return original.call(this, action, handler);
  };

  document.addEventListener('flyylink:media-action', (event) => {
    const [action, seconds] = String(event.detail ?? '').split(' ');
    const handler = handlers.get(action);
    if (!handler) return;

    try {
      handler(action === 'seekto' ? { action, seekTime: Number(seconds), fastSeek: false } : { action });
    } catch {
      // The site's own handler failed; nothing to do.
    }
  });
})();
