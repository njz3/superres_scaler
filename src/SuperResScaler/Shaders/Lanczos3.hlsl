// Copyright (c) Benjamin Maurin (njz3) 2026

// Lanczos 3 (6x6 taps): the sharpest of the smooth filters, for upscaling 3D or video
// content. Some ringing on hard edges; not intended for strong downscaling.
#include "Common.hlsli"

#define PI 3.14159265359

float Lanczos3(float x)
{
    x = abs(x);
    if (x < 1e-5)
        return 1.0;
    if (x >= 3.0)
        return 0.0;
    float px = PI * x;
    return 3.0 * sin(px) * sin(px / 3.0) / (px * px);
}

float4 main(PSInput input) : SV_TARGET
{
    float2 position = input.TexCoord * SourceSize - 0.5;
    int2 base = int2(floor(position));
    float2 f = position - floor(position);

    float wx[6];
    float wy[6];
    [unroll] for (int k = 0; k < 6; ++k)
    {
        wx[k] = Lanczos3(k - 2 - f.x);
        wy[k] = Lanczos3(k - 2 - f.y);
    }

    float4 colour = 0;
    float total = 0;
    [unroll] for (int j = 0; j < 6; ++j)
    {
        [unroll] for (int i = 0; i < 6; ++i)
        {
            float w = wx[i] * wy[j];
            colour += Texel(base + int2(i - 2, j - 2)) * w;
            total += w;
        }
    }
    return saturate(colour / total);
}
