export const site = {
  name: "FlyyLink",
  tagline: "Your Android phone and your computer, finally on speaking terms.",
  description:
    "Your Android phone on your Mac or PC: notifications, texts, calls, files, photos, screen mirroring and clipboard. And a remote for your TV.",
  url: "https://flyylink.toneflix.net",
  playStore: "https://play.google.com/store/apps/details?id=com.toneflix.flyylink",
  amazonAppstore: "https://www.amazon.com/gp/mas/dl/android?p=com.toneflix.flyylink",
  legalEntity: "Toneflix Technologies Limited",
  legalAddress: "197, Ahoada Road, Omoku, Rivers State, Nigeria",
  contactEmail: "support@toneflix.net",
  jurisdiction: "the Federal Republic of Nigeria",
  policyDate: "14 November 2026",
  /** From the TRIAL_DAYS build variable, which the deploy also hands to the worker. */
  trialDays: Number(process.env.NEXT_PUBLIC_TRIAL_DAYS) || 14,
};
