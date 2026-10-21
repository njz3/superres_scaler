// Declarations shared by every SuperResScaler pixel shader.
//
// A shader is one pixel shader with the entry point `main`, compiled as ps_5_0.
// It is run once per output pixel of the destination rectangle and reads the captured
// (already cropped) window image from `Source`.
//
//   input.TexCoord : 0..1 across the destination rectangle, at pixel centres
//   SourceSize     : size of the cropped source image, in pixels
//   OutputSize     : size of the destination rectangle, in pixels

Texture2D<float4> Source : register(t0);
SamplerState PointSampler : register(s0);   // nearest, clamp to edge
SamplerState LinearSampler : register(s1);  // bilinear, clamp to edge

cbuffer Constants : register(b0)
{
    float2 SourceSize;
    float2 InvSourceSize;
    float2 OutputSize;
    float2 InvOutputSize;
    float  Time;          // seconds since the scaling started
    uint   FrameCount;
    float2 OutputOffset;  // top-left corner of the destination rectangle on the screen
};

struct PSInput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

// One source pixel, with clamp-to-edge addressing.
float4 Texel(int2 p)
{
    return Source.Load(int3(clamp(p, int2(0, 0), int2(SourceSize) - 1), 0));
}
