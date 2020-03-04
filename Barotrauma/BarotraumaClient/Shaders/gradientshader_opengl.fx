sampler TextureSampler : register(s0);

float4 color1;
float4 color2;
float midPoint;
float fadeDist;

float4 PixelShaderF(float4 position : SV_Position, float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 t = tex2D(TextureSampler, texCoord);
    
    float a = saturate(((texCoord.y - midPoint) / fadeDist) + 0.5);

    return t * (color1 * a + color2 * (1.0-a));
}


technique Gradient
{
    pass Pass1
    {
        PixelShader = compile ps_2_0 PixelShaderF();
    }
}
