using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.ComputeShader.Reflector;

namespace Godot.ComputeShader;

/// <summary>
/// An individual compute unit compiled from a single shader file
/// </summary>
public partial class ComputeKernel : RefCounted
{
    public bool IsValid { get; private set; } = false;

    public string Name => _info.Name;
    public string Path => _info.Path;

    public KernelInfo Info => _info;
    private readonly KernelInfo _info;

    public Rid PipelineRid { get; private set; } = new();
    public Rid ShaderRid { get; private set; } = new();

    public bool HasPushConstant { get; private set; } = false;
    public Vector3I LocalSize { get; private set; } = new Vector3I(1, 1, 1);

    public IReadOnlyDictionary<UniformKey, UniformInfo> GetUniformMap() => _uniformMap;
    private readonly Dictionary<UniformKey, UniformInfo> _uniformMap = [];

    public static Vector3I CalculateWorkGroups(Vector3I textureSize, Vector3I localSize)
    {
        return new Vector3I(
            (textureSize.X + localSize.X - 1) / localSize.X,
            (textureSize.Y + localSize.Y - 1) / localSize.Y,
            (textureSize.Z + localSize.Z - 1) / localSize.Z
        );
    }

    public ComputeKernel(RenderingDevice rd, KernelInfo info)
    {
        _info = info;
        string source = LoadSource(Path);

        var result = GlslShaderParser.ParseShaderSource(source);
        HasPushConstant = result.HasPushConstant;
        LocalSize = result.LocalSize;

        var validUniformMap = true;
        var uniformKeyInfoPair = result.Uniforms.Select(u =>
        {
            var key = UniformKey.TryParse(u.Name, u.Type);
            if (key == default)
            {
                GD.PushError($"Cannot parse uniform '{u.Name}' into uniform key.");
                validUniformMap = false;
            }
            return (key, u);
        });

        if (!validUniformMap || !TryCompileGlslShader(rd, source, Path, out var shaderRid))
        {
            return;
        }

        _uniformMap = uniformKeyInfoPair.Where(x => x.key != default).ToDictionary(x => x.key, x => x.u);
        PipelineRid = rd.ComputePipelineCreate(shaderRid);
        IsValid = PipelineRid.IsValid;
    }

    private static string LoadSource(string path)
    {
        var shaderFile = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (shaderFile == null)
        {
            GD.PushError(path + '\n' + FileAccess.GetOpenError());
            return string.Empty;
        }
        return shaderFile.GetAsText() ?? string.Empty;
    }

    private static bool TryCompileGlslShader(RenderingDevice rd, string source, string path, out Rid shaderRid)
    {
        shaderRid = new Rid();

        var shaderSpirV = rd.ShaderCompileSpirVFromSource(
            new RDShaderSource
            {
                Language = RenderingDevice.ShaderLanguage.Glsl,
                SourceCompute = source
            });

        if (!string.IsNullOrEmpty(shaderSpirV.CompileErrorCompute))
        {
            GD.PushError(path + '\n' + shaderSpirV.CompileErrorCompute);
            return false;
        }

        shaderRid = rd.ShaderCreateFromSpirV(shaderSpirV);
        if (!shaderRid.IsValid)
        {
            GD.PushError("Rendering Device was unable to create shader resource.");
            return false;
        }

        return true;
    }

    public new void Free()
    {
        if (ComputeKernelRegistry.Instance?.ComputeDevice is not null)
        {
            Free(ComputeKernelRegistry.Instance?.ComputeDevice!);
        }
        else
        {
            GD.PushError($"{nameof(ComputeKernel)} free'd without rendering device. Possible memory leak.");
        }
        base.Free();
    }

    public void Free(RenderingDevice rd)
    {
        rd.FreeRid(PipelineRid);
        rd.FreeRid(ShaderRid);
    }

}