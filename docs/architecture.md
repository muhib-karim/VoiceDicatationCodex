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
1. `MainViewModel` loads the static catalog from `ModelCatalogService`.
2. Installed metadata is merged via `IModelRepository` (`JsonModelRepository` implementation).
3. When the user selects **Download Selected**, `ModelDownloadService` streams the GGML file, reports progress, and persists metadata.

### 2. Live transcription
1. `AudioCaptureService` captures PCM audio frames with `NAudio`.
2. `WhisperRuntime` starts an external `whisper.cpp.exe` process with the chosen model.
3. `TranscriptionService` listens to stdout, aggregates text, and notifies `MainViewModel` for UI updates.

> Current status: steps 1 and 2 run side by side, but captured audio is not yet piped into the Whisper process. The executable defaults to `whisper.cpp.exe` and can be overridden with the `VOICEDICTATION_WHISPER_PATH` environment variable.

### 3. Export workflow
1. `MainViewModel.ExportText` serializes the in-memory transcript to the user's documents folder.
2. Future iterations can add Markdown/Subtitle exporters or integrate with project management tools.

## Extending the app

- **Alternate runtimes:** swap `WhisperRuntime` with a managed Whisper.NET or ONNX runtime that runs directly in-process.
- **Additional UI:** add tabbed navigation for a library of historical sessions; the `MainViewModel` can be split into child view models.
- **Telemetry-free analytics:** implement local-only usage metrics by persisting JSON to the app data directory.

## Offline-first considerations

- All downloads default to public mirrors, but you can ship pre-downloaded GGML files and mark them installed by adjusting `JsonModelRepository`.
- To ensure privacy, never send captured audio or transcripts off-device; the provided services keep everything local.
- Consider enabling optional GPU acceleration via DirectML or Vulkan when pairing with GPU-enabled Whisper builds.
