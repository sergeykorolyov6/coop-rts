using UnityEngine;

namespace Game.Presentation
{
    // Ground of the map in the Game view: one texel per cell, checkerboard so cells read
    // without gizmos. Same layout as SimRunner's gizmo grid: cell (x, y) → world XZ [x, x+1) × [y, y+1).
    [RequireComponent(typeof(SimRunner))]
    public sealed class MapView : MonoBehaviour
    {
        [SerializeField] private Color _cellColorA = new Color(0.32f, 0.36f, 0.30f);
        [SerializeField] private Color _cellColorB = new Color(0.28f, 0.32f, 0.26f);

        private Texture2D? _texture;
        private Material? _material;

        // Start, not Awake: SimRunner creates the Simulation in its Awake.
        private void Start()
        {
            var simulation = GetComponent<SimRunner>().Simulation;
            if (simulation == null)
                return;

            int width = simulation.State.Grid.Width;
            int height = simulation.State.Grid.Height;

            _texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "MapCells",
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = (x + y) % 2 == 0 ? _cellColorA : _cellColorB;
            _texture.SetPixels(pixels);
            _texture.Apply();

            // A primitive gets the active render pipeline's default material, so this works
            // with URP and Built-in alike without Shader.Find.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ground.name = "Ground";
            Destroy(ground.GetComponent<Collider>());
            ground.transform.SetParent(transform, worldPositionStays: false);
            // Quad lies in XY facing -Z; +90° about X lays it on XZ facing up, local Y → world Z.
            // Slightly below zero so the gizmo grid is not hidden in the Scene view.
            ground.transform.localPosition = new Vector3(width * 0.5f, -0.01f, height * 0.5f);
            ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ground.transform.localScale = new Vector3(width, height, 1f);

            var groundRenderer = ground.GetComponent<MeshRenderer>();
            _material = groundRenderer.material;
            _material.mainTexture = _texture;
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
            if (_texture != null)
                Destroy(_texture);
        }
    }
}
