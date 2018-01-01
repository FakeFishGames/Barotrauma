float xBlurDistance;

Texture xTexture;
sampler TextureSampler = sampler_state { Texture = <xTexture>; };

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

technique WaterShader
{
    pass Pass1
    {
        PixelShader = compile ps_2_0 main();
    }
}
