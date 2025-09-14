using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace Godot.ComputeShader.Reflector;

using UniformType = RenderingDevice.UniformType;
using DataFormat = RenderingDevice.DataFormat;
using UniformProperty = UniformRegex.Key;

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

public abstract class UniformParser()
{
    protected readonly record struct ExtractContext
    {
        public string UniformType { get; init; }
        public Match UniformMatch { get; init; }
        public GroupCollection Groups { get; init; }
        public List<string> Failures { get; init; }
    }

    // Uniform property parsing configuration

    protected interface IParserConfig { } // Signature interface 

    protected abstract class ParserConfig<T> : IParserConfig
    {
        public abstract bool IsRequired { get; }
        public abstract T DefaultValue { get; }
        public abstract (bool success, T value) Parser(string raw);
    }

    protected class UintParserConfig : ParserConfig<uint>
    {
        public override bool IsRequired => true;
        public override uint DefaultValue => 0u;
        public override (bool success, uint value) Parser(string raw) =>
            (uint.TryParse(raw, out var value), value);
    }

    private class NameParserConfig : ParserConfig<string>
    {
        public override bool IsRequired => true;
        public override string DefaultValue => string.Empty;
        public override (bool success, string value) Parser(string raw)
        {
            var trimmed = raw.Trim();
            return (!string.IsNullOrWhiteSpace(trimmed), trimmed);
        }
    };

    private class BindingParserConfig : UintParserConfig;

    private class SetParserConfig : UintParserConfig;

    private class ArraySizeParserConfig : UintParserConfig
    {
        public override bool IsRequired => false;
        public override uint DefaultValue => 1u;
    };

    protected class FormatParserConfig : ParserConfig<DataFormat>
    {
        public override bool IsRequired => false;
        public override DataFormat DefaultValue => DataFormat.Max;
        public override (bool success, DataFormat value) Parser(string raw) =>
            (UniformFormat.FormatMap.TryGetValue(raw, out var value), value);
    };

    protected class AccessParserConfig : ParserConfig<UniformAccess>
    {
        public override bool IsRequired => false;
        public override UniformAccess DefaultValue => UniformAccess.Read;
        public override (bool success, UniformAccess value) Parser(string raw) =>
            (AccessMap.TryGetValue(raw, out var value), value);
    };

    protected class DimensionParserConfig : UintParserConfig
    {
        public override bool IsRequired => false;
        public override uint DefaultValue => 0u;
        public override (bool success, uint value) Parser(string raw) =>
            (DimensionMap.TryGetValue(raw, out var value), value);
    };

    // Maps

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

    protected readonly Dictionary<string, IParserConfig> ParserConfigs = new()
    {
        [UniformProperty.Access] = new AccessParserConfig(),
        [UniformProperty.ArraySize] = new ArraySizeParserConfig(),
        [UniformProperty.Binding] = new BindingParserConfig(),
        [UniformProperty.Dimension] = new DimensionParserConfig(),
        [UniformProperty.Format] = new FormatParserConfig(),
        [UniformProperty.Name] = new NameParserConfig(),
        [UniformProperty.Set] = new SetParserConfig(),
    };

    protected abstract Regex Regex { get; }
    protected abstract UniformType UniformType { get; }

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
        var uniformType = UniformType.ToString().ToLower();

        var context = new ExtractContext
        {
            UniformType = uniformType,
            UniformMatch = m,
            Groups = m.Groups,
            Failures = [],
        };

        if (TryGet<string>(context, UniformProperty.Name, out var name) &&
            TryGet<uint>(context, UniformProperty.Binding, out var binding) &&
            TryGet<uint>(context, UniformProperty.Set, out var set) &&
            TryGet<uint>(context, UniformProperty.ArraySize, out var arraySize) &&
            TryGet<DataFormat>(context, UniformProperty.Format, out var format) &&
            TryGet<UniformAccess>(context, UniformProperty.Access, out var access) &&
            TryGet<uint>(context, UniformProperty.Dimension, out var dimension))
        {
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
        else
        {
            ParserWarn(context);
            return null;
        }
    }

    protected bool TryGet<T>(
            ExtractContext context,
            string property,
            out T value)
    {
        var config = ParserConfigs[property] as ParserConfig<T>
            ?? throw new InvalidOperationException($"Config for '{property}' does not exist for type {context.UniformType}.\n{string.Join("\n", ParserConfigs.Values)}");

        string raw = context.Groups[property].Value.Trim();

        var (parsedSuccess, parsedValue) = config.Parser(raw);
        if (parsedSuccess)
        {
            value = parsedValue;
            return true;
        }
        else if (config.IsRequired)
        {
            context.Failures.Add(property);
            value = config.DefaultValue; // Won't be used, but initializes out param
            return false;
        }
        else
        {
            value = config.DefaultValue;
            return true;
        }
    }

