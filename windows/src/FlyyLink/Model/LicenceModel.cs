using System.Reflection;
using FlyyLink.Core;
using FlyyLink.Plugins;
using FlyyLink.Shell;
using Microsoft.UI.Dispatching;

namespace FlyyLink.Model;

/// Whether this PC may use FlyyLink, and the sign-in card that unlocks it. Properties change on the UI thread.
public sealed class LicenceModel : Observable
{
    private static readonly TimeSpan SyncEvery = TimeSpan.FromHours(6);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(10);

    private readonly DispatcherQueue ui;
    private readonly LicenceStore store = new(AppPaths.Licence);
    private readonly LicenceServer server = new();
    private readonly ServerNotices notices;
    private readonly string machine = MachineId.From(HardwareId.Read());
    private readonly string? publicKey = typeof(LicenceModel).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "LicencePublicKey")?.Value;

    private LicenceStatus status = new LicenceStatus.Unlimited();
    private bool sentCode;
    private bool busy;
    private string email = "";
    private string code = "";
    private string? error;

    internal LicenceModel(DispatcherQueue ui, ToastCenter toasts)
    {
        this.ui = ui;
        notices = new ServerNotices(server, toasts);
        Evaluate();
    }

    /// Raised on the UI thread when the PC becomes locked or unlocked.
    public event Action? LockChanged;

    public LicenceStatus Status
    {
        get => status;
        private set
        {
            var wasLocked = status.IsLocked;
            if (!Set(ref status, value)) return;

            Raise(nameof(IsLocked));
            Raise(nameof(ShowsApp));
            Raise(nameof(ShowsCard));
            Raise(nameof(Title));
            Raise(nameof(Body));
            Raise(nameof(BuyTitle));
            Raise(nameof(CanRetry));
            if (wasLocked != value.IsLocked) LockChanged?.Invoke();
        }
    }

    public bool IsLocked => status.IsLocked;

    public bool ShowsApp => !ShowsCard;

    /// The sign-in card, shown in place of the panel while locked.
    public bool ShowsCard => IsLocked;

    /// Who this PC is licensed to, for the settings menu.
    public string? LicensedTo => status is LicenceStatus.Licensed { Email: var to } ? to : null;

    public bool CanSignIn => IsLocked;

    public string Title => status switch
    {
        LicenceStatus.Locked { Reason: LicenceLock.Offline } => "Go online to continue",
        LicenceStatus.Locked { Reason: LicenceLock.Removed } => "This PC was removed",
        LicenceStatus.Locked { Reason: LicenceLock.Revoked } => "Your plan has ended",
        _ => "Sign in",
    };

    public string Body => status switch
    {
        LicenceStatus.Locked { Reason: LicenceLock.Offline } => "FlyyLink needs to check your licence.",
        LicenceStatus.Locked { Reason: LicenceLock.SignedOut } => "Start your free trial on our website, then sign in here with the same email.",
        _ => "Sign in with the email you used for your plan.",
    };

    /// The website button: a trial for someone new, otherwise the plans.
    public string BuyTitle => status is LicenceStatus.Locked { Reason: LicenceLock.SignedOut } ? "Try it free" : "Buy FlyyLink";

    public bool CanRetry => status is LicenceStatus.Locked { Reason: LicenceLock.Offline };

    public bool EntersEmail => !sentCode;

    public bool EntersCode => sentCode;

    public string CodeHint => $"We sent a code to {email}.";

    public string Email
    {
        get => email;
        set => Set(ref email, value);
    }

    public string Code
    {
        get => code;
        set => Set(ref code, value);
    }

    public bool IsBusy
    {
        get => busy;
        private set
        {
            if (Set(ref busy, value)) Raise(nameof(IsIdle));
        }
    }

    public bool IsIdle => !busy;

    public string? Error
    {
        get => error;
        private set
        {
            if (Set(ref error, value)) Raise(nameof(HasError));
        }
    }

    public bool HasError => error is not null;

    /// Checks in with the server now, then every few hours, and re-reads the tokens in between.
    public void Start()
    {
        if (status is LicenceStatus.Unlimited) return;

        _ = Task.Run(async () =>
        {
            var lastSync = DateTimeOffset.MinValue;
            while (true)
            {
                if (DateTimeOffset.UtcNow - lastSync >= SyncEvery && await SyncAsync()) lastSync = DateTimeOffset.UtcNow;
                ui.TryEnqueue(Evaluate);
                await Task.Delay(CheckEvery);
            }
        });
    }

    public void UseAnotherEmail()
    {
        sentCode = false;
        Code = "";
        Error = null;
        RaiseCard();
    }

    public void Retry() => Run(async () =>
    {
        if (!await SyncAsync()) throw new LicenceServerException("offline");
    });

    public void SendCode() => Run(async () =>
    {
        await server.SendCodeAsync(email.Trim());
        sentCode = true;
        RaiseCard();
    });

    public void Activate() => Run(async () =>
    {
        var activation = await server.ActivateAsync(email.Trim(), code.Trim(), machine, Environment.MachineName);
        store.Activated(activation.Token, activation.Secret);
        ui.TryEnqueue(UseAnotherEmail);
    });

    public static void Buy() => Explorer.Open($"{LicenceServer.Site}/buy");

    public static void ManageComputers() => Explorer.Open($"{LicenceServer.Site}/account");

    /// Renews the licence. False when the server couldn't be reached.
    private async Task<bool> SyncAsync()
    {
        try
        {
            if (store.Secret is string secret) store.Renewed(await server.RefreshAsync(machine, secret));

            await notices.CheckAsync(machine, store.Secret);

            return true;
        }
        catch (LicenceServerException e)
        {
            switch (e.Code)
            {
                case "not_activated":
                    store.Locked(LicenceLock.Removed);
                    break;
                case "no_licence":
                    store.Locked(LicenceLock.Revoked);
                    break;
            }

            await notices.CheckAsync(machine, store.Secret);

            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private void Run(Func<Task> action)
    {
        IsBusy = true;
        Error = null;

        _ = Task.Run(async () =>
        {
            string? problem = null;
            try
            {
                await action();
            }
            catch (LicenceServerException e)
            {
                problem = Describe(e.Code);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                problem = Describe("offline");
            }

            ui.TryEnqueue(() =>
            {
                IsBusy = false;
                Error = problem;
                Evaluate();
            });
        });
    }

    private void Evaluate()
    {
        Status = LicenceRules.Evaluate(store.Record, machine, publicKey, DateTimeOffset.UtcNow);
        Raise(nameof(LicensedTo));
        Raise(nameof(CanSignIn));
    }

    private void RaiseCard() => ui.TryEnqueue(() =>
    {
        Raise(nameof(ShowsCard));
        Raise(nameof(ShowsApp));
        Raise(nameof(EntersEmail));
        Raise(nameof(EntersCode));
        Raise(nameof(CodeHint));
    });

    private static string Describe(string code) => code switch
    {
        "bad_email" => "Please check the email address.",
        "bad_code" => "That code didn't work. Ask for a new one.",
        "too_many_codes" => "That's a lot of codes. Please try again in an hour.",
        "no_licence" => "We couldn't find a FlyyLink plan for this email.",
        "computer_limit" => "You're already using FlyyLink on 5 computers. Remove one in your account to add this one.",
        "offline" => "We couldn't reach FlyyLink. Please check your internet connection.",
        _ => "Something went wrong. Please try again.",
    };
}
