(function (root) {
  const flyylink = (root.FlyyLink ??= {});
  const api = root.browser ?? root.chrome;

  /** The add-on's settings, kept in the browser's synced storage. */
  flyylink.settings = {
    defaults: {
      /** "auto" fills a code box by itself, "ask" shows a Fill button by it, "off" leaves codes alone. */
      codes: 'auto',
      /** Calling and texting from tel: and sms: links. */
      links: true,
      /** "always" sends every download to the phone, "ask" offers to, "off" doesn't. */
      downloads: 'ask',
      /** Tabs that play show up on the phone's media controls. */
      media: true,
      pauseForCalls: true,
    },

    async load() {
      const stored = await api.storage.sync.get(Object.keys(this.defaults));
      return { ...this.defaults, ...stored };
    },

    save(changes) {
      return api.storage.sync.set(changes);
    },

    /** Calls `onChange` with all settings whenever one changes. */
    watch(onChange) {
      api.storage.onChanged.addListener((_, area) => {
        if (area === 'sync') this.load().then(onChange);
      });
    },
  };
})(globalThis);
