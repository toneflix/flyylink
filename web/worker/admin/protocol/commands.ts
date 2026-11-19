import { settingKeys } from "./settings";

/** A positional argument of an admin command. */
export interface AdminArgument {
  name: string;
  /** What a prompt asks for, such as "Email". */
  label: string;
  description: string;
  /** Defaults to true. */
  required?: boolean;
  choices?: readonly string[];
}

/** An option of an admin command. `flag` takes no value; `number` and `text` take one. */
export interface AdminOption {
  name: string;
  label: string;
  description: string;
  type: "flag" | "text" | "number";
  default?: string | number;
  choices?: readonly string[];
  /** Asked for in turn by a guided run, rather than offered among the optional settings. */
  ask?: boolean;
  /** False for options a guided run leaves out, such as paging, which it offers as Next and Previous. */
  guided?: boolean;
}

/** Something to do with one row of a listing, by running another command. `{field}` is replaced with that field of the row. */
export interface AdminAction {
  label: string;
  command: string;
  arguments?: Record<string, string>;
  options?: Record<string, string | boolean>;
  /** Only offered for rows whose fields have these values. */
  when?: Record<string, string>;
  /** A question the operator must answer yes to before the command runs. */
  confirm?: string;
}

export interface AdminCommand {
  name: string;
  /** The name a person would give the command, as the wizard lists it. */
  title: string;
  description: string;
  arguments: readonly AdminArgument[];
  options: readonly AdminOption[];
  /** A question a guided run asks before running, with `{argument}` filled in. Yes sets the flag `option`, which the command needs. */
  confirm?: { question: string; option: string };
  /** A guided run given no options offers these options to choose from. */
  chooseOptions?: boolean;
  /** For a listing: how a guided run labels a row (`{field}` is replaced with the row's field), and what can be done with it. */
  results?: { label: string; actions: readonly AdminAction[] };
}

const page: AdminOption = { name: "page", label: "Page", description: "The page to show", type: "number", default: 1, guided: false };

const limit = (things: string): AdminOption => ({
  name: "limit",
  label: "Results per page",
  description: `How many ${things} to show on each page`,
  type: "number",
  default: 20,
  guided: false,
});

const search = (things: string): AdminOption => ({
  name: "search",
  label: "Email contains",
  description: `Only ${things} whose email contains this`,
  type: "text",
});

const force = (what: string): AdminOption => ({ name: "force", label: `${what} without asking`, description: `${what} without asking first`, type: "flag" });

const email: AdminArgument = { name: "email", label: "Email", description: "The account's email" };
const licence: AdminArgument = { name: "id", label: "Licence id", description: "The licence id" };
const machine: AdminArgument = { name: "machine", label: "Machine id", description: "The computer's machine id" };

