// vim:ft=hlsl
sampler TextureSampler : register(s0);

float3 monoColor;

float4 monochrome(float4 position : SV_POSITION, float4 clr : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 texColor = tex2D(TextureSampler, texCoord);
    float gray = dot(texColor.rgb, monoColor);
    return float4(gray, gray, gray, texColor.a * clr.a);
}

technique Monochrome
{
    pass Pass1
    {
        PixelShader = compile ps_4_0_level_9_1 monochrome();
    }
}
