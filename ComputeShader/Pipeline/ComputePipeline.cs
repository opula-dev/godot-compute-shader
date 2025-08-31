using System;
using System.Collections.Generic;
using Godot;
using Godot.ComputeShader.Reflector;

namespace Godot.ComputeShader.Pipeline;

[GlobalClass]
public partial class ComputePipeline : RefCounted
{
    private readonly RenderingDevice rd;
    private readonly ComputeKernel[] steps;
    private readonly Vector3I textureSize;
    private readonly PipelineResourceManager resourceManager;

    private bool pingPong = false;

    public ComputePipeline(
        ComputeKernel[] steps,
        Vector3I textureSize,
        Dictionary<UniformKey, RDSamplerState>? samplerStates = null)
    {
        rd = ComputeKernelRegistry.Instance?.ComputeDevice
            ?? throw new InvalidOperationException("ComputeShaderRegistry singleton not in scene tree.");
        this.steps = steps;
        this.textureSize = textureSize;

        resourceManager = new PipelineResourceManager(rd, textureSize);

        if (PipelineAnalyzer.TryAnalyze(steps, out var analysis))
        {
            resourceManager.Initialize(analysis, samplerStates);
        }
    }

    public void Dispatch(Dictionary<UniformKey, byte[]> updateMap)
    {
        resourceManager.UpdateResources(updateMap);

        var computeList = rd.ComputeListBegin();

        for (int i = 0; i < steps.Length; i++)
        {
            var kernel = steps[i];

            // Get uniforms grouped by set for this step
            var uniformSets = resourceManager.GetUniformSetsForStep(kernel);

            // Create uniform set RIDs
            var uniformSetRids = new Dictionary<uint, Rid>();
            foreach (var (set, uniforms) in uniformSets)
            {
                var uniformSetRid = rd.UniformSetCreate(uniforms, kernel.ShaderRid, set);
                uniformSetRids[set] = uniformSetRid;
            }

            rd.ComputeListBindComputePipeline(computeList, kernel.PipelineRid);

            // Bind all uniform sets
            foreach (var (set, uniformSetRid) in uniformSetRids)
            {
                rd.ComputeListBindUniformSet(computeList, uniformSetRid, set);
            }

            // Set push constant if present
            var pushKey = new UniformKey(string.Empty, UniformRole.Push, -1, i);
            if (kernel.HasPushConstant && updateMap.TryGetValue(pushKey, out var pushData))
            {
                rd.ComputeListSetPushConstant(computeList, pushData, (uint)pushData.Length);
            }
            else
            {
                GD.PushWarning($"Missing push constant data for '{pushKey}' in shader {kernel.Path}.");
                // skip for now to avoid masking issues.
            }

            // Dispatch
            var workGroups = ComputeKernel.CalculateWorkGroups(textureSize, kernel.LocalSize);
            rd.ComputeListDispatch(computeList, (uint)workGroups.X, (uint)workGroups.Y, (uint)workGroups.Z);

            // Free temp uniform set RIDs
            foreach (var uniformSetRid in uniformSetRids.Values)
            {
                rd.FreeRid(uniformSetRid);
            }

            // Add barrier
            rd.ComputeListAddBarrier(computeList);

            // Swap ping-pong only for groups written by this step
            var writtenPingPongs = new List<PingPongTextureResource>();
            foreach (var (key, _) in kernel.GetUniformMap())
            {
                if (!key.IsRole(UniformRole.Write)) { continue; }

                if (resourceManager.TryGetResource(key, out var resource) &&
                    resource is PingPongTextureResource pingPongTextureResource)
                {
                    writtenPingPongs.Add(pingPongTextureResource);
                }
                else
                {
                    GD.PushError($"Unable to find ping pong resource for key: {key}.");
                }
            }

            foreach (var pingPongTextureResource in writtenPingPongs)
            {
                pingPongTextureResource.Flip();
            }
        }

        rd.ComputeListEnd();
        rd.Submit();
        rd.Sync();
    }

    public bool TryGetOutputTexture(UniformKey key, out byte[]? texture)
    {
        texture = null;

        if (resourceManager.TryGetResource(key, out var resource) &&
            resource.GetRid(key) is Rid rid &&
            rid.IsValid)
        {
            texture = rd.TextureGetData(rid, 0);
        }

        return texture != null;
    }

    public void Cleanup()
    {
        resourceManager.Cleanup();
    }
}