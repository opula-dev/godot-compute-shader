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

    public static ComputeKernelRegistry? Instance => _instance;
    private static ComputeKernelRegistry? _instance;

    public RenderingDevice? ComputeDevice => _rd;
    private RenderingDevice? _rd;

    private readonly Dictionary<string, ComputeKernel> _kernels = [];
    private readonly List<KernelInfo> _discoveredKernels = [];

    public override void _EnterTree()
    {
        if (_instance != null)
        {
            GD.PushError($"Duplicate {nameof(ComputeKernelRegistry)} detected; freeing this instance.");
            QueueFree();
            return;
        }

        _instance = this;
    }

    public override void _Ready()
    {
        _rd = RenderingServer.CreateLocalRenderingDevice();
        if (_rd == null)
        {
            GD.PushError("Failed to create local RenderingDevice.");
            return;
        }
        DiscoverKernels(KernelStorageBasePath);
        CompileKernels();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete || what == NotificationWMCloseRequest)
        {
            Cleanup();
        }
    }

    public bool TryGetKernel(string kernelName, out ComputeKernel? kernel)
    {
        return _kernels.TryGetValue(kernelName, out kernel);
    }

    public bool TryGetKernels(string[] kernelNames, out ComputeKernel[]? kernels)
    {
        kernels = new ComputeKernel[kernelNames.Length];
        for (var i = 0; i < kernelNames.Length; i++)
        {
            _kernels.TryGetValue(kernelNames[i], out var kernel);
            if (kernel is null)
            {
                kernels = null;
                break;
            }
            kernels[i] = kernel;
        }

        return kernels is not null;
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
        _instance = null;
    }

    private void CompileKernels()
    {
        foreach (var info in _discoveredKernels)
        {
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
    }

    private void DiscoverKernels(string path)
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
                _discoveredKernels.Add(info);
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