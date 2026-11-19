export interface SettingDefinition {
  key: string;
  label: string;
  type: "number" | "emails";
  min?: number;
  max?: number;
}

/** The settings operators can change at runtime. Each one falls back to the worker's own default when unset. */
export const SettingDefinitions = [
  { key: "max_computers", label: "Computers one licence covers", type: "number", min: 1, max: 100 },
  { key: "removals_per_month", label: "Computers an account can remove in 30 days", type: "number", min: 0, max: 100 },
  { key: "review_emails", label: "Reviewer emails that sign in with REVIEW_CODE, comma separated", type: "emails" },
] as const satisfies readonly SettingDefinition[];

export type SettingKey = (typeof SettingDefinitions)[number]["key"];

export const settingKeys: readonly string[] = SettingDefinitions.map((setting) => setting.key);

export function findSetting(key: string): SettingDefinition | null {
  return SettingDefinitions.find((setting) => setting.key === key) ?? null;
}

/** Checks and tidies a value for a setting. Returns the value to store, or a message saying what is wrong. */
export function cleanSetting(setting: SettingDefinition, value: string): { value: string } | { problem: string } {
  if (setting.type === "number") {
    const number = Number(value.trim());
    const { min = 0, max = Number.MAX_SAFE_INTEGER } = setting;

    if (!/^\d+$/.test(value.trim()) || number < min || number > max) {
      return { problem: `${setting.key} must be a whole number from ${min} to ${max}.` };
    }

    return { value: String(number) };
  }

  const emails = value
    .split(",")
    .map((email) => email.trim().toLowerCase())
    .filter(Boolean);
  const wrong = emails.find((email) => !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email));

  if (wrong) return { problem: `${wrong} is not an email address.` };

  return { value: emails.join(",") };
}
