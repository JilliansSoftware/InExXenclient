// IEX: enhanced lighting. Lays the light map over the world picture with a shader
// (shaders/IexLighting.fx) instead of the stock multiply blend: softened light edges, a glow
// around bright cores, haze in the air around lights and bloom on the brightest parts. Round
// light sources are drawn into the light map by the shader too (BeginLights / DrawLight), with a
// long smooth tail. The light map holds light at half strength (LightScale) so it can go past 1. The game scene asks for it each frame (Requested); RenderTargets
// draws it. If the shader cannot be loaded on this machine the stock lighting is used.

using System;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Renderer.IEX
{
    public sealed class IexLightingPass
    {
        private IexLightingEffect _effect;
        private bool _failed;
        private RenderTarget2D _blurA, _blurB;
        private RenderTarget2D _bloomA, _bloomB;
        private Texture2D _disc;

        /// <summary>The light map holds real light times this, so light can reach 1 / LightScale.</summary>
        public const float LightScale = 0.5f;

        /// <summary>Set by the game scene for the frame being drawn; cleared once drawn.</summary>
        public bool Requested;

        /// <summary>The colour the light map was cleared to.</summary>
        public Vector3 Ambient;

        public float Soften = 0.5f;
        public float GlowStrength = 0.2f;

        /// <summary>Light seen in the air around light sources; scaled by how dark it is.</summary>
        public float HazeStrength;

        /// <summary>How much bloom is added back (0 = no bloom pass at all), and where it starts.</summary>
        public float BloomStrength;
        public float BloomThreshold = 0.8f;

        /// <summary>How fast a round light's tail falls (higher = brighter core, thinner tail).</summary>
        public float Falloff = 12f;

        /// <summary>Colour balance, colour strength and edge darkening of the finished picture.</summary>
        public Vector3 GradeTint = Vector3.One;
        public float Saturation = 1f;
        public float Vignette;

        public bool Available => !_failed;

        private bool _bloomReady;

        /// <summary>Blurs the light map into a half size copy. Call with no render target set and outside a batch.</summary>
        public bool Prepare(UltimaBatcher2D batcher, RenderTarget2D world, RenderTarget2D light)
        {
            if (light == null || light.IsDisposed || !EnsureEffect(batcher.GraphicsDevice))
            {
                return false;
            }

            GraphicsDevice device = batcher.GraphicsDevice;

            int w = Math.Max(1, light.Width / 2);
            int h = Math.Max(1, light.Height / 2);
            EnsureSize(device, ref _blurA, w, h);
            EnsureSize(device, ref _blurB, w, h);

            _effect.CurrentTechnique = _effect.Blur;

            // two rounds, the second twice as wide: about 24 screen pixels of spread
            BlurInto(batcher, light, _blurA, new Vector2(1f / w, 0f));
            BlurInto(batcher, _blurA, _blurB, new Vector2(0f, 1f / h));
            BlurInto(batcher, _blurB, _blurA, new Vector2(2f / w, 0f));
            BlurInto(batcher, _blurA, _blurB, new Vector2(0f, 2f / h));

            _bloomReady = false;

            if (BloomStrength > 0f && world != null && !world.IsDisposed)
            {
                // bloom: the brightest parts of the lit picture at quarter size, blurred wide
                int bw = Math.Max(1, light.Width / 4);
                int bh = Math.Max(1, light.Height / 4);
                EnsureSize(device, ref _bloomA, bw, bh);
                EnsureSize(device, ref _bloomB, bw, bh);

                _effect.CurrentTechnique = _effect.Bright;
                _effect.WorldTexel.SetValue(new Vector2(1f / world.Width, 1f / world.Height));
                _effect.BloomThreshold.SetValue(BloomThreshold);
                device.Textures[4] = light;
                device.SamplerStates[4] = SamplerState.LinearClamp;
                BlurInto(batcher, world, _bloomA, Vector2.Zero);
                device.Textures[4] = null;

                _effect.CurrentTechnique = _effect.Blur;
                BlurInto(batcher, _bloomA, _bloomB, new Vector2(1f / bw, 0f));
                BlurInto(batcher, _bloomB, _bloomA, new Vector2(0f, 1f / bh));
                BlurInto(batcher, _bloomA, _bloomB, new Vector2(2f / bw, 0f));
                BlurInto(batcher, _bloomB, _bloomA, new Vector2(0f, 2f / bh));
                _bloomReady = true;
            }

            device.SetRenderTarget(null);

            return true;
        }

        /// <summary>Draws the lit world. Call after Prepare, outside a batch.</summary>
        public void Draw(UltimaBatcher2D batcher, RenderTarget2D world, RenderTarget2D light, Rectangle destination, SamplerState worldSampler)
        {
            GraphicsDevice device = batcher.GraphicsDevice;

            _effect.CurrentTechnique = _effect.Composite;
            SetProjection(device);
            _effect.Ambient.SetValue(Ambient);
            _effect.Soften.SetValue(Soften);
            _effect.GlowStrength.SetValue(GlowStrength);
            _effect.HazeStrength.SetValue(HazeStrength);
            _effect.BloomStrength.SetValue(_bloomReady ? BloomStrength : 0f);
            _effect.LightMapSize.SetValue(new Vector2(light.Width, light.Height));
            _effect.GradeTint.SetValue(GradeTint);
            _effect.Saturation.SetValue(Saturation);
            _effect.Vignette.SetValue(Vignette);

            device.Textures[4] = light;
            device.Textures[5] = _blurB;
            device.Textures[6] = _bloomReady ? _bloomA : _blurB; // unused when there is no bloom, but bound
            device.SamplerStates[4] = SamplerState.LinearClamp;
            device.SamplerStates[5] = SamplerState.LinearClamp;
            device.SamplerStates[6] = SamplerState.LinearClamp;

            batcher.Begin(_effect);
            batcher.SetSampler(worldSampler);
            batcher.SetBlendState(null);
            batcher.Draw(world, destination, Vector3.UnitZ, 0f);
            batcher.End();

            // the light map is drawn into again next frame: it must not stay bound as a texture
            device.Textures[4] = null;
            device.Textures[5] = null;
            device.Textures[6] = null;
        }

        /// <summary>
        /// Starts drawing round lights into the light map (its render target already set).
        /// <paramref name="transform"/> is the world's zoom matrix. False if the shader is not
        /// available: draw the stock sprites instead.
        /// </summary>
        public bool BeginLights(UltimaBatcher2D batcher, Matrix transform)
        {
            if (!EnsureEffect(batcher.GraphicsDevice))
            {
                return false;
            }

            if (_disc == null || _disc.IsDisposed)
            {
                _disc = new Texture2D(batcher.GraphicsDevice, 4, 4);
                Color[] white = new Color[16];
                Array.Fill(white, Color.White);
                _disc.SetData(white);
            }

            _effect.CurrentTechnique = _effect.Light;
            _effect.Falloff.SetValue(Falloff);
            SetProjection(batcher.GraphicsDevice, transform);

            batcher.Begin(_effect);
            batcher.SetBlendState(BlendState.Additive);

            return true;
        }

        /// <summary>
        /// One round light, between BeginLights and EndLights: centre and radius in world pixels
        /// (before zoom), row of the light colour table plus 1 (as the stock light hue; 0 = white)
        /// and strength in real light (1 = as bright as the stock sprite's centre).
        /// </summary>
        public void DrawLight(UltimaBatcher2D batcher, Vector2 centre, float radius, ushort colorRow, float strength)
        {
            batcher.Draw(
                _disc,
                centre,
                null,
                new Vector3(colorRow, 0f, strength),
                0f,
                new Vector2(_disc.Width * 0.5f, _disc.Height * 0.5f),
                radius * 2f / _disc.Width,
                SpriteEffects.None,
                0f
            );
        }

        public void EndLights(UltimaBatcher2D batcher)
        {
            batcher.SetBlendState(null);
            batcher.End();
        }

        private bool EnsureEffect(GraphicsDevice device)
        {
            if (_failed)
            {
                return false;
            }

            try
            {
                _effect ??= new IexLightingEffect(device);

                return true;
            }
            catch (Exception e)
            {
                _failed = true;
                Log.Warn("IEX: enhanced lighting is off, the shader did not load: " + e.Message);

                return false;
            }
        }

        private void BlurInto(UltimaBatcher2D batcher, Texture2D source, RenderTarget2D target, Vector2 step)
        {
            GraphicsDevice device = batcher.GraphicsDevice;

            device.SetRenderTarget(target);
            SetProjection(device);
            _effect.BlurStep.SetValue(step);

            batcher.Begin(_effect);
            batcher.SetSampler(SamplerState.LinearClamp);
            batcher.SetBlendState(BlendState.Opaque);
            batcher.Draw(source, new Rectangle(0, 0, target.Width, target.Height), Vector3.UnitZ, 0f);
            batcher.SetBlendState(null);
            batcher.SetSampler(null);
            batcher.End();
        }

        // The batcher only feeds its own shader: ours needs the same screen projection.
        private void SetProjection(GraphicsDevice device) => SetProjection(device, Matrix.Identity);

        private void SetProjection(GraphicsDevice device, Matrix transform)
        {
            Viewport viewport = device.Viewport;

            Matrix.CreateOrthographicOffCenter(
                0f,
                viewport.Width,
                viewport.Height,
                0f,
                short.MinValue,
                short.MaxValue,
                out Matrix matrix
            );

            _effect.MatrixTransform.SetValue(transform * matrix);
            _effect.Viewport.SetValue(new Vector2(viewport.Width, viewport.Height));
        }

        private static void EnsureSize(GraphicsDevice device, ref RenderTarget2D target, int width, int height)
        {
            if (target == null || target.IsDisposed || target.Width != width || target.Height != height)
            {
                target?.Dispose();
                target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
            }
        }
    }

    internal sealed class IexLightingEffect : Effect
    {
        public IexLightingEffect(GraphicsDevice device) : base(device, IexShaders.GetLightingShader().ToArray())
        {
            MatrixTransform = Parameters["MatrixTransform"];
            Viewport = Parameters["Viewport"];
            BlurStep = Parameters["BlurStep"];
            Ambient = Parameters["Ambient"];
            Soften = Parameters["Soften"];
            GlowStrength = Parameters["GlowStrength"];
            HazeStrength = Parameters["HazeStrength"];
            BloomStrength = Parameters["BloomStrength"];
            BloomThreshold = Parameters["BloomThreshold"];
            WorldTexel = Parameters["WorldTexel"];
            Falloff = Parameters["Falloff"];
            LightMapSize = Parameters["LightMapSize"];
            GradeTint = Parameters["GradeTint"];
            Saturation = Parameters["Saturation"];
            Vignette = Parameters["Vignette"];

            Light = Techniques["Light"];
            Blur = Techniques["Blur"];
            Bright = Techniques["Bright"];
            Composite = Techniques["Composite"];
        }

        public EffectParameter MatrixTransform { get; }
        public EffectParameter Viewport { get; }
        public EffectParameter BlurStep { get; }
        public EffectParameter Ambient { get; }
        public EffectParameter Soften { get; }
        public EffectParameter GlowStrength { get; }
        public EffectParameter HazeStrength { get; }
        public EffectParameter BloomStrength { get; }
        public EffectParameter BloomThreshold { get; }
        public EffectParameter WorldTexel { get; }
        public EffectParameter Falloff { get; }
        public EffectTechnique Light { get; }
        public EffectTechnique Bright { get; }
        public EffectParameter LightMapSize { get; }
        public EffectParameter GradeTint { get; }
        public EffectParameter Saturation { get; }
        public EffectParameter Vignette { get; }
        public EffectTechnique Blur { get; }
        public EffectTechnique Composite { get; }
    }

    internal static partial class IexShaders
    {
        [FileEmbed.FileEmbed("shaders/IexLighting.fxc")]
        public static partial ReadOnlySpan<byte> GetLightingShader();
    }
}
