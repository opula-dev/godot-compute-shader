using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Godot;

namespace Godot.ComputeShader.Reflector;

public static partial class GlslShaderParser
{
    /// <summary>
    /// Represents the result of parsing a shader source.
    /// </summary>
    public readonly record struct ParseResult
    {
        public IReadOnlyList<UniformInfo> Uniforms { get; init; }
        public bool HasPushConstant { get; init; }
        public Vector3I LocalSize { get; init; }
    }

    [GeneratedRegex(@"//.*?$|/\*.*?\*/", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    /// <summary>
    /// Strips single-line and multi-line comments from the shader source.
    /// </summary>
    /// <param name="source">The original shader source.</param>
    /// <returns>The source without comments.</returns>
    private static string StripComments(string source)
    {
        // Regex to match // comments (to end of line) and /* */ comments (multi-line, non-nested)
        return CommentRegex().Replace(source, string.Empty);
    }

    /// <summary>
    /// Parses the shader source code to extract uniforms, push constant presence, and local workgroup size.
    /// </summary>
    /// <param name="source">The shader source code.</param>
    /// <returns>A <see cref="ParseResult"/> containing the parsed information.</returns>
    public static ParseResult ParseShaderSource(string source)
    {
        source = StripComments(source); // Preprocess to remove comments

        var hasPushConstant = UniformRegex.PushConstant().IsMatch(source);
        var localSize = LocalGroupParser.Parse(in source);

        var parsers = new UniformParser[]
        {
            new SamplerParser(),
            new SamplerWithTextureParser(),
            new SamplerWithTextureBufferParser(),
            new ImageParser(),
            new ImageBufferParser(),
            new TextureParser(),
            new TextureBufferParser(),
            new UniformBufferParser(),
            new StorageBufferParser(),
            new InputAttachmentParser()
        };

        var uniforms = new List<UniformInfo>();
        foreach (var parser in parsers)
        {
            uniforms.AddRange(parser.Parse(source));
        }

        return new ParseResult
        {
            Uniforms = uniforms,
            HasPushConstant = hasPushConstant,
            LocalSize = localSize
        };
    }
}