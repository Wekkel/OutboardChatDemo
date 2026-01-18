// ============================================================================
// OrtxGenAiEngine.cs - The AI Engine Wrapper
// ============================================================================
//
// WHAT DOES THIS FILE DO?
// -----------------------
// This is the "brain connector" of our app. It wraps Microsoft's ONNX Runtime
// GenAI library, which lets us run large language models (LLMs) locally on
// your computer using your GPU.
//
// WHAT IS ONNX?
// -------------
// ONNX (Open Neural Network Exchange) is a universal format for AI models.
// Think of it like PDF for documents - it works everywhere. Models from
// PyTorch, TensorFlow, or other frameworks can be converted to ONNX and
// run on any device that supports it.
//
// WHAT IS DIRECTML?
// -----------------
// DirectML is Microsoft's way of running AI on GPUs. It works with AMD,
// Intel, and NVIDIA graphics cards through DirectX 12. This means you don't
// need a specific brand of GPU to run this code!
//
// HOW DOES TEXT GENERATION WORK?
// ------------------------------
// 1. TOKENIZATION: The text you type is converted into numbers (tokens).
//    For example, "Hello, how are you?" might become [15496, 11, 703, 527, 499, 30]
//
// 2. GENERATION: The model predicts the next token, one at a time.
//    It's like autocomplete on steroids - it keeps predicting the next word
//    until it decides it's done or hits a limit.
//
// 3. DECODING: The generated tokens are converted back into readable text.
//
// This file handles all three steps, hiding the complexity from the rest
// of our application.
// ============================================================================

using Microsoft.ML.OnnxRuntimeGenAI;
using System.Text;

namespace OutboardChatDemo.Services;

/// <summary>
/// Wraps ONNX Runtime GenAI to provide a simple interface for text generation.
/// This class handles loading the model, tokenizing input, generating responses,
/// and cleaning up resources when done.
/// </summary>
public sealed class OrtxGenAiEngine : IDisposable
{
    // The loaded AI model. This is the actual neural network that generates text.
    // It's nullable because it might not be loaded yet (or might fail to load).
    private Model? _model;

    // The tokenizer converts text to/from token IDs.
    // Models don't understand text directly - they work with numbers (tokens).
    // The tokenizer is the translator between human text and model numbers.
    private Tokenizer? _tokenizer;

    // Stores the path to the model folder for reference.
    // Useful for debugging and displaying in the UI.
    public string? ModelFolderPath { get; private set; }

    /// <summary>
    /// Constructor - creates the engine and loads the model immediately.
    /// </summary>
    /// <param name="modelFolderPath">
    /// Path to the folder containing the model files.
    /// This folder should contain:
    /// - model.onnx (or model.onnx + model.onnx.data for large models)
    /// - genai_config.json (generation settings)
    /// - tokenizer.json (vocabulary and tokenization rules)
    /// - Other config files the model needs
    /// </param>
    public OrtxGenAiEngine(string modelFolderPath)
    {
        LoadModel(modelFolderPath);
    }

    /// <summary>
    /// Loads (or reloads) the model from the specified folder.
    /// This is separate from the constructor so you could theoretically
    /// switch models without creating a new engine instance.
    /// </summary>
    private void LoadModel(string modelFolderPath)
    {
        // Dispose any existing model/tokenizer first to free GPU memory.
        // Large models can use several gigabytes of GPU memory!
        // The ?. operator means "only call Dispose if not null".
        _tokenizer?.Dispose();
        _model?.Dispose();

        // Load the model from the folder.
        // This reads the ONNX model file and sets up the neural network
        // on your GPU (via DirectML). This can take several seconds for
        // large models like Qwen3-14B (which has 14 billion parameters!).
        _model = new Model(modelFolderPath);

        // Create a tokenizer from the model.
        // The tokenizer uses vocabulary files in the model folder to know
        // how to convert text to tokens and back.
        _tokenizer = new Tokenizer(_model);
    }

