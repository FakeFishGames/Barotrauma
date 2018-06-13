Texture2D xTexture;
sampler TextureSampler = sampler_state { Texture = <xTexture>; };

float4x4 xTransform;

struct VertexShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TexCoords: TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TexCoords: TEXCOORD0;
}; 

VertexShaderOutput mainVS(in VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput)0;

    output.Position = mul(input.Position, xTransform);
    output.Color = input.Color;
    output.TexCoords = input.TexCoords;

    return output;
}

float4 mainPS(VertexShaderOutput input) : COLOR
{
    return xTexture.Sample(TextureSampler, input.TexCoords) * input.Color;
}

technique DeformShader
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 mainPS();
    }
}