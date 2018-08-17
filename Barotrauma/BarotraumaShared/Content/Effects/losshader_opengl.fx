
Texture xTexture;
sampler TextureSampler : register (s0) = sampler_state { Texture = <xTexture>; };

Texture xLosTexture;
sampler LosSampler = sampler_state { Texture = <xLosTexture>; };

float4 main(float4 position : SV_Position, float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
	float4 sampleColor = tex2D(TextureSampler, texCoord);
	float4 losColor = tex2D(LosSampler, texCoord);

	float obscureAmount = 1.0f - losColor.r;

	float4 outColor = float4(
		sampleColor.r * color.r,
		sampleColor.g * color.g,
		sampleColor.b * color.b,
		obscureAmount);

	return outColor;
}

technique LosShader
{
    pass Pass1
    {
        PixelShader = compile ps_2_0 main();
    }
}
