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

Texture2D raycastMap;
sampler raycastSampler : register (s0) = sampler_state { Texture = <raycastMap>; };

Texture2D occlusionMap;
sampler occlusionSampler = sampler_state { Texture = <occlusionMap>; };

// raycast params
float2 center;
float bias;
float rayStepSize;
float iaspect;
float rayLength;

// rayblur params
float rayBlurDistWeight;

// rayCast 64 steps
float4 losRaycast64(VertexShaderOutput input) : COLOR0
{
	// Init step as unit vector in direction of cast
	float theta = input.TexCoords.x * M_TAU;
	float2 step = float2(cos(theta) * iaspect, sin(theta));

	// Init coord using result from possible previous cast as starting point
	float2 coord = center + step * tex2D(raycastSampler, float2(input.TexCoords.x, 0.0f)).r * rayLength;

	// Set step to appropriate size
	step *= rayStepSize;

	// Step 64 times along ray or until occluder found
	[unroll(64)]
	while (tex2D(occlusionSampler, coord).r > bias) {
		coord += step;
	}

	// store the resulting length
	float2 dist = coord - center; // UV space
	dist.x /= iaspect; // world space
	return length(dist) / rayLength;
}

// blur the raycast with falloff based on angular offset and distance difference between ray hits
// 8 samples in each direction
float4 losBlurRaycast8(VertexShaderOutput input) : COLOR0
{
	// Get main ray distance from ray texture
	float dist = tex2D(raycastSampler, float2(input.TexCoords.x, 0.0f)).r;

	float color = dist;
	float weight = 1.;
	float stepWeight = 1. / 8.;

	// The distance between the samples is increased based on the range
	// Technically this should be ATAN based but this is faster
	// Based on a curve fit with a few values that worked at different distances
	float blurAngle = stepWeight * .0069 / (dist + 0.1337);

	for (int i = 1; i <= 8; i++)
	{
		// Falloff with angle from the ray
		float falloffWeight = 1. - float(i) * stepWeight;

		// Falloff with distance from the ray
		float sampleDist = tex2D(raycastSampler, float2(input.TexCoords.x + float(i) * blurAngle, 0.0f)).r;
		float sampleWeight = falloffWeight * max(0., 1. - rayBlurDistWeight * abs(dist - sampleDist));
		weight += sampleWeight;
		color += sampleWeight * sampleDist;

		// Second sample in other direction        
		sampleDist = tex2D(raycastSampler, float2(input.TexCoords.x - float(i) * blurAngle, 0.0f)).r;
		sampleWeight = falloffWeight * max(0., 1. - rayBlurDistWeight * abs(dist - sampleDist));
		weight += sampleWeight;
		color += sampleWeight * sampleDist;
	}

	color /= weight;

	return float4(color.rrr, 1.);
}

// 4 samples in each direction
float4 losBlurRaycast4(VertexShaderOutput input) : COLOR0
{
	// Get main ray distance from ray texture
	float dist = tex2D(raycastSampler, float2(input.TexCoords.x, 0.0f)).r;

	float color = dist;
	float weight = 1.;
	float stepWeight = 1. / 4.;

	// The distance between the samples is increased based on the range
	// Technically this should be ATAN based but this is faster
	// Based on a curve fit with a few values that worked at different distances
	float blurAngle = stepWeight * .0069 / (dist + 0.1337);

	for (int i = 1; i <= 4; i++)
	{
		// Falloff with angle from the ray
		float falloffWeight = 1. - float(i) * stepWeight;

		// Falloff with distance from the ray
		float sampleDist = tex2D(raycastSampler, float2(input.TexCoords.x + float(i) * blurAngle, 0.0f)).r;
		float sampleWeight = falloffWeight * max(0., 1. - rayBlurDistWeight * abs(dist - sampleDist));
		weight += sampleWeight;
		color += sampleWeight * sampleDist;

		// Second sample in other direction        
		sampleDist = tex2D(raycastSampler, float2(input.TexCoords.x + float(i) * blurAngle, 0.0f)).r;
		sampleWeight = falloffWeight * max(0., 1. - rayBlurDistWeight * abs(dist - sampleDist));
		weight += sampleWeight;
		color += sampleWeight * sampleDist;
	}

	color /= weight;

	return float4(color.rrr, 1.);
}

technique losRaycast64
{
	pass Pass1
	{
		VertexShader = compile vs_4_0_level_9_1 mainVS();
		PixelShader = compile ps_4_0_level_9_3 losRaycast64();
	}
}

technique losBlurRaycast8
{
	pass Pass1
	{
		VertexShader = compile vs_4_0_level_9_1 mainVS();
		PixelShader = compile ps_4_0_level_9_3 losBlurRaycast8();
	}
}

technique losBlurRaycast4
{
	pass Pass1
	{
		VertexShader = compile vs_4_0_level_9_1 mainVS();
		PixelShader = compile ps_4_0_level_9_3 losBlurRaycast4();
	}
}