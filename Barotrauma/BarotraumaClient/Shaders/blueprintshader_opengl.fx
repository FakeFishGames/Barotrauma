// vim:ft=hlsl
sampler TextureSampler : register(s0);

float width;
float height;

float3 sobel(float2 uv)
{
	float x = 0;
	float y = 0;

	float w = 1.0 / width;
	float h = 1.0 / height;

	x += tex2D(TextureSampler, uv + float2(-w, -h)) * -1.0;
	x += tex2D(TextureSampler, uv + float2(-w,  0)) * -2.0;
	x += tex2D(TextureSampler, uv + float2(-w,  h)) * -1.0;

	x += tex2D(TextureSampler, uv + float2( w, -h)) *  1.0;
	x += tex2D(TextureSampler, uv + float2( w,  0)) *  2.0;
	x += tex2D(TextureSampler, uv + float2( w,  h)) *  1.0;

	y += tex2D(TextureSampler, uv + float2(-w, -h)) * -1.0;
	y += tex2D(TextureSampler, uv + float2( 0, -h)) * -2.0;
	y += tex2D(TextureSampler, uv + float2( w, -h)) * -1.0;

	y += tex2D(TextureSampler, uv + float2(-w,  h)) *  1.0;
	y += tex2D(TextureSampler, uv + float2( 0,  h)) *  2.0;
	y += tex2D(TextureSampler, uv + float2( w,  h)) *  1.0;

	return sqrt(x * x + y * y);
}

float4 blueprint(float4 position : SV_POSITION, float4 clr : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
	float3 s = sobel(texCoord);
	float a = tex2D(TextureSampler, texCoord).a;
	a *= clr.a;
	return float4(clr.r + s.r, clr.g + s.g, clr.b + s.b, a);
}

technique Blueprint
{
    pass Pass1
    {
        PixelShader = compile ps_3_0 blueprint();
    }
}
