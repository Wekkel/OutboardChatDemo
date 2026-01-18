// ============================================================================
// ChatMessage.cs - The Chat Message Data Model
// ============================================================================
//
// WHAT IS A DATA MODEL?
// ---------------------
// A data model is a class that represents a piece of data in your app.
// It's like a form or template that says "a chat message has these fields:
// who sent it, what they said, and how it should look."
//
// This class doesn't DO anything - it just holds data. The code that
// creates, displays, and manages messages lives elsewhere (in the ViewModel).
//
// WHY USE A SEPARATE CLASS?
// -------------------------
// We could just use strings everywhere, but a class gives us:
// 1. Organization: All message-related data is in one place
// 2. Type safety: The compiler helps catch mistakes
// 3. Extensibility: Easy to add new fields later (timestamps, avatars, etc.)
// 4. Data binding: WPF can bind to properties on this class
// ============================================================================

using System.Windows.Media;

namespace OutboardChatDemo.ModelsApp;

/// <summary>
/// Represents a single message in the chat conversation.
/// This is an immutable data class - once created, it doesn't change.
/// </summary>
public sealed class ChatMessage
{
    // ========================================================================
    // REQUIRED PROPERTIES
    // ========================================================================
    // The "required" keyword (C# 11+) means these MUST be set when creating
    // a new ChatMessage. The compiler won't let you create one without them.

    /// <summary>
    /// Who sent this message. Examples: "You", "Assistant".
    /// Displayed above the message bubble to show who's talking.
    /// </summary>
    /// <remarks>
    /// "required" means this must be set during initialization.
    /// "init" means it can only be set during initialization, not changed later.
    /// This makes the object immutable (unchangeable after creation).
    /// </remarks>
    public required string Sender { get; init; }

    /// <summary>
    /// The actual message content.
    /// This is what gets displayed in the chat bubble.
    /// </summary>
    public required string Text { get; init; }

    // ========================================================================
    // OPTIONAL PROPERTIES WITH DEFAULTS
    // ========================================================================

    /// <summary>
    /// The background color for this message's bubble.
    /// Different colors help distinguish between user and assistant messages.
    /// </summary>
    /// <remarks>
    /// Brush is WPF's way of specifying colors (and gradients, patterns, etc.).
    /// SolidColorBrush is just a simple solid color.
    ///
    /// The default color (#121826) is a dark blue-gray that works well
    /// with the app's dark theme. User messages override this with a
    /// slightly different color to create visual distinction.
    ///
    /// ColorConverter.ConvertFromString parses hex color strings like "#121826"
    /// into Color objects that WPF can use.
    /// </remarks>
    public Brush BubbleBackground { get; init; } =
        new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#121826"));
}
