# OpenScribe — Architecture & Implementation Plan

> **Goal:** A Windows desktop application that captures a user performing a process (screen, clicks, voice) and leverages a corporate Microsoft Copilot / Azure OpenAI license to generate a polished, click-by-click process document (.docx).

---

## 1. Problem Statement

Creating step-by-step process documentation is tedious and error-prone. Subject-matter experts know the process but rarely have time to write it up. OpenScribe lets them **perform the process once** while the app silently records everything, then **automatically produces** a professional Word document with numbered steps, annotated screenshots, and clear instructions.

---

## 2. High-Level Workflow

```
┌──────────────┐    ┌──────────────────┐    ┌───────────────────┐    ┌─────────────┐
│  1. CAPTURE  │───>│  2. EXTRACT &    │───>│  3. AI ANALYSIS   │───>│ 4. DOCUMENT │
│              │    │     PREPROCESS   │    │    (Copilot /     │    │  GENERATION │
│ • Screen rec │    │ • Key frames     │    │     Azure OpenAI) │    │   (.docx)   │
│ • Click log  │    │ • Click regions  │    │ • Describe steps  │    │ • Numbered  │
│ • Voice rec  │    │ • OCR text       │    │ • Generate prose   │    │   steps     │
│ • Hotkeys    │    │ • Transcription  │    │ • Suggest titles   │    │ • Annotated │
│              │    │ • Timeline sync  │    │                   │    │   screens   │
└──────────────┘    └──────────────────┘    └───────────────────┘    └─────────────┘
```

---

## 3. Technology Stack

| Layer | Technology | Rationale |
|---|---|---|
| **Desktop Shell** | **.NET 8 + WinUI 3 (Windows App SDK)** | Native Windows perf, GPU access for capture, MSIX packaging, modern UI |
| **Screen Capture** | **Windows.Graphics.Capture API** | Built-in Windows 10/11 API; captures any window or full screen with minimal overhead |
| **Click & Input Logging** | **Low-level Windows hooks (SetWindowsHookEx)** via P/Invoke | Captures global mouse clicks, coordinates, active window title, focused control |
| **Voice Recording** | **NAudio** (.NET audio library) | Records microphone input to WAV/FLAC alongside screen capture |
| **Speech-to-Text** | **Azure AI Speech Service** (or Whisper via Azure OpenAI) | Transcribes voice narration; timestamps align with click timeline |
| **OCR** | **Azure AI Vision** or **Windows.Media.Ocr** (on-device) | Extracts text from screenshots to identify button labels, field names |
| **AI Analysis** | **Azure OpenAI Service (GPT-4o)** | Vision + text model analyzes screenshots & metadata to generate step descriptions |
| **Document Generation** | **Open XML SDK** (DocumentFormat.OpenXml) | Produces native .docx without requiring Word installed |
| **Image Annotation** | **SkiaSharp** | Draws red boxes, arrows, step-number badges on screenshots |
| **Data Store** | **SQLite** (via EF Core) | Local project database for sessions, steps, settings |
| **Configuration** | **Microsoft.Extensions.Configuration** | appsettings.json + user secrets for API keys |

### Why not Electron / Python?
- Screen capture and global input hooks require native Windows APIs — .NET has first-class access.
- The corporate Copilot license maps to Azure OpenAI API endpoints, which have excellent .NET SDKs (`Azure.AI.OpenAI`).
- Open XML SDK is the gold standard for .docx generation in .NET.

---

## 4. Microsoft Copilot / Azure OpenAI Integration

### 4.1 License Mapping

A "corporate Microsoft Copilot license" typically provisions one or both of:

| Asset | How OpenScribe Uses It |
|---|---|
| **Azure OpenAI Service** (tenant-level deployment) | Primary path — call GPT-4o (vision) via REST / SDK to analyze screenshots and generate text |
| **Microsoft 365 Copilot** (per-user) | Secondary path — could use Microsoft Graph + Copilot extensibility to invoke Copilot inside Word, but less automatable |

**Recommended primary path:** Azure OpenAI Service with a GPT-4o deployment. This gives full programmatic control and supports vision (image) inputs.

### 4.2 API Flow

```
For each captured step:
  1. Send to GPT-4o Vision:
     - The screenshot image (base64)
     - Click coordinates & active control info
     - OCR text extracted from the region
     - Previous step context (for continuity)
     - Voice transcript segment (if any)

  2. Prompt template (simplified):
     ────────────────────────────────
     You are a technical writer. Given the screenshot, click location,
     OCR text, and voice narration, write a single numbered step
     instruction. Be specific: reference the exact button label,
     menu item, or field name visible in the screenshot.
     Return JSON: { "stepTitle": "...", "instruction": "...",
                     "highlightRegion": {x,y,w,h}, "notes": "..." }
     ────────────────────────────────

  3. Receive structured JSON → feed into document builder.
```

