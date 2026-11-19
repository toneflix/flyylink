(function (root) {
  const flyylink = (root.FlyyLink ??= {});

  const sites = {
    'youtube.com': 'YouTube',
    'music.youtube.com': 'YouTube Music',
    'open.spotify.com': 'Spotify',
    'netflix.com': 'Netflix',
    'soundcloud.com': 'SoundCloud',
    'twitch.tv': 'Twitch',
    'vimeo.com': 'Vimeo',
    'primevideo.com': 'Prime Video',
    'disneyplus.com': 'Disney+',
    'deezer.com': 'Deezer',
    'audiomack.com': 'Audiomack',
    'boomplay.com': 'Boomplay',
    'music.apple.com': 'Apple Music',
    'tv.apple.com': 'Apple TV',
    'tidal.com': 'TIDAL',
    'dailymotion.com': 'Dailymotion',
  };

  const types = {
    mp4: 'video/mp4',
    m4v: 'video/mp4',
    webm: 'video/webm',
    mov: 'video/quicktime',
    mkv: 'video/x-matroska',
    m3u8: 'application/x-mpegURL',
    mp3: 'audio/mpeg',
    m4a: 'audio/mp4',
    aac: 'audio/aac',
    ogg: 'audio/ogg',
    oga: 'audio/ogg',
    opus: 'audio/opus',
    wav: 'audio/wav',
    flac: 'audio/flac',
  };

  flyylink.page = {
    /** A friendly name for a site, such as "YouTube", or its address without "www.". */
    siteName(hostname) {
      const host = String(hostname ?? '').toLowerCase().replace(/^(www|m)\./, '');
      if (sites[host]) return sites[host];

      const parent = Object.keys(sites).find((site) => host.endsWith(`.${site}`));

      return parent ? sites[parent] : host;
    },

    /**
     * The page's address, opening where the user is: a video at the same second on sites that take a start time,
     * otherwise scrolled to the first words on screen, which phone browsers find with a text fragment.
     */
    positionUrl(href, { seconds = 0, text = '' } = {}) {
      const url = new URL(href);
      const host = url.hostname.replace(/^(www|m)\./, '');
      const at = Math.floor(seconds);

      if (at >= 5 && (host === 'youtube.com' || host === 'youtu.be' || host === 'music.youtube.com')) {
        url.searchParams.set('t', `${at}s`);
        return url.href;
      }
      if (at >= 5 && host === 'vimeo.com') {
        url.hash = `t=${at}s`;
        return url.href;
      }
      if (at >= 5 && host === 'dailymotion.com') {
        url.searchParams.set('start', String(at));
        return url.href;
      }

      const words = this.fragmentWords(text);
      if (!words) return url.href;

      const hash = url.hash.split(':~:')[0];
      return `${url.href.split('#')[0]}${hash || '#'}:~:text=${encodeFragment(words)}`;
    },

    /** The first few words of `text`, enough to find the spot again without breaking on later changes. */
    fragmentWords(text) {
      const words = String(text ?? '').replace(/\s+/g, ' ').trim().split(' ').filter(Boolean);
      if (words.length < 3) return '';

      return words.slice(0, 8).join(' ');
    },

    /** The media type a TV needs, from the address's extension or the kind of element. */
    mimeFor(src, isAudio = false) {
      let extension = '';
      try {
        extension = new URL(src).pathname.split('.').pop().toLowerCase();
      } catch {
        extension = '';
      }

      return types[extension] ?? (isAudio ? 'audio/mpeg' : 'video/mp4');
    },

    /** Whether a TV can fetch the address itself, which blob: and data: addresses don't allow. */
    isPlayableByTv(src) {
      return /^https?:\/\//i.test(String(src ?? ''));
    },

    /** A file name for something fetched from `src`. */
    fileName(src, fallback = 'download') {
      try {
        const name = decodeURIComponent(new URL(src).pathname.split('/').pop() ?? '');
        return name.replace(/[\\/:*?"<>|]/g, '').trim() || fallback;
      } catch {
        return fallback;
      }
    },
  };

  function encodeFragment(text) {
    return encodeURIComponent(text).replace(/-/g, '%2D').replace(/,/g, '%2C').replace(/&/g, '%26');
  }
})(globalThis);
