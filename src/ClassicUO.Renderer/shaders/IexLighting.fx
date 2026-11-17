// IEX: enhanced lighting. Techniques, all drawn through the sprite batcher
// (same vertex layout as IsometricWorld.fx):
//   Light     - one round light source: a bright core with a long, smooth tail, coloured from the
//               light colour table. Drawn additively into the light map in place of the stock
//               round light sprites (whose 32 steps and short reach are what it replaces).
//   Blur      - one direction of a soft blur (light map and bloom, run in pairs)
//   Bright    - the brightest parts of the lit picture, at quarter size, for the bloom
//   Composite - the world picture lit by the light map, in place of the stock multiply blend,
//               with haze in the air around lights and bloom on top
// The light map holds light at half strength (LightScale), so light can go up to twice the
// plain art's brightness: lit stone near a flame can be brighter than by day.
// The world sprites are only ever multiplied by light here, never filtered or repainted.
// Compile with compile_shaders.bat (fxc, fx_2_0 container, shader model 3 like the world shader).

float4x4 MatrixTransform;
float2 Viewport;

// Light map units: stored = real light * LightScale.
static const float LightScale = 0.5f;

// Light: how fast the tail falls (higher = brighter core, thinner tail).
float Falloff;

// Blur: one texel step along the blur direction, in texture coordinates.
float2 BlurStep;

// Bright
float2 WorldTexel;     // one world picture pixel, in texture coordinates
float BloomThreshold;  // lit brightness where bloom starts

// Composite
float3 Ambient;        // the darkness colour (real units) the light map was cleared to
float Soften;          // 0..1, how much of the blurred light map replaces the sharp one
float GlowStrength;    // how far bright cores bleed and lift the picture
float HazeStrength;    // light seen in the air around light sources, over dark ground
float BloomStrength;   // how much of the bloom is added back
float2 LightMapSize;   // light map size in pixels, for the dither pattern
float3 GradeTint;      // colour balance of the finished picture (white = untouched)
float Saturation;      // 1 = untouched, lower drains colour (night, storms)
float Vignette;        // 0 = none, darkening of the screen edges (underground, deep night)

sampler DrawSampler : register(s0);
// s1/s2 hold the hue tables (s2: the light colour table) and s3 is reset to point sampling by
// the batcher: use s4 and up.
sampler LightTableSampler : register(s2);
sampler LightSampler : register(s4);
sampler SoftSampler : register(s5);
sampler BloomSampler : register(s6);

struct VS_INPUT
{
	float4 Position : POSITION0;
	float3 Normal	: NORMAL0;
	float3 TexCoord : TEXCOORD0;
	float3 Hue		: TEXCOORD1;
};

struct PS_INPUT
{
	float4 Position : POSITION0;
	float3 TexCoord : TEXCOORD0;
};

struct LIGHT_INPUT
{
	float4 Position : POSITION0;
	float3 TexCoord : TEXCOORD0;
	float3 Hue		: TEXCOORD1;
	float2 Screen	: TEXCOORD2;
};

PS_INPUT VertexShaderFunction(VS_INPUT IN)
{
	PS_INPUT OUT;

	OUT.Position = mul(IN.Position, MatrixTransform);

	// same half pixel nudge as IsometricWorld.fx, so the picture lands on the same pixels
	OUT.Position.x -= 0.5 / Viewport.x;
	OUT.Position.y += 0.5 / Viewport.y;

	OUT.TexCoord = IN.TexCoord;

	return OUT;
}

LIGHT_INPUT VertexShader_Light(VS_INPUT IN)
{
	LIGHT_INPUT OUT;

	OUT.Position = mul(IN.Position, MatrixTransform);
	OUT.Position.x -= 0.5 / Viewport.x;
	OUT.Position.y += 0.5 / Viewport.y;

	OUT.TexCoord = IN.TexCoord;
	OUT.Hue = IN.Hue;
	OUT.Screen = (OUT.Position.xy * float2(0.5f, -0.5f) + 0.5f) * Viewport;

	return OUT;
}

// a fixed pattern of noise, one value per pixel (interleaved gradient noise)
float Noise(float2 pixel)
{
	float2 p = floor(pixel);

	return frac(52.9829189f * frac(dot(p, float2(0.06711056f, 0.00583715f))));
}

// Hue.x: light colour row + 1 (1 or less = white), Hue.z: strength in real units
float4 PixelShader_Light(LIGHT_INPUT IN) : COLOR0
{
	float2 q = IN.TexCoord.xy * 2.0f - 1.0f;
	float d2 = dot(q, q);

	// inverse square, lowered so it reaches exactly 0 at the rim, and normalised to 1 at the centre
	float edge = 1.0f / (1.0f + Falloff);
	float f = saturate((1.0f / (1.0f + Falloff * d2) - edge) / (1.0f - edge));

	float3 color = float3(1.0f, 1.0f, 1.0f);

	if (IN.Hue.x > 1.5f)
	{
		// the brightest step of this light colour (same lookup as the world shader's LIGHTS mode)
		color = tex2D(LightTableSampler, float2(0.97f, (IN.Hue.x - 1.5f) / 63.0f)).rgb;
	}

	float3 c = color * f * IN.Hue.z * LightScale;

	// half a step of noise: the long tail would otherwise show rings in an 8 bit light map
	c += (Noise(IN.Screen) - 0.5f) / 255.0f * step(0.0001f, f);

	return float4(max(c, 0.0f), 1.0f);
}

