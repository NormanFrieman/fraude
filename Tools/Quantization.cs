using System.Runtime.CompilerServices;

namespace Fraude.Tools;

public static class Quantization
{
    public const int Dimensions = 14;
    public const int PaddedDimensions = 16;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static sbyte Quantize(float value, int dimension)
    {
        if ((dimension == 5 || dimension == 6) && value < -0.5f)
            return -128;
        var clamped = Math.Clamp(value, 0f, 1f);
        return (sbyte)Math.Round(clamped * 127f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void QuantizeVector(ReadOnlySpan<float> vector, Span<sbyte> output)
    {
        for (var i = 0; i < Dimensions; i++)
            output[i] = Quantize(vector[i], i);
        output[14] = 0;
        output[15] = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short QuantizeToInt16(float value, int dimension)
    {
        if ((dimension == 5 || dimension == 6) && value < -0.5f)
            return -32768;
        var clamped = Math.Clamp(value, 0f, 1f);
        return (short)Math.Round(clamped * 32767f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void QuantizeVectorToInt16(ReadOnlySpan<float> vector, Span<short> output)
    {
        for (var i = 0; i < Dimensions; i++)
            output[i] = QuantizeToInt16(vector[i], i);
        output[14] = 0;
        output[15] = 0;
    }
}
