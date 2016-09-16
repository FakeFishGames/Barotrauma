
Texture xTexture;
sampler TextureSampler : register (s0) = sampler_state { Texture = <xTexture>; };

Texture xStencil;
sampler StencilSampler = sampler_state { Texture = <xStencil>; };


float cutoff;
float multiplier;

float4 main(float4 position : SV_Position, float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
	float4 c = tex2D(TextureSampler, texCoord);

	float4 stencilColor = tex2D(StencilSampler, texCoord);

	float a = stencilColor.a - cutoff;

	clip(a);

	a = min(a * multiplier, 1.0f);
	c = lerp(c, stencilColor, 1.0f - a);

	return c * a;
}

technique StencilShader
{
    pass Pass1
    {
        PixelShader = compile ps_4_0_level_9_3 main();
    }
}
