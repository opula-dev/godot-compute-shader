using System;
using System.Collections.Generic;
using Godot;
using Godot.ComputeShader.Reflector;

namespace Godot.ComputeShader.Pipeline;

[GlobalClass]
public partial class ComputePipeline : RefCounted
{
    private readonly RenderingDevice _rd;
    private readonly ComputeKernel[] _steps;
    private readonly Vector3I _textureSize;
    private readonly PipelineResourceManager _resourceManager;

    private bool pingPong = false;

    public ComputePipeline(
        ComputeKernel[] steps,
        Vector3I textureSize,
        Dictionary<UniformKey, RDSamplerState>? samplerStates = null)
    {
        _rd = ComputeKernelRegistry.Instance?.ComputeDevice
            ?? throw new InvalidOperationException("ComputeShaderRegistry singleton not in scene tree.");
        _steps = steps;
        _textureSize = textureSize;

        _resourceManager = new PipelineResourceManager(_rd, textureSize);

        if (PipelineAnalyzer.TryAnalyze(steps, out var analysis))
        {
            _resourceManager.Initialize(analysis, samplerStates);
        }
    }
    public void Dispatch(Dictionary<UniformKey, byte[]> updateMap)
    {
        // Update pipeline resources with the provided data map
        _resourceManager.UpdateResources(updateMap);

        // Begin recording commands into a compute list
        var computeList = _rd.ComputeListBegin();

        // Process each kernel step in sequence
        for (int i = 0; i < _steps.Length; i++)
        {
            ProcessKernel(computeList, _steps[i], updateMap, i);
        }

        // End the compute list recording
        _rd.ComputeListEnd();
        // Submit the compute list to the GPU for execution
        _rd.Submit();
        // Synchronize to wait for GPU completion
        _rd.Sync();
    }

    private void ProcessKernel(long computeList, ComputeKernel kernel, Dictionary<UniformKey, byte[]> updateMap, int stepIndex)
    {
        // Get uniform sets specific to this kernel
        var uniformSets = _resourceManager.GetUniformSetsForStep(kernel);

        // Create temporary RIDs for the uniform sets
        var uniformSetRids = CreateUniformSetRids(uniformSets, kernel.ShaderRid);

        // Bind the compute pipeline for this kernel
        _rd.ComputeListBindComputePipeline(computeList, kernel.PipelineRid);

        // Bind all uniform sets to the pipeline
        BindUniformSets(computeList, uniformSetRids);

        // Set push constant data if the kernel uses it
        SetPushConstant(computeList, kernel, updateMap, stepIndex);

        // Dispatch the compute workgroups based on texture size and local group size
        DispatchCompute(computeList, kernel);

        // Free the temporary uniform set RIDs
        FreeUniformSetRids(uniformSetRids);

        // Add a barrier to ensure previous dispatches complete before next
        _rd.ComputeListAddBarrier(computeList);

        // Flip any ping-pong resources that were written by this kernel
        FlipWrittenPingPongs(kernel);
    }

    private Dictionary<uint, Rid> CreateUniformSetRids(Dictionary<uint, Godot.Collections.Array<RDUniform>> uniformSets, Rid shaderRid)
    {
        var uniformSetRids = new Dictionary<uint, Rid>();
        foreach (var (set, uniforms) in uniformSets)
        {
            var uniformSetRid = _rd.UniformSetCreate(uniforms, shaderRid, set);
            uniformSetRids[set] = uniformSetRid;
        }
        return uniformSetRids;
    }

    private void BindUniformSets(long computeList, Dictionary<uint, Rid> uniformSetRids)
    {
        foreach (var (set, uniformSetRid) in uniformSetRids)
        {
            _rd.ComputeListBindUniformSet(computeList, uniformSetRid, set);
        }
    }

    private void SetPushConstant(long computeList, ComputeKernel kernel, Dictionary<UniformKey, byte[]> updateMap, int stepIndex)
    {
        var pushKey = new UniformKey(string.Empty, UniformRole.Push, -1, stepIndex);
        if (kernel.HasPushConstant && updateMap.TryGetValue(pushKey, out var pushData))
        {
            _rd.ComputeListSetPushConstant(computeList, pushData, (uint)pushData.Length);
        }
        else if (kernel.HasPushConstant)
        {
            GD.PushWarning($"Missing push constant data for '{pushKey}' in shader {kernel.Path}.");
            // skip for now to avoid masking issues.
        }
    }

    private void DispatchCompute(long computeList, ComputeKernel kernel)
    {
        var workGroups = ComputeKernel.CalculateWorkGroups(_textureSize, kernel.LocalSize);
        _rd.ComputeListDispatch(computeList, (uint)workGroups.X, (uint)workGroups.Y, (uint)workGroups.Z);
    }

    private void FreeUniformSetRids(Dictionary<uint, Rid> uniformSetRids)
    {
        foreach (var uniformSetRid in uniformSetRids.Values)
        {
            _rd.FreeRid(uniformSetRid);
        }
    }

    private void FlipWrittenPingPongs(ComputeKernel kernel)
    {
        var writtenPingPongs = new List<IPingPongResource>();
        foreach (var (key, _) in kernel.GetUniformMap())
        {
            if (!key.IsRole(UniformRole.Write)) { continue; }

            if (_resourceManager.TryGetResource(key, out var resource) &&
                resource is IPingPongResource pingPongResource)
            {
                writtenPingPongs.Add(pingPongResource);
            }
            else
            {
                GD.PushError($"Unable to find ping pong resource for key: {key}.");
            }
        }

        foreach (var pingPongResource in writtenPingPongs)
        {
            pingPongResource.Flip();
        }
    }

    public bool TryGetUniformResource(UniformKey key, out Rid resource)
    {
        resource = new Rid();

        if (_resourceManager.TryGetResource(key, out var pipelineResource))
        {
            resource = pipelineResource.GetRid(key);
        }

        return resource.IsValid;
    }

    public void Cleanup()
    {
        _resourceManager.Cleanup();
    }
}