    /// <summary>
    /// Generates text based on a prompt. This is the main method you'll use!
    /// </summary>
    /// <param name="prompt">
    /// The input text to send to the model. For chat models, this includes
    /// special formatting (like chat templates) that tells the model how
    /// to respond.
    /// </param>
    /// <param name="maxNewTokens">
    /// Maximum number of tokens to generate. More tokens = longer response
    /// but also more time. Default is 256, which is about 150-200 words.
    /// </param>
    /// <param name="temperature">
    /// Controls randomness. Range is 0.0 to 2.0.
    /// - 0.0 = always pick the most likely next word (deterministic)
    /// - 0.2 = slight variation, good for factual responses (our default)
    /// - 1.0 = balanced randomness
    /// - 2.0 = very random, creative but might not make sense
    /// </param>
    /// <param name="topP">
    /// "Nucleus sampling" - only consider tokens that make up the top P% of
    /// probability. 0.9 means "only consider tokens in the top 90%".
    /// This prevents the model from picking very unlikely words.
    /// </param>
    /// <returns>The generated text response from the model.</returns>
    public async Task<string> GenerateAsync(
        string prompt,
        int maxNewTokens = 256,
        double temperature = 0.2,
        double topP = 0.9
    )
    {
        // We use Task.Run to move the heavy computation to a background thread.
        // This keeps the UI responsive while the model is "thinking".
        // Without this, the app would freeze during generation!
        return await Task.Run(() =>
        {
            // Safety check: make sure the model is actually loaded
            if (_model is null || _tokenizer is null)
                throw new InvalidOperationException("Model not loaded.");

            // STEP 1: TOKENIZATION
            // Convert the prompt text into token IDs that the model understands.
            // inputSeq is a sequence of numbers representing the input text.
            using var inputSeq = _tokenizer.Encode(prompt);

            // STEP 2: SET UP GENERATION PARAMETERS
            // GeneratorParams tells the model how to generate text.
            using var genParams = new GeneratorParams(_model);

            // Get the length of our input prompt (in tokens, not characters).
            // We need this to set the maximum total length.
            var promptLen = inputSeq[0].Length;

            // Set the maximum total length (input + output).
            // If prompt is 100 tokens and maxNewTokens is 256, max_length is 356.
            genParams.SetSearchOption("max_length", (double)(promptLen + maxNewTokens));

            // Set temperature for randomness control (explained above)
            genParams.SetSearchOption("temperature", temperature);

            // Set top_p for nucleus sampling (explained above)
            genParams.SetSearchOption("top_p", topP);

            // Enable sampling (random selection based on probabilities).
            // If false, it would always pick the highest probability token.
            genParams.SetSearchOption("do_sample", true);

            // STEP 3: CREATE THE GENERATOR
            // The Generator is the thing that actually produces tokens one by one.
            using var generator = new Generator(_model, genParams);

            // Give the generator our input tokens to start from
            generator.AppendTokenSequences(inputSeq);

            // Create a streaming decoder to convert tokens back to text.
            // "Stream" means it can decode tokens one at a time as they're generated.
            using var stream = _tokenizer.CreateStream();

            // StringBuilder collects the generated text efficiently.
            // It's faster than concatenating strings with + in a loop.
            var sb = new StringBuilder();

            // STEP 4: GENERATE TOKENS ONE BY ONE
            // This is the core generation loop. It keeps going until the model
            // outputs a "stop" token (like end-of-sentence) or hits max length.
            while (!generator.IsDone())
            {
                // Generate the next token. This is where the GPU does its magic!
                // The model looks at all previous tokens and predicts the next one.
                generator.GenerateNextToken();

                // Get the generated sequence (all tokens so far)
                var seq = generator.GetSequence(0);

                // Decode just the last token (^1 means "last element" in C#)
                // and append it to our result string
                sb.Append(stream.Decode(seq[^1]));
            }

            // Return the complete generated text
            return sb.ToString();
        });
    }

    /// <summary>
    /// Cleans up resources when the engine is no longer needed.
    /// This is VERY important for GPU resources! Failing to dispose
    /// the model can leave GPU memory allocated, eventually crashing
    /// your system or other GPU applications.
    /// </summary>
    public void Dispose()
    {
        _tokenizer?.Dispose();
        _model?.Dispose();
    }
}
