using System;
using System.IO.Hashing;
using Godot;
using Godot.ComputeShader.Reflector;

namespace Godot.ComputeShader.Pipeline;

using UniformType = RenderingDevice.UniformType;


// Interface for resources that support ping-pong (flipping)
public interface IPingPongResource
{
    void Flip();
}

// Abstract base for all resources, encapsulating type-specific logic
public abstract class PipelineResource
{
    public BindingInfo Binding { get; }
    public uint ArraySize => Binding.Uniform.ArraySize;

    protected RenderingDevice RenderingDevice { get; }
    protected Rid[] Rids { get; }

    public PipelineResource(RenderingDevice rd, BindingInfo binding)
    {
        RenderingDevice = rd;
        Binding = binding;
        Rids = new Rid[ArraySize];
    }

    // Initialize the resource (e.g., create RIDs)
    public abstract void Initialize();

    // Update data if supported (e.g., buffers/textures); subclasses can implement dirty-check
    public abstract void Update(byte[]? data, UniformKey key);

    // Get the RID for the given uniform key
    public virtual Rid GetRid(UniformKey key)
    {
        if (!IsValidIndex(key.ArrayIndex))
        {
            return new Rid();
        }
        return Rids[key.ArrayIndex];
    }

    // Cleanup RIDs
    public virtual void Cleanup()
    {
        ResourceHelper.FreeRids(RenderingDevice, Rids);
    }

    protected static (bool IsDirty, uint NewHash) CheckBufferDirtyAndGetHash(byte[] newData, uint lastEntry)
    {
        // Existing logic from manager
        if (newData.Length == 0) return (lastEntry == 0u, 0u);

        uint newHash = XxHash32.HashToUInt32(newData.AsSpan());
        bool isDirty = lastEntry != newHash;

        return (isDirty, newHash);
    }

    protected bool IsValidIndex(int index)
    {
        var valid = index >= 0 && index < ArraySize;
        if (!valid)
        {
            GD.PushError($"Index {index} out of range for buffer array size {ArraySize}.");
        }
        return valid;
    }
}

public class BufferResource : PipelineResource
{
    private readonly uint[] sizes;
    private readonly uint[] lastHashes;

    public BufferResource(RenderingDevice rd, BindingInfo binding) : base(rd, binding)
    {
        sizes = new uint[ArraySize];
        lastHashes = new uint[ArraySize];
    }

    public override void Initialize()
    {
        var uniform = Binding.Uniform;
        for (uint i = 0; i < ArraySize; i++)
        {
            Rids[i] = ResourceHelper.CreateBuffer(RenderingDevice, uniform.Type, 0, []);
            if (!Rids[i].IsValid)
            {
                GD.PushError($"Failed to create texture (index {i}) for {Binding.Uniform.Name}");
            }
        }
    }

    public override void Update(byte[]? data, UniformKey key)
    {
        var index = key.ArrayIndex;
        if (data == null || data.Length == 0 || !IsValidIndex(index)) return;

        var bufferRid = Rids[index];
        var lastHash = lastHashes[index];
        var size = sizes[index];

        var (isDirty, newHash) = CheckBufferDirtyAndGetHash(data, lastHash);
        if (!isDirty) return;

        Rid newRid = Binding.Uniform.Type switch
        {
            UniformType.StorageBuffer => RenderingDevice.StorageBufferCreate((uint)data.Length, data),
            UniformType.UniformBuffer => RenderingDevice.UniformBufferCreate((uint)data.Length, data),
            UniformType.TextureBuffer => RenderingDevice.TextureBufferCreate((uint)data.Length, Binding.Uniform.Format, data),
            _ => throw new NotSupportedException($"Unsupported buffer type {Binding.Uniform.Type}")
        };

        if (!newRid.IsValid)
        {
            GD.PushError($"Failed to create buffer for {Binding.Uniform.Name}");
            return;
        }

        if (bufferRid.IsValid)
        {
            if (size == (uint)data.Length)
            {
                ResourceHelper.UpdateBuffer(RenderingDevice, bufferRid, (uint)data.Length, data);
                newRid = bufferRid; // Reuse
            }
            else
            {
                RenderingDevice.FreeRid(bufferRid);
            }
        }

        Rids[index] = newRid;
        sizes[index] = (uint)data.Length;
        lastHashes[index] = newHash;
    }

