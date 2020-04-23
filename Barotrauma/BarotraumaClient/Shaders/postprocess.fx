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

float4x4 MatrixTransform;

VertexShaderOutput mainVS(in VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput)0;

    output.Position = mul(input.Position, MatrixTransform);
    output.TexCoords = input.TexCoords;

    return output;
}

Texture2D xTexture;
sampler TextureSampler = sampler_state { Texture = <xTexture>; };

Texture2D xDistortTexture;
sampler DistortSampler = 
sampler_state 
{ 
    Texture = <xDistortTexture>;
	MagFilter = LINEAR; 
	MinFilter = LINEAR; 
	MipFilter = LINEAR; 
	AddressU = WRAP; 
	AddressV = WRAP;
};

float blurDistance;

float2 distortScale;
float2 distortUvOffset;

float3 chromaticAberrationStrength;

// Apply radial distortion to the given coordinate. 
float2 radialDistortion(float2 coord, float distortion)
{
    float2 cc = coord - 0.5;
    float dist = dot(cc, cc) * distortion;
    //return coord * (pos + cc * (1.0 + dist) * dist) / pos;
    return coord + (dist * dist + dist) * cc;
}

/*float4 sampleWithChromaticAberration(float2 samplePos)
{
    return float4(
        xTexture.Sample(TextureSampler, radialDistortion(samplePos, chromaticAberrationStrength.r)).r,
        xTexture.Sample(TextureSampler, radialDistortion(samplePos, chromaticAberrationStrength.g)).g,
        xTexture.Sample(TextureSampler, radialDistortion(samplePos, chromaticAberrationStrength.b)).b,
        1);
}*/
float3 sampleChannelsSeparately(float2 samplePosR, float2 samplePosG, float2 samplePosB)
{
    return float3(
        xTexture.Sample(TextureSampler, samplePosR).r,
        xTexture.Sample(TextureSampler, samplePosG).g,
        xTexture.Sample(TextureSampler, samplePosB).b);
}

float3 sampleWithChromaticAberration(float2 samplePos)
{
    return float3(
        xTexture.Sample(TextureSampler, radialDistortion(samplePos, chromaticAberrationStrength.r)).r,
        xTexture.Sample(TextureSampler, radialDistortion(samplePos, chromaticAberrationStrength.g)).g,
        xTexture.Sample(TextureSampler, radialDistortion(samplePos, chromaticAberrationStrength.b)).b);
}
 
float4 blur(VertexShaderOutput input) : COLOR0
{
    float4 sample;
    sample = xTexture.Sample(TextureSampler, float2(input.TexCoords.x + blurDistance, input.TexCoords.y + blurDistance));
    sample += xTexture.Sample(TextureSampler, float2(input.TexCoords.x - blurDistance, input.TexCoords.y - blurDistance));
    sample += xTexture.Sample(TextureSampler, float2(input.TexCoords.x + blurDistance, input.TexCoords.y - blurDistance));
    sample += xTexture.Sample(TextureSampler, float2(input.TexCoords.x - blurDistance, input.TexCoords.y + blurDistance));	
    sample = sample * 0.25f;
	
    return sample;
}

float4 distort(VertexShaderOutput input) : COLOR0
{
    float4 bumpColor = xDistortTexture.Sample(DistortSampler, input.TexCoords + distortUvOffset);
    bumpColor = (bumpColor + xDistortTexture.Sample(DistortSampler, input.TexCoords - distortUvOffset * 2.0f)) * 0.5f;
	
    float2 samplePos = input.TexCoords;
	
    samplePos.x += (bumpColor.r - 0.5f) * distortScale.x;
    samplePos.y += (bumpColor.g - 0.5f) * distortScale.y;
    
    return xTexture.Sample(TextureSampler, samplePos);
}

float4 blurDistort(VertexShaderOutput input) : COLOR0
{
    float4 bumpColor = xDistortTexture.Sample(DistortSampler, input.TexCoords + distortUvOffset);
    bumpColor = (bumpColor + xDistortTexture.Sample(DistortSampler, input.TexCoords - distortUvOffset * 2.0f)) * 0.5f;
	
    float2 samplePos = input.TexCoords;
	
    samplePos.x += (bumpColor.r - 0.5f) * distortScale.x;
    samplePos.y += (bumpColor.g - 0.5f) * distortScale.y;

    float4 sample;
    sample = xTexture.Sample(TextureSampler, float2(samplePos.x + blurDistance, samplePos.y + blurDistance));
    sample += xTexture.Sample(TextureSampler, float2(samplePos.x - blurDistance, samplePos.y - blurDistance));
    sample += xTexture.Sample(TextureSampler, float2(samplePos.x + blurDistance, samplePos.y - blurDistance));
    sample += xTexture.Sample(TextureSampler, float2(samplePos.x - blurDistance, samplePos.y + blurDistance));
	
    sample = sample * 0.25f;
	
    return sample;
}
 
