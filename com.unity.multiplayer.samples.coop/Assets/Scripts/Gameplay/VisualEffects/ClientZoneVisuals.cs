using System.Collections.Generic;
using Mirror;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameState;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.VisualEffects
{
    /// <summary>
    /// Draws the effect zones the server publishes, as discs on the ground.
    /// </summary>
    /// <remarks>
    /// <para>Client-side and read-only: it renders <see cref="NetworkGameState.Zones"/> and never
    /// writes to it. Whether standing somewhere heals or hurts is decided entirely by the server —
    /// this only has to make the same circle visible, and being wrong about it costs a picture
    /// rather than hit points.</para>
    ///
    /// <para>Meshes and materials are built in code, like the rest of this project's runtime
    /// visuals. A zone is a flat translucent disc with a brighter rim: the fill says where the
    /// effect reaches and the rim survives being seen at a shallow camera angle, where a fill alone
    /// flattens into a smear.</para>
    ///
    /// <para>Self-bootstrapping, so it needs no scene edit. It attaches itself once the match state
    /// exists and gives up quietly on a headless server, which draws nothing.</para>
    /// </remarks>
    public class ClientZoneVisuals : MonoBehaviour
    {
        const int k_Segments = 48;

        /// <summary>Fill opacity. Low: the player has to be able to see the fight through it.</summary>
        const float k_FillAlpha = 0.16f;

        const float k_RimAlpha = 0.75f;

        /// <summary>Rim thickness as a fraction of the radius.</summary>
        const float k_RimWidth = 0.06f;

        /// <summary>Seconds of fade before a zone expires, so it does not simply blink out.</summary>
        const float k_FadeSeconds = 2.5f;

        static ClientZoneVisuals s_Instance;

        NetworkGameState m_GameState;
        Mesh m_Disc;
        Mesh m_Rim;
        readonly Dictionary<int, GameObject> m_Live = new Dictionary<int, GameObject>();
        readonly List<int> m_Stale = new List<int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // A dedicated server runs this same build headless and draws nothing.
            if (Application.isBatchMode || s_Instance != null)
            {
                return;
            }

            var host = new GameObject(nameof(ClientZoneVisuals));
            DontDestroyOnLoad(host);
            s_Instance = host.AddComponent<ClientZoneVisuals>();
        }

        void Awake()
        {
            m_Disc = BuildDisc(1f, k_Segments);
            m_Rim = BuildRing(1f, k_RimWidth, k_Segments);
        }

        void Update()
        {
            if (m_GameState == null)
            {
                // Cheap enough to look for once a frame: this object outlives every scene, and the
                // state it wants only exists while a match is running.
                m_GameState = FindAnyObjectByType<NetworkGameState>();
                if (m_GameState == null)
                {
                    ClearAll();
                    return;
                }
            }

            Sync();
        }

        void Sync()
        {
            m_Stale.Clear();
            foreach (var id in m_Live.Keys)
            {
                m_Stale.Add(id);
            }

            foreach (var zone in m_GameState.Zones)
            {
                m_Stale.Remove(zone.Id);

                if (!m_Live.TryGetValue(zone.Id, out var visual) || visual == null)
                {
                    visual = BuildVisual(zone);
                    m_Live[zone.Id] = visual;
                    Debug.Log($"[Zones] Drawing {zone.Kind} zone {zone.Id} at {zone.Position}.");
                }

                Animate(visual, zone);
            }

            foreach (var id in m_Stale)
            {
                if (m_Live.TryGetValue(id, out var dead) && dead != null)
                {
                    Destroy(dead);
                }

                m_Live.Remove(id);
            }
        }

        GameObject BuildVisual(ZoneState zone)
        {
            Color colour = ZoneRules.ColorFor(zone.Kind);

            // The root stays world-aligned and only carries the radius as scale, so the particles
            // can use plain world axes. Only the flat meshes are tipped onto the ground.
            var root = new GameObject($"Zone_{zone.Kind}_{zone.Id}");
            root.transform.position = zone.Position;
            root.transform.localScale = Vector3.one * zone.Radius;

            AddLayer(root.transform, m_Disc, new Color(colour.r, colour.g, colour.b, k_FillAlpha), "Fill");
            AddLayer(root.transform, m_Rim, new Color(colour.r, colour.g, colour.b, k_RimAlpha), "Rim");
            AddParticles(root.transform, colour);

            return root;
        }

        static void AddLayer(Transform parent, Mesh mesh, Color colour, string name)
        {
            var layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            // Laid flat, facing UP. The meshes are built in XY with their normal on +Z, and
            // rotating +90 about X sends that normal to -Y — face down into the ground, which a
            // back-face-culling shader draws as nothing at all. -90 is the one that points it at
            // the camera.
            layer.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            layer.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = layer.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var material = BuildMaterial(colour);
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        /// <summary>
        /// A column of coloured motes rising out of the zone.
        /// </summary>
        /// <remarks>
        /// <para>The flat disc is the honest read — it says exactly where the effect reaches — but
        /// it is also the fragile one: it is a single-sided mesh lying on the floor, so it depends
        /// on facing the right way, on the ground not z-fighting it, and on the camera not being
        /// edge-on. The particles depend on none of that. They are billboards, so they always face
        /// the camera, and they stand up off the floor where nothing can hide them.</para>
        ///
        /// <para>Emission scales with area rather than being a flat rate, so a bigger zone reads as
        /// denser instead of sparser.</para>
        /// </remarks>
        static void AddParticles(Transform parent, Color colour)
        {
            var host = new GameObject("Motes");
            host.transform.SetParent(parent, false);

            var system = host.AddComponent<ParticleSystem>();
            system.Stop();

            var main = system.main;
            main.startLifetime = 1.7f;
            main.startSpeed = 0.55f;
            main.startSize = 0.09f;
            main.startColor = new Color(colour.r, colour.g, colour.b, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 220;
            main.playOnAwake = false;

            var emission = system.emission;
            emission.rateOverTime = 34f;

            // A disc on the floor, filled rather than edge-only, so the whole footprint sparkles.
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1f;                       // the root's scale is the zone radius
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            // Straight up, so they read as a column and not as smoke drifting off the map.
            //
            // All three axes are assigned, and all three in the same mode. Unity requires that of
            // velocityOverLifetime, and setting only Y left X and Z on their default constant mode
            // — which logs "Particle Velocity curves must all be in the same mode" once per system
            // per frame. With three zones on the ground that is thousands of lines a minute into
            // the player log, which is a real cost on a phone quite apart from the noise it buried
            // every other error under.
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.5f, 1.3f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);

            // Fade out at the top rather than popping.
            var colourOverLife = system.colorOverLifetime;
            colourOverLife.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(colour, 0f), new GradientColorKey(colour, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.18f), new GradientAlphaKey(0f, 1f) });
            colourOverLife.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLife = system.sizeOverLifetime;
            sizeOverLife.enabled = true;
            var curve = new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.2f));
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, curve);

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.material = BuildMoteMaterial();

            system.Play();
        }

        static Material s_MoteMaterial;

        /// <summary>A soft round dot, so the motes are glows rather than squares.</summary>
        /// <remarks>
        /// Shared by every zone and tinted per particle through startColor, so the four kinds cost
        /// one material and one texture between them rather than one each.
        /// </remarks>
        static Material BuildMoteMaterial()
        {
            if (s_MoteMaterial != null)
            {
                return s_MoteMaterial;
            }

            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ZoneMote",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                    // Squared falloff: a hot centre with a soft edge, which is what reads as a glow.
                    float a = Mathf.Clamp01(1f - d);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            var shader = ResolveShader();
            s_MoteMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            foreach (var property in new[] { "_MainTex", "_BaseMap" })
            {
                if (s_MoteMaterial.HasProperty(property))
                {
                    s_MoteMaterial.SetTexture(property, texture);
                    break;
                }
            }

            return s_MoteMaterial;
        }

        static Shader s_Shader;

        /// <summary>
        /// A shader that will still be there in a player build.
        /// </summary>
        /// <remarks>
        /// <para><c>Shader.Find</c> only sees shaders the build actually included, and a build
        /// includes a shader because some material references it or because it is in Always
        /// Included Shaders. Nothing in this project references URP/Unlit from an asset — the only
        /// user is code — so asking for it works in the Editor, where everything is available, and
        /// returns null in the build. A null shader makes a material that draws nothing, which is
        /// exactly "the zones do not appear, and there is no error either".</para>
        ///
        /// <para><c>Sprites/Default</c> is the backstop: it is unlit, it is already transparent,
        /// and it is always in a build. On a flat ground disc it looks the same.</para>
        /// </remarks>
        static Shader ResolveShader()
        {
            if (s_Shader != null)
            {
                return s_Shader;
            }

            foreach (var name in new[]
                     {
                         "Universal Render Pipeline/Unlit",
                         "Sprites/Default",
                         "Unlit/Transparent",
                         "UI/Default",
                     })
            {
                s_Shader = Shader.Find(name);
                if (s_Shader != null)
                {
                    return s_Shader;
                }
            }

            return null;
        }

        /// <summary>An unlit, additive-ish transparent material in the zone's colour.</summary>
        /// <remarks>
        /// Built rather than referenced so this needs no asset. The URP Unlit shader defaults to
        /// opaque, and its transparency lives in shader properties plus a keyword rather than in a
        /// single flag — setting the colour alone would give a solid disc with the alpha ignored.
        /// </remarks>
        static Material BuildMaterial(Color colour)
        {
            var shader = ResolveShader();
            if (shader == null)
            {
                Debug.LogError("[Zones] No usable shader found — zones will not be drawn.");
                return null;
            }

            var material = new Material(shader);

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);                       // transparent
                material.SetFloat("_Blend", 0f);                         // alpha
                material.SetFloat("_ZWrite", 0f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON");
            }

            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            foreach (var property in new[] { "_BaseColor", "_Color" })
            {
                if (material.HasProperty(property))
                {
                    material.SetColor(property, colour);
                }
            }

            return material;
        }

        /// <summary>Breathes, and fades out as the zone runs down.</summary>
        void Animate(GameObject visual, ZoneState zone)
        {
            float remaining = (float)(zone.ExpiresAt - NetworkTime.time);
            float fade = Mathf.Clamp01(remaining / k_FadeSeconds);

            // A slow pulse, which is what stops a flat disc reading as a texture on the floor.
            float pulse = 1f + Mathf.Sin(Time.time * 2.2f) * 0.035f;
            visual.transform.localScale = Vector3.one * zone.Radius * pulse;

            Color colour = ZoneRules.ColorFor(zone.Kind);

            // Stop emitting before the zone goes, so the last motes have time to finish rising
            // instead of vanishing mid-air with it.
            var motes = visual.GetComponentInChildren<ParticleSystem>();
            if (motes != null)
            {
                var emission = motes.emission;
                emission.rateOverTimeMultiplier = 34f * fade;
            }

            var renderers = visual.GetComponentsInChildren<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                float baseAlpha = renderers[i].gameObject.name == "Rim" ? k_RimAlpha : k_FillAlpha;
                var material = renderers[i].sharedMaterial;
                var faded = new Color(colour.r, colour.g, colour.b, baseAlpha * fade);

                foreach (var property in new[] { "_BaseColor", "_Color" })
                {
                    if (material.HasProperty(property))
                    {
                        material.SetColor(property, faded);
                    }
                }
            }
        }

        void ClearAll()
        {
            foreach (var visual in m_Live.Values)
            {
                if (visual != null)
                {
                    Destroy(visual);
                }
            }

            m_Live.Clear();
        }

        void OnDestroy()
        {
            ClearAll();
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        // ── Meshes ────────────────────────────────────────────────────────────────────────────

        static Mesh BuildDisc(float radius, int segments)
        {
            var vertices = new Vector3[segments + 1];
            var triangles = new int[segments * 3];

            vertices[0] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;

                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }

            var mesh = new Mesh { name = "ZoneDisc" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildRing(float radius, float width, int segments)
        {
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            float inner = radius - width;

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                vertices[i * 2] = direction * inner;
                vertices[i * 2 + 1] = direction * radius;

                int next = (i + 1) % segments;
                triangles[i * 6] = i * 2;
                triangles[i * 6 + 1] = i * 2 + 1;
                triangles[i * 6 + 2] = next * 2 + 1;
                triangles[i * 6 + 3] = i * 2;
                triangles[i * 6 + 4] = next * 2 + 1;
                triangles[i * 6 + 5] = next * 2;
            }

            var mesh = new Mesh { name = "ZoneRim" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
