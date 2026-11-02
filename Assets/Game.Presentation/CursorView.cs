using UnityEngine;

namespace Game.Presentation
{
    // Cursor by what a right click would do (combat.md: "attack", manual-mining.md: "mine",
    // creep-camp.md: "carry"; plus deliver and repair). Drawn in code until there is art:
    // simple glyphs with a black outline so they read over any ground.
    [RequireComponent(typeof(OrderController))]
    public sealed class CursorView : MonoBehaviour
    {
        private const int Size = 32;

        [SerializeField] private Color _attackColor = new Color(1.00f, 0.30f, 0.25f);
        [SerializeField] private Color _mineColor = new Color(0.95f, 0.65f, 0.25f);
        [SerializeField] private Color _carryColor = new Color(0.98f, 0.85f, 0.30f);
        [SerializeField] private Color _deliverColor = new Color(0.45f, 0.90f, 0.45f);
        [SerializeField] private Color _repairColor = new Color(0.45f, 0.75f, 1.00f);

        private OrderController? _orders;
        private Texture2D? _attack, _mine, _carry, _deliver, _repair;
        private OrderController.Intent _shown = OrderController.Intent.None;

        private void Awake()
        {
            _orders = GetComponent<OrderController>();
            _attack = Glyph(_attackColor, DrawCrosshair);
            _mine = Glyph(_mineColor, DrawPick);
            _carry = Glyph(_carryColor, DrawBox);
            _deliver = Glyph(_deliverColor, DrawArrowDown);
            _repair = Glyph(_repairColor, DrawPlus);
        }

        private void LateUpdate()
        {
            OrderController.Intent intent = _orders!.CurrentIntent;
            if (intent == _shown)
                return;
            _shown = intent;
            Texture2D? texture = intent switch
            {
                OrderController.Intent.Attack => _attack,
                OrderController.Intent.Mine => _mine,
                OrderController.Intent.Carry => _carry,
                OrderController.Intent.Deliver => _deliver,
                OrderController.Intent.Repair => _repair,
                _ => null,
            };
            Cursor.SetCursor(texture, texture != null ? new Vector2(Size / 2, Size / 2) : Vector2.zero, CursorMode.Auto);
        }

        private void OnDisable()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            _shown = OrderController.Intent.None;
        }

        private void OnDestroy()
        {
            foreach (Texture2D? texture in new[] { _attack, _mine, _carry, _deliver, _repair })
            {
                if (texture != null)
                    Destroy(texture);
            }
        }

        private delegate void Painter(bool[] mask);

        // The glyph is painted as a mask, then a one-pixel black outline is grown around it.
        private static Texture2D Glyph(Color color, Painter paint)
        {
            var mask = new bool[Size * Size];
            paint(mask);
            var pixels = new Color32[Size * Size];
            Color32 fill = color, outline = new Color32(0, 0, 0, 255), clear = new Color32(0, 0, 0, 0);
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    if (mask[i])
                        pixels[i] = fill;
                    else
                        pixels[i] = Near(mask, x, y) ? outline : clear;
                }
            }
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                name = "Cursor",
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static bool Near(bool[] mask, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (Get(mask, x + dx, y + dy))
                        return true;
            return false;
        }

        private static bool Get(bool[] mask, int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size && mask[y * Size + x];

        private static void Set(bool[] mask, int x, int y)
        {
            if (x >= 0 && y >= 0 && x < Size && y < Size)
                mask[y * Size + x] = true;
        }

        private static void Rect(bool[] mask, int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    Set(mask, x, y);
        }

        private static void Ring(bool[] mask, float radius, float thickness)
        {
            float c = (Size - 1) * 0.5f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    if (Mathf.Abs(d - radius) <= thickness * 0.5f)
                        Set(mask, x, y);
                }
        }

        private static void Line(bool[] mask, float x0, float y0, float x1, float y1, float thickness)
        {
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) * 2f);
            int r = Mathf.FloorToInt(thickness * 0.5f);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                Rect(mask, x - r, y - r, x + r, y + r);
            }
        }

        // Texture rows grow upwards: y = 0 is the bottom of the cursor.
        private static void DrawCrosshair(bool[] m)
        {
            Ring(m, 10f, 2.4f);
            Rect(m, 15, 2, 16, 9);
            Rect(m, 15, 22, 16, 29);
            Rect(m, 2, 15, 9, 16);
            Rect(m, 22, 15, 29, 16);
        }

        private static void DrawPick(bool[] m)
        {
            Line(m, 6, 4, 22, 22, 3f);           // handle
            Line(m, 12, 27, 28, 13, 3f);         // head
            Line(m, 12, 27, 9, 22, 2f);
            Line(m, 28, 13, 23, 10, 2f);
        }

        private static void DrawBox(bool[] m)
        {
            Rect(m, 7, 5, 24, 18);
            Line(m, 15.5f, 21, 15.5f, 29, 3f);   // arrow up: "carry"
            Line(m, 15.5f, 29, 10, 24, 2f);
            Line(m, 15.5f, 29, 21, 24, 2f);
        }

        private static void DrawArrowDown(bool[] m)
        {
            Line(m, 15.5f, 28, 15.5f, 10, 4f);
            Line(m, 15.5f, 4, 7, 13, 3f);
            Line(m, 15.5f, 4, 24, 13, 3f);
        }

        private static void DrawPlus(bool[] m)
        {
            Rect(m, 13, 4, 18, 27);
            Rect(m, 4, 13, 27, 18);
        }
    }
}
