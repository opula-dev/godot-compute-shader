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

    protected RenderingDevice _rd;
    protected Rid[] _rids;

    public PipelineResource(RenderingDevice rd, BindingInfo binding)
    {
        Binding = binding;
        _rd = rd;
        _rids = new Rid[ArraySize];
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
        return _rids[key.ArrayIndex];
    }

    // Cleanup RIDs
    public virtual void Cleanup()
    {
        ResourceHelper.FreeRids(_rd, _rids);
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
    private readonly uint[] _sizes;
    private readonly uint[] _lastHashes;

    public BufferResource(RenderingDevice rd, BindingInfo binding) : base(rd, binding)
    {
        _sizes = new uint[ArraySize];
        _lastHashes = new uint[ArraySize];
    }

    public override void Initialize()
    {
        var uniform = Binding.Uniform;
        for (uint i = 0; i < ArraySize; i++)
        {
            _rids[i] = ResourceHelper.CreateBuffer(_rd, uniform.Type, 0, []);
            if (!_rids[i].IsValid)
            {
                GD.PushError($"Failed to create texture (index {i}) for {Binding.Uniform.Name}");
            }
        }
    }

    public override void Update(byte[]? data, UniformKey key)
    {
        var index = key.ArrayIndex;
        if (data == null || data.Length == 0 || !IsValidIndex(index)) return;

        var bufferRid = _rids[index];
        var lastHash = _lastHashes[index];
        var size = _sizes[index];

        var (isDirty, newHash) = CheckBufferDirtyAndGetHash(data, lastHash);
        if (!isDirty) return;

        Rid newRid = Binding.Uniform.Type switch
        {
            UniformType.StorageBuffer => _rd.StorageBufferCreate((uint)data.Length, data),
            UniformType.UniformBuffer => _rd.UniformBufferCreate((uint)data.Length, data),
            UniformType.TextureBuffer => _rd.TextureBufferCreate((uint)data.Length, Binding.Uniform.Format, data),
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
                ResourceHelper.UpdateBuffer(_rd, bufferRid, (uint)data.Length, data);
                newRid = bufferRid; // Reuse
            }
            else
            {
                _rd.FreeRid(bufferRid);
            }
        }

        _rids[index] = newRid;
        _sizes[index] = (uint)data.Length;
        _lastHashes[index] = newHash;
    }

    public override Rid GetRid(UniformKey key)
    {
        return base.GetRid(key);
    }

    public override void Cleanup()
    {
        base.Cleanup();
        Array.Fill(_sizes, 0u);
        Array.Fill(_lastHashes, 0u);
    }
}

public class TextureResource : PipelineResource
{
    private readonly Vector3I _textureSize;
    private readonly uint[] _lastHashes;

    public TextureResource(RenderingDevice rd, BindingInfo binding, Vector3I textureSize) : base(rd, binding)
    {
        _textureSize = textureSize;
        _lastHashes = new uint[ArraySize];
    }

    public override void Initialize()
    {
        var uniform = Binding.Uniform;
        for (uint i = 0; i < ArraySize; i++)
        {
            _rids[i] = ResourceHelper.CreateTexture(_rd, uniform.Format, _textureSize, uniform.Dimension);
            if (!_rids[i].IsValid)
            {
                GD.PushError($"Failed to create texture (index {i}) for {Binding.Uniform.Name}");
            }
        }
    }

    public override void Update(byte[]? data, UniformKey key)
    {
        var index = key.ArrayIndex;
        if (!IsValidIndex(index)) { return; }

        var textureRid = _rids[index];
        if (data == null || data.Length == 0 || !textureRid.IsValid) return;

        var (isDirty, newHash) = CheckBufferDirtyAndGetHash(data, _lastHashes[index]);
        if (!isDirty)
        {
            return;
        }

        // Assume layer 0 for simplicity; extend for 3D if needed
        ResourceHelper.UpdateTexture(_rd, textureRid, layer: 0, data);
        _lastHashes[index] = newHash;
    }

