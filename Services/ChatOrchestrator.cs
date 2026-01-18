// ============================================================================
// ChatOrchestrator.cs - The Conversation Manager
// ============================================================================
//
// WHAT DOES THIS FILE DO?
// -----------------------
// This is the "conversation manager" or "orchestrator". It sits between the
// user interface and the AI engine, handling all the logic for:
// - Building prompts that tell the AI how to behave
// - Parsing the AI's responses (which come as JSON)
// - Tracking conversation state (what products were selected, user's email)
// - Making sure the AI stays on track
//
// WHY DO WE NEED AN ORCHESTRATOR?
// -------------------------------
// Raw AI models just generate text - they don't "remember" things or follow
// business rules. The orchestrator adds structure:
// 1. It builds a "system prompt" that tells the AI its role and rules
// 2. It tracks what's been discussed (state management)
// 3. It parses structured output (JSON) from the AI
// 4. It applies business logic (validating emails, updating selections)
//
// THE JSON APPROACH
// -----------------
// Instead of letting the AI respond with free-form text, we ask it to reply
// with JSON. This makes responses predictable and easy to parse:
// {
//   "reply": "What the AI says to the user",
//   "selected_lines": ["Products the user wants"],
//   "email": "user@example.com",
//   ...
// }
//
// This is a common pattern called "structured output" or "function calling".
// ============================================================================

using OutboardChatDemo.ModelsApp;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OutboardChatDemo.Services;

/// <summary>
/// Orchestrates the conversation between the user and the AI.
/// Handles prompt building, response parsing, and state management.
/// </summary>
public sealed class ChatOrchestrator
{
    // The AI engine that generates responses
    private readonly OrtxGenAiEngine _engine;

    // Tracks the current state of the conversation (selected products, email)
    private readonly BotState _state = new();

    // Public property so the UI can access the state (for displaying selections)
    public BotState State => _state;

    // Our product catalog - the six types of outboard motors we "sell".
    // These are the valid options the AI can recommend.
    // Making this static readonly means it's shared across all instances
    // and can't be changed after the program starts.
    private static readonly string[] ProductLines =
    {
        "RiverLite 2–6hp (portable)",      // Small, light motors for kayaks/dinghies
        "CoastCruiser 8–15hp (compact)",   // Mid-size for small fishing boats
        "BayRunner 20–40hp (family)",      // Family recreational boats
        "OffshorePro 50–90hp (performance)", // Sport fishing, performance boats
        "WorkHorse 100–150hp (commercial)",  // Commercial fishing, work boats
        "OceanMax 200–300hp (large boats)"   // Large boats, serious offshore
    };

    /// <summary>
    /// Constructor - creates the orchestrator and loads the AI model.
    /// </summary>
    /// <param name="modelFolderPath">Path to the ONNX model folder</param>
    public ChatOrchestrator(string modelFolderPath)
    {
        // Create our AI engine with the specified model
        _engine = new OrtxGenAiEngine(modelFolderPath);
    }

    /// <summary>
    /// Clean up the AI engine when we're done.
    /// This frees GPU memory.
    /// </summary>
    public void Dispose() => _engine.Dispose();

