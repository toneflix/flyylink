using FlyyLink.Core;
using FlyyLink.Icons;
using FlyyLink.Plugins;

namespace FlyyLink.Model;

/// "Play on TV": finds DLNA TVs, serves the chosen file to one and controls what it plays.
/// Mirrors PlayOnTv.kt and PlayOnTvModel.swift.
public sealed class PlayOnTvState : Observable, IDisposable
{
    private readonly TvList seen = new();
    private MediaServer? server;
    private TvPlayer? player;
    private CancellationTokenSource? watcher;

    private List<TvRenderer> tvs = [];
    private bool searching;
    private MediaFile? picked;
    private MediaFile? playing;
    private TvRenderer? tv;
    private CastState state = CastState.Loading;
    private long position;
    private long duration;
    private string? error;

    public List<TvRenderer> Tvs { get => tvs; private set { if (Set(ref tvs, value)) RaiseLists(); } }

    public bool Searching { get => searching; private set { if (Set(ref searching, value)) RaiseLists(); } }

    /// A file picked to play, waiting for a TV to be chosen.
    public MediaFile? Picked { get => picked; private set { if (Set(ref picked, value)) RaiseModes(); } }

    /// The card shows only while a TV is around, or while something is picked or playing.
    public bool IsShown => playing is not null || picked is not null || tvs.Count > 0;

    public bool IsIdle => playing is null && picked is null;

    public bool IsPicking => playing is null && picked is not null;

    public bool IsPlaying => playing is not null;

    public string PickedName => picked?.Name ?? "";

    public Lucide PickedIcon => KindIcon(picked?.Mime);

    public string PickStatus => Tvs.Count > 0 ? "Pick a TV" : Searching ? "Looking for TVs…" : "No TVs found";

    public bool ShowsSearchAgain => Tvs.Count == 0 && !Searching;

    public string IdleStatus => error ?? "Photos, videos and music";

    public string PlayingName => playing?.Name ?? "";

    public string PlayingStatus => tv is null ? "" : $"On {tv.Name}";

    public Lucide PlayingIcon => KindIcon(playing?.Mime);

    public bool IsLoading => playing is not null && state == CastState.Loading;

    public bool ShowsPlayPause => playing is not null && state != CastState.Loading && CastKinds.Of(playing.Mime) != CastKind.Photo;

    public Lucide PlayPauseIcon => state == CastState.Paused ? Lucide.Play : Lucide.Pause;

    public bool HasDuration => duration > 0;

    public double Progress => duration > 0 ? Math.Clamp((double)position / duration, 0, 1) : 0;

    public async void Search()
    {
        if (Searching) return;

        Searching = true;
        try
        {
            Tvs = seen.Update(await TvFinder.SearchAsync());
        }
        catch (System.Net.Sockets.SocketException)
        {
            Tvs = [];
        }
        finally
        {
            Searching = false;
        }
    }

    /// Searches again every 30 seconds, so the list follows TVs as they come and go.
    public async void KeepSearching()
    {
        while (true)
        {
            Search();
            await Task.Delay(TimeSpan.FromSeconds(30));
        }
    }

    public void Pick(string path)
    {
        var mime = MimeTypes.Of(path);
        if (CastKinds.Of(mime) is null) return;

        error = null;
        Raise(nameof(IdleStatus));
        Picked = new MediaFile(path, mime);
        Search();
    }

    public void CancelPick() => Picked = null;

    public async void Play(TvRenderer target)
    {
        if (picked is not MediaFile file) return;

        watcher?.Cancel();
        var previous = player;
        var tvPlayer = new TvPlayer(target);
        player = tvPlayer;
        tv = target;
        playing = file;
        state = CastState.Loading;
        position = 0;
        duration = 0;
        Picked = null;
        RaisePlayback();

        try
        {
            if (previous is not null) await previous.StopAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ProtocolException)
        {
        }

        try
        {
            server ??= new MediaServer();
            server.Clear();
            var url = server.Share(file, target.Host);

            await tvPlayer.PlayAsync(url, file.Name, file.Mime, file.Size);
            Watch(tvPlayer);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ProtocolException or System.Net.Sockets.SocketException or IOException)
        {
            End();
            error = $"Couldn't play on {target.Name}";
            Raise(nameof(IdleStatus));
        }
    }

    public void TogglePause()
    {
        if (player is not TvPlayer tvPlayer || playing is null) return;

        var paused = state == CastState.Paused;
        state = paused ? CastState.Playing : CastState.Paused;
        RaisePlayback();

        _ = Quietly(paused ? tvPlayer.ResumeAsync() : tvPlayer.PauseAsync());
    }

    public void Seek(double fraction)
    {
        if (player is not TvPlayer tvPlayer || duration <= 0) return;

        position = (long)(Math.Clamp(fraction, 0, 1) * duration);
        RaisePlayback();

        _ = Quietly(tvPlayer.SeekAsync(position));
    }

    public void Stop()
    {
        var tvPlayer = player;

        End();
        if (tvPlayer is not null) _ = Quietly(tvPlayer.StopAsync());
    }

    public void Dispose()
    {
        watcher?.Cancel();
        server?.Dispose();
    }

    /// Follows the TV about once a second, and ends the session once it stops on its own.
    private async void Watch(TvPlayer tvPlayer)
    {
        var cancel = new CancellationTokenSource();
        watcher = cancel;
        var started = false;

        while (!cancel.IsCancellationRequested && player == tvPlayer)
        {
            var current = await Quietly(tvPlayer.StateAsync());
            var at = await Quietly(tvPlayer.PositionAsync());
            if (cancel.IsCancellationRequested || player != tvPlayer) return;

            if (current is CastState.Playing or CastState.Paused) started = true;

            if (started && current == CastState.Stopped && CastKinds.Of(playing?.Mime) != CastKind.Photo)
            {
                End();
                return;
            }

            state = current ?? state;
            position = at?.Position ?? position;
            if (at is { Duration: > 0 }) duration = at.Duration;
            RaisePlayback();

            try { await Task.Delay(1000, cancel.Token); } catch (TaskCanceledException) { return; }
        }
    }

    private void End()
    {
        watcher?.Cancel();
        watcher = null;
        player = null;
        tv = null;
        playing = null;
        server?.Clear();
        RaiseModes();
        RaisePlayback();
    }

    private static async Task Quietly(Task task)
    {
        try { await task; } catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ProtocolException) { }
    }

    private static async Task<T?> Quietly<T>(Task<T?> task)
    {
        try { return await task; } catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ProtocolException) { return default; }
    }

    private static Lucide KindIcon(string? mime) => CastKinds.Of(mime) switch
    {
        CastKind.Photo => Lucide.Image,
        CastKind.Music => Lucide.Music,
        _ => Lucide.Film,
    };

    private void RaiseLists()
    {
        Raise(nameof(IsShown));
        Raise(nameof(PickStatus));
        Raise(nameof(ShowsSearchAgain));
    }

    private void RaiseModes()
    {
        Raise(nameof(IsShown));
        Raise(nameof(IsIdle));
        Raise(nameof(IsPicking));
        Raise(nameof(IsPlaying));
        Raise(nameof(PickedName));
        Raise(nameof(PickedIcon));
    }

    private void RaisePlayback()
    {
        RaiseModes();
        Raise(nameof(PlayingName));
        Raise(nameof(PlayingStatus));
        Raise(nameof(PlayingIcon));
        Raise(nameof(IsLoading));
        Raise(nameof(ShowsPlayPause));
        Raise(nameof(PlayPauseIcon));
        Raise(nameof(HasDuration));
        Raise(nameof(Progress));
    }
}
