namespace OpenScribe.AI.Prompts;

/// <summary>
/// Manages prompt templates for AI analysis.
/// Templates can be loaded from files or use built-in defaults.
/// </summary>
public static class PromptTemplates
{
    /// <summary>
    /// System prompt that sets the AI's persona and output format.
    /// </summary>
    public const string SystemPrompt = """
        You are an expert technical writer specializing in creating clear, concise, step-by-step
        process documentation for end users. You write instructions that are easy to follow,
        reference exact UI element names visible on screen, and use active voice with imperative mood.

        IMPORTANT: The user may have provided voice narration describing what they are doing and why.
        The voice narration is the PRIMARY source of the user's intent and context. Always incorporate
        the meaning and context from the voice narration into your instructions. The screenshot and
        click data tell you WHAT was clicked; the voice narration tells you WHY and provides the
        user's own description of the action, which should be reflected in the output.

        PRECISION PRINCIPLE: Each step instruction must describe ONLY the single action that was
        actually captured (one click, one keyboard input). Never describe actions that were not
        performed. Never mention UI elements just because they are visible — only reference the
        element that was actually interacted with. If the voice narration does not mention
        something and the click/keyboard data does not show it, leave it out.

        When analyzing a screenshot with click coordinates, you must:
        1. Read the voice narration first to understand the user's intent and context.
        2. Identify the exact UI element that was clicked based on the coordinates, OCR text,
           and UI Automation metadata (control type, element name, automation ID).
        3. Write a clear, specific instruction for ONLY that one action — do not describe
           other elements visible on screen or actions the user did not perform.
        4. Reference the exact button label, menu item, field name, or link text of the clicked element.
        5. Include any tips, warnings, or context the user mentioned in their narration.

        Always respond with valid JSON matching the requested schema.
        """;

    /// <summary>
    /// Prompt template for researching a process before per-step analysis.
    /// Placeholder: {processName}
    /// </summary>
    public const string ProcessResearchPrompt = """
        You are a technical process expert. The user is creating step-by-step documentation
        for the following process:

        **Process Name:** {processName}

        Provide a concise summary of this process:
        1. What is this process and why would someone perform it?
        2. List the typical steps in order (numbered, 1-2 sentences each).
        3. What key UI elements, menus, buttons, or settings should the user look for?
        4. Any common pitfalls or important notes?

        Be specific and practical. If you are not familiar with this exact process,
        provide your best understanding based on the software/system mentioned.
        Keep the response under 500 words.
        """;

    /// <summary>
    /// Web-search-optimized prompt for researching a process using live documentation.
    /// Used when the search model is available. Placeholder: {processName}
    /// </summary>
    public const string ProcessResearchWebSearchPrompt = """
        Search for current, official documentation about the following process:

        **Process Name:** {processName}

        Search the web for the vendor's official documentation about this process.

        Provide ONLY background context (NOT a step-by-step guide):
        1. What is this process and why would someone perform it? (1-2 sentences)
        2. What application or module does this process belong to?
        3. Key terminology or concepts a technical writer should know.
        4. Any common pitfalls or prerequisites worth noting.

        IMPORTANT: Do NOT list steps, menu paths, or button names. This context will be
        used alongside actual screen captures — detailed steps from documentation would
        conflict with what was actually recorded. Keep it to background knowledge only.
        Keep the response under 300 words.
        """;

