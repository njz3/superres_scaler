// Nearest neighbour: each output pixel takes the colour of the source pixel it falls in.
// Sharpest result. On a super-resolution mode (1920x240, 2560x240...) the horizontal error
// is a tiny fraction of a source pixel, so it is often the best choice there.
// Downscaling 480 -> 240 lines drops every other line (use Area instead).
#include "Common.hlsli"

float4 main(PSInput input) : SV_TARGET
{
    return Texel(int2(floor(input.TexCoord * SourceSize)));
}