    public override Rid GetRid(UniformKey key)
    {
        return base.GetRid(key);
    }

    public override void Cleanup()
    {
        base.Cleanup();
        Array.Fill(sizes, 0u);
        Array.Fill(lastHashes, 0u);
    }
}

public class TextureResource : PipelineResource
{
    private readonly Vector3I textureSize;
    private readonly uint[] lastHashes;

    public TextureResource(RenderingDevice rd, BindingInfo binding, Vector3I textureSize) : base(rd, binding)
    {
        this.textureSize = textureSize;
        lastHashes = new uint[ArraySize];
    }

    public override void Initialize()
    {
        var uniform = Binding.Uniform;
        for (uint i = 0; i < ArraySize; i++)
        {
            Rids[i] = ResourceHelper.CreateTexture(RenderingDevice, uniform.Format, textureSize, uniform.Dimension);
            if (!Rids[i].IsValid)
            {
                GD.PushError($"Failed to create texture (index {i}) for {Binding.Uniform.Name}");
            }
        }
    }

    public override void Update(byte[]? data, UniformKey key)
    {
        var index = key.ArrayIndex;
        if (!IsValidIndex(index)) { return; }

        var textureRid = Rids[index];
        if (data == null || data.Length == 0 || !textureRid.IsValid) return;

        var (isDirty, newHash) = CheckBufferDirtyAndGetHash(data, lastHashes[index]);
        if (!isDirty)
        {
            return;
        }

        // Assume layer 0 for simplicity; extend for 3D if needed
        ResourceHelper.UpdateTexture(RenderingDevice, textureRid, layer: 0, data);
        lastHashes[index] = newHash;
    }

    public override Rid GetRid(UniformKey key)
    {
        return base.GetRid(key);
    }

    public override void Cleanup()
    {
        base.Cleanup();
        Array.Fill(lastHashes, 0u);
    }
}

public class SamplerResource(
    RenderingDevice rd,
    BindingInfo binding,
    RDSamplerState? customState
) : PipelineResource(rd, binding)
{
    private readonly RDSamplerState? customState = customState;

    public override void Initialize()
    {
        for (uint i = 0; i < ArraySize; i++)
        {
            Rids[i] = ResourceHelper.CreateSampler(RenderingDevice, customState);
            if (!Rids[i].IsValid)
            {
                GD.PushError($"Failed to create sampler (index {i}) for {Binding.Uniform.Name}");
            }
        }
    }

    public override void Update(byte[]? data, UniformKey key)
    {
        // No-op, as sampler states are static after initialization
    }

    public override Rid GetRid(UniformKey key)
    {
        return base.GetRid(key);
    }

    public override void Cleanup()
    {
        base.Cleanup();
    }
}

public class PingPongTextureResource : PipelineResource, IPingPongResource
{
    public BindingInfo ReadBinding { get; }
    public BindingInfo WriteBinding { get; }

    private readonly Rid[] pongRids; // use base.Rids for ping textures, this array for pong
    private readonly Vector3I textureSize;
    private bool state; // false: textures[0] = write; true: textures[1] = read;

    private readonly uint[] lastHashesPing; // For Rids (ping)
    private readonly uint[] lastHashesPong; // For pongRids

    public PingPongTextureResource(
        RenderingDevice rd,
        PingPongBindingInfo binding,
        Vector3I textureSize
    ) : base(rd, binding.ReadBinding)
    {
        this.textureSize = textureSize;
        ReadBinding = binding.ReadBinding;
        WriteBinding = binding.WriteBinding;

        pongRids = new Rid[ArraySize];
        lastHashesPing = new uint[ArraySize];
        lastHashesPong = new uint[ArraySize];
    }

