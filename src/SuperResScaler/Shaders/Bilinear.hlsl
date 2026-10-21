// Plain bilinear filtering: smooth, but blurs pixel art when upscaling a lot.
#include "Common.hlsli"

float4 main(PSInput input) : SV_TARGET
{
    return Source.SampleLevel(LinearSampler, input.TexCoord, 0);
}