/** The commands operators may run through `POST /api/admin/run`, and the input each one takes. */
export const AdminCommands = [
  {
    name: "users:list",
    title: "List users",
    description: "List accounts, newest first",
    chooseOptions: true,
    arguments: [],
    options: [
      search("accounts"),
      { name: "licence", label: "Licence", description: "Only accounts with or without a licence", type: "text", choices: ["with", "without"] },
      page,
      limit("accounts"),
    ],
    results: {
      label: "{email}  {licensed}",
      actions: [
        { label: "Show details", command: "users:show", arguments: { email: "{email}" } },
        { label: "Give a free licence", command: "licences:grant", arguments: { email: "{email}" }, when: { licensed: "no" } },
        { label: "Send them a notice", command: "notify:send", arguments: { to: "{email}" } },
        { label: "Sign out of the website", command: "users:sign-out", arguments: { email: "{email}" } },
        { label: "Delete the account", command: "users:delete", arguments: { email: "{email}" } },
      ],
    },
  },
  {
    name: "users:show",
    title: "Show a user",
    description: "Show an account with its licences and computers",
    arguments: [email],
    options: [],
  },
  {
    name: "users:sign-out",
    title: "Sign a user out",
    description: "End an account's website sessions",
    arguments: [email],
    options: [],
  },
  {
    name: "users:delete",
    title: "Delete a user",
    description: "Delete an account with its licences, computers and sessions",
    arguments: [email],
    options: [force("Delete")],
    confirm: { question: "Delete {email} with its licences and computers? This can't be undone.", option: "force" },
  },
  {
    name: "computers:remove",
    title: "Remove a computer",
    description: "Remove a computer from an account, even past its monthly limit",
    arguments: [email, machine],
    options: [],
  },
  {
    name: "licences:list",
    title: "List licences",
    description: "List licences, newest first",
    chooseOptions: true,
    arguments: [],
    options: [
      search("licences"),
      { name: "status", label: "Status", description: "Only licences with this status", type: "text", choices: ["active", "ended", "revoked"] },
      { name: "provider", label: "Source", description: "Only licences from this source", type: "text", choices: ["paddle", "manual"] },
      { name: "plan", label: "Plan", description: "Only licences on this plan", type: "text", choices: ["monthly", "annual", "lifetime"] },
      page,
      limit("licences"),
    ],
    results: {
      label: "{email}  {plan}  {provider}  {status}",
      actions: [
        { label: "Show the user", command: "users:show", arguments: { email: "{email}" } },
        {
          label: "Revoke it",
          command: "licences:revoke",
          arguments: { id: "{id}" },
          when: { status: "active" },
          confirm: "Revoke {email}'s licence? Their computers lock when their token next renews.",
        },
        { label: "Restore it", command: "licences:restore", arguments: { id: "{id}" }, when: { status: "revoked" } },
      ],
    },
  },
  {
    name: "licences:grant",
    title: "Give a free licence",
    description: "Give an email a licence without a purchase, making the account if needed",
    arguments: [email],
    options: [],
  },
  {
    name: "licences:revoke",
    title: "Revoke a licence",
    description: "Revoke a licence; its computers lock when their token next renews",
    arguments: [licence],
    options: [],
  },
  {
    name: "licences:restore",
    title: "Restore a licence",
    description: "Make a revoked licence active again",
    arguments: [licence],
    options: [],
  },
  {
    name: "settings:list",
    title: "Show settings",
    description: "Show the settings and their values",
    arguments: [],
    options: [],
    results: {
      label: "{key}  {value}",
      actions: [
        { label: "Change it", command: "settings:set", arguments: { key: "{key}" } },
        { label: "Go back to the default", command: "settings:unset", arguments: { key: "{key}" }, when: { changed: "yes" } },
      ],
    },
  },
  {
    name: "settings:set",
    title: "Change a setting",
    description: "Change a setting",
    arguments: [
      { name: "key", label: "Setting", description: "The setting", choices: settingKeys },
      { name: "value", label: "New value", description: "The new value" },
    ],
    options: [],
  },
  {
    name: "settings:unset",
    title: "Reset a setting",
    description: "Put a setting back to its default",
    arguments: [{ name: "key", label: "Setting", description: "The setting", choices: settingKeys }],
    options: [],
  },
  {
    name: "notify:send",
    title: "Notify users",
    description: "Send a notice by email, to the Mac and Windows apps, or both",
    arguments: [
      {
        name: "to",
        label: "Send to (all, licensed, unlicensed, or emails separated by commas)",
        description: "all, licensed, unlicensed, or emails separated by commas",
      },
    ],
    options: [
      {
        name: "channel",
        label: "Send it by",
        description: "email, app (the Mac and Windows apps, never phones) or both",
        type: "text",
        choices: ["email", "app", "both"],
        default: "email",
        ask: true,
      },
      { name: "subject", label: "Title", description: "The email's subject, or the app notification's title", type: "text", ask: true },
      { name: "message", label: "Message (\\n starts a new paragraph)", description: "The message; \\n starts a new paragraph", type: "text", ask: true },
      { name: "link", label: "Button link", description: "A link to show as a button under the message", type: "text" },
      { name: "link-text", label: "Button text", description: "The button's text", type: "text", default: "Open" },
      { name: "dry-run", label: "Only count the recipients", description: "Count the recipients without sending anything", type: "flag" },
      force("Send"),
    ],
    chooseOptions: true,
    confirm: { question: "Send this notice to {to}?", option: "force" },
  },
  {
    name: "notify:continue",
    title: "Resume a notice",
    description: "Email a notice's next batch, for one that stopped part way",
    arguments: [{ name: "id", label: "Notice id", description: "The id notify:list shows" }],
    options: [],
  },
  {
    name: "notify:list",
    title: "List notices",
    description: "List the notices sent to users, newest first",
    chooseOptions: true,
    arguments: [],
    options: [page, limit("notices")],
    results: {
      label: "#{id}  {subject}  {status}",
      actions: [{ label: "Send the rest", command: "notify:continue", arguments: { id: "{id}" }, when: { status: "sending" } }],
    },
  },
  {
    name: "operators:add",
    title: "Add an operator",
    description: "Allow an operator key to run admin commands",
    arguments: [
      { name: "name", label: "Operator name", description: "A name for the operator, such as obi" },
      { name: "key", label: "Their public key", description: "The public key flyylink-cli login printed" },
    ],
    options: [],
  },
  {
    name: "operators:list",
    title: "List operators",
    description: "List the operators allowed to run admin commands",
    arguments: [],
    options: [page, limit("operators")],
    results: {
      label: "{name}  {status}",
      actions: [
        {
          label: "Revoke their key",
          command: "operators:revoke",
          arguments: { name: "{name}" },
          when: { status: "active" },
          confirm: "Revoke {name}'s key? Their flyylink-cli stops working at once.",
        },
      ],
    },
  },
  {
    name: "operators:revoke",
    title: "Revoke an operator",
    description: "Revoke an operator's key",
    arguments: [{ name: "name", label: "Operator name", description: "The operator name" }],
    options: [],
  },
  {
    name: "audit:list",
    title: "Show the audit log",
    description: "List the admin commands operators ran, newest first",
    chooseOptions: true,
    arguments: [],
    options: [{ name: "actor", label: "Operator name", description: "Only commands this operator ran", type: "text" }, page, limit("entries")],
  },
  {
    name: "stats",
    title: "Show stats",
    description: "Show accounts, licences and computers",
    arguments: [],
    options: [],
  },
] as const satisfies readonly AdminCommand[];

export type AdminCommandName = (typeof AdminCommands)[number]["name"];

/** The admin command with this name, or null when there is none. */
export function findAdminCommand(name: string): AdminCommand | null {
  return AdminCommands.find((command) => command.name === name) ?? null;
}