    public override void Initialize()
    {
        var uniform = ReadBinding.Uniform; // Formats match from analyzer

        for (uint i = 0; i < ArraySize; i++)
        {
            Rids[i] = ResourceHelper.CreateTexture(RenderingDevice, uniform.Format, textureSize, uniform.Dimension);
            pongRids[i] = ResourceHelper.CreateTexture(RenderingDevice, uniform.Format, textureSize, uniform.Dimension);
            if (!Rids[i].IsValid || !pongRids[i].IsValid)
            {
                GD.PushError($"Failed to create texture (index {i}) for {Binding.Uniform.Name}");
            }
        }
    }

    public override void Update(byte[]? data, UniformKey key)
    {
        var index = key.ArrayIndex;
        if (!IsValidIndex(index) || data == null || data.Length == 0)
        {
            return;
        }

        var isRead = key.IsRole(UniformRole.Read);
        var isWrite = key.IsRole(UniformRole.Write);

        if (!isRead && !isWrite)
        {
            GD.PushError($"Ping-pong update key must specify Read or Write role: {key}");
            return;
        }

        // Determine target based on current state and role
        var targetRid = isRead
            ? (state ? Rids[index] : pongRids[index])
            : (state ? pongRids[index] : Rids[index]);

        var targetHashes = isRead
            ? (state ? lastHashesPing : lastHashesPong)
            : (state ? lastHashesPong : lastHashesPing);

        if (!targetRid.IsValid)
        {
            return;
        }

        var (isDirty, newHash) = CheckBufferDirtyAndGetHash(data, targetHashes[index]);
        if (!isDirty)
        {
            return;
        }

        // Assume layer 0 for simplicity; extend for 3D if needed
        ResourceHelper.UpdateTexture(RenderingDevice, targetRid, layer: 0, data);
        targetHashes[index] = newHash;
    }

    public override Rid GetRid(UniformKey key)
    {
        var index = key.ArrayIndex;
        if (!IsValidIndex(index)) return new Rid();

        if (key.IsRole(UniformRole.Read))
        {
            return state ? Rids[index] : pongRids[index];
        }
        if (key.IsRole(UniformRole.Write))
        {
            return state ? pongRids[index] : Rids[index];
        }
        GD.PushError(
            $"Invalid uniform key '{key}' for ping-pong {ReadBinding.Uniform.Name}." +
            "Key does not define a Read or Write Role.");
        return new Rid();
    }

    public void Flip()
    {
        state = !state;
    }

    public override void Cleanup()
    {
        base.Cleanup();
        ResourceHelper.FreeRids(RenderingDevice, pongRids);
        Array.Fill(lastHashesPing, 0u);
        Array.Fill(lastHashesPong, 0u);
    }
}

public class PingPongBufferResource : PipelineResource, IPingPongResource
{
    public BindingInfo ReadBinding { get; }
    public BindingInfo WriteBinding { get; }

    private readonly Rid[] pongRids; // use base.Rids for ping buffers, this array for pong
    private bool state; // false: Rids = write; true: Rids = read;

    private readonly uint[] sizesPing; // For Rids (ping)
    private readonly uint[] sizesPong; // For pongRids
    private readonly uint[] lastHashesPing; // For Rids (ping)
    private readonly uint[] lastHashesPong; // For pongRids

    public PingPongBufferResource(
        RenderingDevice rd,
        PingPongBindingInfo binding
    ) : base(rd, binding.ReadBinding)
    {
        ReadBinding = binding.ReadBinding;
        WriteBinding = binding.WriteBinding;

        pongRids = new Rid[ArraySize];
        sizesPing = new uint[ArraySize];
        sizesPong = new uint[ArraySize];
        lastHashesPing = new uint[ArraySize];
        lastHashesPong = new uint[ArraySize];
    }

    public override void Initialize()
    {
        var uniform = ReadBinding.Uniform; // Formats match from analyzer
        for (uint i = 0; i < ArraySize; i++)
        {
            Rids[i] = ResourceHelper.CreateBuffer(RenderingDevice, uniform.Type, 0, []);
            pongRids[i] = ResourceHelper.CreateBuffer(RenderingDevice, uniform.Type, 0, []);
            if (!Rids[i].IsValid || !pongRids[i].IsValid)
            {
                GD.PushError($"Failed to create buffer (index {i}) for {Binding.Uniform.Name}");
            }
        }
    }

