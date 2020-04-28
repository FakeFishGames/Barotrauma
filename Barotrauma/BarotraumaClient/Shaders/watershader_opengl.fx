float xBlurDistance;

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

Texture xTexture;
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
	float4 texCoords = mul(input.Position, xUvTransform);
	float2 texCoords2D = texCoords.xy;
	//This is to stop the HLSL compiler from optimizing xUvTransform since that crashes MonoGame
	//Seems to be a known bug, see https://github.com/MonoGame/MonoGame/issues/5628
	//We should probably send this to them so they have more to work with
	texCoords2D.x += floor(texCoords.w*0.001);
	//--------
    output.TexCoords = texCoords2D;

    return output;
}

float4 mainPS(VertexShaderOutput input) : COLOR
{	
	float4 bumpColor = tex2D(WaterBumpSampler, xUvOffset + (input.TexCoords+xBumpPos) * xBumpScale);
	bumpColor = (bumpColor + tex2D(WaterBumpSampler, xUvOffset + (input.TexCoords-xBumpPos*2) * xBumpScale)) * 0.5f;
	
	float2 samplePos = input.TexCoords;
	
	samplePos.x += (bumpColor.r - 0.5f) * xWaveWidth * input.Color.r;	
	samplePos.y += (bumpColor.g - 0.5f) * xWaveHeight * input.Color.g;	

	float4 sample = tex2D(TextureSampler, samplePos);
		
	sample.a = input.Color.a;
	sample = lerp(sample, sample * waterColor, input.Color.b);

    return sample;
}

float4 mainPSBlurred(VertexShaderOutput input) : COLOR
{
    float4 bumpColor = tex2D(WaterBumpSampler, xUvOffset + (input.TexCoords + xBumpPos) * xBumpScale);
    bumpColor = (bumpColor + tex2D(WaterBumpSampler, xUvOffset + (input.TexCoords - xBumpPos * 2) * xBumpScale)) * 0.5f;
	
    float2 samplePos = input.TexCoords;
	
    samplePos.x += (bumpColor.r - 0.5f) * xWaveWidth * input.Color.r;
    samplePos.y += (bumpColor.g - 0.5f) * xWaveHeight * input.Color.g;

    float4 sample;
    sample = tex2D(TextureSampler, float2(samplePos.x + xBlurDistance, samplePos.y + xBlurDistance));
    sample += tex2D(TextureSampler, float2(samplePos.x - xBlurDistance, samplePos.y - xBlurDistance));
    sample += tex2D(TextureSampler, float2(samplePos.x + xBlurDistance, samplePos.y - xBlurDistance));
    sample += tex2D(TextureSampler, float2(samplePos.x - xBlurDistance, samplePos.y + xBlurDistance));
	
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
        VertexShader = compile vs_3_0 mainVS();
        PixelShader = compile ps_3_0 mainPS();
    }
}

technique WaterShaderBlurred
{
    pass Pass1
    {
        VertexShader = compile vs_3_0 mainVS();
        PixelShader = compile ps_3_0 mainPSBlurred();
    }
}

technique WaterShaderPostProcess
{
    pass Pass1
    {
        PixelShader = compile ps_3_0 mainPostProcess();
    }
}
