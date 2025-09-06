using System;
using System.Collections.Generic;
using Godot;
using Godot.ComputeShader.Reflector;

namespace Godot.ComputeShader.Pipeline;

using UniformType = RenderingDevice.UniformType;

public readonly struct BindingInfo(UniformInfo uniformInfo, int length)
{
    public UniformInfo Uniform { get; init; } = uniformInfo;
    public ComputeKernel?[] Kernels { get; init; } = new ComputeKernel?[length];
}

public readonly struct PingPongBindingInfo
{
    public BindingInfo ReadBinding { get; init; }
    public BindingInfo WriteBinding { get; init; }
}

public static class PipelineAnalyzer
{
    public readonly struct AnalysisResult
    {
        public int MaxBinding { get; init; }
        public Dictionary<UniformKey, PingPongBindingInfo> PingPongBindings { get; init; }
        public Dictionary<UniformKey, BindingInfo> SamplerBindings { get; init; }
        public Dictionary<UniformKey, BindingInfo> TextureBindings { get; init; }
        public Dictionary<UniformKey, BindingInfo> BufferBindings { get; init; }
    }

    public static bool TryAnalyze(ComputeKernel[] steps, out AnalysisResult analysis)
    {
        analysis = default;
        var bindings = new Dictionary<UniformKey, BindingInfo>();
        int maxBinding = -1;

        for (var kIndex = 0; kIndex < steps.Length; kIndex++)
        {
            var kernel = steps[kIndex];
            foreach (var (key, info) in steps[kIndex].GetUniformMap())
            {
                if (!bindings.TryGetValue(key, out var binf))
                {
                    binf = new BindingInfo(info, steps.Length);
                    bindings[key] = binf;
                }
                // Check consistency
                else if (!binf.Uniform.Equals(info))
                {
                    GD.PushError(
                        $"Inconsistent binding {key}. Expected {info.Name} in '{kernel.Path}' " +
                        $"to match the uniform signature of {binf.Uniform.Name} in other steps.");
                }

                // Add shader-specific map info
                binf.Kernels[kIndex] = steps[kIndex];

                maxBinding = Math.Max(maxBinding, (int)info.Binding);
            }
        }

        // Detect ping-pong pairs
        var potentialReads = new Dictionary<string, BindingInfo>();
        var potentialWrites = new Dictionary<string, BindingInfo>();

        foreach (var (key, binf) in bindings)
        {
            if (key.IsRole(UniformRole.Read))
            {
                if (!potentialReads.TryAdd(key.BaseName, binf))
                {
                    GD.PushError($"Duplicate read binding for base name '{key.BaseName}' in pipeline.");
                    continue;
                }
                potentialReads[key.BaseName] = binf;
            }
            else if (key.IsRole(UniformRole.Write))
            {
                if (!potentialWrites.TryAdd(key.BaseName, binf))
                {
                    GD.PushError($"Duplicate write binding for base name '{key.BaseName}' in pipeline.");
                    continue;
                }
            }
        }

        var pingPongBindings = new Dictionary<UniformKey, PingPongBindingInfo>();

        foreach (var (baseName, readBinf) in potentialReads)
        {
            if (potentialWrites.TryGetValue(baseName, out var writeBinf) &&
                readBinf.Uniform.Format == writeBinf.Uniform.Format &&
                readBinf.Uniform.Dimension == writeBinf.Uniform.Dimension &&
                readBinf.Uniform.Set == writeBinf.Uniform.Set)
            {
                var uniformType = readBinf.Uniform.Type;
                var role = UniformKey.GetTypeRole(uniformType) | UniformRole.Read | UniformRole.Write;
                var canonicalPingPongKey = new UniformKey(baseName, role, -1, -1, canonical: true);
                pingPongBindings[canonicalPingPongKey] =
                    new PingPongBindingInfo
                    {
                        ReadBinding = readBinf,
                        WriteBinding = writeBinf,
                    };
            }
        }

        // Classify remaining bindings
        var samplerBindings = new Dictionary<UniformKey, BindingInfo>();
        var textureBindings = new Dictionary<UniformKey, BindingInfo>();
        var bufferBindings = new Dictionary<UniformKey, BindingInfo>();
        foreach (var (key, binf) in bindings)
        {
            var uniformType = binf.Uniform.Type;
            if (uniformType == UniformType.Sampler)
            {
                samplerBindings[key.AsCanonical()] = binf;
            }
            else if (
                uniformType == UniformType.Image ||
                uniformType == UniformType.Texture ||
                uniformType == UniformType.SamplerWithTexture ||
                uniformType == UniformType.SamplerWithTextureBuffer)
            {
                textureBindings[key.AsCanonical()] = binf;
            }
            else if (
                uniformType == UniformType.StorageBuffer ||
                uniformType == UniformType.UniformBuffer ||
                uniformType == UniformType.TextureBuffer)
            {
                bufferBindings[key.AsCanonical()] = binf;
            }
            // TODO: UniformType.InputAttachment is not yet implemented
        }

        analysis = new AnalysisResult
        {
            MaxBinding = maxBinding,
            PingPongBindings = pingPongBindings,
            SamplerBindings = samplerBindings,
            TextureBindings = textureBindings,
            BufferBindings = bufferBindings,
        };
        return true;
    }
}