    public override void Update(byte[]? data, UniformKey key)
    {
        var index = key.ArrayIndex;
        if (data == null || data.Length == 0 || !IsValidIndex(index)) return;

        var isRead = key.IsRole(UniformRole.Read);
        var isWrite = key.IsRole(UniformRole.Write);

        if (!isRead && !isWrite)
        {
            GD.PushError($"Ping-pong update key must specify Read or Write role: {key}");
            return;
        }

        // Determine target based on current state and role
        var targetRid = isRead
            ? (state ? Rids[index] : pongRids[index])
            : (state ? pongRids[index] : Rids[index]);

        var targetSizes = isRead
            ? (state ? sizesPing : sizesPong)
            : (state ? sizesPong : sizesPing);

        var targetHashes = isRead
            ? (state ? lastHashesPing : lastHashesPong)
            : (state ? lastHashesPong : lastHashesPing);

        var lastHash = targetHashes[index];
        var size = targetSizes[index];

        var (isDirty, newHash) = CheckBufferDirtyAndGetHash(data, lastHash);
        if (!isDirty) return;

        Rid newRid = Binding.Uniform.Type switch
        {
            UniformType.StorageBuffer => RenderingDevice.StorageBufferCreate((uint)data.Length, data),
            UniformType.UniformBuffer => RenderingDevice.UniformBufferCreate((uint)data.Length, data),
            UniformType.TextureBuffer => RenderingDevice.TextureBufferCreate((uint)data.Length, Binding.Uniform.Format, data),
            _ => throw new NotSupportedException($"Unsupported buffer type {Binding.Uniform.Type}")
        };

        if (!newRid.IsValid)
        {
            GD.PushError($"Failed to create buffer for {Binding.Uniform.Name}");
            return;
        }

        if (targetRid.IsValid)
        {
            if (size == (uint)data.Length)
            {
                ResourceHelper.UpdateBuffer(RenderingDevice, targetRid, (uint)data.Length, data);
                newRid = targetRid; // Reuse
            }
            else
            {
                RenderingDevice.FreeRid(targetRid);
            }
        }

        // Assign back to the correct array
        if (isRead)
        {
            if (state)
            {
                Rids[index] = newRid;
                sizesPing[index] = (uint)data.Length;
                lastHashesPing[index] = newHash;
            }
            else
            {
                pongRids[index] = newRid;
                sizesPong[index] = (uint)data.Length;
                lastHashesPong[index] = newHash;
            }
        }
        else // Write
        {
            if (state)
            {
                pongRids[index] = newRid;
                sizesPong[index] = (uint)data.Length;
                lastHashesPong[index] = newHash;
            }
            else
            {
                Rids[index] = newRid;
                sizesPing[index] = (uint)data.Length;
                lastHashesPing[index] = newHash;
            }
        }
    }

    public override Rid GetRid(UniformKey key)
    {
        var index = key.ArrayIndex;
        if (!IsValidIndex(index)) return new Rid();

        if (key.IsRole(UniformRole.Read))
        {
            return state ? Rids[index] : pongRids[index];
        }
        if (key.IsRole(UniformRole.Write))
        {
            return state ? pongRids[index] : Rids[index];
        }
        GD.PushError(
            $"Invalid uniform key '{key}' for ping-pong {ReadBinding.Uniform.Name}." +
            "Key does not define a Read or Write Role.");
        return new Rid();
    }

    public void Flip()
    {
        state = !state;
    }

    public override void Cleanup()
    {
        base.Cleanup();
        ResourceHelper.FreeRids(RenderingDevice, pongRids);
        Array.Fill(sizesPing, 0u);
        Array.Fill(sizesPong, 0u);
        Array.Fill(lastHashesPing, 0u);
        Array.Fill(lastHashesPong, 0u);
    }
}