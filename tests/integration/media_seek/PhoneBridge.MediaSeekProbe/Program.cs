using System.Text.Json;
using Windows.Media.Core;
using Windows.Media.Playback;

if (args.Length != 1) throw new ArgumentException("Expected one MP4 path.");
var path = Path.GetFullPath(args[0]);
if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("A local or mounted MP4 file is required.");

var positions = new[] { 30d, 180d, 360d, 540d };
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var failed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
var frameCount = 0L;
using var source = MediaSource.CreateFromUri(new Uri(path));
using var player = new MediaPlayer
{
    IsMuted = true,
    IsVideoFrameServerEnabled = true,
    Source = source
};
player.CommandManager.IsEnabled = false;
player.VideoFrameAvailable += (_, _) => Interlocked.Increment(ref frameCount);
player.MediaOpened += (_, _) => opened.TrySetResult();
player.MediaFailed += (_, error) => failed.TrySetResult(error.ExtendedErrorCode.HResult);
player.Play();

using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
{
    var winner = await Task.WhenAny(opened.Task, failed.Task, Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token));
    if (winner == failed.Task) throw new InvalidOperationException($"Media open failed: 0x{(await failed.Task):x8}");
    if (winner != opened.Task) throw new TimeoutException("Media did not open in time.");
}

var session = player.PlaybackSession;
var duration = session.NaturalDuration.TotalSeconds;
if (!session.CanSeek || duration <= 545) throw new InvalidOperationException("Media is not seekable or has an invalid duration.");
var results = new List<object>();
foreach (var requested in positions)
{
    player.Pause();
    session.Position = TimeSpan.FromSeconds(requested);
    var framesBefore = Interlocked.Read(ref frameCount);
    player.Play();
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while ((session.PlaybackState != MediaPlaybackState.Playing ||
            Math.Abs(session.Position.TotalSeconds - requested) > 8 ||
            Interlocked.Read(ref frameCount) == framesBefore) && DateTime.UtcNow < deadline)
        await Task.Delay(100);
    var start = session.Position.TotalSeconds;
    if (session.PlaybackState != MediaPlaybackState.Playing || Math.Abs(start - requested) > 8 ||
        Interlocked.Read(ref frameCount) == framesBefore)
        throw new InvalidOperationException($"Playback did not start with decoded frames near {requested} seconds.");
    await Task.Delay(TimeSpan.FromSeconds(2));
    var end = session.Position.TotalSeconds;
    var framesAfter = Interlocked.Read(ref frameCount);
    var stateDuringPlayback = session.PlaybackState.ToString();
    player.Pause();
    var advanced = end - start;
    var decodedFrames = framesAfter - framesBefore;
    if (advanced is < 1 or > 7 || decodedFrames < 1)
        throw new InvalidOperationException($"Playback did not advance normally after seek to {requested} seconds.");
    results.Add(new
    {
        requestedSeconds = requested,
        playbackStartSeconds = Math.Round(start, 3),
        playbackEndSeconds = Math.Round(end, 3),
        advancedSeconds = Math.Round(advanced, 3),
        decodedFrames,
        state = stateDuringPlayback
    });
}
player.Pause();

Console.WriteLine(JsonSerializer.Serialize(new
{
    schema = 1,
    engine = "Windows.Media.Playback.MediaPlayer",
    fileLength = new FileInfo(path).Length,
    durationSeconds = Math.Round(duration, 3),
    width = session.NaturalVideoWidth,
    height = session.NaturalVideoHeight,
    canSeek = session.CanSeek,
    seekCount = results.Count,
    allAdvancedWithDecodedFrames = true,
    seeks = results
}));
