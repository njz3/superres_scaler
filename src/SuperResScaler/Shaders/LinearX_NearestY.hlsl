// Copyright (c) Benjamin Maurin (njz3) 2026

// Bilinear horizontally, nearest vertically: a smooth horizontal stretch while every
// scanline stays crisp. Matches what an analog CRT does with a super-resolution mode
// (horizontally continuous, vertically discrete lines).
#include "Common.hlsli"

float4 main(PSInput input) : SV_TARGET
{
    float2 texel = input.TexCoord * SourceSize;
    texel.y = floor(texel.y) + 0.5;
    return Source.SampleLevel(LinearSampler, texel * InvSourceSize, 0);
}