### 4.3 Authentication

- Use **Azure.Identity** (`DefaultAzureCredential`) so the app can authenticate via:
  - Interactive browser (user signs in with corporate Entra ID)
  - Managed identity (if deployed on Azure)
  - Environment variables (for CI/CD)
- The Azure OpenAI endpoint and deployment name come from app configuration.

---

## 5. Detailed Module Design

### 5.1 Capture Engine (`OpenScribe.Capture`)

| Component | Responsibility |
|---|---|
| `ScreenRecorder` | Uses `Windows.Graphics.Capture` to record screen frames to an MP4 (via MediaFoundation) and simultaneously save key-frame PNGs on each click event |
| `InputHookManager` | Installs low-level mouse/keyboard hooks; emits `ClickEvent { Timestamp, X, Y, WindowTitle, ControlName, ClickType }` |
| `AudioRecorder` | Records microphone via NAudio; outputs timestamped WAV |
| `CaptureSession` | Orchestrator — starts/stops all recorders, synchronizes timestamps, writes a `session.json` manifest |

**Click-triggered screenshots:** On every mouse click, the engine saves a full-resolution PNG of the active monitor and logs the click coordinates. This is the core data for step generation.

### 5.2 Preprocessing Pipeline (`OpenScribe.Processing`)

| Component | Responsibility |
|---|---|
| `FrameExtractor` | If only video (no click-triggered PNGs), extracts frames at click timestamps from the MP4 |
| `RegionCropper` | Crops a context region around each click point (e.g., 400×300 px) for focused analysis |
| `OcrProcessor` | Runs OCR on the cropped region → extracts button labels, field names, menu text |
| `TranscriptionService` | Sends audio segments (per-step time ranges) to Azure Speech → returns transcripts |
| `TimelineBuilder` | Merges click log + screenshots + transcripts into an ordered `List<RawStep>` |

### 5.3 AI Analysis Service (`OpenScribe.AI`)

| Component | Responsibility |
|---|---|
| `CopilotClient` | Wraps `Azure.AI.OpenAI` SDK; sends multimodal prompts (image + text) to GPT-4o |
| `StepAnalyzer` | For each `RawStep`, builds the prompt, calls `CopilotClient`, parses JSON response into `AnalyzedStep` |
| `DocumentPlanner` | Sends the full list of `AnalyzedStep` objects for a final "editorial pass" — GPT-4o reviews the entire procedure for consistency, adds an introduction and summary |
| `PromptTemplateManager` | Loads/stores customizable prompt templates so power users can tune output style |

### 5.4 Document Generator (`OpenScribe.DocGen`)

| Component | Responsibility |
|---|---|
| `ScreenshotAnnotator` | Uses SkiaSharp to draw red rectangles, numbered badges, and arrows on screenshots |
| `DocxBuilder` | Uses Open XML SDK to assemble the .docx: title page, TOC, numbered steps with images, notes, footer |
| `StyleManager` | Applies corporate branding (fonts, colors, logo) from a configurable template .docx |
| `ExportService` | Saves .docx; optionally converts to PDF via LibreOffice CLI or Interop |

### 5.5 UI Layer (`OpenScribe.App`)

- **WinUI 3** application with MVVM (CommunityToolkit.Mvvm)
- Key screens:

| Screen | Description |
|---|---|
| **Home / Dashboard** | Recent sessions, quick-start capture button |
| **Capture Overlay** | Minimal floating toolbar (Record / Pause / Stop / Screenshot) that stays on top during capture |
| **Session Review** | Timeline view of captured steps; user can reorder, delete, re-capture individual steps |
| **Step Editor** | Edit AI-generated text, re-crop screenshot, add manual notes |
| **Settings** | Azure OpenAI endpoint, model deployment, audio device, output template, branding |
| **Export** | Choose output format (.docx, .pdf), filename, and generate |

---

## 6. Data Model (Core Entities)

```
CaptureSession
├── Id: Guid
├── Name: string
├── CreatedAt: DateTime
├── Status: enum (Recording, Processing, Reviewed, Exported)
├── Settings: SessionSettings (JSON)
└── Steps: List<ProcessStep>

ProcessStep
├── Id: Guid
├── SessionId: Guid
├── SequenceNumber: int
├── Timestamp: TimeSpan
├── ScreenshotPath: string          // full-res PNG
├── CroppedScreenshotPath: string   // region around click
├── AnnotatedScreenshotPath: string // with red box/arrow
├── ClickX, ClickY: int
├── WindowTitle: string
├── ControlName: string
├── OcrText: string
├── VoiceTranscript: string
├── AiGeneratedTitle: string
├── AiGeneratedInstruction: string
├── UserEditedInstruction: string   // if user overrides AI
├── Notes: string
```

