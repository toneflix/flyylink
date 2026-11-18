import { cleanSetting, findSetting, type SettingKey } from "../protocol/settings";
import type { LicenceStore } from "../../licence/store";
import type { Settings } from "../../settings";
import { CommandGroup, type CommandContext } from "./group";

const day = 24 * 60 * 60;

/** Runtime settings and the stats overview. */
export class SystemCommands extends CommandGroup {
  constructor(
    private readonly settings: Settings,
    private readonly store: LicenceStore,
  ) {
    super();
  }

  handlers() {
    return {
      "settings:list": (context: CommandContext) => this.list(context),
      "settings:set": (context: CommandContext) => this.set(context),
      "settings:unset": (context: CommandContext) => this.unset(context),
      stats: (context: CommandContext) => this.stats(context),
    };
  }

  private async list({ reply }: CommandContext): Promise<void> {
    const settings = await this.settings.all();

    reply.listing({
      columns: [
        { key: "key", label: "Setting" },
        { key: "value", label: "Value" },
        { key: "fallback", label: "Default" },
        { key: "label", label: "What it is" },
      ],
      rows: settings.map((setting) => ({ ...setting, value: setting.value || "-", fallback: setting.fallback || "-", changed: SystemCommands.yesNo(setting.changed) })),
      page: 1,
      lastPage: 1,
      total: settings.length,
      empty: "No settings.",
    });
  }

  private async set({ input, reply, now }: CommandContext): Promise<void> {
    const setting = findSetting(input.argument("key"))!;
    const cleaned = cleanSetting(setting, input.argument("value"));

    if ("problem" in cleaned) {
      reply.error(cleaned.problem);

      return;
    }

    await this.settings.set(setting.key as SettingKey, cleaned.value, now);

    reply.success(`${setting.key} is now ${cleaned.value || "empty"}.`);
  }

  private async unset({ input, reply }: CommandContext): Promise<void> {
    const key = input.argument("key") as SettingKey;

    await this.settings.unset(key);

    reply.success(`${key} is back to its default, ${(await this.settings.get(key)) || "empty"}.`);
  }

  private async stats({ reply, now }: CommandContext): Promise<void> {
    const totals = await this.store.totals(now - 30 * day);

    reply.fields([
      ["Accounts", `${totals.accounts} (${totals.newAccounts} in the last 30 days)`],
      ["Licensed accounts", String(totals.licensed)],
      ["Active licences", `${totals.monthly} monthly, ${totals.annual} annual, ${totals.lifetime} lifetime, ${totals.free} free (${totals.newLicences} new in the last 30 days)`],
      ["Ended subscriptions", String(totals.ended)],
      ["Revoked licences", String(totals.revoked)],
      ["Computers", `${totals.macs} Mac, ${totals.pcs} PC (${totals.activeComputers} seen in the last 30 days)`],
      ["Trials", `${totals.trials} (${totals.newTrials} started in the last 30 days)`],
    ]);
  }
}
