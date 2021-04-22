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

Texture xTexture;
sampler TextureSampler : register (s0) = sampler_state { Texture = <xTexture>; };

Texture xLosTexture;
sampler LosSampler = sampler_state { Texture = <xLosTexture>; };

float4 xColor;

float4 mainPS(VertexShaderOutput input) : COLOR0
{
	float4 sampleColor = tex2D(TextureSampler, input.TexCoords);
	float4 losColor = tex2D(LosSampler, input.TexCoords);

	float obscureAmount = 1.0f - losColor.r;

	float4 outColor = float4(
		sampleColor.r * xColor.r,
		sampleColor.g * xColor.g,
		sampleColor.b * xColor.b,
		obscureAmount);

	return outColor;
}

technique LosShader
{
    pass Pass1
    {
		VertexShader = compile vs_2_0 mainVS();
        PixelShader = compile ps_2_0 mainPS();
    }
}