    /// <summary>
    /// Prompt template for analyzing a single step.
    /// Placeholders: {stepNumber}, {clickX}, {clickY}, {clickType}, {windowTitle},
    /// {controlName}, {applicationName}, {ocrText}, {typedText}, {voiceTranscript},
    /// {previousStepContext}, {imageWidth}, {imageHeight}, {uiaControlType}, {uiaElementName},
    /// {uiaAutomationId}, {uiaClassName}, {uiaElementBounds}, {processContext}
    /// </summary>
    public const string StepAnalysisPrompt = """
        Analyze this screenshot and user action to generate a step instruction.

        ## Process Context (background reference only — do NOT use to generate instructions)
        {processContext}

        This is background context about the overall process. Use it ONLY to understand which part
        of the UI is relevant to this step. Do NOT describe actions, menus, buttons, or clicks
        that are not directly evidenced by the click coordinates, UI Automation metadata, keyboard
        input, or voice narration below. Your instruction must describe ONLY the single action
        that was actually captured — nothing more.

        ## Action Metadata
        **Step Number:** {stepNumber}
        **Click Location:** ({clickX}, {clickY}) on a {imageWidth}x{imageHeight} screenshot — {clickType}
        **Window Title:** {windowTitle}
        **Application:** {applicationName}

        ## UI Automation Context
        **Control Type:** {uiaControlType}
        **Element Name:** {uiaElementName}
        **Automation ID:** {uiaAutomationId}
        **Class Name:** {uiaClassName}
        **Element Bounds:** {uiaElementBounds}

        ## OCR Text Near Click
        {ocrText}

        ## Keyboard Input
        {typedText}

        ## Previous Step
        {previousStepContext}

        ## Voice Narration (PRIMARY SOURCE — the user's own description of this action)
        {voiceTranscript}

        The voice narration is the user speaking in real time. It describes their intent, what they
        are doing, and why. You MUST incorporate the context and meaning from the narration into
        your instruction. Do not ignore it.

        If the narration describes actions beyond just the click (e.g., typing a value, entering
        credentials, or explaining purpose), include that context in the instruction and notes.

        ## Instructions
        Write ONE instruction for the ONE action that was captured. Your instruction MUST be
        grounded in the evidence: the click coordinates, UI Automation metadata, keyboard input,
        OCR text, and voice narration. Do NOT mention menus, buttons, or actions that are not
        evidenced by this data — even if they are visible in the screenshot or expected from
        the process context.

        If keyboard input is present, reference what was typed in the instruction (e.g., "Type
        'ls -la' and press Enter"). The UI Automation metadata tells you exactly what control
        was clicked (e.g., a Button named "Submit", a TextBox with automation ID "txtEmail").
        Use this to write precise instructions.

        PRECISION RULE: If the voice narration does not mention it, and the click/keyboard data
        does not show it, do NOT include it in the instruction.

        You MUST also return a highlightRegion that tightly bounds the clicked UI element.
        Use the click coordinates ({clickX}, {clickY}) and the image dimensions ({imageWidth}x{imageHeight})
        to estimate a bounding box around the element that was clicked. Add ~4-8 pixels of padding on each side.
        Use UI Automation element bounds if available, but prefer your own visual analysis of the screenshot
        to determine the actual rendered bounds of the element. If you cannot confidently determine the
        element bounds, return null for highlightRegion.

        You MUST also return a cropRegion that bounds the area of the screenshot a reader needs to see
        to understand this step. This should include the clicked element plus any relevant context:
        dropdown menus that appeared, form sections, dialog boxes, navigation panels, or settings groups.
        The cropRegion should be larger than the highlightRegion — typically 400-1000px in each dimension.
        If a popup or menu is visible, the cropRegion MUST include it even if it's far from the click point.
        The cropRegion does NOT need to be centered on the click — it should be centered on the most
        relevant content area for this step.

        Respond with this exact JSON structure:
        ```json
        {
          "stepTitle": "Short action title (e.g., 'Click the Submit button')",
          "instruction": "Detailed instruction paragraph for the end user. Be specific about what to click, where to find it, and what will happen. Incorporate the user's narrated context and intent.",
          "notes": "Include any tips, warnings, or additional context from the voice narration (or null if none)",
          "highlightRegion": { "x": 100, "y": 200, "width": 150, "height": 40 },
          "cropRegion": { "x": 50, "y": 150, "width": 800, "height": 500 }
        }
        ```
        """;