// 9 tap gaussian done as 5 linear samples
float4 PixelShader_Blur(PS_INPUT IN) : COLOR0
{
	float2 uv = IN.TexCoord.xy;

	float3 c = tex2D(DrawSampler, uv).rgb * 0.2270270270f;
	c += tex2D(DrawSampler, uv + BlurStep * 1.3846153846f).rgb * 0.3162162162f;
	c += tex2D(DrawSampler, uv - BlurStep * 1.3846153846f).rgb * 0.3162162162f;
	c += tex2D(DrawSampler, uv + BlurStep * 3.2307692308f).rgb * 0.0702702703f;
	c += tex2D(DrawSampler, uv - BlurStep * 3.2307692308f).rgb * 0.0702702703f;

	return float4(c, 1.0f);
}

// Light past the knee is eased toward a ceiling, so overlapping lights and hot cores
// do not flatten to one colour.
float3 ToneMap(float3 l)
{
	const float knee = 0.9f;
	const float range = 0.75f; // ceiling = knee + range

	float3 over = max(l - knee, 0.0f);

	return min(l, knee) + range * (1.0f - exp(-over / range));
}

float3 LightAt(float2 uv)
{
	return ToneMap(tex2D(LightSampler, uv).rgb / LightScale);
}

// only what rises over the threshold, keeping its colour
float3 BrightTap(float2 uv)
{
	float3 lit = tex2D(DrawSampler, uv).rgb * LightAt(uv);
	float luma = dot(lit, float3(0.2126f, 0.7152f, 0.0722f));

	return lit * (max(luma - BloomThreshold, 0.0f) / max(luma, 0.001f));
}

float4 PixelShader_Bright(PS_INPUT IN) : COLOR0
{
	float2 uv = IN.TexCoord.xy;

	// four linear taps cover the 4x4 world pixels behind each quarter size pixel
	float3 c = BrightTap(uv + float2(-1.0f, -1.0f) * WorldTexel);
	c += BrightTap(uv + float2(1.0f, -1.0f) * WorldTexel);
	c += BrightTap(uv + float2(-1.0f, 1.0f) * WorldTexel);
	c += BrightTap(uv + float2(1.0f, 1.0f) * WorldTexel);

	return float4(c * 0.25f, 1.0f);
}

float4 PixelShader_Composite(PS_INPUT IN) : COLOR0
{
	float2 uv = IN.TexCoord.xy;

	float4 world = tex2D(DrawSampler, uv);
	float3 sharp = tex2D(LightSampler, uv).rgb / LightScale;
	float3 soft = tex2D(SoftSampler, uv).rgb / LightScale;

	// the stock light sprites have 32 brightness steps: mixing in the blurred map hides the rings
	float3 light = lerp(sharp, soft, Soften);

	// what light sources add over the darkness
	float3 added = max(soft - Ambient, 0.0f);

	// glow: near bright cores the light swells a little
	float core = smoothstep(0.6f, 1.6f, max(soft.r, max(soft.g, soft.b)));
	light += added * core * GlowStrength;

	light = ToneMap(light);

	// a pixel of noise under one colour step breaks up what banding is left in dark gradients
	light += (Noise(uv * LightMapSize) - 0.5f) / 255.0f;

	float3 lit = world.rgb * max(light, 0.0f);

	// haze: light in the air, seen even over black ground and gaps (screen blend, never clips)
	float3 haze = saturate(ToneMap(added) * HazeStrength);
	lit = 1.0f - (1.0f - lit) * (1.0f - haze);

	// bloom: the brightest parts bleed into what is around them
	lit += tex2D(BloomSampler, uv).rgb * BloomStrength;

	// grade: the whole picture, lit areas included, leans with the hour and the weather
	float luma = dot(lit, float3(0.2126f, 0.7152f, 0.0722f));
	lit = lerp(float3(luma, luma, luma), lit, Saturation) * GradeTint;

	// vignette: the corners fall away
	float2 q = uv * 2.0f - 1.0f;
	lit *= 1.0f - Vignette * smoothstep(0.55f, 1.35f, length(q));

	return float4(lit, world.a);
}

technique Light
{
	pass p0
	{
		VertexShader = compile vs_3_0 VertexShader_Light();
		PixelShader = compile ps_3_0 PixelShader_Light();
	}
}

technique Blur
{
	pass p0
	{
		VertexShader = compile vs_3_0 VertexShaderFunction();
		PixelShader = compile ps_3_0 PixelShader_Blur();
	}
}

technique Bright
{
	pass p0
	{
		VertexShader = compile vs_3_0 VertexShaderFunction();
		PixelShader = compile ps_3_0 PixelShader_Bright();
	}
}

technique Composite
{
	pass p0
	{
		VertexShader = compile vs_3_0 VertexShaderFunction();
		PixelShader = compile ps_3_0 PixelShader_Composite();
	}
}