    /// <summary>
    /// Main method: processes user input and gets an AI response.
    /// This is the heart of the conversation flow.
    /// </summary>
    /// <param name="userText">What the user typed</param>
    /// <returns>
    /// A tuple containing:
    /// - assistantText: What to show the user
    /// - stateChanged: Whether selections or email were updated
    /// </returns>
    public async Task<(string assistantText, bool stateChanged)> HandleUserAsync(string userText)
    {
        // Build the two parts of our prompt:
        // 1. System prompt: tells the AI who it is and how to behave
        // 2. Context: current conversation state and product info
        var sys = BuildSystemPrompt();
        var context = BuildContext();

        // BUILD THE FULL PROMPT
        // =====================
        // Qwen3 (and many other chat models) use special tokens to separate
        // different parts of the conversation. This format is called a
        // "chat template" and varies by model.
        //
        // <|im_start|> = "instant message start" - begins a new message
        // <|im_end|> = end of that message
        // system/user/assistant = who is "speaking"
        //
        // The /no_think command is specific to Qwen3 - it tells the model
        // not to show its internal reasoning (which it puts in <think> tags).
        var prompt =
            "<|im_start|>system\n"    // Start system message
            + sys                      // Our system prompt (AI's personality/rules)
            + "\n\n"
            + context                  // Current state and product info
            + "\n"
            + "<|im_end|>\n"           // End system message
            + "<|im_start|>user\n"     // Start user message
            + "/no_think\n"            // Tell Qwen3 to skip <think> tags
            + userText                 // What the user actually typed
            + "\n"
            + "<|im_end|>\n"           // End user message
            + "<|im_start|>assistant\n"; // Start assistant response (AI continues from here)

        // GENERATE THE RESPONSE
        // =====================
        // Send the prompt to the AI engine and get back generated text.
        // We use conservative settings:
        // - maxNewTokens: 300 (enough for a response, but not a novel)
        // - temperature: 0.15 (low = more deterministic/predictable)
        // - topP: 0.9 (only consider highly probable tokens)
        var raw = await _engine.GenerateAsync(
            prompt,
            maxNewTokens: 300,
            temperature: 0.15,
            topP: 0.9
        );

        // CLEAN UP THE RESPONSE
        // =====================
        // Sometimes the model includes <think>...</think> tags with its reasoning.
        // We don't want to show this to the user, so we strip it out.
        // RegexOptions.Singleline makes . match newlines too.
        raw = Regex.Replace(raw ?? "", @"<think>.*?</think>", "", RegexOptions.Singleline);

        // EXTRACT JSON FROM THE RESPONSE
        // ===============================
        // The model should output a JSON object, but sometimes it adds extra
        // text before or after. ExtractFirstJsonObject finds the JSON part.
        var json = ExtractFirstJsonObject(raw);
        if (json is null)
        {
            // If we couldn't find JSON, show the raw output for debugging.
            // This helps during development to see what went wrong.
            var preview = raw is null ? "<null>" : raw.Trim();
            if (preview.Length > 500)
                preview = preview[..500] + "…";  // Truncate long outputs
            return ($"Model output was not JSON. First 500 chars:\n{preview}", false);
        }

        // PARSE THE JSON
        // ==============
        // Convert the JSON string into a strongly-typed C# object.
        var parsed = TryParse(json);
        if (parsed is null)
        {
            // JSON was found but couldn't be parsed (maybe malformed)
            return ("Sorry—my output wasn't valid JSON. Please try again.", false);
        }

        // APPLY STATE CHANGES
        // ===================
        // Update our conversation state based on what the AI returned.
        // This tracks selected products and the user's email.
        bool changed = Apply(parsed);

        // Return the friendly reply text and whether state changed
        // (UI uses 'changed' to know if it should refresh the display)
        return (parsed.reply ?? "Ok.", changed);
    }

    /// <summary>
    /// Builds the system prompt - the "personality" and rules for the AI.
    /// This is crucial! A good system prompt makes the AI behave consistently.
    /// </summary>
    private static string BuildSystemPrompt()
    {
        // The system prompt defines:
        // 1. WHO the AI is (a sales assistant for outboard motors)
        // 2. HOW it should respond (JSON only, no extra text)
        // 3. WHAT the JSON format should be (the schema)
        // 4. RULES to follow (keep it short, validate email, etc.)
        //
        // We're very explicit about the JSON format because LLMs can be
        // inconsistent. The more specific you are, the better results.
        return @"You are a helpful sales assistant for a company that sells outboard motors.

Output format rule (very important):
- Respond with ONE single JSON object and nothing else.
- No <think> tags.
- No extra text before or after the JSON.
- Start with '{' and end with '}'.

JSON schema:
{
  ""reply"": string,
  ""selected_lines"": string[],
  ""ask_email"": boolean,
  ""email"": string|null,
  ""done"": boolean
}

Rules:
- Keep questions short.
- Only select lines that match user needs.
- If not enough info, ask 1-2 questions.
- When ready to send info packs, ask for email if missing.
- Validate email format loosely; if missing/invalid, ask again.
- If CURRENT STATE already contains selected_lines, do not ask which lines the user is interested in; continue with those lines.

If you are unsure, still output valid JSON using best effort.";
    }

