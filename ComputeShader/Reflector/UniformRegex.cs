using System;
using System.Text.RegularExpressions;
using Godot;

namespace Godot.ComputeShader.Reflector;

public static partial class UniformRegex
{
    [GeneratedRegex(@"layout\s*\(\s*push_constant\s*\)\s*uniform\s*(?<name>\w+)\s*\{", RegexOptions.Multiline)]
    public static partial Regex PushConstant();

    [GeneratedRegex(@"layout\s*\(\s*([^)]+)\s*\)\s*in\s*;", RegexOptions.Multiline)]
    public static partial Regex LocalSize();

    [GeneratedRegex(@"layout\s*\(\s*(?:set\s*=\s*(?<set>\d+)\s*,)?\s*binding\s*=\s*(?<binding>\d+)\s*\)\s*uniform\s*sampler\s*(?<name>\w+)(?:\s*\[\s*(?<array_size>\d+)\s*\])?\s*;", RegexOptions.Multiline)]
    public static partial Regex Sampler();

    [GeneratedRegex(@"layout\s*\(\s*(?:set\s*=\s*(?<set>\d+)\s*,)?\s*binding\s*=\s*(?<binding>\d+)\s*\)\s*uniform\s*sampler(?<dimension>1D|2D|3D|Cube|2DArray|CubeArray)\s*(?<name>\w+)(?:\s*\[\s*(?<array_size>\d+)\s*\])?\s*;", RegexOptions.Multiline)]
    public static partial Regex SamplerWithTexture();

    [GeneratedRegex(@"layout\s*\(\s*(?:set\s*=\s*(?<set>\d+)\s*,)?\s*binding\s*=\s*(?<binding>\d+)\s*\)\s*uniform\s*texture(?<dimension>1D|2D|3D|Cube|2DArray|CubeArray)\s*(?<name>\w+)(?:\s*\[\s*(?<array_size>\d+)\s*\])?\s*;", RegexOptions.Multiline)]
    public static partial Regex Texture();

    [GeneratedRegex(@"layout\s*\(\s*(?:set\s*=\s*(?<set>\d+)\s*,)?\s*binding\s*=\s*(?<binding>\d+)\s*(?:,\s*(?<format>\w+))?\s*\)\s*uniform\s*(?<access>readonly|writeonly)?\s*image(?<dimension>1D|2D|3D)\s*(?<name>\w+)(?:\s*\[\s*(?<array_size>\d+)\s*\])?\s*;", RegexOptions.Multiline)]
    public static partial Regex Image();

    [GeneratedRegex(@"layout\s*\(\s*(?:set\s*=\s*(?<set>\d+)\s*,)?\s*binding\s*=\s*(?<binding>\d+)\s*\)\s*uniform\s*textureBuffer\s*(?<name>\w+)(?:\s*\[\s*(?<array_size>\d+)\s*\])?\s*;", RegexOptions.Multiline)]
    public static partial Regex TextureBuffer();

    [GeneratedRegex(@"layout\s*\(\s*(?:set\s*=\s*(?<set>\d+)\s*,)?\s*binding\s*=\s*(?<binding>\d+)\s*\)\s*uniform\s*samplerBuffer\s*(?<name>\w+)(?:\s*\[\s*(?<array_size>\d+)\s*\])?\s*;", RegexOptions.Multiline)]
    public static partial Regex SamplerWithTextureBuffer();

    [GeneratedRegex(@"layout\s*\(\s*(?:set\s*=\s*(?<set>\d+)\s*,)?\s*binding\s*=\s*(?<binding>\d+)\s*(?:,\s*(?<format>\w+))?\s*\)\s*uniform\s*(?<access>readonly|writeonly)?\s*imageBuffer\s*(?<name>\w+)(?:\s*\[\s*(?<array_size>\d+)\s*\])?\s*;", RegexOptions.Multiline)]
    public static partial Regex ImageBuffer();

    [GeneratedRegex(@"layout\s*\(\s*(?:set\s*=\s*(?<set>\d+)\s*,)?\s*binding\s*=\s*(?<binding>\d+)\s*\)\s*uniform\s*(?<name>\w+)(?:\s*\[\s*(?<array_size>\d+)\s*\])?\s*\{", RegexOptions.Multiline)]
    public static partial Regex UniformBuffer();

    [GeneratedRegex(@"layout\s*\(\s*(?:std\d+\s*,)?\s*(?:set\s*=\s*(?<set>\d+)\s*,)?\s*binding\s*=\s*(?<binding>\d+)\s*\)\s*(?<access>readonly|writeonly)?\s*buffer\s*(?<name>\w+)(?:\s*\[\s*(?<array_size>\d+)\s*\])?\s*\{", RegexOptions.Multiline)]
    public static partial Regex StorageBuffer();

    [GeneratedRegex(@"layout\s*\(\s*(?:input_attachment_index\s*=\s*\d+\s*,)?\s*(?:set\s*=\s*(?<set>\d+)\s*,)?\s*binding\s*=\s*(?<binding>\d+)\s*\)\s*uniform\s*subpassInput\s*(?<name>\w+)(?:\s*\[\s*(?<array_size>\d+)\s*\])?\s*;", RegexOptions.Multiline)]
    public static partial Regex InputAttachment();

    public static class Key
    {
        public static readonly string Access = "access";
        public static readonly string ArraySize = "array_size";
        public static readonly string Binding = "binding";
        public static readonly string Dimension = "dimension";
        public static readonly string Format = "format";
        public static readonly string Name = "name";
        public static readonly string Set = "set";
    }
}