Texture2D xBaseTexture;
sampler BaseTextureSampler = sampler_state { Texture = <xBaseTexture>; };
Texture2D xTintMaskTexture;
sampler TintMaskTextureSampler = sampler_state { Texture = <xTintMaskTexture>; };
Texture2D xCutoffTexture;
sampler CutoffTextureSampler = sampler_state { Texture = <xCutoffTexture>; };

float highlightThreshold;
float highlightMultiplier;

float baseToCutoffSizeRatio;

float4 mainPS(float4 position : SV_POSITION, float4 clr : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
	float4 baseSample = tex2D(BaseTextureSampler, texCoord);
	float3 tintMaskSample = tex2D(TintMaskTextureSampler, texCoord).rgb;
	float cutoffSample = tex2D(CutoffTextureSampler, texCoord * baseToCutoffSizeRatio).r;

	float3 highlight = saturate((baseSample.rgb - (highlightThreshold * float3(1,1,1))) * highlightMultiplier);
	float3 tinted = saturate(baseSample.rgb * clr.rgb + highlight);
	return float4(
	    (tinted * tintMaskSample) + (baseSample.rgb * (float3(1,1,1) - tintMaskSample)),
	    baseSample.a * cutoffSample * clr.a);
}

technique ThresholdTintShader
{
	pass Pass1
	{
		PixelShader = compile ps_2_0 mainPS();
	}
}
