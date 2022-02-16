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

const float M_TAU = 6.28318530717958647692528676655900577;

Texture2D xTexture;
sampler TextureSampler : register (s0) = sampler_state { Texture = <xTexture>; };

Texture2D occlusionMap;
sampler occlusionSampler = sampler_state { Texture = <occlusionMap>; };

float2 center;
float bias;
float rayStepSize;

// rayCast 64 stepsll
float4 rayCast64(VertexShaderOutput input) : COLOR0
{
    // Init step as unit vector in direction of cast
    float theta = input.TexCoords.x * M_TAU;
    float2 step = float2(cos(theta), sin(theta));

    // Init coord using result from possible previous cast as starting point
    float2 coord = center + step * tex2D(TextureSampler, float2(input.TexCoords.x, 0.0f)).r;

    // Set step to appropriate size
    step *= rayStepSize;

    // Step 64 times along ray or until occluder found
    [unroll(64)]
    while (tex2D(occlusionSampler, coord).r > bias) {
        coord += step;
    }

    // store the resulting length
    return length(coord - center);
}

technique rayCast64
{
    pass Pass1
    {
        VertexShader = compile vs_4_0_level_9_1 mainVS();
        PixelShader = compile ps_4_0_level_9_3 rayCast64();
    }
}