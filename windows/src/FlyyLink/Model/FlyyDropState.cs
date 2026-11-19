using FlyyLink.Core;
using FlyyLink.Icons;

namespace FlyyLink.Model;

/// A FlyyDrop someone nearby wants to send, waiting for the user to answer.
public sealed record FlyyDropAsk(FlyyDrop.Offer Offer, string DeviceName, DeviceType DeviceType, bool IsPaired)
{
    private static readonly HashSet<string> ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".heif"];

    public string Id => Offer.Id;

    public string Title => $"{DeviceName} wants to FlyyDrop";

    public bool IsImages => Offer.Items.Count > 0 && Offer.Items.All(item => ImageExtensions.Contains(Path.GetExtension(item.Name).ToLowerInvariant()));

    /// "3 photos · 12 MB", "report.pdf · 2 MB" or the link itself.
    public string Summary
    {
        get
        {
            var items = Offer.Items;
            if (items.Count == 0) return Offer.Text?.Split('\n').FirstOrDefault() ?? "";

            var what = items.Count == 1 ? items[0].SafeName : $"{items.Count} {(IsImages ? "photos" : "files")}";

            return $"{what} · {Bytes.Text(Offer.TotalSize)}";
        }
    }

    /// Where it lands once accepted, and whether the sender is paired.
    public string Hint => (Offer.Items.Count > 0, IsPaired) switch
    {
        (true, true) => "Saved to Downloads",
        (true, false) => "Saved to Downloads · Not paired",
        (false, true) => "",
        (false, false) => "Not paired",
    };

    public Lucide Icon => Offer.Items.Count == 0 ? Lucide.Link : IsImages ? Lucide.Image : Lucide.File;
}

public enum FlyyDropStage { Waiting, Sending, Sent, Declined, NoAnswer, Failed }

/// How a FlyyDrop to one device is going.
public sealed record FlyyDropSend(string Id, FlyyDropStage Stage, TransferProgress.Snapshot? Progress = null, FlyyDrop.Reason? Reason = null)
{
    public bool IsFinished => Stage is not (FlyyDropStage.Waiting or FlyyDropStage.Sending);

    /// A short line for the device's row.
    public string Status => Stage switch
    {
        FlyyDropStage.Waiting => "Waiting for them to accept…",
        FlyyDropStage.Sending => Progress is { } progress ? $"Sending · {(int)(progress.Fraction * 100)}%" : "Sending…",
        FlyyDropStage.Sent => "Sent",
        FlyyDropStage.NoAnswer => "They didn't answer",
        FlyyDropStage.Failed => "It didn't finish. Please try again.",
        _ => Reason switch
        {
            FlyyDrop.Reason.Unavailable => "They're not taking FlyyDrops right now",
            FlyyDrop.Reason.Busy => "They're busy with another request",
            _ => "Declined",
        },
    };
}
