
Texture2D xTexture;
sampler TextureSampler : register (s0) = sampler_state { Texture = <xTexture>; };

Texture2D xStencil;
sampler StencilSampler = sampler_state { Texture = <xStencil>; };

float aCutoff;
float4x4 wearableUvToClipperUv;
float clipperTexelSize;

float2 stencilUVmin, stencilUVmax;

float stencilSample(float2 texCoord, float2 offset)
{
    return tex2D(
        StencilSampler,
        mul(float4(texCoord.x, texCoord.y, 0, 1), wearableUvToClipperUv).xy + offset).a;
}

float4 main(float4 position : POSITION0, float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
	float4 c = tex2D(TextureSampler, texCoord) * color;
    
    float2 stencilUV = mul(float4(texCoord.x, texCoord.y, 0, 1), wearableUvToClipperUv).xy;
    clip(stencilUV.x - stencilUVmin.x);
    clip(stencilUV.y - stencilUVmin.y);
    clip(stencilUVmax.y - stencilUV.x);
    clip(stencilUVmax.y - stencilUV.y);

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
        PixelShader = compile ps_2_0 main();
    }
}