    /// <summary>
    /// Builds the context section - current state and available products.
    /// This is injected into each prompt so the AI "remembers" the conversation.
    /// </summary>
    private string BuildContext()
    {
        // Format the currently selected packages (or "none" if empty)
        var selected =
            _state.SelectedPackages.Count == 0
                ? "none"
                : string.Join(", ", _state.SelectedPackages);

        // Format the captured email (or "none" if not yet provided)
        var email = string.IsNullOrWhiteSpace(_state.Email) ? "none" : _state.Email;

        // Build the context string with product list and current state.
        // The "CURRENT STATE (authoritative)" part tells the AI to trust
        // this information and not ask questions we already have answers to.
        return $@"PRODUCT LINES (exact strings):
1) {ProductLines[0]}
2) {ProductLines[1]}
3) {ProductLines[2]}
4) {ProductLines[3]}
5) {ProductLines[4]}
6) {ProductLines[5]}

CURRENT STATE (authoritative; do not ask to confirm):
- selected_lines: {selected}
- email: {email}

User may write Dutch or English.

You are deciding which info pack(s) to send for one or more lines.";
    }

    // ========================================================================
    // JSON PARSING HELPERS
    // ========================================================================

    /// <summary>
    /// Represents the structure of the AI's JSON response.
    /// Using a "record" gives us a simple, immutable data class.
    /// The property names match the JSON keys (case-insensitive parsing).
    /// </summary>
    private sealed record LlmJson(
        string? reply,           // What to say to the user
        string[]? selected_lines, // Product lines the user wants
        bool ask_email,          // Should we ask for email?
        string? email,           // User's email if provided
        bool done               // Is the conversation complete?
    );

    /// <summary>
    /// Attempts to parse a JSON string into our LlmJson record.
    /// Returns null if parsing fails (invalid JSON format).
    /// </summary>
    private static LlmJson? TryParse(string json)
    {
        try
        {
            // JsonSerializer.Deserialize converts JSON text to a C# object.
            // PropertyNameCaseInsensitive means "reply" and "Reply" both work.
            return JsonSerializer.Deserialize<LlmJson>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }
        catch
        {
            // If JSON parsing fails for any reason, just return null.
            // The calling code will handle this gracefully.
            return null;
        }
    }

    /// <summary>
    /// Applies the AI's response to our conversation state.
    /// Updates selected packages and email if valid values are provided.
    /// </summary>
    /// <returns>True if anything actually changed</returns>
    private bool Apply(LlmJson j)
    {
        bool changed = false;

        // PROCESS SELECTED PRODUCT LINES
        // ==============================
        // If the AI included selected_lines in its response, add any new ones
        // to our state. We check for duplicates to avoid double-adding.
        if (j.selected_lines is { Length: > 0 })
        {
            foreach (var line in j.selected_lines)
            {
                // Only add non-empty lines that aren't already selected
                if (!string.IsNullOrWhiteSpace(line) && !_state.SelectedPackages.Contains(line))
                {
                    _state.SelectedPackages.Add(line);
                    changed = true;
                }
            }
        }

        // PROCESS EMAIL
        // =============
        // If the AI extracted an email from the user's message, validate
        // and store it. We only update if it looks like a real email.
        if (!string.IsNullOrWhiteSpace(j.email))
        {
            var email = j.email.Trim();
            if (LooksLikeEmail(email))
            {
                // Only update if the email is different (avoid false positives)
                if (_state.Email != email)
                {
                    _state.Email = email;
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Simple email validation using regex.
    /// This is intentionally loose - just checks for basic format: x@y.z
    /// Real email validation is surprisingly complex; this is "good enough".
    /// </summary>
    private static bool LooksLikeEmail(string s) => Regex.IsMatch(s, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    /// <summary>
    /// Extracts the first JSON object from a string.
    /// Handles cases where the model outputs extra text before/after the JSON.
    /// Uses balanced brace matching to find the complete object.
    /// </summary>
    /// <returns>The JSON object as a string, or null if not found</returns>
    private static string? ExtractFirstJsonObject(string s)
    {
        // Find the first opening brace
        int start = s.IndexOf('{');
        if (start < 0)
            return null; // No JSON object found

        // Track brace depth to find the matching closing brace.
        // This handles nested objects like {"a": {"b": 1}}
        int depth = 0;
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] == '{')
                depth++;   // Going deeper into nesting
            else if (s[i] == '}')
            {
                depth--;   // Coming back out
                if (depth == 0)
                {
                    // Found the matching close brace - extract the substring
                    return s.Substring(start, i - start + 1);
                }
            }
        }

        // If we get here, braces weren't balanced (malformed JSON)
        return null;
    }
}
