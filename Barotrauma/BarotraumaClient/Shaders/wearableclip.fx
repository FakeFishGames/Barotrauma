
Texture2D xTexture;
sampler TextureSampler : register (s0) = sampler_state { Texture = <xTexture>; };

Texture2D xStencil;
sampler StencilSampler = sampler_state { Texture = <xStencil>; };

float aCutoff;
float4x4 wearableUvToClipperUv;
float clipperTexelSize;

float stencilSample(float2 texCoord, float2 offset)
{
    return xStencil.Sample(
        StencilSampler,
        mul(float4(texCoord.x, texCoord.y, 0, 1), wearableUvToClipperUv).xy + offset).a;
}

float4 main(float4 position : POSITION0, float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
	float4 c = xTexture.Sample(TextureSampler, texCoord) * color;

	float minStencil = stencilSample(texCoord, float2(0,0));
    minStencil = min(minStencil, stencilSample(texCoord, float2(-clipperTexelSize,0)));
    minStencil = min(minStencil, stencilSample(texCoord, float2(clipperTexelSize,0)));
    minStencil = min(minStencil, stencilSample(texCoord, float2(0,-clipperTexelSize)));
    minStencil = min(minStencil, stencilSample(texCoord, float2(0,clipperTexelSize)));

	float aDiff = minStencil - aCutoff;

	clip(aDiff);

	return c;
}

technique StencilShader
{
    pass Pass1
    {
        PixelShader = compile ps_4_0_level_9_1 main();
    }
}
