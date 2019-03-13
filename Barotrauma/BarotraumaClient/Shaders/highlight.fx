Texture2D xTexture;
sampler TextureSampler = sampler_state { Texture = <xTexture>; };

float outlineSize;

float rasterScale;
float4 rasterColor;

float4 tint;

float4 outline(float4 position : SV_Position, float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 sample = tex2D(TextureSampler, texCoord);

    float outlineSample = tex2D(TextureSampler, float2(texCoord.x + outlineSize, texCoord.y + outlineSize)).a;
    outlineSample += tex2D(TextureSampler, float2(texCoord.x - outlineSize, texCoord.y - outlineSize)).a;
    outlineSample += tex2D(TextureSampler, float2(texCoord.x + outlineSize, texCoord.y - outlineSize)).a;
    outlineSample += tex2D(TextureSampler, float2(texCoord.x - outlineSize, texCoord.y + outlineSize)).a;

    float4 rasterSample = (texCoord.y * rasterScale) % 1.0f < 0.5f ? rasterColor : float4(0.0f,0.0f,0.0f,0.0f);
    	
    float4 outColor = tint * (outlineSample < 3.0f ? 1.0f : 0.0f);
    outColor += rasterSample;
    outColor *= sample.a;
    return outColor;
}

technique Outline
{
    pass Pass1
    {
        PixelShader = compile ps_4_0_level_9_1 outline();
    }
}