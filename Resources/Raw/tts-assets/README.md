# tts-assets

Pre-generated TTS audio files go here. These files are bundled into the app as MAUI raw assets
and served by `BundledTtsAssets` before falling back to the runtime file cache or Azure synthesis.

## How to populate

Run the TtsPregen tool from `Tools/TtsPregen/`:

```sh
cd Tools/TtsPregen
dotnet run
```

The default `outputDirectory` in `tts-pregen-config.json` already points to this folder.
Set your Azure Speech key and region in the config before running.

## File naming

Files are named `<sha256-of-cache-key>.<ext>` (e.g. `.webm`, `.mp3`).
The cache key is `azure|<locale>|<voice>|<tts-text>` — identical to the scheme used by
`FileTtsCache` at runtime, so the same hash identifies the same audio regardless of location.

## Recommended format

- **`webm-24khz-16bit-mono-opus`** (`.webm`) — best quality/size, no transcoding; Android + Windows
- **`audio-24khz-96kbitrate-mono-mp3`** (`.mp3`) — universal including iOS/macOS

The app tries `.webm` → `.mp3` → `.wav` in that order for each lookup.
