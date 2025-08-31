using System;
using System.Collections.Generic;
using Godot;

namespace Godot.ComputeShader.Reflector;

public static partial class LocalGroupParser
{
    public static Vector3I Parse(ref readonly string source)
    {
        var localSize = new Vector3I(1, 1, 1);

        // Flexible local size parsing
        var localMatch = UniformRegex.LocalSize().Match(source);
        if (localMatch.Success)
        {
            var qualifiers = localMatch.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var sizeDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var qual in qualifiers)
            {
                var parts = qual.Split('=', StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && parts[0].StartsWith("local_size_", StringComparison.OrdinalIgnoreCase))
                {
                    var key = parts[0]["local_size_".Length..].ToLowerInvariant();
                    if (int.TryParse(parts[1], out int val))
                    {
                        sizeDict[key] = val;
                    }
                }
            }

            if (sizeDict.TryGetValue("x", out int x)) localSize.X = x;
            if (sizeDict.TryGetValue("y", out int y)) localSize.Y = y;
            if (sizeDict.TryGetValue("z", out int z)) localSize.Z = z;

            if (sizeDict.Count < 3)
            {
                GD.PushWarning($"Partial local workgroup size parsed; defaults applied. '{localMatch.Value}'");
            }
            else if (sizeDict.Count == 0)
            {
                GD.PushWarning($"Failed to parse local workgroup in shader. '{localMatch.Value}'");
            }
        }

        return localSize;
    }
}