    public override Rid GetRid(UniformKey key)
    {
        return base.GetRid(key);
    }

    public override void Cleanup()
    {
        base.Cleanup();
        Array.Fill(_lastHashes, 0u);
    }
}

public class SamplerResource(
    RenderingDevice rd,
    BindingInfo binding,
    RDSamplerState? customState
) : PipelineResource(rd, binding)
{
    private readonly RDSamplerState? _customState = customState;

    public override void Initialize()
    {
        for (uint i = 0; i < ArraySize; i++)
        {
            _rids[i] = ResourceHelper.CreateSampler(_rd, _customState);
            if (!_rids[i].IsValid)
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

    private readonly Rid[] _pongRids; // use base.Rids for ping textures, this array for pong
    private readonly Vector3I _textureSize;
    private bool _state; // false: textures[0] = write; true: textures[1] = read;

    private readonly uint[] _lastHashesPing; // For Rids (ping)
    private readonly uint[] _lastHashesPong; // For pongRids

    public PingPongTextureResource(
        RenderingDevice rd,
        PingPongBindingInfo binding,
        Vector3I textureSize
    ) : base(rd, binding.ReadBinding)
    {
        _textureSize = textureSize;
        ReadBinding = binding.ReadBinding;
        WriteBinding = binding.WriteBinding;

        _pongRids = new Rid[ArraySize];
        _lastHashesPing = new uint[ArraySize];
        _lastHashesPong = new uint[ArraySize];
    }

    public override void Initialize()
    {
        var uniform = ReadBinding.Uniform; // Formats match from analyzer

        for (uint i = 0; i < ArraySize; i++)
        {
            _rids[i] = ResourceHelper.CreateTexture(_rd, uniform.Format, _textureSize, uniform.Dimension);
            _pongRids[i] = ResourceHelper.CreateTexture(_rd, uniform.Format, _textureSize, uniform.Dimension);
            if (!_rids[i].IsValid || !_pongRids[i].IsValid)
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
            ? (_state ? _rids[index] : _pongRids[index])
            : (_state ? _pongRids[index] : _rids[index]);

        var targetHashes = isRead
            ? (_state ? _lastHashesPing : _lastHashesPong)
            : (_state ? _lastHashesPong : _lastHashesPing);

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
        ResourceHelper.UpdateTexture(_rd, targetRid, layer: 0, data);
        targetHashes[index] = newHash;
    }

    public override Rid GetRid(UniformKey key)
    {
        var index = key.ArrayIndex;
        if (!IsValidIndex(index)) return new Rid();

        if (key.IsRole(UniformRole.Read))
        {
            return _state ? _rids[index] : _pongRids[index];
        }
        if (key.IsRole(UniformRole.Write))
        {
            return _state ? _pongRids[index] : _rids[index];
        }
        GD.PushError(
            $"Invalid uniform key '{key}' for ping-pong {ReadBinding.Uniform.Name}." +
            "Key does not define a Read or Write Role.");
        return new Rid();
    }

    public void Flip()
    {
        _state = !_state;
    }

    public override void Cleanup()
    {
        base.Cleanup();
        ResourceHelper.FreeRids(_rd, _pongRids);
        Array.Fill(_lastHashesPing, 0u);
        Array.Fill(_lastHashesPong, 0u);
    }
}

public class PingPongBufferResource : PipelineResource, IPingPongResource
{
    public BindingInfo ReadBinding { get; }
    public BindingInfo WriteBinding { get; }

    private readonly Rid[] _pongRids; // use base.Rids for ping buffers, this array for pong
    private bool _state; // false: Rids = write; true: Rids = read;

    private readonly uint[] _sizesPing; // For Rids (ping)
    private readonly uint[] _sizesPong; // For pongRids
    private readonly uint[] _lastHashesPing; // For Rids (ping)
    private readonly uint[] _lastHashesPong; // For pongRids

    public PingPongBufferResource(
        RenderingDevice rd,
        PingPongBindingInfo binding
    ) : base(rd, binding.ReadBinding)
    {
        ReadBinding = binding.ReadBinding;
        WriteBinding = binding.WriteBinding;

        _pongRids = new Rid[ArraySize];
        _sizesPing = new uint[ArraySize];
        _sizesPong = new uint[ArraySize];
        _lastHashesPing = new uint[ArraySize];
        _lastHashesPong = new uint[ArraySize];
    }

    public override void Initialize()
    {
        var uniform = ReadBinding.Uniform; // Formats match from analyzer
        for (uint i = 0; i < ArraySize; i++)
        {
            _rids[i] = ResourceHelper.CreateBuffer(_rd, uniform.Type, 0, []);
            _pongRids[i] = ResourceHelper.CreateBuffer(_rd, uniform.Type, 0, []);
            if (!_rids[i].IsValid || !_pongRids[i].IsValid)
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
            ? (_state ? _rids[index] : _pongRids[index])
            : (_state ? _pongRids[index] : _rids[index]);

        var targetSizes = isRead
            ? (_state ? _sizesPing : _sizesPong)
            : (_state ? _sizesPong : _sizesPing);

        var targetHashes = isRead
            ? (_state ? _lastHashesPing : _lastHashesPong)
            : (_state ? _lastHashesPong : _lastHashesPing);

        var lastHash = targetHashes[index];
        var size = targetSizes[index];

        var (isDirty, newHash) = CheckBufferDirtyAndGetHash(data, lastHash);
        if (!isDirty) return;

        Rid newRid = Binding.Uniform.Type switch
        {
            UniformType.StorageBuffer => _rd.StorageBufferCreate((uint)data.Length, data),
            UniformType.UniformBuffer => _rd.UniformBufferCreate((uint)data.Length, data),
            UniformType.TextureBuffer => _rd.TextureBufferCreate((uint)data.Length, Binding.Uniform.Format, data),
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
                ResourceHelper.UpdateBuffer(_rd, targetRid, (uint)data.Length, data);
                newRid = targetRid; // Reuse
            }
            else
            {
                _rd.FreeRid(targetRid);
            }
        }

        // Assign back to the correct array
        if (isRead)
        {
            if (_state)
            {
                _rids[index] = newRid;
                _sizesPing[index] = (uint)data.Length;
                _lastHashesPing[index] = newHash;
            }
            else
            {
                _pongRids[index] = newRid;
                _sizesPong[index] = (uint)data.Length;
                _lastHashesPong[index] = newHash;
            }
        }
        else // Write
        {
            if (_state)
            {
                _pongRids[index] = newRid;
                _sizesPong[index] = (uint)data.Length;
                _lastHashesPong[index] = newHash;
            }
            else
            {
                _rids[index] = newRid;
                _sizesPing[index] = (uint)data.Length;
                _lastHashesPing[index] = newHash;
            }
        }
    }

    public override Rid GetRid(UniformKey key)
    {
        var index = key.ArrayIndex;
        if (!IsValidIndex(index)) return new Rid();

        if (key.IsRole(UniformRole.Read))
        {
            return _state ? _rids[index] : _pongRids[index];
        }
        if (key.IsRole(UniformRole.Write))
        {
            return _state ? _pongRids[index] : _rids[index];
        }
        GD.PushError(
            $"Invalid uniform key '{key}' for ping-pong {ReadBinding.Uniform.Name}." +
            "Key does not define a Read or Write Role.");
        return new Rid();
    }

    public void Flip()
    {
        _state = !_state;
    }

    public override void Cleanup()
    {
        base.Cleanup();
        ResourceHelper.FreeRids(_rd, _pongRids);
        Array.Fill(_sizesPing, 0u);
        Array.Fill(_sizesPong, 0u);
        Array.Fill(_lastHashesPing, 0u);
        Array.Fill(_lastHashesPong, 0u);
    }
}