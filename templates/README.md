# OpenScribe Templates

This directory contains customizable templates used by OpenScribe.

## Prompt Templates (`prompts/`)

- **system.txt** — System prompt that sets the AI persona and output format
- **step-analysis.txt** — Per-step analysis prompt with placeholders for click data, OCR, and transcript
- **document-plan.txt** — Editorial review prompt for generating title, intro, summary, and revised instructions

### Placeholders

The step-analysis template supports these placeholders:

| Placeholder | Description |
|---|---|
| `{stepNumber}` | 1-based step index |
| `{clickX}`, `{clickY}` | Click coordinates |
| `{clickType}` | Left, Right, Middle, or DoubleClick |
| `{windowTitle}` | Title of the focused window |
| `{applicationName}` | Process name of the focused app |
| `{controlName}` | UI Automation control name (if available) |
| `{ocrText}` | OCR text extracted from the click region |
| `{voiceTranscript}` | Voice narration for this step's time window |
| `{previousStepContext}` | Summary of the previous step |

The document-plan template uses `{stepsJson}` — a JSON array of all analyzed steps.

## Customization

Edit these files to adjust the AI's writing style, add domain-specific context, or change the output format. The app will load file-based templates if found; otherwise it falls back to built-in defaults.
