Texture2D xTexture;
sampler TextureSampler = sampler_state { Texture = <xTexture>; };

float4x4 xTransform;

const int MaxDeformResolution = 15 * 15;

float2 deformArray[15 * 15];

int deformArrayWidth;
int deformArrayHeight;

float2 uvTopLeft;
float2 uvBottomRight;

float4 tintColor;
float4 solidColor;

struct VertexShaderInput
{
	float4 Position : POSITION0;
	float4 Color : COLOR0;
	float2 TexCoords: TEXCOORD0;
};

struct VertexShaderOutput
{
	float4 Position : POSITION0;
	float4 Color : COLOR0;
	float2 TexCoords: TEXCOORD0;
}; 

VertexShaderOutput mainVS(in VertexShaderInput input)
{
	VertexShaderOutput output = (VertexShaderOutput)0;

	float2 normalizedUv = (input.TexCoords - uvTopLeft) / (uvBottomRight - uvTopLeft);

	int2 deformIndexTopLeft =
	{ 
        min(floor(normalizedUv.x * (deformArrayWidth - 1)), deformArrayWidth - 1),
		min(floor(normalizedUv.y * (deformArrayHeight - 1)), deformArrayHeight - 1)
    };
	int2 deformIndexBottomRight =
	{
		min(deformIndexTopLeft.x + 1, deformArrayWidth - 1),
		min(deformIndexTopLeft.y + 1, deformArrayHeight - 1)
	};

    float2 deformTopLeft = deformArray[deformIndexTopLeft.x + deformIndexTopLeft.y * deformArrayWidth];
    float2 deformTopRight = deformArray[deformIndexBottomRight.x + deformIndexTopLeft.y * deformArrayWidth];
    float2 deformBottomLeft = deformArray[deformIndexTopLeft.x + deformIndexBottomRight.y * deformArrayWidth];
    float2 deformBottomRight = deformArray[deformIndexBottomRight.x + deformIndexBottomRight.y * deformArrayWidth];

    float divX = 1.0 / (deformArrayWidth - 1);
    float divY = 1.0 / (deformArrayHeight - 1);

	float2 vertexOffset = 
	{
        lerp(
			lerp(deformTopLeft, deformTopRight, (normalizedUv.x % divX) / divX),
			lerp(deformBottomLeft, deformBottomRight, (normalizedUv.x % divX) / divX),
			(normalizedUv.y % divY) / divY)
    };

    output.Position = mul(input.Position + float4(vertexOffset, 0, 0), xTransform);
	output.Color = input.Color * tintColor;
	output.TexCoords = input.TexCoords;

	return output;
}

float4 mainPS(VertexShaderOutput input) : COLOR
{
	return xTexture.Sample(TextureSampler, input.TexCoords) * input.Color;
}

float4 solidVertexColorPS(VertexShaderOutput input) : COLOR
{
	return input.Color * xTexture.Sample(TextureSampler, input.TexCoords).a;
}

float4 solidColorPS(VertexShaderOutput input) : COLOR
{
	return solidColor * xTexture.Sample(TextureSampler, input.TexCoords).a;
}

technique DeformShader
{
	pass Pass1
	{
		VertexShader = compile vs_4_0_level_9_1 mainVS();
		PixelShader = compile ps_4_0_level_9_1 mainPS();
	}
}

technique DeformShaderSolidColor
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 solidColorPS();
    }
}

technique DeformShaderSolidVertexColor
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 solidVertexColorPS();
    }
}