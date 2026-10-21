// Sharp bilinear: nearest-neighbour inside each source pixel, a short bilinear ramp at its
// edges. Equivalent to an integer nearest prescale followed by bilinear filtering: crisp
// pixels without the uneven widths of plain nearest at non-integer ratios.
// When an axis is not upscaled it falls back to plain bilinear on that axis.
#include "Common.hlsli"

float4 main(PSInput input) : SV_TARGET
{
    float2 texel = input.TexCoord * SourceSize;
    float2 scale = max(floor(OutputSize * InvSourceSize), 1.0);

    float2 base = floor(texel);
    float2 offset = frac(texel) - 0.5;
    float2 range = 0.5 - 0.5 / scale;   // flat region around the pixel centre
    float2 ramp = (offset - clamp(offset, -range, range)) * scale + 0.5;

    return Source.SampleLevel(LinearSampler, (base + ramp) * InvSourceSize, 0);
}