---

## 7. Project Structure

```
OpenScribe/
├── src/
│   ├── OpenScribe.App/             # WinUI 3 desktop app (entry point)
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Controls/               # Custom controls (capture overlay, timeline)
│   │   └── App.xaml
│   ├── OpenScribe.Capture/         # Screen, input, audio capture
│   ├── OpenScribe.Processing/      # Frame extraction, OCR, transcription
│   ├── OpenScribe.AI/              # Azure OpenAI / Copilot integration
│   ├── OpenScribe.DocGen/          # .docx generation & image annotation
│   ├── OpenScribe.Core/            # Shared models, interfaces, config
│   └── OpenScribe.Data/            # SQLite / EF Core data access
├── tests/
│   ├── OpenScribe.Capture.Tests/
│   ├── OpenScribe.Processing.Tests/
│   ├── OpenScribe.AI.Tests/
│   └── OpenScribe.DocGen.Tests/
├── templates/
│   ├── default-style.docx          # Base Word template with corporate styles
│   └── prompts/
│       ├── step-analysis.txt       # Prompt template for individual step
│       └── document-review.txt     # Prompt template for full-doc editorial pass
├── docs/
│   ├── PLAN.md                     # This file
│   ├── ARCHITECTURE.md
│   └── SETUP.md
├── OpenScribe.sln
├── .editorconfig
├── Directory.Build.props
└── README.md
```

---

## 8. Implementation Phases

### Phase 1 — Foundation (Weeks 1–2)
| # | Task | Deliverable |
|---|---|---|
| 1.1 | Create solution structure, projects, Directory.Build.props | Compiling solution |
| 1.2 | Implement `ScreenRecorder` using Windows.Graphics.Capture | Can record screen to MP4 |
| 1.3 | Implement `InputHookManager` (global mouse hooks) | Click events with coordinates logged |
| 1.4 | Implement click-triggered screenshot capture | PNG saved on every click |
| 1.5 | Implement `AudioRecorder` with NAudio | WAV recording alongside screen |
| 1.6 | Build `CaptureSession` orchestrator | Start/stop coordinated capture |
| 1.7 | Basic floating capture toolbar (WinUI 3) | Record/Stop buttons that work |

### Phase 2 — Processing Pipeline (Weeks 3–4)
| # | Task | Deliverable |
|---|---|---|
| 2.1 | Implement `OcrProcessor` (Windows.Media.Ocr or Azure Vision) | Text extracted from click regions |
| 2.2 | Implement `TranscriptionService` (Azure Speech) | Audio segments → text |
| 2.3 | Build `TimelineBuilder` to merge all data sources | Ordered `List<RawStep>` |
| 2.4 | Set up SQLite + EF Core data layer | Sessions and steps persisted |
| 2.5 | Session Review screen — view captured steps in order | UI to browse raw capture |

### Phase 3 — AI Integration (Weeks 5–6)
| # | Task | Deliverable |
|---|---|---|
| 3.1 | Implement `CopilotClient` wrapping Azure.AI.OpenAI SDK | Authenticated calls to GPT-4o |
| 3.2 | Design & test prompt templates for step analysis | JSON output per step |
| 3.3 | Implement `StepAnalyzer` — per-step AI processing | AI-generated instructions |
| 3.4 | Implement `DocumentPlanner` — full-doc editorial pass | Consistent, polished text |
| 3.5 | Step Editor screen — view/edit AI output per step | User can refine before export |

### Phase 4 — Document Generation (Weeks 7–8)
| # | Task | Deliverable |
|---|---|---|
| 4.1 | Implement `ScreenshotAnnotator` (SkiaSharp) | Screenshots with red boxes, arrows, badges |
| 4.2 | Implement `DocxBuilder` with Open XML SDK | Structured .docx with steps + images |
| 4.3 | Implement `StyleManager` — apply template branding | Professional-looking output |
| 4.4 | Export screen + PDF option | End-to-end: capture → .docx |

### Phase 5 — Polish & Release (Weeks 9–10)
| # | Task | Deliverable |
|---|---|---|
| 5.1 | Dashboard / Home screen | Session management |
| 5.2 | Settings screen (Azure config, audio device, branding) | User-configurable |
| 5.3 | Error handling, retry logic, progress indicators | Production-quality UX |
| 5.4 | Unit & integration tests | Confidence in core logic |
| 5.5 | MSIX packaging & installer | Distributable app |
| 5.6 | Documentation (README, SETUP, user guide) | Onboarding materials |

