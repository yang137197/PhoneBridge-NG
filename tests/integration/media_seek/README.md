# P1-021 media seek probes

These probes use only synthetic or public test media. They do not log pairing credentials, device IDs, certificate material, arbitrary paths, or response bodies.

- `Compare-RandomRanges.ps1` reads the fixed ranges in a baseline JSON file from a local or mounted path and compares SHA-256 values.
- `PhoneBridge.MediaSeekProbe` opens an MP4 through the current Windows `Windows.Media.Playback.MediaPlayer` engine, seeks to four bounded positions, and proves playback time and decoded video-frame count advance after each seek. It mutes audio, disables system transport controls, and does not modify the file.
- `PhoneBridge.RangeProbe` opens the current user's protected active pairing record in memory, uses the pinned CA and strict TLS, and checks that every fixed range receives an exact `206`, `Content-Range`, length, and hash. It requires an exact device display name and IP endpoint and never prints authentication material.

P1-021 uses Blender Foundation's public Big Buck Bunny H.264/MP4 sample from `https://download.blender.org/demo/movies/BBB/`. Windows H.264 support is documented at `https://learn.microsoft.com/windows/uwp/audio-video-camera/supported-codecs`.

Example commands:

```powershell
pwsh -File .\tests\integration\media_seek\Compare-RandomRanges.ps1 -Path 'P:\PhoneBridge-P1-021\sample.mp4' -BaselinePath '.\.audit\p1-021-data\range-baseline.json'

dotnet run --project .\tests\integration\media_seek\PhoneBridge.MediaSeekProbe -- 'P:\PhoneBridge-P1-021\sample.mp4'

dotnet run --project .\tests\integration\media_seek\PhoneBridge.RangeProbe -- --address 192.0.2.1 --port 8273 --path /PhoneBridge-P1-021/sample.mp4 --baseline .\.audit\p1-021-data\range-baseline.json --device-name 'test device'
```

The sample, baselines, and raw results remain under ignored `.audit/p1-021-data`; only bounded summaries belong in repository documentation.
