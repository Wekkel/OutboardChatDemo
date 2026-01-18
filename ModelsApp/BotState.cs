// ============================================================================
// BotState.cs - Conversation State Tracking
// ============================================================================
//
// WHAT IS STATE?
// --------------
// "State" is the data that changes as your app runs. In a chat app, state
// includes things like:
// - What messages have been sent
// - What the user has selected
// - What information has been collected
//
// This class tracks the "business state" of our sales conversation:
// which products the customer is interested in, and their email address.
//
// WHY TRACK STATE SEPARATELY?
// ---------------------------
// AI models don't have memory! Each time you send a message, the model
// has no idea what you talked about before (unless you tell it).
//
// We solve this by:
// 1. Storing important information in this BotState class
// 2. Including that information in every prompt we send
// 3. Updating the state based on the AI's responses
//
// This gives the illusion of a continuous conversation, even though
// each AI call is technically independent.
// ============================================================================

namespace OutboardChatDemo.ModelsApp;

/// <summary>
/// Tracks the state of the sales conversation.
/// Stores what products the user wants and their contact information.
/// </summary>
public sealed class BotState
{
    /// <summary>
    /// The product lines (motor types) the user has expressed interest in.
    /// This is a List, not an array, because we add to it as the conversation
    /// progresses. Each item is a string like "RiverLite 2–6hp (portable)".
    /// </summary>
    /// <remarks>
    /// We initialize this with "new()" to create an empty list.
    /// This prevents null reference errors - the list always exists,
    /// it might just be empty.
    /// </remarks>
    public List<string> SelectedPackages { get; } = new();

    /// <summary>
    /// The user's email address, if they've provided one.
    /// Null until the user gives us an email, then set to that value.
    /// </summary>
    /// <remarks>
    /// The "?" makes this a "nullable string" - it can be null.
    /// We use null to mean "not yet provided" rather than empty string,
    /// because empty string could theoretically be a valid (though weird) input.
    /// </remarks>
    public string? Email { get; set; }

    /// <summary>
    /// Returns true when we have everything we need to complete the sale:
    /// at least one product selected AND a valid email address.
    /// </summary>
    /// <remarks>
    /// This is a "computed property" - it calculates its value each time
    /// you access it, rather than storing a value.
    ///
    /// The "=>" syntax is a shorthand for:
    ///   get { return SelectedPackages.Count > 0 && !string.IsNullOrWhiteSpace(Email); }
    ///
    /// We use this to know when the conversation is "complete" and we can
    /// show a success message or trigger the next step (sending info packs).
    /// </remarks>
    public bool Done => SelectedPackages.Count > 0 && !string.IsNullOrWhiteSpace(Email);
}