---

## 9. Key Design Decisions & Trade-offs

### 9.1 Click-Triggered Screenshots vs. Post-Process Frame Extraction
**Decision:** Capture a screenshot on every click event in real time.
**Why:** Frame extraction from video loses quality and requires timestamp precision. Click-triggered PNGs are pixel-perfect and guaranteed to show the exact state the user clicked on.

### 9.2 On-Device OCR vs. Cloud OCR
**Decision:** Start with Windows.Media.Ocr (on-device, free, fast). Fall back to Azure AI Vision for complex UIs.
**Why:** Most business apps have clear text that on-device OCR handles well. Avoids extra API cost. Azure Vision is there for edge cases.

### 9.3 Single-Step AI Calls vs. Batch
**Decision:** Analyze each step individually with GPT-4o Vision, then do one final editorial pass over all steps.
**Why:** Individual calls allow per-step image analysis (vision). The editorial pass ensures consistency across the full document. Batching all images in one call would exceed token limits for long procedures.

### 9.4 Voice as Primary vs. Supplementary
**Decision:** Voice narration is optional and supplementary.
**Why:** Many users won't narrate. The app must produce great docs from screenshots + clicks alone. Voice transcripts enrich the output when available.

### 9.5 WinUI 3 vs. Electron
**Decision:** WinUI 3.
**Why:** Native performance for real-time capture, direct access to Windows APIs without IPC overhead, smaller footprint, and better integration with Windows 11 design language.

---

## 10. Security & Compliance Considerations

| Concern | Mitigation |
|---|---|
| **Sensitive data in screenshots** | Option to blur/redact regions before sending to AI; all processing uses tenant's own Azure OpenAI deployment (data stays within corporate boundary) |
| **API key management** | Azure.Identity + Entra ID token auth — no API keys stored locally |
| **Local data storage** | SQLite DB and screenshots stored in user's AppData; encrypted at rest via Windows DPAPI |
| **Audio recordings** | Auto-deleted after transcription unless user opts to keep |
| **Network traffic** | All API calls over HTTPS to customer's own Azure endpoints |

---

## 11. Future Enhancements (Post-MVP)

- **Smart step merging** — detect and collapse redundant clicks (e.g., clicking into a field before typing)
- **Template library** — pre-built doc templates for different audiences (end-user, admin, training)
- **Multi-monitor support** — capture follows the active window across monitors
- **Video embedding** — optionally embed short GIF/MP4 clips in the .docx for complex interactions
- **SharePoint integration** — publish docs directly to a SharePoint document library
- **Collaborative review** — share draft docs for team review before finalizing
- **Batch processing** — queue multiple recordings for overnight AI processing
- **Localization** — generate docs in multiple languages via AI translation
- **Accessibility annotations** — auto-generate alt text for all images

---

## 12. Dependencies & Prerequisites

| Requirement | Details |
|---|---|
| **Windows 10 1903+** or **Windows 11** | Required for Windows.Graphics.Capture API |
| **.NET 8 SDK** | Build and run |
| **Windows App SDK 1.5+** | WinUI 3 runtime |
| **Azure OpenAI Service** | GPT-4o deployment in tenant's Azure subscription |
| **Azure AI Speech** (optional) | For voice transcription; can be skipped if voice not needed |
| **Corporate Entra ID** | For authentication to Azure services |

---

## 13. Rough NuGet Package List

```xml
<!-- Core -->
<PackageReference Include="Microsoft.WindowsAppSDK" />
<PackageReference Include="CommunityToolkit.Mvvm" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />

<!-- AI -->
<PackageReference Include="Azure.AI.OpenAI" />
<PackageReference Include="Azure.Identity" />
<PackageReference Include="Microsoft.CognitiveServices.Speech" />

<!-- Document Generation -->
<PackageReference Include="DocumentFormat.OpenXml" />
<PackageReference Include="SkiaSharp" />

<!-- Audio -->
<PackageReference Include="NAudio" />

<!-- Testing -->
<PackageReference Include="xunit" />
<PackageReference Include="Moq" />
<PackageReference Include="FluentAssertions" />
```

---

## 14. Success Criteria

1. A user can start a capture, perform a 10-step process in any Windows application, stop the capture, and receive a .docx within 2 minutes.
2. The generated document is 90%+ accurate — step descriptions correctly reference the UI elements clicked.
3. Screenshots are annotated with clear visual indicators of where to click.
4. The document reads as if written by a professional technical writer.
5. No sensitive data leaves the corporate Azure boundary.

---

*This plan is a living document. Update it as decisions evolve.*
