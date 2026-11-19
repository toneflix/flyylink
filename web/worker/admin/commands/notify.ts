import { type Channel, type Notice, Notices } from "../../notices";
import { CommandGroup, type CommandContext } from "./group";

/** Notices to users, by email, in the computer apps, or both. */
export class NotifyCommands extends CommandGroup {
  constructor(private readonly notices: Notices) {
    super();
  }

  handlers() {
    return {
      "notify:send": (context: CommandContext) => this.send(context),
      "notify:continue": (context: CommandContext) => this.continue(context),
      "notify:list": (context: CommandContext) => this.list(context),
    };
  }

  private async send({ input, reply, actor, now }: CommandContext): Promise<void> {
    const to = Notices.audience(input.argument("to"));
    const channel = input.text("channel") as Channel;
    const subject = input.text("subject");
    const message = input.text("message");
    const link = input.text("link");

    if (!to) return void reply.error("<to> must be all, licensed, unlicensed, or emails separated by commas.");
    if (!subject || !message) return void reply.error("Give the notice a --subject and a --message.");
    if (subject.length > 150) return void reply.error("Keep --subject to 150 characters.");
    if (link && !/^https:\/\/\S+$/.test(link)) return void reply.error("--link must start with https://");

    const count = await this.notices.count(to);
    const reach = NotifyCommands.reach(channel, count, input.argument("to"));

    if (input.flag("dry-run")) return void reply.info(`This would reach ${reach}. Nothing was sent.`);
    if (!input.flag("force")) return void reply.error("Pass --force to send it.");

    const notice = await this.notices.create({ to: input.argument("to"), channel, subject, message, link: link ? { url: link, text: input.text("link-text") } : undefined }, actor, now);

    if (channel !== "email") reply.success(`Notice #${notice.id} is ready for ${NotifyCommands.reach("app", count, input.argument("to"))}. Each one shows it when it next checks in, within a few hours.`);
    if (channel !== "app") await this.emailBatch(notice, reply, now);
  }

  private async continue({ input, reply, now }: CommandContext): Promise<void> {
    const notice = await this.notices.find(Number(input.argument("id")) || 0);

    if (!notice) return void reply.error(`No notice has the id ${input.argument("id")}.`);
    if (notice.status === "sent") return void reply.info(`Notice #${notice.id} has already gone out.`);

    await this.emailBatch(notice, reply, now);
  }

  private async list({ input, reply }: CommandContext): Promise<void> {
    const paging = NotifyCommands.paging(input);

    reply.listing(
      NotifyCommands.listing(
        await this.notices.list(paging),
        paging,
        [
          { key: "id", label: "Id" },
          { key: "subject", label: "Title" },
          { key: "channel", label: "Channel" },
          { key: "to", label: "To" },
          { key: "status", label: "Status" },
          { key: "emailed", label: "Emailed" },
          { key: "at", label: "Sent" },
        ],
        (notice) => ({
          id: notice.id,
          subject: notice.subject,
          channel: notice.channel,
          to: notice.audience === "emails" ? (JSON.parse(notice.emails ?? "[]") as string[]).join(", ") : notice.audience,
          status: notice.status,
          emailed: notice.channel === "app" ? "-" : `${notice.sent} of ${notice.total}${notice.failed ? `, ${notice.failed} failed` : ""}`,
          at: `${NotifyCommands.when(notice.created_at)} by ${notice.created_by}`,
        }),
        "No notices sent yet.",
      ),
    );
  }

  /** Emails one batch and asks the CLI to run the next, until everyone has it. */
  private async emailBatch(notice: Notice, reply: CommandContext["reply"], now: number): Promise<void> {
    const after = await this.notices.sendBatch(notice, now);
    const failed = after.failed ? ` (${after.failed} failed, see the worker logs)` : "";

    if (after.status === "sent") {
      reply.success(`Notice #${after.id} emailed to ${after.sent} of ${after.total}${failed}.`);

      return;
    }

    reply.info(`Notice #${after.id}: emailed ${after.sent} of ${after.total}${failed}…`);
    reply.next("notify:continue", { id: String(after.id) });
  }

  /** Who a notice reaches, in words. Signed-out computers have no account, so app reach is described, not counted. */
  private static reach(channel: Channel, accounts: number, to: string): string {
    const email = `${accounts} account${accounts === 1 ? "" : "s"} by email`;
    const word = to.trim().toLowerCase();
    const apps = ["all", "licensed", "unlicensed"].includes(word) ? `${word} computers` : `the computers signed in as ${word}`;

    if (channel === "email") return email;
    if (channel === "app") return apps;

    return `${email} and ${apps}`;
  }
}
