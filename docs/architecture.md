# VoiceDictationCodex architecture

VoiceDictationCodex follows a lightweight MVVM architecture designed for Windows Presentation Foundation (WPF) applications. The goal is to keep UI, state, and domain concerns separated while making it easy to swap out infrastructure components such as the Whisper runtime or model catalog source.

## Layers

| Layer        | Description |
|--------------|-------------|
| Views        | XAML compositions that define the modern interface (see `Views/MainWindow.xaml`). |
| ViewModels   | Glue between UI and services. Exposes commands and observable state. |
| Models       | Immutable records or simple POCOs that describe data flowing through the app. |
| Services     | Long-running or IO-heavy operations such as downloading models, recording audio, and orchestrating Whisper. |

## Key flows

### 1. Model management
1. `MainViewModel` loads the static catalog from `ModelCatalogService`. Each entry carries the exact file size and SHA-256.
2. Installed metadata is merged via `IModelRepository` (`JsonModelRepository` implementation). Saves go to a temporary file that is moved into place, and an unreadable `models.json` is set aside as `models.json.corrupt`.
3. When the user selects **Download Selected**, `ModelDownloadService` (`IModelDownloader`) streams the GGML file from a pinned Hugging Face revision into `<id>.bin.partial`, reports progress, verifies size and SHA-256, moves the file into `%LOCALAPPDATA%\VoiceDictationCodex\Models`, and persists metadata. Any failure, stall or cancellation deletes the partial file.

### 2. Live transcription
1. `AudioCaptureService` captures PCM audio frames with `NAudio`.
2. `WhisperRuntime` (`IWhisperRuntime`) starts an external whisper.cpp process with the chosen model. It drains stdout and stderr concurrently as UTF-8, and the task returned by `StartAsync` completes with a `WhisperExit` (exit code, last stderr lines, stopped-by-user flag) when the process ends.
3. `TranscriptionService` listens to stdout, aggregates text, and notifies `MainViewModel` for UI updates. Lines that are JSON objects contribute their `text` property; every other line is treated as plain text.

> Current status: steps 1 and 2 run side by side, but captured audio is not yet piped into the Whisper process. The executable defaults to `whisper-cli.exe` and can be overridden with the `VOICEDICTATION_WHISPER_PATH` environment variable.

### 3. Export workflow
1. `MainViewModel.ExportText` serializes the in-memory transcript to the user's documents folder.
2. Future iterations can add Markdown/Subtitle exporters or integrate with project management tools.

### 4. Threading and lifetime
- Whisper output and audio callbacks arrive on background threads. `MainViewModel` applies them through `IUiDispatcher`, so bound properties and commands only change on the UI thread.
- `MainViewModel` watches the task returned by `StartAsync`. When the process ends on its own, the UI returns to idle and shows the exit code and the last stderr line.
- `MainViewModel` is `IDisposable`; `MainWindow` disposes it on `Closed`, which stops the whisper process, releases the microphone and cancels a running download.
- `AsyncRelayCommand` routes exceptions to an error callback instead of letting them escape an `async void`. `App` logs anything still unhandled.

## Testing seams

`IModelRepository`, `IModelDownloader`, `IWhisperRuntime`, `IAudioCapture`, `IDialogService` and `IUiDispatcher` are injected through an internal `MainViewModel` constructor. The public parameterless constructor used by XAML wires the real implementations.

## Extending the app

- **Alternate runtimes:** swap `WhisperRuntime` with a managed Whisper.NET or ONNX runtime that runs directly in-process.
- **Additional UI:** add tabbed navigation for a library of historical sessions; the `MainViewModel` can be split into child view models.
- **Telemetry-free analytics:** implement local-only usage metrics by persisting JSON to the app data directory.

## Offline-first considerations

- Downloads come from a pinned Hugging Face revision and are verified by SHA-256. You can also ship pre-downloaded GGML files and register them by writing an entry with the file's full path to `models.json`.
- Model files and audio recordings are never committed to this repository; `.gitignore` excludes them.
- To ensure privacy, never send captured audio or transcripts off-device; the provided services keep everything local.
- Consider enabling optional GPU acceleration via DirectML or Vulkan when pairing with GPU-enabled Whisper builds.
