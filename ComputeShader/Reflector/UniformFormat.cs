using System;
using System.Collections.Generic;
using Godot;

namespace Godot.ComputeShader.Reflector;

using DataFormat = RenderingDevice.DataFormat;

public static class UniformFormat
{
    public static readonly Dictionary<string, DataFormat> FormatMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Single Precision Float   
        ["rgba32f"] = DataFormat.R32G32B32A32Sfloat, // 4 channels
        ["rg32f"] = DataFormat.R32G32Sfloat, // 2 channels
        ["r32f"] = DataFormat.R32Sfloat, // 1 channel
        // Half Precision Float
        ["rgba16f"] = DataFormat.R16G16B16A16Sfloat, // 4 channels
        ["rg16f"] = DataFormat.R16G16Sfloat, // 2 channels
        ["r16f"] = DataFormat.R16Sfloat, // 1 channel
        // 11 Bit Red/Green 10 Bit Blue for HDR Color (32bit packed)
        ["r11f_g11f_b10f"] = DataFormat.B10G11R11UfloatPack32,
        // Short Unsigned Integer Normalized [0 to 65535] => [0.0 to 1.0] per channel
        ["rgba16"] = DataFormat.R16G16B16A16Unorm, // 4 channels
        ["rg16"] = DataFormat.R16G16Unorm, // 2 channels
        ["r16"] = DataFormat.R16Unorm, // 1 channel
        // 8 Bit Unsigned Integer Normalized [0 to 255] => [0.0 to 1.0] per channel
        ["rgba8"] = DataFormat.R8G8B8A8Unorm, // 4 channels
        ["rg8"] = DataFormat.R8G8Unorm, // 2 channels
        ["r8"] = DataFormat.R8Unorm, // 1 channel
        // 10 bit Unsigned Normalized RGB with 2 Bit Alpha (32bit Packed)
        ["rgb10_a2"] = DataFormat.A2B10G10R10UnormPack32, // [0 to 1023] => [0.0 to 1.0] per channel
        // Short Signed Integers Normalized [-32768 to 32767] => [-1.0 to 1.0]
        ["rgba16_snorm"] = DataFormat.R16G16B16A16Snorm, // 4 channels
        ["rg16_snorm"] = DataFormat.R16G16Snorm, // 2 channels
        ["r16_snorm"] = DataFormat.R16Snorm, // 1 channel
        // 8 Bit Signed Integers Normalized [-128 to 127] => [-1.0 to 1.0] per channel
        ["rgba8_snorm"] = DataFormat.R8G8B8A8Snorm, // 4  channels
        ["rg8_snorm"] = DataFormat.R8G8Snorm, // 2 channels
        ["r8_snorm"] = DataFormat.R8Snorm, // 1 channel
        // Signed Integers
        ["rgba32i"] = DataFormat.R32G32B32A32Sint,
        ["rg32i"] = DataFormat.R32G32Sint,
        ["r32i"] = DataFormat.R32Sint,
        // Short Signed Integers
        ["rgba16i"] = DataFormat.R16G16B16A16Sint,
        ["rg16i"] = DataFormat.R16G16Sint,
        ["r16i"] = DataFormat.R16Sint,
        // 8 Bit Signed Integers
        ["rgba8i"] = DataFormat.R8G8B8A8Sint,
        ["rg8i"] = DataFormat.R8G8Sint,
        ["r8i"] = DataFormat.R8Sint,
        // Unsigned Integers
        ["rgba32ui"] = DataFormat.R32G32B32A32Uint,
        ["rg32ui"] = DataFormat.R32G32Uint,
        ["r32ui"] = DataFormat.R32Uint,
        // Short Unsigned Integers
        ["rgba16ui"] = DataFormat.R16G16B16A16Uint,
        ["rg16ui"] = DataFormat.R16G16Uint,
        ["r16ui"] = DataFormat.R16Uint,
        // 8 Bit Unsigned Integers
        ["rgba8ui"] = DataFormat.R8G8B8A8Uint,
        ["rg8ui"] = DataFormat.R8G8Uint,
        ["r8ui"] = DataFormat.R8Uint,
        // 10 Bit Unsigned RGB With 2 Bit Alpha (32bit Packed)
        ["rgb10_a2ui"] = DataFormat.A2B10G10R10UintPack32,
    };
}