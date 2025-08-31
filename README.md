# Godot Compute Shader Pipeline Abstraction

[![Godot Version](https://img.shields.io/badge/Godot-4.4.1-blue)](https://godotengine.org/)
[![.NET Version](https://img.shields.io/badge/.NET-9-green)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-yellow)](LICENSE)

This project provides a high-level abstraction for building and managing compute shader pipelines in Godot 4.4.1 using GLSL shaders. It handles shader compilation, resource management (including ping-pong buffers/textures for efficient data swapping), uniform binding, and dispatching compute operations. The system is designed for performance-critical applications like simulations, image processing, or render post-processing in Godot.

Built with C# (.NET 9), it leverages Godot's `RenderingDevice` for local compute operations. Shaders are automatically discovered from GLSL files in your project, parsed for uniforms, and integrated into user-defined multi-step pipelines.

This repository serves as a public example on GitHub for developers looking to implement compute shaders in Godot without dealing with low-level boilerplate.

## Features

- **Automatic Shader Discovery and Compilation**: Scans your project for GLSL compute shaders and compiles them into reusable kernels.
- **Pipeline Construction**: Chain multiple compute kernels into a pipeline, with automatic analysis for resource bindings.
- **Resource Management**: Handles creation, updating, and cleanup of textures, buffers, samplers, and ping-pong resources (for read/write swapping).
- **Uniform Parsing and Binding**: Parses GLSL for uniforms (samplers, images, textures, buffers), push constants, and local workgroup sizes. Supports arrays and access qualifiers (read/write).
- **Ping-Pong Optimization**: Detects and manages read/write pairs for efficient data flipping between pipeline steps.
- **Dispatching**: Executes the pipeline with workgroup calculation based on texture sizes and local sizes.
- **Error Handling**: Robust validation, warnings, and errors for inconsistent bindings or parsing issues.
- **Extensibility**: Modular design with helpers for custom resource types and parsers.

## Todo

* Sample Demo
* Resource Pooling
* Benchmarking
* Testing for Ensured GLSL Support
* Maybe Rewrite for GDScript

## Requirements

- Godot 4.4.1 Mono (stable or compatible)
- .NET SDK 9 (for C# scripting)
- GLSL compute shaders (`.glsl` files) in your project (e.g., under `res://shaders/`)

## Installation

1. **Clone the Repository**:

   ```
   git clone https://github.com/opula-dev/godot-compute-shader.git
   ```
2. **Import into Godot**:

   - Open Godot and create a new project or add this as a submodule.
   - Copy the source files (e.g., `ComputeKernelRegistry.cs`, `ComputePipeline.cs`, etc.) into your project's `scripts/` or `addons/` folder.
   - Ensure `ComputeKernelRegistry` is added to your scene tree as a singleton (autoload) for automatic initialization.
3. **Add Shaders**:

   - Place your GLSL compute shaders in `res://shaders/` (or any subdirectory). File names become kernel names (e.g., `my_shader.glsl` → kernel "my_shader").
4. **Build and Run**:

   - Godot will compile the C# scripts automatically.
   - Run the project; the registry will discover and compile shaders on ready.

## Usage

### 1. Setting Up the Registry

Add `ComputeKernelRegistry` as an autoload singleton in your project settings. It creates a local `RenderingDevice` and compiles all discovered shaders.

```csharp
// In your main scene or script
var registry = GetNode<ComputeKernelRegistry>("/root/ComputeKernelRegistry");
```

### 2. Creating a Compute Pipeline

Fetch kernels by name and construct a pipeline. Provide texture sizes for workgroup dispatch and optional sampler states.

```csharp
using Godot.ComputeShader;
using Godot.ComputeShader.Pipeline;

// Example: Chain two kernels for image processing
string[] kernelNames = { "step1_process", "step2_filter" };
if (registry.TryGetKernels(kernelNames, out var kernels))
{
    var textureSize = new Vector3I(1024, 1024, 1); // 2D texture example

    // Optional: Custom sampler states for specific uniforms
    var samplerStates = new Dictionary<UniformKey, RDSamplerState>
    {
        { new UniformKey("my_sampler"), new RDSamplerState { MagFilter = RenderingDevice.SamplerFilter.Linear } }
    };

    var pipeline = new ComputePipeline(kernels, textureSize, samplerStates);
}
else
{
    GD.PushError("Failed to load kernels.");
}
```

### 3. Updating Resources and Dispatching

Prepare a map of uniform keys to data (byte arrays). Keys support roles like `_read`, `_write`, arrays `[index]`, and push constants `push_<step>`.

```csharp
var updateMap = new Dictionary<UniformKey, byte[]>
{
    // Texture data (e.g., input image as byte[])
    { new UniformKey("input_texture_read"), inputData },

    // Buffer data
    { new UniformKey("params_buffer"), paramBytes },

    // Push constant for step 0
    { new UniformKey(string.Empty, UniformRole.Push, -1, 0), pushDataStep0 },

    // Array example
    { new UniformKey("array_texture[0]"), arrayData0 },
    { new UniformKey("array_texture[1]"), arrayData1 }
};

// Dispatch the pipeline
pipeline.Dispatch(updateMap);

// Retrieve output (e.g., after sync if needed)
if (pipeline.TryGetOutputTexture(new UniformKey("output_texture_write"), out var outputData))
{
    // Process outputData (byte[])
}
```

### 4. Cleanup

Call `Cleanup()` when done to free resources.

```csharp
pipeline.Cleanup();
```

### GLSL Shader Guidelines

- Use standard GLSL compute syntax.
- Define uniforms with bindings (e.g., `layout(set=0, binding=0) uniform sampler2D my_sampler;`).
- Support for push constants: `layout(push_constant) uniform Push { ... } push;`.
- Local workgroup size: `layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;`.
- Ping-pong: Use matching `_read` and `_write` suffixes for paired resources (e.g., `image2D tex_read` and `image2D tex_write`).

Example shader (`step1_process.glsl`):

```glsl
#version 450
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(set = 0, binding = 0, rgba8) uniform readonly image2D input_read;
layout(set = 0, binding = 1, rgba8) uniform writeonly image2D output_write;

void main() {
    ivec2 coord = ivec2(gl_GlobalInvocationID.xy);
    vec4 color = imageLoad(input_read, coord);
    // Process color...
    imageStore(output_write, coord, color);
}
```

## License

MIT License - see [LICENSE](LICENSE) for details.
