using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace Godot.ComputeShader;

public readonly record struct KernelInfo
{
    public string Name { get; init; }
    public string Path { get; init; }
}

[GlobalClass]
public partial class ComputeKernelRegistry : Node
{
    public static readonly string KernelStorageBasePath = "res://";
    public static readonly string[] KernelFileExtensions = ["glsl"];

    private static readonly Dictionary<string, KernelInfo> s_kernelInformation = [];

    public RenderingDevice? ComputeDevice => _rd;
    private RenderingDevice? _rd;
    private readonly Dictionary<string, ComputeKernel> _kernels = [];

    /// <summary>
    /// Whether or not this registry's compute shader operate on the RenderServer's
    /// main rendering device or creates a new local rendering device.
    /// <br/> <br/>
    /// This is important for if you want GPU resources to be sharable with 
    /// the Godot render pipeline like material shaders.
    /// </summary>
    [Export]
    public bool UseGlobalRenderingDevice { get; set; } = false;

    /// <summary>
    /// Whether or not to compile compute kernels when the registry is ready 
    /// on the tree or when a pipeline requests a discovered kernel.
    /// </summary>
    [Export]
    public bool CompileKernelsOnReady { get; set; } = false;

    public override void _Ready()
    {
        if (UseGlobalRenderingDevice)
        {
            _rd = RenderingServer.GetRenderingDevice();
        }
        else
        {
            _rd = RenderingServer.CreateLocalRenderingDevice();
        }

        if (_rd is null)
        {
            GD.PushError($"Failed to {(UseGlobalRenderingDevice ? "aquire" : "create")} RenderingDevice.");
            return;
        }

        DiscoverKernels(KernelStorageBasePath);

        if (CompileKernelsOnReady)
        {
            CompileKernels();
        }
    }

    public override void _ExitTree()
    {
        Cleanup();
    }

    public bool TryGetKernel(string kernelName, out ComputeKernel? kernel)
    {
        if (_kernels.TryGetValue(kernelName, out kernel))
        {
            return true;
        }

        CompileKernel(kernelName);

        return _kernels.TryGetValue(kernelName, out kernel);
    }

    public bool TryGetKernels(string[] kernelNames, out ComputeKernel[] kernels)
    {
        kernels = new ComputeKernel[kernelNames.Length];
        for (var i = 0; i < kernelNames.Length; i++)
        {
            _kernels.TryGetValue(kernelNames[i], out var kernel);
            if (kernel is null)
            {
                kernels = [];
                break;
            }
            kernels[i] = kernel;
        }

        return kernels.Length != 0;
    }

    private void Cleanup()
    {
        if (_rd is null)
        {
            GD.PushError($"{nameof(ComputeKernelRegistry)}.Cleanup() called without a render device.");
        }
        else
        {
            foreach (var (_, kernel) in _kernels)
            {
                kernel.Free(_rd);
            }
        }
        _kernels.Clear();
    }

    private void CompileKernels()
    {
        foreach (var kernelName in s_kernelInformation.Keys)
        {
            CompileKernel(kernelName);
        }
    }

    private void CompileKernel(string kernelName)
    {
        if (!s_kernelInformation.TryGetValue(kernelName, out var info))
        {
            GD.PushError($"Kernel '{kernelName}' not found in project");
            return;
        }

        var kernel = new ComputeKernel(_rd!, info);

        if (!kernel.IsValid)
        {
            GD.PushError($"Skipping invalid shader at {kernel.Path}");
        }

        if (!_kernels.TryAdd(kernel.Name, kernel))
        {
            GD.PushError($"Skipping duplicate named shader at {kernel.Path}");
        }
    }

    private static void DiscoverKernels(string path)
    {
        DirAccess? iterator = DirAccess.Open(path);

        if (iterator is null)
        {
            GD.PushWarning($"DiscoverKernels(string) - Invalid file path '{path}'");
            return;
        }

        iterator.ListDirBegin();
        var filename = iterator.GetNext();
        while (!string.IsNullOrEmpty(filename))
        {
            if (iterator.CurrentIsDir())
            {
                DiscoverKernels(Path.Combine(path, filename));
            }
            else if (IsKernelFile(filename))
            {
                var info = new KernelInfo()
                {
                    Name = filename.GetBaseName(),
                    Path = Path.Combine(path, filename),
                };
                if (!s_kernelInformation.TryAdd(info.Name, info))
                {
                    GD.PushError($"Duplicate kernel name '{info.Name}' found at {info.Path}");
                }
            }
            filename = iterator.GetNext();
        }
    }

    private static bool IsKernelFile(string filename)
    {
        var extension = filename.GetExtension();

        return KernelFileExtensions.Any(kernelExtension =>
            extension.Equals(kernelExtension, StringComparison.OrdinalIgnoreCase));
    }

}