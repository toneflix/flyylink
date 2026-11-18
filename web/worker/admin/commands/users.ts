import { SignInCodes } from "../../licence/codes";
import type { Licensing } from "../../licence/licensing";
import type { Account, LicenceStore } from "../../licence/store";
import { CommandGroup, type CommandContext } from "./group";

/** Accounts and their computers. */
export class UserCommands extends CommandGroup {
  constructor(
    private readonly store: LicenceStore,
    private readonly licensing: Licensing,
  ) {
    super();
  }

  handlers() {
    return {
      "users:list": (context: CommandContext) => this.list(context),
      "users:show": (context: CommandContext) => this.show(context),
      "users:sign-out": (context: CommandContext) => this.signOut(context),
      "users:delete": (context: CommandContext) => this.delete(context),
      "computers:remove": (context: CommandContext) => this.removeComputer(context),
    };
  }

  private async list({ input, reply }: CommandContext): Promise<void> {
    const paging = UserCommands.paging(input);
    const found = await this.store.findAccounts({ search: input.text("search"), licence: input.text("licence") }, paging);

    reply.listing(
      UserCommands.listing(
        found,
        paging,
        [
          { key: "email", label: "Email" },
          { key: "licensed", label: "Licensed" },
          { key: "computers", label: "Computers" },
          { key: "joined", label: "Joined" },
        ],
        (account) => ({ email: account.email, licensed: UserCommands.yesNo(account.licensed), computers: account.computers, joined: UserCommands.when(account.created_at) }),
        "No accounts match.",
      ),
    );
  }

  private async show({ input, reply, now }: CommandContext): Promise<void> {
    const account = await this.account(input.argument("email"), reply);
    if (!account) return;

    const licences = await this.store.licences(account.id);
    const computers = (await this.store.computers(account.id)).filter((computer) => computer.removed_at === null);

    reply.fields([
      ["Email", account.email],
      ["Licensed", UserCommands.yesNo(await this.store.hasActiveLicence(account.id))],
      ["Computers", `${computers.length} of ${await this.licensing.limit()}`],
      ["Removals left", String(await this.licensing.removalsLeft(account, now))],
    ]);
    reply.table({
      title: "Licences",
      columns: [
        { key: "id", label: "Id" },
        { key: "provider", label: "Source" },
        { key: "order", label: "Order" },
        { key: "plan", label: "Plan" },
        { key: "status", label: "Status" },
        { key: "ends", label: "Ends" },
        { key: "created", label: "Created" },
      ],
      rows: licences.map((licence) => ({
        id: licence.id,
        provider: licence.provider,
        order: licence.order_id,
        plan: licence.plan,
        status: licence.status,
        ends: licence.ends_at === null ? "never" : UserCommands.when(licence.ends_at),
        created: UserCommands.when(licence.created_at),
      })),
      empty: "No licences.",
    });
    reply.table({
      title: "Computers",
      columns: [
        { key: "machine", label: "Machine" },
        { key: "name", label: "Name" },
        { key: "platform", label: "Platform" },
        { key: "activated", label: "Activated" },
        { key: "seen", label: "Last seen" },
      ],
      rows: computers.map((computer) => ({
        machine: computer.machine,
        name: computer.name,
        platform: computer.platform,
        activated: UserCommands.when(computer.activated_at),
        seen: UserCommands.when(computer.seen_at),
      })),
      empty: "No computers.",
    });
  }

  private async signOut({ input, reply }: CommandContext): Promise<void> {
    const account = await this.account(input.argument("email"), reply);
    if (!account) return;

    const ended = await this.store.endSessions(account.id);

    reply.success(`Ended ${ended} website session${ended === 1 ? "" : "s"} for ${account.email}.`);
  }

  private async delete({ input, reply }: CommandContext): Promise<void> {
    if (!input.flag("force")) {
      reply.error("Pass --force to delete the account.");

      return;
    }

    const account = await this.account(input.argument("email"), reply);
    if (!account) return;

    await this.store.deleteAccount(account);

    reply.success(`Deleted ${account.email} with its licences, computers and sessions.`);
  }

  private async removeComputer({ input, reply, now }: CommandContext): Promise<void> {
    const account = await this.account(input.argument("email"), reply);
    if (!account) return;

    const machine = input.argument("machine");
    const computer = (await this.store.computers(account.id)).find((c) => c.machine === machine && c.removed_at === null);

    if (!computer) {
      reply.error(`${account.email} has no computer ${machine}.`);

      return;
    }

    await this.store.removeComputer(account.id, machine, now);

    reply.success(`Removed ${computer.name} from ${account.email}. It locks when its token next renews.`);
  }

  private async account(email: string, reply: CommandContext["reply"]): Promise<Account | null> {
    const account = await this.store.findAccount(SignInCodes.normalise(email));

    if (!account) reply.error(`No account has the email ${email}.`);

    return account;
  }
}
