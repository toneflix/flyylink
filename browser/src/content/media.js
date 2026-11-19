(function (root) {
  const flyylink = (root.FlyyLink ??= {});

  /**
   * Follows the audio and video elements on the page and describes the ones worth showing on a phone: anything
   * playing, or paused part way through. Short or muted clips, such as animated backgrounds, are left out.
   */
  class MediaWatcher {
    /** Called with the page's players whenever one starts, stops, changes or moves on. */
    onChange = () => {};

    #ids = new WeakMap();
    #nextId = 1;
    #elements = new Set();
    #pausedForCall = new Set();
    #timer = null;
    #lastSent = '';

    start() {
      for (const type of ['play', 'pause', 'ended', 'emptied', 'durationchange', 'seeked', 'loadedmetadata', 'volumechange']) {
        document.addEventListener(type, (event) => this.#track(event.target), true);
      }
      document.querySelectorAll('video, audio').forEach((element) => this.#track(element));
      setInterval(() => this.#report(true), 10_000);
    }

    players() {
      const site = flyylink.page.siteName(location.hostname);
      const metadata = navigator.mediaSession?.metadata;
      const actions = (document.documentElement.dataset.flyylinkActions ?? '').split(' ');

      return this.#candidates().map((element) => {
        const isMain = element === this.#main();
        const artwork = isMain ? largestArtwork(metadata) : null;

        return {
          key: String(this.#id(element)),
          site,
          title: (isMain && metadata?.title) || document.title || site,
          artist: (isMain && metadata?.artist) || '',
          album: (isMain && metadata?.album) || '',
          artwork,
          playing: !element.paused && !element.ended,
          position: Math.round(element.currentTime * 1000),
          length: Number.isFinite(element.duration) ? Math.round(element.duration * 1000) : 0,
          canSeek: Number.isFinite(element.duration) && element.seekable?.length > 0,
          canNext: isMain && actions.includes('nexttrack'),
          canPrevious: isMain && actions.includes('previoustrack'),
        };
      });
    }

    /** Presses a button a phone or computer sent for one of the players. */
    control(key, action, seekTo) {
      const element = this.#candidates().find((candidate) => String(this.#id(candidate)) === key);
      if (!element) return;

      if (seekTo != null) element.currentTime = seekTo / 1000;
      switch (action) {
        case 'PlayPause':
          if (element.paused) element.play().catch(() => {});
          else element.pause();
          break;
        case 'Play':
          element.play().catch(() => {});
          break;
        case 'Pause':
        case 'Stop':
          element.pause();
          break;
        case 'Next':
          document.dispatchEvent(new CustomEvent('flyylink:media-action', { detail: 'nexttrack' }));
          break;
        case 'Previous':
          document.dispatchEvent(new CustomEvent('flyylink:media-action', { detail: 'previoustrack' }));
          break;
      }
    }

    /** Pauses whatever plays while a call is on, and remembers what to start again afterwards. */
    pauseForCall() {
      for (const element of this.#elements) {
        if (!element.paused && !element.muted) {
          element.pause();
          this.#pausedForCall.add(element);
        }
      }
    }

    resumeAfterCall() {
      for (const element of this.#pausedForCall) {
        if (element.isConnected && element.paused) element.play().catch(() => {});
      }
      this.#pausedForCall.clear();
    }

    /** The video playing, or the longest one on the page, for "Open on phone" and "Play on TV". */
    mainVideo() {
      const videos = Array.from(document.querySelectorAll('video')).filter((video) => video.duration > 0 || !video.paused);
      return videos.find((video) => !video.paused) ?? videos.sort((a, b) => (b.duration || 0) - (a.duration || 0))[0] ?? null;
    }

    #track(element) {
      if (!(element instanceof HTMLMediaElement)) return;

      this.#elements.add(element);
      clearTimeout(this.#timer);
      this.#timer = setTimeout(() => this.#report(false), 300);
    }

    /** Elements a person would call "what's playing". */
    #candidates() {
      return Array.from(this.#elements).filter((element) => {
        if (!element.isConnected || element.muted) return false;
        if (Number.isFinite(element.duration) && element.duration < 5) return false;

        return !element.paused || element.currentTime > 0;
      });
    }

    /** The player the page's media session describes: the one playing, or else the first. */
    #main() {
      const candidates = this.#candidates();
      return candidates.find((element) => !element.paused) ?? candidates[0];
    }

    #id(element) {
      if (!this.#ids.has(element)) this.#ids.set(element, this.#nextId++);
      return this.#ids.get(element);
    }

    /** Reports changes at once, and a playing player's position every so often so a phone's progress bar stays true. */
    #report(periodic) {
      const players = this.players();
      const summary = JSON.stringify(players.map(({ position, ...rest }) => rest));
      if (summary === this.#lastSent && !(periodic && players.some((player) => player.playing))) return;

      this.#lastSent = summary;
      this.onChange(players);
    }
  }

  function largestArtwork(metadata) {
    const artwork = Array.from(metadata?.artwork ?? []);
    const size = (item) => Math.max(...String(item.sizes ?? '0x0').split(/\s+/).map((pair) => Number(pair.split('x')[0]) || 0));
    const best = artwork.sort((a, b) => size(b) - size(a))[0];
    if (!best?.src) return null;

    try {
      const url = new URL(best.src, location.href);
      return /^https?:$/.test(url.protocol) ? url.href : null;
    } catch {
      return null;
    }
  }

  flyylink.MediaWatcher = MediaWatcher;
})(globalThis);
