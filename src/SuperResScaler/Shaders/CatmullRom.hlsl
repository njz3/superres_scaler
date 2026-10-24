// Copyright (c) Benjamin Maurin (njz3) 2026

// Bicubic Catmull-Rom (4x4 taps): sharper than bilinear for photographic / 3D content.
// Slight ringing on hard edges.
#include "Common.hlsli"

float4 CatmullRomWeights(float t)
{
    float t2 = t * t;
    float t3 = t2 * t;
    return float4(
        -0.5 * t3 + t2 - 0.5 * t,
         1.5 * t3 - 2.5 * t2 + 1.0,
        -1.5 * t3 + 2.0 * t2 + 0.5 * t,
         0.5 * t3 - 0.5 * t2);
}

float4 main(PSInput input) : SV_TARGET
{
    float2 position = input.TexCoord * SourceSize - 0.5;
    int2 base = int2(floor(position));
    float2 f = position - floor(position);
    float4 wx = CatmullRomWeights(f.x);
    float4 wy = CatmullRomWeights(f.y);

    float4 colour = 0;
    [unroll] for (int j = 0; j < 4; ++j)
    {
        float4 row = 0;
        [unroll] for (int i = 0; i < 4; ++i)
            row += Texel(base + int2(i - 1, j - 1)) * wx[i];
        colour += row * wy[j];
    }
    return saturate(colour);
}
