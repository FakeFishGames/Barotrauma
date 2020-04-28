float xBlurDistance;

Texture2D xWaterBumpMap;
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

Texture2D xTexture;
sampler TextureSampler = sampler_state { Texture = <xTexture>; };

float4x4 xTransform; 
float4x4 xUvTransform; 

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TexCoords: TEXCOORD0; // added
};

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TexCoords: TEXCOORD0; // added
}; 


float xWaveWidth;
float xWaveHeight;
float2 xBumpPos;
float2 xBumpScale;

float2 xUvOffset;

float4 waterColor;

VertexShaderOutput mainVS(in VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput)0;

    output.Position = mul(input.Position, xTransform);
    output.Color = input.Color;
    output.TexCoords = mul(input.Position, xUvTransform).xy;

    return output;
}

float4 mainPS(VertexShaderOutput input) : COLOR
{	
	float4 bumpColor = xWaterBumpMap.Sample(WaterBumpSampler, xUvOffset + (input.TexCoords+xBumpPos) * xBumpScale);
	bumpColor = (bumpColor + xWaterBumpMap.Sample(WaterBumpSampler, xUvOffset + (input.TexCoords-xBumpPos*2) * xBumpScale)) * 0.5f;
	
	float2 samplePos = input.TexCoords;
	
	samplePos.x += (bumpColor.r - 0.5f) * xWaveWidth * input.Color.r;	
	samplePos.y += (bumpColor.g - 0.5f) * xWaveHeight * input.Color.g;	

	float4 sample = xTexture.Sample(TextureSampler, samplePos);
		
	sample.a = input.Color.a;
	sample = lerp(sample, sample * waterColor, input.Color.b);

    return sample;
}

float4 mainPSBlurred(VertexShaderOutput input) : COLOR
{
    float4 bumpColor = xWaterBumpMap.Sample(WaterBumpSampler, xUvOffset + (input.TexCoords + xBumpPos) * xBumpScale);
    bumpColor = (bumpColor + xWaterBumpMap.Sample(WaterBumpSampler, xUvOffset + (input.TexCoords - xBumpPos * 2) * xBumpScale)) * 0.5f;
	
    float2 samplePos = input.TexCoords;
	
    samplePos.x += (bumpColor.r - 0.5f) * xWaveWidth * input.Color.r;
    samplePos.y += (bumpColor.g - 0.5f) * xWaveHeight * input.Color.g;

    float4 sample;
    sample = xTexture.Sample(TextureSampler, float2(samplePos.x + xBlurDistance, samplePos.y + xBlurDistance));
    sample += xTexture.Sample(TextureSampler, float2(samplePos.x - xBlurDistance, samplePos.y - xBlurDistance));
    sample += xTexture.Sample(TextureSampler, float2(samplePos.x + xBlurDistance, samplePos.y - xBlurDistance));
    sample += xTexture.Sample(TextureSampler, float2(samplePos.x - xBlurDistance, samplePos.y + xBlurDistance));
	
    sample = sample * 0.25;
	
    sample.a = input.Color.a;
    sample = lerp(sample, sample * waterColor, input.Color.b);

    return sample;
}

float4 mainPostProcess(float4 position : POSITION0, float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 bumpColor = tex2D(WaterBumpSampler, texCoord + xUvOffset + xBumpPos);
    bumpColor = (bumpColor + tex2D(WaterBumpSampler, texCoord - xUvOffset * 2.0f + xBumpPos)) * 0.5f;
	
    float2 samplePos = texCoord;
	
    samplePos.x += (bumpColor.r - 0.5f) * xWaveWidth;
    samplePos.y += (bumpColor.g - 0.5f) * xWaveHeight;

    float4 sample;
    sample = tex2D(TextureSampler, float2(samplePos.x + xBlurDistance, samplePos.y + xBlurDistance));
    sample += tex2D(TextureSampler, float2(samplePos.x - xBlurDistance, samplePos.y - xBlurDistance));
    sample += tex2D(TextureSampler, float2(samplePos.x + xBlurDistance, samplePos.y - xBlurDistance));
    sample += tex2D(TextureSampler, float2(samplePos.x - xBlurDistance, samplePos.y + xBlurDistance));
	
    sample = sample * 0.25;
	
    return sample;
}

technique WaterShader
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 mainPS();
    }
}

technique WaterShaderBlurred
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 mainPSBlurred();
    }
}

technique WaterShaderPostProcess
{
    pass Pass1
    {
        PixelShader = compile ps_4_0_level_9_1 mainPostProcess();
    }
}
