# Godot Compute Shader Pipeline Abstraction

[![Godot Version](https://img.shields.io/badge/Godot-4.4.1-blue)](https://godotengine.org/)
[![.NET Version](https://img.shields.io/badge/.NET-9-green)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-yellow)](LICENSE)

This project offers a high-level abstraction for building and managing compute shader pipelines in Godot 4.4.1 using GLSL shaders. It handles shader compilation, resource management (including ping-pong buffers and textures for efficient data swapping), uniform binding, and dispatching compute operations.

Built with C# (.NET 9), it leverages Godot's RenderingDevice for local or global compute operations. Shaders are automatically discovered from GLSL files in your project, parsed for uniforms, and integrated into user-defined multi-step pipelines.

This repository serves as a public example on GitHub for developers looking to implement compute shaders in Godot without dealing with low-level boilerplate.

Inspired by [Acerola-Compute](https://github.com/GarrettGunnell/Acerola-Compute), but focusing more on resource management and shader reflection. Rather than GLSL abstraction and shader-file processing.

## Features

- **Automatic Shader Discovery and Compilation**: Scans your project for GLSL compute shaders and compiles them into reusable kernels, with options for eager or lazy compilation.
- **Pipeline Construction**: Chain multiple compute kernels into a pipeline, with automatic analysis for resource bindings.
- **Resource Management**: Handles creation, updating, and cleanup of textures, buffers, samplers, and ping-pong resources (for read/write swapping).
- **Uniform Parsing and Binding**: Parses GLSL for uniforms (samplers, images, textures, buffers), push constants, and local workgroup sizes. Supports arrays and access qualifiers (read/write).
- **Ping-Pong Optimization**: Detects and manages read/write pairs for efficient data flipping between pipeline steps.
- **Dispatching**: Executes the pipeline with workgroup calculation based on texture sizes and local sizes.
- **Error Handling**: Robust validation, warnings, and errors for inconsistent bindings or parsing issues.
- **Extensibility**: Modular design with helpers for custom resource types and parsers.
- **Rendering Device Options**: Supports using a local `RenderingDevice` or the global one from `RenderingServer` for integration with the render pipeline.

## Todo

- Sample Demo
- Resource Pooling
- Benchmarking
- Testing for Ensured GLSL Support
- Convert to Plugin

## Requirements

- Godot 4.4.1 Mono (stable or compatible; requires Vulkan-compatible drivers for compute shaders).
- .NET SDK 9 (for C# scripting).
- GLSL compute shaders (`.glsl` files) in your project (scans entire `res://` by default; place in subfolders like `res://shaders/` for organization).

## Installation

1. **Clone the Repository**:

   ```
   git clone https://github.com/opula-dev/godot-compute-shader.git
   ```

2. **Import into Godot**:

   - Open Godot and create a new project or add this as a submodule.
   - Copy the source files (e.g., under namespaces like `Godot.ComputeShader`) into your project's `scripts/` or `addons/` folder.
   - Add an instance of `ComputeKernelRegistry` to your scene tree (e.g., via the editor or script). Multiple instances are supported if needed (e.g., for separate rendering devices).

3. **Add Shaders**:

   - Place your GLSL compute shaders anywhere under `res://` (e.g., `res://shaders/`). File names become kernel names (e.g., `my_shader.glsl` → kernel "my_shader"). Note that `ComputeKernelRegistry` can configure its base search directory (default is `res://`).

4. **Build and Run**:

   - Build the Godot project.
   - Run the project; the registry will discover shaders on ready and compile them based on the `CompileKernelsOnReady` flag.

## How It Works

At a high level, the system follows this flow:

**Shader Discovery** (`ComputeKernelRegistry.cs`): As a `Node`, it uses `DirAccess` to scan directories like `res://` for `.glsl` files. Each file is stored as a `KernelInfo`. Kernels are compiled either eagerly (on `_Ready()` if `CompileKernelsOnReady` is true) or lazily (on first request via `TryGetKernel`). Compiled kernels are stored in a dictionary for easy access via `TryGetKernel` or `TryGetKernels`. The registry supports using the global `RenderingDevice` (via `UseGlobalRenderingDevice` export) for integration with Godot's render pipeline or creating a local rendering device for use in resource creation or baking that is not reliant on the render pipeline.

**GLSL Parsing** (`GlslShaderParser.cs` and `UniformParser.cs`): Strips comments. Uses regex parsers (from `UniformRegex.cs`) to extract uniforms with details like binding, set, format, access (read/write), and array size. Detects push constants and local workgroup sizes (`LocalGroupParser.cs`). This creates `UniformInfo` records for binding checks.

**Uniform Keys** (`UniformKey.cs`): Immutable structs with roles (e.g., `UniformRole.Read`, `Write`, `Array`). Parses from suffixes like `_read` or `[index]`. Validation via `UniformKeyValidator.cs` ensures correctness. Canonical forms from `AsCanonical()` aid lookups.

**Pipeline Analysis** (`PipelineAnalyzer.cs`): Scans uniforms to build `AnalysisResult` with bindings. Detects ping-pong by matching read/write pairs (e.g., based on name, format, set). Classifies into samplers, textures, buffers for creation.

**Resource Management** (`PipelineResourceManager.cs` and `PipelineResource.cs`): Handles RIDs for resources. Subclasses of `PipelineResource` manage specific uniform types:

- `TextureResource` and `BufferResource` create/update with `ResourceHelper.cs`.
- Ping-pong variants implement `IPingPongResource` for RID flipping without copies, optimizing multi-step pipelines.
- Dirty checks use hashing (`XxHash32`) to skip unchanged updates.

**Execution** (`ComputePipeline.cs`): In `Dispatch()`, updates resources. Begins a compute list. Processes each kernel (binds sets, sets push constants, dispatches). Ends the list, submits, and syncs. Workgroups calculated as `(textureSize + localSize - 1) / localSize`.

## Usage

### 1. Setting Up the Registry

Instance `ComputeKernelRegistry` and add it to your scene tree. Configure exports like `UseGlobalRenderingDevice` (for render pipeline integration) and `CompileKernelsOnReady` (for eager vs. lazy compilation).

```csharp
// In your main scene or script (e.g., in _Ready())
var registry = new ComputeKernelRegistry();
AddChild(registry); // Ensures _Ready() is called for initialization

// Or add via editor and access:
var registry = GetNode<ComputeKernelRegistry>("ComputeKernelRegistry");
```

### 2. Creating a Compute Pipeline

Fetch kernels by name and construct a pipeline. Provide texture sizes for workgroup dispatch and optional sampler states.

```csharp
using Godot.ComputeShader;
using Godot.ComputeShader.Pipeline;

// Example: Chain two kernels for image processing
string[] kernelNames = { "step1_process", "step2_filter" };
var textureSize = new Vector3I(1024, 1024, 1); // 2D texture example

// Optional: Custom sampler states for specific uniforms
var samplerKey = new UniformKey("my_sampler", UniformRole.Sampler);
var samplerState = new RDSamplerState { MagFilter = RenderingDevice.SamplerFilter.Linear };
var samplerStates = new Dictionary<UniformKey, RDSamplerState>
{
    { samplerKey, samplerState }
};

var pipeline = new ComputePipeline(registry, kernelNames, textureSize, samplerStates);
```

### 3. Updating Resources and Dispatching

Prepare a map of uniform keys to data (byte arrays). Keys support roles like `Read`, `Write`, arrays (via `ArrayIndex`), and push constants (via `step`). Base names exclude suffixes like `_read`. Prepare data in format-compatible bytes (e.g., RGBA8 for images).

```csharp
var readTextureRole = UniformRole.Texture | UniformRole.Read;
var arrayTextureRole = UniformRole.Texture | UniformRole.Array;

var readTextureKey = new UniformKey("texture", role: readTextureRole);
var paramBufferKey = new UniformKey("param_buffer", role: UniformRole.Buffer);
var pushConstKeyS0 = new UniformKey(role: UniformRole.Push, step: 0);
var arrayTexKeyIx0 = new UniformKey("array_texture", role: arrayTextureRole, arrayIndex: 0);
var arrayTexKeyIx1 = new UniformKey("array_texture", role: arrayTextureRole, arrayIndex: 1)

var updateMap = new Dictionary<UniformKey, byte[]>
{
    // texture_read uniform
    { readTextureKey, texture },

    // param_buffer uniform
    { paramBufferKey, paramBuffer },

    // Push Constant uniform for step 0
    { pushConstKeyS0, pushConstant },

    // texture_array[2] uniform
    // index 0
    { arrayTexKeyIx0, textureIx0 },
    // index 1
    { arrayTexKeyIx1, textureIx1 }
};

// Dispatch the pipeline
pipeline.Dispatch(updateMap, /* optional - sync: true */);

// Retrieve output (e.g., after sync if needed)
if (pipeline.TryGetUniformResource(new UniformKey("texture", UniformRole.Texture | UniformRole.Write), out var rid))
{
    // Use rid for CPU-side data management.
    // Rid's are GPU-side memory and pulling down data requires RenderDevice syncing.
    // However, syncing can take a while and should not be used in high-frequency.
    // Example: byte[] outputData = registry.ComputeDevice!.TextureGetData(rid, 0); // After sync
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
- Ping-pong: Use matching `_read` and `_write` suffixes for paired resources with the same base name (e.g., `image2D texture_read` and `image2D texture_write`).

Example shader (`step1_process.glsl`):

```glsl
#version 450
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(set = 0, binding = 0, rgba8) uniform readonly image2D texture_read;
layout(set = 0, binding = 1, rgba8) uniform writeonly image2D texture_write;

void main() {
    ivec2 coord = ivec2(gl_GlobalInvocationID.xy);
    vec4 color = imageLoad(texture_read, coord);
    // Process color...
    imageStore(tex_writexture_writete, coord, color);
}
```

### Troubleshooting

- **Kernel Not Found**: Ensure `.glsl` files are in `res://` (or configured directory) and names match (case-sensitive).
- **Invalid RID**: Check for compilation errors (`GD.PushError` logs them).
- **Ping-Pong Not Detected**: Verify same base name, format, dimension, and set for read/write pairs.
- **Performance Issues**: Avoid frequent syncs; use local `RenderingDevice` for isolated compute. Keep operations on the GPU as much as possible.

## License

MIT License - see [LICENSE](LICENSE) for details.
