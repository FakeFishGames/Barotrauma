struct VertexShaderInput
{
    float4 Position : POSITION0;
    float2 TexCoords: TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float2 TexCoords: TEXCOORD0;
}; 

VertexShaderOutput mainVS(in VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput)0;

    output.Position = input.Position;
    output.TexCoords = input.TexCoords;

    return output;
}

Texture2D xTexture;
sampler TextureSampler : register (s0) = sampler_state { Texture = <xTexture>; };

Texture2D xLosTexture;
sampler LosSampler = sampler_state { Texture = <xLosTexture>; };

float xLosAlpha;

float4 xColor;

float blurDistance;

float4 mainPS(VertexShaderOutput input) : COLOR0
{	
	float4 sampleColor = xTexture.Sample(TextureSampler, input.TexCoords);

    float4 losColor = xLosTexture.Sample(LosSampler, float2(input.TexCoords.x + blurDistance, input.TexCoords.y + blurDistance));
    losColor += xLosTexture.Sample(LosSampler, float2(input.TexCoords.x - blurDistance, input.TexCoords.y - blurDistance));
    losColor += xLosTexture.Sample(LosSampler, float2(input.TexCoords.x + blurDistance, input.TexCoords.y - blurDistance));
    losColor += xLosTexture.Sample(LosSampler, float2(input.TexCoords.x - blurDistance, input.TexCoords.y + blurDistance));	
    losColor = losColor * 0.25f;

	float obscureAmount = 1.0f - losColor.r;

	float4 outColor = float4(
		sampleColor.r * xColor.r, 
		sampleColor.g * xColor.g, 
		sampleColor.b * xColor.b,
		obscureAmount * xLosAlpha);
		
	return outColor;
}

technique LosShader
{
    pass Pass1
    {
		VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 mainPS();
    }
}