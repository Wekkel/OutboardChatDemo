# Outboard Chat Demo

A demonstration WPF application showing how to run a local LLM (Large Language Model) using DirectML and ONNX Runtime in C#.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![WPF](https://img.shields.io/badge/WPF-Desktop-blue)
![DirectML](https://img.shields.io/badge/DirectML-GPU%20Accelerated-green)

## What Is This?

This is a sample chat application that runs an AI assistant entirely on your local machine — no cloud APIs, no internet required. It demonstrates:

- Loading and running ONNX LLM models with DirectML (GPU acceleration)
- Building a chat interface with WPF and MVVM
- Prompt engineering for structured JSON output
- Managing conversation state

The app pretends to be an "outboard motor sales assistant" but the concepts apply to any conversational AI use case.

> **Note:** This is a learning demo, not production-ready software. See the tutorial article for a full explanation of the code.

## Requirements

- **Windows 10/11** with DirectX 12 support
- **.NET 8 SDK**
- **Visual Studio 2022** (or VS Code with C# extension)
- **GPU with 12GB+ VRAM** (16GB+ recommended)
- **~10GB disk space** for the model files

## ⚠️ Important: Model Not Included

**Due to size constraints (~9GB), the LLM model files are NOT included in this repository.**

You must download the model separately and set it up yourself.

### Step 1: Create the Models Folder

Create a `Models` folder in the project root:

```
OutboardChatDemo/
├── Models/                    <-- Create this folder
│   └── (your model here)
├── ModelsApp/
├── Services/
├── ViewModels/
├── MainWindow.xaml
└── ...
```

### Step 2: Download a Model

Download a DirectML-compatible ONNX model. The code was built using:

**[Qwen3-14B-Instruct-DirectML-INT4](https://huggingface.co/wekkel/Qwen3-14B-Instruct-DirectML-INT4)**

1. Go to the Hugging Face page
2. Click "Files and versions"
3. Download ALL files
4. Place them in `Models/Qwen3-14B-Instruct-DirectML-INT4/`

Your structure should look like:

```
Models/
└── Qwen3-14B-Instruct-DirectML-INT4/
    ├── model.onnx
    ├── model.onnx.data           (~9GB - the actual weights)
    ├── genai_config.json
    ├── tokenizer.json
    ├── config.json
    └── ... (other config files)
```

### Step 3: Update the Code (If Using a Different Model)

If you use a different model, update the path in `ViewModels/MainViewModel.cs`:

```csharp
ModelPath = Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory,
    "Models",
    "Your-Model-Folder-Name"    // <-- Change this
);
```

You may also need to adjust the chat template in `Services/ChatOrchestrator.cs` if your model uses different special tokens (not all models use `<|im_start|>` and `<|im_end|>`).

## Building and Running

1. Open `OutboardChatDemo.sln` in Visual Studio 2022
2. Restore NuGet packages (should happen automatically)
3. Build the solution (Ctrl+Shift+B)
4. Run (F5)

The first launch will take 10-30 seconds as the model loads onto your GPU.

## Project Structure

```
OutboardChatDemo/
├── App.xaml                    # Application entry point
├── MainWindow.xaml             # Main UI layout
├── MainWindow.xaml.cs          # Window code-behind
├── OutboardChatDemo.csproj     # Project configuration
│
├── ModelsApp/
│   ├── BotState.cs             # Conversation state tracking
│   └── ChatMessage.cs          # Chat message data model
│
├── Services/
│   ├── ChatOrchestrator.cs     # Prompt building & response parsing
│   └── OrtxGenAiEngine.cs      # ONNX Runtime wrapper
│
├── ViewModels/
│   └── MainViewModel.cs        # MVVM ViewModel
│
└── Models/                     # YOUR MODEL GOES HERE (not in repo)
    └── (model folder)/
```

## Tutorial

For a complete walkthrough of how this code works, see the accompanying Medium article:

https://medium.com/@wekkelekkel/run-your-own-chatgpt-locally-a-c-developers-first-steps-with-directml-and-local-llms-feb20a6686c3

The source code is extensively commented to serve as a learning resource.

## Troubleshooting

**"Model not loaded" or path errors**
- Verify the `Models` folder exists and contains the model files
- Check that `ModelPath` in `MainViewModel.cs` matches your folder name

**Out of memory errors**
- Your GPU doesn't have enough VRAM
- Try a smaller quantized model (look for INT4 or smaller parameter counts)

**Slow response times**
- This is normal for large models on consumer hardware
- Response times of 5-20 seconds are typical depending on your GPU

**Model loads but outputs garbage**
- Different models use different chat templates
- Check if your model uses the same `<|im_start|>` format or adjust `ChatOrchestrator.cs`

## Acknowledgments

- [Microsoft ONNX Runtime](https://github.com/microsoft/onnxruntime) for the inference engine
- [Qwen Team](https://github.com/QwenLM/Qwen) for the base model
- The Hugging Face community for model hosting and conversion tools# OutboardChatDemo
