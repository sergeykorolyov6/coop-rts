using UnityEngine;

namespace Game.Presentation
{
    // Every shape in the game is a Unity primitive. In the editor a primitive takes the render
    // pipeline's default material, but in a player build it gets the built-in Standard one, which
    // URP cannot draw: everything turns pink. So primitives take a URP Lit material from
    // Assets/Resources, which is always shipped with the build.
    public static class Primitives
    {
        private const string MaterialPath = "PrimitiveLit";

        private static Material? _material;
        private static bool _warned;

        // The shared material itself: textured models (TerrainView) make their own copies of it.
        public static Material? Lit => _material ??= UnityEngine.Resources.Load<Material>(MaterialPath);

        public static GameObject Create(PrimitiveType type)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            if (Lit != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = _material;
            else if (!_warned)
            {
                _warned = true;
                Debug.LogError($"Primitives: Assets/Resources/{MaterialPath}.mat is missing; shapes will be pink in a build.");
            }
            return go;
        }
    }
}
