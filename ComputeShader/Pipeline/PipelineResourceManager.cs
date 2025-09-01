using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Godot;
using Godot.ComputeShader.Reflector;

namespace Godot.ComputeShader.Pipeline;

public partial class PipelineResourceManager(RenderingDevice rd, Vector3I textureSize) : RefCounted
{
    private readonly RenderingDevice _rd = rd;
    private readonly Vector3I _textureSize = textureSize;
    private readonly Dictionary<UniformKey, PipelineResource> _resourceMap = [];

    // Max binding index
    private int _maxBinding = -1;

    public void Initialize(
        PipelineAnalyzer.AnalysisResult analysis,
        Dictionary<UniformKey, RDSamplerState>? samplerStates = null)
    {
        // Create ping-pong resources
        foreach (var (key, pingPong) in analysis.PingPongBindings)
        {
            var resource = new PingPongTextureResource(_rd, pingPong, _textureSize);
            resource.Initialize();
            _resourceMap[key] = resource;
        }

        // Create single textures
        foreach (var (key, texture) in analysis.TextureBindings)
        {
            var resource = new TextureResource(_rd, texture, _textureSize);
            resource.Initialize();
            _resourceMap[key] = resource;
        }

        // Create samplers
        foreach (var (key, sampler) in analysis.SamplerBindings)
        {
            RDSamplerState? state = null;
            samplerStates?.TryGetValue(key, out state);
            var resource = new SamplerResource(_rd, sampler, state);
            resource.Initialize();
            _resourceMap[key] = resource;
        }

        // Create buffers (lazy, so just add placeholders)
        foreach (var (key, buffer) in analysis.BufferBindings)
        {
            var resource = new BufferResource(_rd, buffer);
            resource.Initialize(); // No-op, but consistent
            _resourceMap[key] = resource;
        }
    }

    public void UpdateResources(Dictionary<UniformKey, byte[]> updateMap)
    {
        foreach (var (key, data) in updateMap)
        {
            if (_resourceMap.TryGetValue(key.AsCanonical(), out var resource))
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

            if (TryGetResource(key.AsCanonical(), out var resource))
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
        if (_resourceMap.TryGetValue(key.AsCanonical(), out resource))
        {
            return true;
        }
        return false;
    }

    public void Cleanup()
    {
        foreach (var (_, resource) in _resourceMap)
        {
            resource.Cleanup();
        }

        _resourceMap.Clear();
    }
}