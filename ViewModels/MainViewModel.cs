// ============================================================================
// MainViewModel.cs - The ViewModel (MVVM Pattern)
// ============================================================================
//
// WHAT IS MVVM?
// -------------
// MVVM stands for Model-View-ViewModel. It's a design pattern that separates
// your app into three parts:
//
// 1. MODEL: The data and business logic (BotState, ChatMessage, ChatOrchestrator)
// 2. VIEW: The user interface (MainWindow.xaml)
// 3. VIEWMODEL: The glue between them (this file!)
//
// Think of it like a restaurant:
// - Model = the kitchen (makes the food, knows the recipes)
// - View = the dining room (where customers see and eat food)
// - ViewModel = the waiter (takes orders, brings food, handles requests)
//
// WHY USE MVVM?
// -------------
// - Separation of concerns: UI code stays in XAML, logic stays in C#
// - Testability: You can test the ViewModel without a UI
// - Data binding: Changes in the ViewModel automatically update the UI
// - Maintainability: Easier to change one part without breaking others
//
// DATA BINDING IN WPF
// -------------------
// In the XAML, you'll see things like {Binding StatusText}. This means:
// "Show the value of the StatusText property from the ViewModel, and
// update the display whenever that property changes."
//
// For this to work, we implement INotifyPropertyChanged and call
// OnPropertyChanged() whenever a property value changes.
// ============================================================================

using OutboardChatDemo.ModelsApp;
using OutboardChatDemo.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;

namespace OutboardChatDemo.ViewModels;

