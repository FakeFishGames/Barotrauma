// vim:ft=hlsl
float seed;
float intensity;
float4 grainColor;

float nrand(float2 uv)
{
    return frac(sin(dot(uv, float2(12.9898, 78.233) * seed)) * 43758.5453);
}

float4 grain(float4 position : SV_POSITION, float4 clr : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
	float4 baseColor = grainColor;
	float4 color = baseColor * nrand(texCoord);
	float2 center = { 0.5, 0.5 };
	float2 diff = texCoord - center;
	float alpha = diff.x * diff.x + diff.y * diff.y;
	color.a = alpha * intensity;
	return color;
}

technique Grain
{
    pass Pass1
    {
        PixelShader = compile ps_4_0_level_9_1 grain();
    }
}
