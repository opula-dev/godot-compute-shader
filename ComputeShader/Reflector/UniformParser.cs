using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace Godot.ComputeShader.Reflector;

using UniformType = RenderingDevice.UniformType;
using DataFormat = RenderingDevice.DataFormat;

[Flags]
public enum UniformAccess
{
    None = 0b00,
    Read = 0b01,
    Write = 0b10,
    ReadWrite = 0b11,
}

public readonly struct UniformInfo
{
    public string Name { get; init; }
    public uint Binding { get; init; }
    public uint Set { get; init; }
    public UniformType Type { get; init; }
    public DataFormat Format { get; init; }
    public UniformAccess Access { get; init; }
    public uint Dimension { get; init; }
    public uint ArraySize { get; init; }
}

public abstract class UniformParser
{
    protected abstract Regex Regex { get; }
    protected abstract UniformType UniformType { get; }
    protected virtual bool HasFormat => false;
    protected virtual bool HasAccess => false;
    protected virtual bool HasDimension => false;
    protected virtual UniformAccess DefaultAccess => UniformAccess.Read;
    protected virtual uint DefaultDimension => 0;
    protected virtual DataFormat DefaultFormat => DataFormat.Max;

    protected readonly static Dictionary<string, uint> DimensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        {"1D", 1 },
        {"2D", 2 },
        {"3D", 3 },
        {"Cube", 2 },
        {"2DArray", 2 },
        {"CubeArray", 2 },
    };

    protected readonly static Dictionary<string, UniformAccess> AccessMap = new(StringComparer.OrdinalIgnoreCase)
    {
        {"readonly", UniformAccess.Read },
        {"writeonly", UniformAccess.Write },
    };

    public IReadOnlyList<UniformInfo> Parse(string source)
    {
        var result = new List<UniformInfo>();
        foreach (Match m in Regex.Matches(source))
        {
            if (m.Success)
            {
                var info = Extract(m);
                if (info != null)
                {
                    result.Add(info.Value);
                }
            }
        }
        return result;
    }

    protected virtual UniformInfo? Extract(Match m)
    {
        var groups = m.Groups;
        var typeStr = UniformType.ToString().ToLower();

        var format = DefaultFormat;
        var access = DefaultAccess;
        var dimension = DefaultDimension;

        if (!TryGetRequired(typeStr, "name", m.Value, groups, out var name) ||
            !TryGetRequiredUInt(typeStr, "binding", m.Value, groups, out var binding) ||
            (HasFormat && !FormatParser.TryGetDataFormat(groups["format"].Value, out format)) ||
            (HasAccess && !AccessMap.TryGetValue(groups["access"].Value, out access)) ||
            (HasDimension && !TryGetRequiredUInt(typeStr, "dimension", m.Value, groups, out dimension)))
        {
            return null;
        }

        TryGetOptionalUInt(typeStr, "set", m.Value, groups, out var set);
        var arraySize = TryGetOptionalUInt(typeStr, "array_size", m.Value, groups, out var parsedArraySize)
            ? parsedArraySize
            : 1u;

        return new UniformInfo
        {
            Name = name,
            Binding = binding,
            Set = set,
            Type = UniformType,
            Format = format,
            Access = access,
            Dimension = dimension,
            ArraySize = arraySize
        };
    }

    protected static bool TryGetRequired(string type, string property, string matchValue, GroupCollection groups, out string value)
    {
        value = groups[property].Value;
        if (string.IsNullOrEmpty(value))
        {
            ParserWarn(type, property, matchValue);
            return false;
        }
        return true;
    }

    protected static bool TryGetRequiredUInt(string type, string property, string matchValue, GroupCollection groups, out uint value)
    {
        value = 0;
        var strValue = groups[property].Value;
        if (string.IsNullOrEmpty(strValue) || !uint.TryParse(strValue, out value))
        {
            ParserWarn(type, property, matchValue);
            return false;
        }
        return true;
    }

    protected static bool TryGetOptionalUInt(string type, string property, string matchValue, GroupCollection groups, out uint value)
    {
        value = 0;
        var strValue = groups[property].Value;
        if (string.IsNullOrEmpty(strValue))
        {
            return false; // Optional: No warning, as it's defaulted
        }
        if (!uint.TryParse(strValue, out value))
        {
            ParserWarn(type, property, matchValue);
            return false;
        }
        return true;
    }

    private static void ParserWarn(string type, string property, string matchValue)
    {
        GD.PushWarning($"Failed to parse {type} uniform {property} in shader. Uniform: '{matchValue}'");
    }
}

public class SamplerParser : UniformParser
{
    protected override Regex Regex => UniformRegex.Sampler();
    protected override UniformType UniformType => UniformType.Sampler;
}

public class SamplerWithTextureParser : UniformParser
{
    protected override Regex Regex => UniformRegex.SamplerWithTexture();
    protected override UniformType UniformType => UniformType.SamplerWithTexture;
    protected override bool HasDimension => true;
}

public class SamplerWithTextureBufferParser : UniformParser
{
    protected override Regex Regex => UniformRegex.SamplerWithTextureBuffer();
    protected override UniformType UniformType => UniformType.SamplerWithTextureBuffer;
    protected override uint DefaultDimension => 1;
}

public class ImageParser : UniformParser
{
    protected override Regex Regex => UniformRegex.Image();
    protected override UniformType UniformType => UniformType.Image;
    protected override bool HasFormat => true;
    protected override bool HasAccess => true;
    protected override bool HasDimension => true;
    protected override UniformAccess DefaultAccess => UniformAccess.ReadWrite;
}

public class ImageBufferParser : UniformParser
{
    protected override Regex Regex => UniformRegex.ImageBuffer();
    protected override UniformType UniformType => UniformType.ImageBuffer;
    protected override bool HasFormat => true;
    protected override bool HasAccess => true;
    protected override UniformAccess DefaultAccess => UniformAccess.ReadWrite;
    protected override uint DefaultDimension => 1;
}

public class TextureParser : UniformParser
{
    protected override Regex Regex => UniformRegex.Texture();
    protected override UniformType UniformType => UniformType.Texture;
    protected override bool HasDimension => true;
}

public class TextureBufferParser : UniformParser
{
    protected override Regex Regex => UniformRegex.TextureBuffer();
    protected override UniformType UniformType => UniformType.TextureBuffer;
    protected override uint DefaultDimension => 1;
}

public class UniformBufferParser : UniformParser
{
    protected override Regex Regex => UniformRegex.UniformBuffer();
    protected override UniformType UniformType => UniformType.UniformBuffer;
}

public class StorageBufferParser : UniformParser
{
    protected override Regex Regex => UniformRegex.StorageBuffer();
    protected override UniformType UniformType => UniformType.StorageBuffer;
    protected override bool HasAccess => true;
    protected override UniformAccess DefaultAccess => UniformAccess.ReadWrite;
}

public class InputAttachmentParser : UniformParser
{
    protected override Regex Regex => UniformRegex.InputAttachment();
    protected override UniformType UniformType => UniformType.InputAttachment;
    protected override uint DefaultDimension => 2;
}