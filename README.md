# OpenScribe

Automated process documentation for Windows. Perform a process once — OpenScribe captures your clicks, screenshots, and voice narration, then uses AI to generate a polished step-by-step Word document.

## How It Works

```
 1. CAPTURE          2. PROCESS           3. AI ANALYSIS        4. DOCUMENT
 Click-triggered     OCR text from        GPT-4o describes      Annotated .docx
 screenshots,        screenshots,         each step from        with numbered
 mouse/keyboard      transcribe audio,    screenshots +         steps, red-box
 hooks, mic audio    build timeline       metadata              highlights
```

1. **Start a capture session** — OpenScribe installs low-level hooks to detect your clicks and keystrokes
2. **Perform your process** — every click triggers a full-resolution screenshot and logs coordinates, window title, and UI element info
3. **Stop recording** — OpenScribe runs OCR, transcribes audio, and sends each step to Azure OpenAI (GPT-4o with vision)
4. **Export** — generates a professional `.docx` with numbered steps, annotated screenshots, and clear instructions

## Features

- Click-triggered screenshots with UI element detection (via Windows UI Automation)
- Global mouse and keyboard capture with scope filtering (entire screen, single monitor, or specific window)
- Optional microphone recording with device picker and real-time level meter
- Optional screen video recording
- On-device OCR (Windows.Media.Ocr) for extracting button labels and field names
- Azure Speech transcription for voice narration
- AI-powered step description generation (Azure OpenAI / OpenAI GPT-4o vision)
- Annotated screenshots with red highlight boxes and step number badges (SkiaSharp)
- Word document generation with customizable templates (Open XML SDK)
- Session review and step editing before export
- Floating overlay toolbar during capture (Record / Pause / Stop)

## Prerequisites

- **Windows 10 (1903+)** or **Windows 11**
- **.NET 8 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Azure OpenAI Service** or **OpenAI API** access for AI-powered step generation
- **Azure Speech Service** (optional) for voice transcription

## Getting Started

### Build from source

```powershell
git clone https://github.com/YOUR-USERNAME/OpenScribe.git
cd OpenScribe
dotnet build
```

### Run

```powershell
dotnet run --project src/OpenScribe.App
```

### Build installer

```powershell
cd build
.\Build-OpenScribe.ps1 -Version "1.0.1" -SkipSign
```

This produces a self-contained installer in `build/artifacts/`. Use `-Architecture arm64` for ARM builds.

### Configure

On first launch, go to **Settings** and configure:

1. **AI Provider** — Azure OpenAI endpoint + deployment name (or OpenAI API key)
2. **Azure Speech** (optional) — subscription key and region for voice transcription
3. **General** — organization name, default author, crop region size

Settings are stored in `%LocalAppData%\OpenScribe\usersettings.json`.

## Architecture

| Project | Purpose |
|---|---|
| `OpenScribe.App` | WinUI 3 desktop shell (MVVM with CommunityToolkit.Mvvm) |
| `OpenScribe.Capture` | Screen capture, global input hooks, audio recording (NAudio) |
| `OpenScribe.Processing` | OCR, audio transcription, timeline building |
| `OpenScribe.AI` | Azure OpenAI / OpenAI integration, prompt templates |
| `OpenScribe.DocGen` | Word document generation (Open XML SDK), screenshot annotation (SkiaSharp) |
| `OpenScribe.Core` | Shared interfaces, models, configuration |
| `OpenScribe.Data` | SQLite persistence via EF Core |

See [PLAN.md](PLAN.md) for detailed architecture and design decisions.

## Tech Stack

- .NET 8 + WinUI 3 (Windows App SDK)
- NAudio for microphone recording
- Win32 low-level hooks (SetWindowsHookEx) for input capture
- Windows.Media.Ocr for on-device text extraction
- Azure.AI.OpenAI SDK for GPT-4o vision calls
- Microsoft.CognitiveServices.Speech for transcription
- DocumentFormat.OpenXml for .docx generation
- SkiaSharp for screenshot annotation
- SQLite + EF Core for local data storage

## License

[MIT](LICENSE)