    /// <summary>
    /// Prompt for the editorial pass that reviews all steps for consistency.
    /// Placeholders: {stepsJson}, {voiceTranscriptsJson}, {processContext}
    /// </summary>
    public const string DocumentPlanPrompt = """
        You are an expert technical editor reviewing a complete set of step-by-step instructions
        for a corporate process document. Your job is to polish the content into a cohesive,
        professional document.

        ## Process Context (background reference only)
        {processContext}

        Use this reference for the document title and introduction ONLY. Do NOT add steps,
        actions, or UI references that were not present in the analyzed steps below. The
        analyzed steps are the authoritative record of what the user actually did.

        ## Analyzed Steps
        {stepsJson}

        ## Voice Narration Transcripts (the user's own descriptions, in step order)
        {voiceTranscriptsJson}

        ## Style Rubric — you MUST follow all of these rules:

        ### Titles
        - Every step title MUST begin with an imperative verb: "Click", "Select", "Enter", "Navigate", "Open", etc.
        - Titles must be 3-8 words. No punctuation except hyphens.
        - Titles must name the specific UI element: "Click the Save button", NOT "Save your work".
        - No step numbers in titles (those are added automatically).

        ### Instructions
        - Use imperative mood, second person ("Click...", "Select...", "Enter...").
        - First sentence: the specific action. Mention the exact UI element name, its location
          (e.g., "in the top-right corner", "in the left navigation panel"), and what part of the
          application the user should be looking at.
        - Second sentence (if needed): what happens after the action (e.g., "A confirmation
          dialog will appear.", "The Settings page will open.").
        - Third sentence (if needed): context from the voice narration — why this step matters.
        - Maximum 3 sentences per instruction. No filler words.
        - Reference exact UI labels in **bold** (e.g., 'Click **Save**').

        ### Document Structure
        - Title: "How to [verb phrase describing the process]" — e.g., "How to Configure Email Notifications"
        - Introduction: 2-3 sentences. First sentence states what the document covers. Second sentence
          states who should follow these steps. Third sentence (optional) states any prerequisites.
        - Summary: 2-3 sentences. First sentence confirms what was accomplished. Second sentence
          mentions any follow-up actions. No filler.

        ### Voice Narration Integration
        - The voice transcripts contain the user's real-time commentary explaining WHY they
          performed each action. Extract meaningful context (purpose, warnings, tips) and weave
          it into the instructions and notes. Do NOT ignore the voice narration.

        ### Precision
        - Each revised instruction must describe ONLY the action from the corresponding analyzed step.
        - Do NOT add actions, menus, buttons, or clicks that were not in the original analyzed steps.
        - Do NOT infer extra steps from the process context — it is for framing only.
        - If the analyzed step says "Click X", the revised instruction must be about clicking X — not
          about navigating to a menu, scrolling, or performing a different action.

        ## Response Format
        Respond with this exact JSON structure:
        ```json
        {
          "title": "How to [verb phrase]",
          "introduction": "Introduction paragraph following the rules above.",
          "summary": "Summary paragraph following the rules above.",
          "revisedTitles": [
            "Imperative verb + UI element for step 1",
            "Imperative verb + UI element for step 2"
          ],
          "revisedInstructions": [
            "Polished instruction for step 1 following the style rubric.",
            "Polished instruction for step 2 following the style rubric."
          ]
        }
        ```

        IMPORTANT: revisedTitles and revisedInstructions must have exactly the same number of
        entries as there are steps. Do not add or remove steps.
        """;

    /// <summary>
    /// Load a prompt template from a file, falling back to the built-in default.
    /// </summary>
    public static async Task<string> LoadTemplateAsync(string templateName, string? templateDirectory = null)
    {
        if (!string.IsNullOrEmpty(templateDirectory))
        {
            var filePath = Path.Combine(templateDirectory, $"{templateName}.txt");
            if (File.Exists(filePath))
                return await File.ReadAllTextAsync(filePath);
        }

        return templateName switch
        {
            "system" => SystemPrompt,
            "step-analysis" => StepAnalysisPrompt,
            "document-plan" => DocumentPlanPrompt,
            "process-research" => ProcessResearchPrompt,
            _ => throw new ArgumentException($"Unknown template: {templateName}")
        };
    }
}
