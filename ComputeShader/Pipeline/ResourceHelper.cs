using System;
using System.Collections.Generic;
using Godot;

namespace Godot.ComputeShader.Pipeline;

public static class ResourceHelper
{
    public static Rid CreateTexture(
        RenderingDevice rd,
        RenderingDevice.DataFormat format,
        Vector3I size,
        uint dimension = 3,
        RenderingDevice.TextureUsageBits extraUsage = 0)
    {
        var texFormat = new RDTextureFormat
        {
            Width = (uint)size.X,
            Height = dimension >= 2 ? (uint)size.Y : 1u,
            Depth = dimension == 3 ? (uint)size.Z : 1u,
            Format = format,
            TextureType = dimension switch
            {
                1 => RenderingDevice.TextureType.Type1D,
                2 => RenderingDevice.TextureType.Type2D,
                _ => RenderingDevice.TextureType.Type3D
            },
            UsageBits = RenderingDevice.TextureUsageBits.StorageBit |
                        RenderingDevice.TextureUsageBits.CanUpdateBit |
                        RenderingDevice.TextureUsageBits.CanCopyFromBit |
                        RenderingDevice.TextureUsageBits.SamplingBit |
                        extraUsage
        };
        return rd.TextureCreate(texFormat, new RDTextureView());
    }

    public static void UpdateTexture(RenderingDevice rd, Rid rid, uint layer, byte[] data)
    {
        rd.TextureUpdate(rid, layer, data);
    }

    public static Rid CreateBuffer(RenderingDevice rd, RenderingDevice.UniformType type, uint size, byte[] data)
    {
        return type switch
        {
            RenderingDevice.UniformType.StorageBuffer => rd.StorageBufferCreate(size, data),
            RenderingDevice.UniformType.UniformBuffer => rd.UniformBufferCreate(size, data),
            _ => throw new ArgumentException($"Unsupported buffer type {type}")
        };
    }

    public static void UpdateBuffer(RenderingDevice rd, Rid rid, uint size, byte[] data)
    {
        rd.BufferUpdate(rid, 0, size, data);
    }

    public static Rid CreateSampler(RenderingDevice rd, RDSamplerState? state = null)
    {
        state ??= new RDSamplerState
        {
            MagFilter = RenderingDevice.SamplerFilter.Linear,
            MinFilter = RenderingDevice.SamplerFilter.Linear,
            MipFilter = RenderingDevice.SamplerFilter.Linear,
            RepeatU = RenderingDevice.SamplerRepeatMode.Repeat,
            RepeatV = RenderingDevice.SamplerRepeatMode.Repeat,
            RepeatW = RenderingDevice.SamplerRepeatMode.Repeat
        };
        return rd.SamplerCreate(state);
    }

    public static void FreeRids(RenderingDevice rd, params Rid[] rids)
    {
        foreach (var rid in rids)
        {
            if (rid.IsValid)
            {
                rd.FreeRid(rid);
            }
        }
    }

    public static void FreeRids(RenderingDevice rd, IEnumerable<Rid> rids)
    {
        foreach (var rid in rids)
        {
            if (rid.IsValid)
            {
                rd.FreeRid(rid);
            }
        }
    }
}