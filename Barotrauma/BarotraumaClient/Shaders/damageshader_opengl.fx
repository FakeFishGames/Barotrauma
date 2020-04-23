
Texture xTexture;
sampler TextureSampler : register (s0) = sampler_state { Texture = <xTexture>; };

Texture xStencil;
sampler StencilSampler = sampler_state { Texture = <xStencil>; };

float4 inColor;

float aCutoff;
float aMultiplier;

float cCutoff;
float cMultiplier;

float4 main(float4 position : POSITION0, float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
	float4 c = tex2D(TextureSampler, texCoord) * inColor;

	float4 stencilColor = tex2D(StencilSampler, texCoord);

	float aDiff = stencilColor.a - aCutoff;

	clip(aDiff);

	float cDiff = stencilColor.a - cCutoff;

	return float4(
		lerp(stencilColor.rgb, c.rgb, clamp(cDiff * cMultiplier, 0.0f, 1.0f)),
		min(aDiff * aMultiplier, c.a));
}

technique StencilShader
{
    pass Pass1
    {
        PixelShader = compile ps_2_0 main();
    }
}
