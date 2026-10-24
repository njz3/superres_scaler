// Copyright (c) Benjamin Maurin (njz3) 2026

// Area (box) filter: each output pixel is the exact average of the source area it covers.
//  - Upscaling (320 -> 1920): same as nearest, except output pixels that straddle two source
//    pixels get the blended colour, so non-integer ratios (256 -> 1920 = 7.5x) stay even.
//  - Downscaling (480 -> 240 lines): lines are averaged instead of dropped.
// Default shader: a good fit for both axes of a super-resolution mode.
#include "Common.hlsli"

#define MAX_TAPS 8

float4 main(PSInput input) : SV_TARGET
{
    float2 footprint = SourceSize * InvOutputSize;   // source pixels covered by one output pixel
    float2 centre = input.TexCoord * SourceSize;
    float2 lo = centre - 0.5 * footprint;
    float2 hi = centre + 0.5 * footprint;
    int2 first = int2(floor(lo));
    int2 last = min(int2(ceil(hi)) - 1, first + MAX_TAPS - 1);

    float4 sum = 0;
    float total = 0;
    [loop] for (int y = first.y; y <= last.y; ++y)
    {
        float wy = min(hi.y, y + 1) - max(lo.y, y);
        [loop] for (int x = first.x; x <= last.x; ++x)
        {
            float w = (min(hi.x, x + 1) - max(lo.x, x)) * wy;
            sum += Texel(int2(x, y)) * w;
            total += w;
        }
    }
    return sum / max(total, 1e-6);
}
