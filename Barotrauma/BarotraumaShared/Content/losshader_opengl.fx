
Texture2D xTexture;
sampler TextureSampler : register (s0) = sampler_state { Texture = <xTexture>; };

Texture2D xLosTexture;
sampler LosSampler = sampler_state { Texture = <xLosTexture>; };

float4 main(float4 position : SV_Position, float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{	
	float4 losColor = tex2D(LosSampler, texCoord);
	float4 sample = tex2D(TextureSampler, texCoord);
	
	float4 outColor = float4(sample.x*losColor.x, sample.y*losColor.x, sample.z*losColor.x, losColor.x);
		
	return outColor;
}

technique LosShader
{
    pass Pass1
    {
        PixelShader = compile ps_2_0 main();
    }
}