    private static void ParserWarn(ExtractContext context)
    {
        var warnings = new List<string>();
        foreach (var failedProperty in context.Failures)
        {
            warnings.Add($"Failed to parse {context.UniformType} uniform {failedProperty} in shader. Uniform: '{context.UniformMatch.Value}'");
        }
        GD.PushWarning(string.Join("\n", warnings));
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

    protected class SwTDimensionParserConfig : DimensionParserConfig
    {
        public override bool IsRequired => true;
    }

    public SamplerWithTextureParser()
    {
        ParserConfigs[UniformProperty.Dimension] = new SwTDimensionParserConfig();
    }
}

public class SamplerWithTextureBufferParser : UniformParser
{
    protected override Regex Regex => UniformRegex.SamplerWithTextureBuffer();
    protected override UniformType UniformType => UniformType.SamplerWithTextureBuffer;

    protected class SwTBDimensionParserConfig : DimensionParserConfig
    {
        public override uint DefaultValue => 1u;
    }

    public SamplerWithTextureBufferParser()
    {
        ParserConfigs[UniformProperty.Dimension] = new SwTBDimensionParserConfig();
    }
}

public class ImageParser : UniformParser
{
    protected override Regex Regex => UniformRegex.Image();
    protected override UniformType UniformType => UniformType.Image;

    protected class ImageFormatParserConfig : FormatParserConfig
    {
        public override bool IsRequired => true;
    };

    protected class ImageAccessParserConfig : AccessParserConfig
    {
        public override UniformAccess DefaultValue => UniformAccess.ReadWrite;
    };

    protected class ImageDimensionParserConfig : DimensionParserConfig
    {
        public override bool IsRequired => true;
        public override uint DefaultValue => 1u;
    };

    public ImageParser()
    {
        ParserConfigs[UniformProperty.Format] = new ImageFormatParserConfig();
        ParserConfigs[UniformProperty.Access] = new ImageAccessParserConfig();
        ParserConfigs[UniformProperty.Dimension] = new ImageDimensionParserConfig();
    }
}

public class ImageBufferParser : UniformParser
{
    protected override Regex Regex => UniformRegex.ImageBuffer();
    protected override UniformType UniformType => UniformType.ImageBuffer;

    protected class ImageBufferFormatParserConfig : FormatParserConfig
    {
        public override bool IsRequired => true;
    };

    protected class ImageBufferAccessParserConfig : AccessParserConfig
    {
        public override bool IsRequired => true;
        public override UniformAccess DefaultValue => UniformAccess.ReadWrite;
    };

    protected class ImageBufferDimensionParserConfig : DimensionParserConfig
    {
        public override uint DefaultValue => 1u;
    };

    public ImageBufferParser()
    {
        ParserConfigs[UniformProperty.Format] = new ImageBufferFormatParserConfig();
        ParserConfigs[UniformProperty.Access] = new ImageBufferAccessParserConfig();
        ParserConfigs[UniformProperty.Dimension] = new ImageBufferDimensionParserConfig();
    }
}

public class TextureParser : UniformParser
{
    protected override Regex Regex => UniformRegex.Texture();
    protected override UniformType UniformType => UniformType.Texture;

    protected class TextureDimensionParserConfig : DimensionParserConfig
    {
        public override bool IsRequired => true;
        public override uint DefaultValue => 1u;
    };

    public TextureParser()
    {
        ParserConfigs[UniformProperty.Dimension] = new TextureDimensionParserConfig();
    }
}

public class TextureBufferParser : UniformParser
{
    protected override Regex Regex => UniformRegex.TextureBuffer();
    protected override UniformType UniformType => UniformType.TextureBuffer;

    protected class TextureBufferDimensionParserConfig : DimensionParserConfig
    {
        public override uint DefaultValue => 1u;
    };

    public TextureBufferParser()
    {
        ParserConfigs[UniformProperty.Dimension] = new TextureBufferDimensionParserConfig();
    }
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

    protected class StorageBufferAccessParserConfig : AccessParserConfig
    {
        public override UniformAccess DefaultValue => UniformAccess.ReadWrite;
    };

    public StorageBufferParser()
    {
        ParserConfigs[UniformProperty.Access] = new StorageBufferAccessParserConfig();
    }
}

public class InputAttachmentParser : UniformParser
{
    protected override Regex Regex => UniformRegex.InputAttachment();
    protected override UniformType UniformType => UniformType.InputAttachment;

    protected class InputAttachmentDimensionParserConfig : DimensionParserConfig
    {
        public override uint DefaultValue => 2u;
    };

    public InputAttachmentParser()
    {
        ParserConfigs[UniformProperty.Dimension] = new InputAttachmentDimensionParserConfig();
    }
}