float4 chromaticAberration(VertexShaderOutput input) : COLOR0
{
    return float4(sampleWithChromaticAberration(input.TexCoords), 1);
}

float4 chromaticAberrationDistort(VertexShaderOutput input) : COLOR0
{
    float4 bumpColor = xDistortTexture.Sample(DistortSampler, input.TexCoords + distortUvOffset);
    bumpColor = (bumpColor + xDistortTexture.Sample(DistortSampler, input.TexCoords - distortUvOffset * 2.0f)) * 0.5f;
	
    float2 samplePos = input.TexCoords;
	
    samplePos.x += (bumpColor.r - 0.5f) * distortScale.x;
    samplePos.y += (bumpColor.g - 0.5f) * distortScale.y;
    	
    return float4(sampleWithChromaticAberration(samplePos), 1);
}

float4 blurChromaticAberration(VertexShaderOutput input) : COLOR0
{
    float2 samplePosR = radialDistortion(input.TexCoords, chromaticAberrationStrength.r);
    float2 samplePosG = radialDistortion(input.TexCoords, chromaticAberrationStrength.g);
    float2 samplePosB = radialDistortion(input.TexCoords, chromaticAberrationStrength.b);

    float2 blurTopLeft = -blurDistance;
    float2 blurTopRight = float2(blurDistance, -blurDistance);
    float2 blurBottomRight = blurDistance;
    float2 blurBottomLeft = float2(-blurDistance, blurDistance);

    float3 sample;
    sample = sampleChannelsSeparately(samplePosR + blurTopLeft, samplePosG + blurTopLeft, samplePosB + blurTopLeft);
    sample += sampleChannelsSeparately(samplePosR + blurTopRight, samplePosG + blurTopRight, samplePosB + blurTopRight);
    sample += sampleChannelsSeparately(samplePosR + blurBottomRight, samplePosG + blurBottomRight, samplePosB + blurBottomRight);
    sample += sampleChannelsSeparately(samplePosR + blurBottomLeft, samplePosG + blurBottomLeft, samplePosB + blurBottomLeft);
    
    sample = sample * 0.25f;
	
    return float4(sample, 1);
}

float4 blurChromaticAberrationDistort(VertexShaderOutput input) : COLOR0
{
    float4 bumpColor = xDistortTexture.Sample(DistortSampler, input.TexCoords + distortUvOffset);
    bumpColor = (bumpColor + xDistortTexture.Sample(DistortSampler, input.TexCoords - distortUvOffset * 2.0f)) * 0.5f;
	
    float2 samplePos = input.TexCoords;
	
    samplePos.x += (bumpColor.r - 0.5f) * distortScale.x;
    samplePos.y += (bumpColor.g - 0.5f) * distortScale.y;

    float2 samplePosR = radialDistortion(samplePos, chromaticAberrationStrength.r);
    float2 samplePosG = radialDistortion(samplePos, chromaticAberrationStrength.g);
    float2 samplePosB = radialDistortion(samplePos, chromaticAberrationStrength.b);

    float2 blurTopLeft = -blurDistance;
    float2 blurTopRight = float2(blurDistance, -blurDistance);
    float2 blurBottomRight = blurDistance;
    float2 blurBottomLeft = float2(-blurDistance, blurDistance);

    float3 sample;
    sample = sampleChannelsSeparately(samplePosR + blurTopLeft, samplePosG + blurTopLeft, samplePosB + blurTopLeft);
    sample += sampleChannelsSeparately(samplePosR + blurTopRight, samplePosG + blurTopRight, samplePosB + blurTopRight);
    sample += sampleChannelsSeparately(samplePosR + blurBottomRight, samplePosG + blurBottomRight, samplePosB + blurBottomRight);
    sample += sampleChannelsSeparately(samplePosR + blurBottomLeft, samplePosG + blurBottomLeft, samplePosB + blurBottomLeft);
    
    sample = sample * 0.25f;
	
    return float4(sample, 1);
}

technique Distort
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 distort();
    }
}

technique Blur
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 blur();
    }
}

technique BlurDistort
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 blurDistort();
    }
}

technique BlurChromaticAberration
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 blurChromaticAberration();
    }
}


technique ChromaticAberration
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 chromaticAberration();
    }
}

technique ChromaticAberrationDistort
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 chromaticAberrationDistort();
    }
}

technique BlurChromaticAberrationDistort
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_1 blurChromaticAberrationDistort();
    }
}

