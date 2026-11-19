import { SignInCodes } from "../../licence/codes";
import type { LicenceStatus, LicenceStore } from "../../licence/store";
import { CommandGroup, type CommandContext } from "./group";

/** Licences. */
export class LicenceCommands extends CommandGroup {
  constructor(private readonly store: LicenceStore) {
    super();
  }

  handlers() {
    return {
      "licences:list": (context: CommandContext) => this.list(context),
      "licences:grant": (context: CommandContext) => this.grant(context),
      "licences:revoke": (context: CommandContext) => this.setStatus(context, "revoked"),
      "licences:restore": (context: CommandContext) => this.setStatus(context, "active"),
    };
  }

  /** The order id a free licence is recorded under, one per email, so granting twice changes nothing. */
  static grantOrder(email: string): string {
    return `grant:${email}`;
  }

  private async list({ input, reply }: CommandContext): Promise<void> {
    const paging = LicenceCommands.paging(input);
    const found = await this.store.findLicences(
      { search: input.text("search"), status: input.text("status"), provider: input.text("provider"), plan: input.text("plan") },
      paging,
    );

    reply.listing(
      LicenceCommands.listing(
        found,
        paging,
        [
          { key: "id", label: "Id" },
          { key: "email", label: "Email" },
          { key: "provider", label: "Source" },
          { key: "plan", label: "Plan" },
          { key: "status", label: "Status" },
          { key: "ends", label: "Ends" },
          { key: "created", label: "Created" },
        ],
        (licence) => ({
          id: licence.id,
          email: licence.email,
          provider: licence.provider,
          plan: licence.plan,
          status: licence.status,
          ends: licence.ends_at === null ? "never" : LicenceCommands.when(licence.ends_at),
          created: LicenceCommands.when(licence.created_at),
        }),
        "No licences match.",
      ),
    );
  }

  private async grant({ input, reply, now }: CommandContext): Promise<void> {
    const email = SignInCodes.normalise(input.argument("email"));
    const account = await this.store.accountFor(email, now);
    const order = LicenceCommands.grantOrder(email);

    await this.store.addLicence(account.id, "manual", order, now);
    await this.store.setLicenceStatus("manual", order, "active");

    reply.success(`${email} has a free licence.`);
  }

  private async setStatus({ input, reply }: CommandContext, status: LicenceStatus): Promise<void> {
    const licence = await this.store.findLicence(input.argument("id"));

    if (!licence) {
      reply.error(`No licence has the id ${input.argument("id")}.`);

      return;
    }

    await this.store.setLicenceStatusById(licence.id, status);

    if (status === "revoked") reply.success(`Revoked ${licence.email}'s licence. Their computers lock when their token next renews.`);
    else reply.success(`${licence.email}'s licence is active again.`);
  }
}
