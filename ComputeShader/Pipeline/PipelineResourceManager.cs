using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Godot;
using Godot.ComputeShader.Reflector;

namespace Godot.ComputeShader.Pipeline;

public partial class PipelineResourceManager(RenderingDevice rd, Vector3I textureSize) : RefCounted
{
    private readonly RenderingDevice rd = rd;
    private readonly Vector3I textureSize = textureSize;
    private readonly Dictionary<UniformKey, PipelineResource> resourceMap = [];

    // Max binding index
    private int maxBinding = -1;

    private static (bool IsDirty, int NewHash) CheckBufferDirtyAndGetHash(byte[] newData, int lastEntry)
    {
        if (newData.Length == 0)
        {
            return (lastEntry == 0, 0);
        }

        var hc = new HashCode();
        hc.AddBytes(newData);
        int newHash = hc.ToHashCode();

        bool isDirty = lastEntry != newHash;

        return (isDirty, newHash);
    }
    public void Initialize(
        PipelineAnalyzer.AnalysisResult analysis,
        Dictionary<UniformKey, RDSamplerState>? samplerStates = null)
    {
        // Create ping-pong resources
        foreach (var (key, pingPong) in analysis.PingPongBindings)
        {
            var resource = new PingPongTextureResource(rd, pingPong, textureSize);
            resource.Initialize();
            resourceMap[key] = resource;
        }

        // Create single textures
        foreach (var (key, texture) in analysis.TextureBindings)
        {
            var resource = new TextureResource(rd, texture, textureSize);
            resource.Initialize();
            resourceMap[key] = resource;
        }

        // Create samplers
        foreach (var (key, sampler) in analysis.SamplerBindings)
        {
            RDSamplerState? state = null;
            samplerStates?.TryGetValue(key, out state);
            var resource = new SamplerResource(rd, sampler, state);
            resource.Initialize();
            resourceMap[key] = resource;
        }

        // Create buffers (lazy, so just add placeholders)
        foreach (var (key, buffer) in analysis.BufferBindings)
        {
            var resource = new BufferResource(rd, buffer);
            resource.Initialize(); // No-op, but consistent
            resourceMap[key] = resource;
        }
    }

    public void UpdateResources(Dictionary<UniformKey, byte[]> updateMap)
    {
        foreach (var (key, data) in updateMap)
        {
            if (resourceMap.TryGetValue(UniformKey.GetCanonical(key), out var resource))
            {
                resource.Update(data, key);
            }
            else
            {
                GD.PushWarning($"No resource found for update key: {key}");
            }
        }
    }

    public Dictionary<uint, Godot.Collections.Array<RDUniform>> GetUniformSetsForStep(ComputeKernel shader)
    {
        var uniformSets = new Dictionary<uint, Godot.Collections.Array<RDUniform>>();
        var shaderUniformMap = shader.GetUniformMap();

        foreach (var (key, info) in shaderUniformMap)
        {
            var set = info.Set;
            if (!uniformSets.TryGetValue(set, out var resourceUniforms))
            {
                resourceUniforms = [];
                uniformSets[set] = resourceUniforms;
            }

            var resourceUniform = new RDUniform
            {
                Binding = (int)info.Binding,
                UniformType = info.Type
            };

            if (TryGetResource(UniformKey.GetCanonical(key), out var resource))
            {
                if (key.IsRole(UniformRole.Array))
                {
                    for (var i = 0; i < resource.ArraySize; i++)
                    {
                        resourceUniform.AddId(resource.GetRid(key with { ArrayIndex = i }));
                    }
                }
                else
                {
                    resourceUniform.AddId(resource.GetRid(key));
                }

                resourceUniforms.Add(resourceUniform);
            }
            else
            {
                GD.PushError($"Unable to create uniform for {key}. Resource not found.");
            }
        }

        return uniformSets;
    }

    public bool TryGetResource(UniformKey key, [MaybeNullWhen(false)] out PipelineResource resource)
    {
        if (resourceMap.TryGetValue(UniformKey.GetCanonical(key), out resource))
        {
            return true;
        }
        return false;
    }

    public void Cleanup()
    {
        foreach (var (_, resource) in resourceMap)
        {
            resource.Cleanup();
        }

        resourceMap.Clear();
    }

    private static string StripSuffix(string name)
    {
        if (name.EndsWith("_read")) return name[..^5];
        if (name.EndsWith("_write")) return name[..^6];
        return name;
    }
}