/// <summary>
/// The ViewModel for the main window.
/// Contains all the data and logic that the UI displays and interacts with.
/// Implements INotifyPropertyChanged so WPF knows when properties change.
/// Implements IDisposable to clean up the AI engine when the app closes.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    // ========================================================================
    // OBSERVABLE COLLECTION
    // ========================================================================

    /// <summary>
    /// The list of chat messages shown in the UI.
    /// ObservableCollection automatically notifies the UI when items are
    /// added or removed - no extra code needed! This is why chat bubbles
    /// appear instantly when we add messages.
    /// </summary>
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    // ========================================================================
    // PRIVATE FIELDS
    // ========================================================================

    // The chat orchestrator handles conversation logic and AI calls.
    // It's nullable because we load it asynchronously (the model takes time to load).
    private ChatOrchestrator? _chat;

    // ========================================================================
    // BINDABLE PROPERTIES
    // ========================================================================
    // These properties are "bound" to the UI via {Binding PropertyName} in XAML.
    // When we call OnPropertyChanged(), WPF automatically updates the display.

    // Backing field for UserInput (what the user is typing)
    private string _userInput = "";

    /// <summary>
    /// The text the user is currently typing in the input box.
    /// Two-way bound to the TextBox, so changes flow both directions.
    /// </summary>
    public string UserInput
    {
        get => _userInput;
        set
        {
            _userInput = value;

            // Tell the UI this property changed
            OnPropertyChanged();

            // Also update whether the Send button can be clicked.
            // If the text box is empty, the button should be disabled.
            ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
        }
    }

    // Backing field for StatusText
    private string _statusText = "Loading model…";

    /// <summary>
    /// Status message shown in the header (e.g., "Loading model…", "Ready", "Generating…").
    /// Gives the user feedback about what the app is doing.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    // Backing field for ModelPath
    private string _modelPath = "";

    /// <summary>
    /// The path to the AI model folder. Displayed in the UI for reference.
    /// </summary>
    public string ModelPath
    {
        get => _modelPath;
        private set
        {
            _modelPath = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Human-readable list of selected product packages.
    /// Shows "—" if nothing selected, otherwise comma-separated list.
    /// This is a "computed property" - it calculates its value on the fly.
    /// </summary>
    public string SelectedPackagesText =>
        _chat.State.SelectedPackages.Count == 0
            ? "—"
            : string.Join(", ", _chat.State.SelectedPackages);

    /// <summary>
    /// The captured email address, or "—" if not yet provided.
    /// Another computed property that reads from the chat state.
    /// </summary>
    public string CapturedEmail =>
        string.IsNullOrWhiteSpace(_chat.State.Email) ? "—" : _chat.State.Email!;

    /// <summary>
    /// Dynamic text for the Send button.
    /// Shows "Thinking…" while generating a response, "Send" otherwise.
    /// This gives the user clear feedback that something is happening.
    /// </summary>
    public string SendButtonText => IsBusy ? "Thinking…" : "Send";

    // Backing field for IsBusy
    private bool _isBusy;

    /// <summary>
    /// True when the app is busy (loading model or generating a response).
    /// Used to disable the Send button and show status messages.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;

            // Notify that IsBusy changed
            OnPropertyChanged();

            // SendButtonText depends on IsBusy, so notify that too
            OnPropertyChanged(nameof(SendButtonText));

            // Update whether the Send command can execute
            ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
        }
    }

    // ========================================================================
    // COMMANDS
    // ========================================================================
    // Commands are WPF's way of handling button clicks and other actions.
    // Instead of event handlers (Button_Click), we use commands that can
    // also specify when they're allowed to execute (CanExecute).

    /// <summary>
    /// Command that sends the user's message to the AI.
    /// Bound to the Send button and triggered by Enter key.
    /// </summary>
    public ICommand SendCommand { get; }

    // ========================================================================
    // CONSTRUCTOR
    // ========================================================================

    /// <summary>
    /// Constructor - sets up the ViewModel and starts loading the AI model.
    /// </summary>
    public MainViewModel()
    {
        // SET UP THE MODEL PATH
        // =====================
        // AppDomain.CurrentDomain.BaseDirectory gives us the folder where
        // our .exe is running. We look for the model in a subfolder.
        // Initialize with the default model location
        ModelPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Models",
            "Qwen3-14B-Instruct-DirectML-INT4"
        );

        // ADD AN INITIAL GREETING
        // =======================
        // Show a welcome message so the chat doesn't start empty.
        // This gives the user context about what the app does.
        Messages.Add(
            new ChatMessage
            {
                Sender = "Assistant",
                Text =
                    "Hi! What kind of outboard motor are you looking for? (boat type, horsepower range, and use: river/coast/offshore/work)",
                BubbleBackground = BrushFrom("#121826")
            }
        );

        // SET UP THE SEND COMMAND
        // =======================
        // RelayCommand takes two things:
        // 1. What to do when executed (call SendAsync)
        // 2. When it can execute (not busy, text isn't empty, model is loaded)
        // Command can only be executed if _chat is not null and not busy
        SendCommand = new RelayCommand(
            async () => await SendAsync(),
            () => !IsBusy && !string.IsNullOrWhiteSpace(UserInput) && _chat != null
        );

        // LOAD THE MODEL IN THE BACKGROUND
        // =================================
        // Loading the AI model can take several seconds (it's ~9GB!).
        // If we did this on the main thread, the app would freeze.
        // Instead, we load it in the background so the UI appears immediately.
        StatusText = "Loading model (this may take a while)...";
        IsBusy = true; // Block the send button while loading

        // Start loading in the background so your UI appears immediately
        Task.Run(() =>
        {
            try
            {
                // This is the heavy work - loading the model onto the GPU
                _chat = new ChatOrchestrator(ModelPath);

                // Updating UI properties must happen on the main thread,
                // but because we're using INotifyPropertyChanged, WPF
                // automatically marshals property change notifications
                // to the UI thread for us. That's why this is safe.
                StatusText = "Ready (DirectML)";
            }
            catch (Exception ex)
            {
                // If loading fails, show the error
                StatusText = $"Error loading model: {ex.Message}";
            }
            finally
            {
                // Done loading (success or failure)
                IsBusy = false;

                // Force a refresh of the Send button's enabled state
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
            }
        });
    }

    // ========================================================================
    // SEND MESSAGE LOGIC
    // ========================================================================

    /// <summary>
    /// Handles sending a message to the AI and displaying the response.
    /// This is called when the user clicks Send or presses Enter.
    /// </summary>
    private async Task SendAsync()
    {
        // Safety check - make sure the model is loaded
        if (_chat == null) return;

        // Get and clean up the user's input
        var text = UserInput.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Clear the input box for the next message
        UserInput = "";

        // ADD THE USER'S MESSAGE TO THE CHAT
        // ===================================
        // We show it immediately so the user knows their message was received.
        Messages.Add(
            new ChatMessage
            {
                Sender = "You",
                Text = text,
                BubbleBackground = BrushFrom("#0B0F14") // Darker background for user
            }
        );

        // UPDATE STATUS
        // =============
        IsBusy = true;
        StatusText = "Generating…";

        try
        {
            // CALL THE AI
            // ===========
            // This is where the magic happens! We send the user's text
            // to the orchestrator, which builds a prompt, calls the AI,
            // parses the response, and returns the assistant's reply.
            var (assistant, changed) = await _chat.HandleUserAsync(text);

            // ADD THE AI'S RESPONSE TO THE CHAT
            // ==================================
            Messages.Add(
                new ChatMessage
                {
                    Sender = "Assistant",
                    Text = assistant,
                    BubbleBackground = BrushFrom("#121826") // Lighter for assistant
                }
            );

            // UPDATE THE STATE DISPLAY
            // ========================
            // If the conversation state changed (new product selected,
            // email captured), refresh the UI to show the updates.
            if (changed)
            {
                OnPropertyChanged(nameof(SelectedPackagesText));
                OnPropertyChanged(nameof(CapturedEmail));
            }

            // Update status based on whether we're "done" (have email + selection)
            StatusText = _chat.State.Done ? "Done (info pack ready)" : "Ready";
        }
        catch (Exception ex)
        {
            // HANDLE ERRORS
            // =============
            // If something goes wrong, show the error in the chat.
            // This is better than crashing or showing a dialog.
            Messages.Add(
                new ChatMessage
                {
                    Sender = "Assistant",
                    Text = $"Error: {ex.Message}",
                    BubbleBackground = BrushFrom("#2B1B1B") // Reddish for errors
                }
            );
            StatusText = "Error";
        }
        finally
        {
            // Always reset the busy state when done
            IsBusy = false;
        }
    }

    // ========================================================================
    // HELPER METHODS
    // ========================================================================

    /// <summary>
    /// Converts a hex color string (like "#121826") to a WPF Brush.
    /// This is a helper so we can specify colors as strings in our code.
    /// </summary>
    private static Brush BrushFrom(string hex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    /// <summary>
    /// Cleans up resources when the ViewModel is disposed.
    /// This frees GPU memory used by the AI model.
    /// </summary>
    public void Dispose() => _chat.Dispose();

    // ========================================================================
    // INOTIFYPROPERTYCHANGED IMPLEMENTATION
    // ========================================================================
    // This is the "magic" that makes data binding work. When a property
    // changes, we fire the PropertyChanged event. WPF listens for this
    // event and automatically updates any UI elements bound to that property.

    /// <summary>
    /// Event that WPF listens to for property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Fires the PropertyChanged event to notify WPF of a change.
    /// [CallerMemberName] automatically fills in the property name,
    /// so you can just call OnPropertyChanged() without specifying it.
    /// </summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ============================================================================
// RelayCommand - A Reusable Command Implementation
// ============================================================================
//
// WHAT IS THIS?
// -------------
// ICommand is WPF's interface for handling button clicks and other actions.
// RelayCommand is a simple implementation that lets you define the action
// and the "can execute" logic inline, without creating a new class each time.
//
// WHY "RELAY"?
// ------------
// It "relays" the Execute and CanExecute calls to the functions you provide.
// It's like a middleman that connects the button to your code.
// ============================================================================

/// <summary>
/// A simple ICommand implementation for MVVM.
/// Allows async execution and provides CanExecute logic.
/// </summary>
public sealed class RelayCommand : ICommand
{
    // The function to run when the command is executed (async)
    private readonly Func<Task> _executeAsync;

    // The function that determines if the command can execute right now
    private readonly Func<bool> _canExecute;

    /// <summary>
    /// Creates a new RelayCommand.
    /// </summary>
    /// <param name="executeAsync">What to do when the command runs</param>
    /// <param name="canExecute">Returns true if the command can run right now</param>
    public RelayCommand(Func<Task> executeAsync, Func<bool> canExecute)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    /// <summary>
    /// Called by WPF to check if the button should be enabled.
    /// </summary>
    public bool CanExecute(object? parameter) => _canExecute();

    /// <summary>
    /// Called by WPF when the button is clicked.
    /// We use async void here (normally avoided) because ICommand.Execute
    /// must return void. The async work happens inside.
    /// </summary>
    public async void Execute(object? parameter) => await _executeAsync();

    /// <summary>
    /// Event that WPF listens to. When fired, WPF rechecks CanExecute.
    /// We fire this whenever the enabled state might have changed.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Call this when the CanExecute result might have changed.
    /// For example, after the user types text or when loading finishes.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
