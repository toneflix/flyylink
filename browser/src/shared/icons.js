(function (root) {
  const flyylink = (root.FlyyLink ??= {});

  const lucide = (body) =>
    `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${body}</svg>`;

  /** The FlyyLink mark and the Lucide icons (ISC licence) the add-on draws. */
  flyylink.icons = {
    mark: `<svg viewBox="0 0 100 100" width="20" height="20" aria-hidden="true"><defs><linearGradient id="flyylink-mark" gradientUnits="userSpaceOnUse" x1="20.95" y1="100" x2="47.22" y2="0"><stop offset="0" stop-color="#00afff"/><stop offset="1" stop-color="#00eaff"/></linearGradient></defs><path fill="url(#flyylink-mark)" d="M52.78,0 L52.89,31.76 C53,37.4 52.37,49.65 54.54,62.47 L61.58,62.47 L61.58,58.82 L68.37,67.18 L55.46,67.18 C55.58,67.7 55.7,68.22 55.83,68.74 L68.28,68.74 L68.28,73.44 L57.15,73.44 C60.71,84.58 67.21,94.81 79.05,99.99 L79.05,61.36 C79.05,36.64 77.84,8.97 52.78,0 Z M42.85,73.44 L38.7,73.44 L38.7,77.1 L31.9,68.74 L44.18,68.74 C44.3,68.22 44.43,67.7 44.54,67.18 L31.79,67.18 L31.79,62.47 L45.46,62.47 C47.63,49.66 47,37.4 47.11,31.77 L47.22,0.01 C22.16,8.98 20.95,36.65 20.95,61.37 L20.95,100 C32.8,94.81 39.29,84.58 42.85,73.44 Z"/></svg>`,
    phone: lucide('<path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7A2 2 0 0 1 22 16.92z" />'),
    message: lucide('<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />'),
    send: lucide('<path d="M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z" /><path d="m21.854 2.147-10.94 10.939" />'),
    x: lucide('<path d="M18 6 6 18" /><path d="m6 6 12 12" />'),
    check: lucide('<path d="M20 6 9 17l-5-5" />'),
    key: lucide('<path d="M2.586 17.414A2 2 0 0 0 2 18.828V21a1 1 0 0 0 1 1h3a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h1a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h.172a2 2 0 0 0 1.414-.586l.814-.814a6.5 6.5 0 1 0-4-4z" /><circle cx="16.5" cy="7.5" r=".5" fill="currentColor" />'),
    smartphone: lucide('<rect width="14" height="20" x="5" y="2" rx="2" ry="2" /><path d="M12 18h.01" />'),
    tablet: lucide('<rect width="16" height="20" x="4" y="2" rx="2" ry="2" /><line x1="12" x2="12.01" y1="18" y2="18" />'),
    tv: lucide('<rect width="20" height="15" x="2" y="7" rx="2" ry="2" /><polyline points="17 2 12 7 7 2" />'),
    cast: lucide('<path d="M2 8V6a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-6" /><path d="M2 12a9 9 0 0 1 8 8" /><path d="M2 16a5 5 0 0 1 4 4" /><line x1="2" x2="2.01" y1="20" y2="20" />'),
    externalLink: lucide('<path d="M15 3h6v6" /><path d="M10 14 21 3" /><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" />'),
    download: lucide('<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><polyline points="7 10 12 15 17 10" /><line x1="12" x2="12" y1="15" y2="3" />'),
    copy: lucide('<rect width="14" height="14" x="8" y="8" rx="2" ry="2" /><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" />'),
    settings: lucide('<path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z" /><circle cx="12" cy="12" r="3" />'),
    file: lucide('<path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z" /><path d="M14 2v4a2 2 0 0 0 2 2h4" />'),
    laptop: lucide('<path d="M20 16V7a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v9m16 0H4m16 0 1.28 2.55a1 1 0 0 1-.9 1.45H3.62a1 1 0 0 1-.9-1.45L4 16" />'),
    music: lucide('<path d="M9 18V5l12-2v13" /><circle cx="6" cy="18" r="3" /><circle cx="18" cy="16" r="3" />'),
    alert: lucide('<circle cx="12" cy="12" r="10" /><line x1="12" x2="12" y1="8" y2="12" /><line x1="12" x2="12.01" y1="16" y2="16" />'),
    plug: lucide('<path d="M12 22v-5" /><path d="M9 8V2" /><path d="M15 8V2" /><path d="M18 8v5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V8Z" />'),
  };
})(globalThis);
