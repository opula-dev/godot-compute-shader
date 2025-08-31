using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace Godot.ComputeShader.Reflector;

[Flags]
public enum UniformRole
{
    None = 0,
    Push = 1 << 0,
    Read = 1 << 1,
    Write = 1 << 2,
    Sampler = 1 << 3,
    Texture = 1 << 4,
    Buffer = 1 << 5,
    Array = 1 << 6,
}

public readonly partial struct UniformKey : IEquatable<UniformKey>
{

    [GeneratedRegex(@"^(.*)\[(\d+)\]$")]
    private static partial Regex ArrayFormatRegex();

    public static readonly string WriteSuffix = "_write";
    public static readonly string ReadSuffix = "_read";
    public static readonly string PushPrefix = "push_";

    public bool IsCanonical { get; init; } = false;
    public string BaseName { get; init; } = string.Empty;
    public UniformRole Role { get; init; } = UniformRole.None;
    public bool IsArray => IsRole(UniformRole.Array);
    public int ArrayIndex { get; init; } = -1;
    public bool IsPushConstant => IsRole(UniformRole.Push);
    public int Step { get; init; } = -1;

    // Constructor for normal/ping-pong/array uniforms
    public UniformKey(
        string baseName = "",
        UniformRole role = UniformRole.None,
        int arraySize = -1,
        int step = -1,
        bool canonical = false)
    {
        IsCanonical = canonical;
        BaseName = baseName;
        Role = role;
        ArrayIndex = arraySize;
        Step = step;

        if (!UniformKeyValidator.IsValid(this, out var errors))
        {
            GD.PushError($"Invalid uniform key: '{this}'\n{errors}");
        }
    }

    public bool Equals(UniformKey other)
    {
        return BaseName == other.BaseName &&
               Role == other.Role &&
               ArrayIndex == other.ArrayIndex &&
               IsPushConstant == other.IsPushConstant &&
               Step == other.Step;
    }

    public override bool Equals(object? obj) => obj is UniformKey other && Equals(other);

    public static bool operator ==(UniformKey left, UniformKey right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(UniformKey left, UniformKey right)
    {
        return !(left == right);
    }

    public override int GetHashCode() =>
        HashCode.Combine(
            BaseName,
            Role,
            IsArray,
            ArrayIndex,
            IsPushConstant,
            Step);

    public override string ToString()
    {
        var roleValue = (int)Role;

        var baseName = string.Empty;
        var suffix = string.Empty;
        var index = string.Empty;

        while (roleValue != 0)
        {
            // Isolate the lowest set bit (e.g., if role = Texture | Array (0b0101_0000), lowest = 1 << 4)
            int lowestBit = roleValue & -roleValue;

            switch ((UniformRole)lowestBit)
            {
                case UniformRole.None:
                    baseName = BaseName;
                    break;
                case UniformRole.Push:
                    return IsCanonical ? $"{PushPrefix}<step>" : $"{PushPrefix}{Step}"; // Steps are uniquely named
                case UniformRole.Read:
                    suffix = ReadSuffix;
                    break;
                case UniformRole.Write:
                    suffix = WriteSuffix;
                    break;
                case UniformRole.Sampler:
                    baseName = BaseName;
                    break;
                case UniformRole.Texture:
                    baseName = BaseName;
                    break;
                case UniformRole.Buffer:
                    baseName = BaseName;
                    break;
                case UniformRole.Array:
                    index = IsCanonical ? "[]" : $"[{ArrayIndex}]";
                    break;
                default:
                    GD.PushWarning($"Unknown uniform role flag '{lowestBit}'");
                    break;
            }

            // Clear the lowest bit from the value
            roleValue ^= lowestBit;
        }

        return $"{baseName}{suffix}{index}";
    }

    public bool IsRole(UniformRole role) => (Role & role) == role;

    public static UniformKey TryParse(string input, RenderingDevice.UniformType uniformType)
    {
        UniformKey result = default;
        if (string.IsNullOrEmpty(input))
        {
            GD.PushError("Invalid uniform key string.");
            return result;
        }

        if (input.StartsWith(PushPrefix))
        {
            if (int.TryParse(input[PushPrefix.Length..], out int step) && step >= 0)
            {
                result = new UniformKey(string.Empty, UniformRole.Push, -1, step);
            }
            GD.PushError($"Invalid push constant key: '{input}'. Expected format '{PushPrefix}<step>' with non-negative step.");
            return result;
        }

        string baseName = input;
        int arrayIndex = -1;
        UniformRole role = GetTypeRole(uniformType);

        // Parse array index
        var arrayMatch = ArrayFormatRegex().Match(input);
        if (arrayMatch.Success)
        {
            baseName = arrayMatch.Groups[1].Value;
            arrayIndex = int.Parse(arrayMatch.Groups[2].Value);
            role |= UniformRole.Array;
        }

        // Parse read/write suffixes (override or add to typeRole)
        if (baseName.EndsWith(WriteSuffix))
        {
            baseName = baseName[..^WriteSuffix.Length];
            role |= UniformRole.Write;
        }
        else if (baseName.EndsWith(ReadSuffix))
        {
            baseName = baseName[..^ReadSuffix.Length];
            role |= UniformRole.Read;
        }

        result = new UniformKey(baseName, role, arrayIndex, -1);
        return result;
    }

    public static UniformKey GetCanonical(UniformKey key)
    {
        UniformRole canonicalRole = key.Role & ~UniformRole.Array; // Remove array index specificity
        if (key.IsRole(UniformRole.Read) || key.IsRole(UniformRole.Write))
        {
            canonicalRole |= UniformRole.Read | UniformRole.Write; // Normalize to paired read-write for ping-pong lookups
        }

        return new UniformKey(key.BaseName, canonicalRole, -1, -1, canonical: true);
    }

    public static UniformRole GetTypeRole(RenderingDevice.UniformType uniformType)
    {
        return uniformType switch
        {
            RenderingDevice.UniformType.Sampler => UniformRole.Sampler,
            RenderingDevice.UniformType.SamplerWithTexture => UniformRole.Sampler | UniformRole.Texture,
            RenderingDevice.UniformType.Texture => UniformRole.Texture,
            RenderingDevice.UniformType.Image => UniformRole.Texture,
            RenderingDevice.UniformType.TextureBuffer => UniformRole.Texture | UniformRole.Buffer,
            RenderingDevice.UniformType.SamplerWithTextureBuffer => UniformRole.Sampler | UniformRole.Texture | UniformRole.Buffer,
            RenderingDevice.UniformType.ImageBuffer => UniformRole.Texture | UniformRole.Buffer,
            RenderingDevice.UniformType.UniformBuffer => UniformRole.Buffer,
            RenderingDevice.UniformType.StorageBuffer => UniformRole.Buffer,
            RenderingDevice.UniformType.InputAttachment => UniformRole.None, // Unimplemented
            _ => UniformRole.None,
        };
    }
}

public static class UniformKeyValidator
{
    private readonly struct Validator
    {
        public Func<UniformKey, bool> Invoker { get; init; }
        public string Error { get; init; }
    }

    public static bool IsValid(UniformKey key, out List<string> errors)
    {
        errors = [];

        var validators = new List<Validator>()
        {
            new() {
                Invoker = (key) => !key.IsCanonical && key.IsRole(UniformRole.Push) && key.Step < 0,
                Error = $"{nameof(UniformKey)} must define step for Push Constant Uniform key"
            },
            new() {
                Invoker = (key) => !key.IsCanonical && key.IsRole(UniformRole.Array) && key.ArrayIndex < 0,
                Error = $"{nameof(UniformKey)} must define array index for Array Uniform key"
            },
        };

        foreach (var validator in validators)
        {
            if (validator.Invoker(key))
            {
                errors.Add(validator.Error);
            }
        }

        return errors.Count == 0;
    }
}