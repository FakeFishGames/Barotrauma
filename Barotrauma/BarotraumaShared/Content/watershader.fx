float xBlurDistance;

Texture xTexture;
sampler TextureSampler = sampler_state { Texture = <xTexture>; };

Texture xLosTexture;
sampler LosSampler = sampler_state { Texture = <xLosTexture>; };


Texture xWaterBumpMap;
sampler WaterBumpSampler  = 
sampler_state 
{ 
	Texture = <xWaterBumpMap>; 
	MagFilter = LINEAR; 
	MinFilter = LINEAR; 
	MipFilter = LINEAR; 
	AddressU = WRAP; 
	AddressV = WRAP;
};

float xWaveWidth;
float xWaveHeight;
float2 xWavePos;
float2 xBumpPos;

float4 main(float4 position : SV_Position, float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{	
	float4 bumpColor = tex2D(WaterBumpSampler, texCoord+xWavePos+xBumpPos);
	bumpColor = (bumpColor + tex2D(WaterBumpSampler, texCoord-xWavePos*2.0f+xBumpPos))*0.5f;
	
	float2 samplePos = texCoord;
	
	samplePos.x+=(bumpColor.r-0.5f)*xWaveWidth;	
	samplePos.y+=(bumpColor.g-0.5f)*xWaveHeight;	

	float4 sample;
	sample = tex2D( TextureSampler, float2(samplePos.x+xBlurDistance, samplePos.y+xBlurDistance));
	sample += tex2D( TextureSampler, float2(samplePos.x-xBlurDistance, samplePos.y-xBlurDistance));
	sample += tex2D( TextureSampler, float2(samplePos.x+xBlurDistance, samplePos.y-xBlurDistance));
	sample += tex2D( TextureSampler, float2(samplePos.x-xBlurDistance, samplePos.y+xBlurDistance));	
	
	sample = sample * 0.25;
	
    return sample;
}

float4 main2(float4 position : SV_Position, float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{	
	float4 losColor = tex2D(LosSampler, texCoord);
	
	float4 outColor = float4(losColor.x, losColor.y, losColor.z, color.w);
		
    return outColor;
}


technique WaterShader
{
    pass Pass1
    {
        PixelShader = compile ps_4_0_level_9_1 main();
    }
}

technique LosShader
{
    pass Pass1
    {
        PixelShader = compile ps_4_0_level_9_1 main2();
    }
}