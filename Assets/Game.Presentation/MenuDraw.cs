using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // What Assets/UI/Menu.uss cannot draw: a frame with two cut corners, and a fade across an element.
    // A frame takes its look from custom properties of the element's style, so a rule with :hover
    // or a class changes it, and the change is eased:
    //   --frame-fill, --frame-line   the plate and its outline;
    //   --frame-accent               brackets on the two cut corners;
    //   --frame-bar                  a bar along the left edge (the open section of the menu);
    //   --frame-cut                  the size of the cut, pixels.
    public static class MenuDraw
    {
        private const float EaseSeconds = 0.12f;
        private const float BracketLength = 9f;
        private const float BarWidth = 3f;

        private static readonly CustomStyleProperty<Color> FillProperty = new CustomStyleProperty<Color>("--frame-fill");
        private static readonly CustomStyleProperty<Color> LineProperty = new CustomStyleProperty<Color>("--frame-line");
        private static readonly CustomStyleProperty<Color> AccentProperty = new CustomStyleProperty<Color>("--frame-accent");
        private static readonly CustomStyleProperty<Color> BarProperty = new CustomStyleProperty<Color>("--frame-bar");
        private static readonly CustomStyleProperty<float> CutProperty = new CustomStyleProperty<float>("--frame-cut");

        public static void Frame(VisualElement element) => new FrameLook(element);

        // A fade across the element, left to right or top to bottom: stops are (place 0..1, colour).
        public static void Gradient(VisualElement element, bool horizontal, params (float At, Color Color)[] stops)
        {
            element.generateVisualContent += context =>
            {
                Rect box = element.contentRect;
                if (box.width < 1f || box.height < 1f || stops.Length < 2)
                    return;
                MeshWriteData mesh = context.Allocate(stops.Length * 2, (stops.Length - 1) * 6);
                foreach ((float at, Color color) in stops)
                {
                    // Two vertices per stop: top and bottom of a column, or left and right of a row.
                    Vector2 a = horizontal ? new Vector2(box.xMin + box.width * at, box.yMin) : new Vector2(box.xMin, box.yMin + box.height * at);
                    Vector2 b = horizontal ? new Vector2(a.x, box.yMax) : new Vector2(box.xMax, a.y);
                    mesh.SetNextVertex(new Vertex { position = new Vector3(a.x, a.y, Vertex.nearZ), tint = color });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(b.x, b.y, Vertex.nearZ), tint = color });
                }
                for (int i = 0; i < stops.Length - 1; i++)
                {
                    int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                    // Clockwise on the screen, whichever way the strip runs.
                    if (horizontal)
                    {
                        Triangle(mesh, b, a, c);
                        Triangle(mesh, c, d, b);
                    }
                    else
                    {
                        Triangle(mesh, c, a, b);
                        Triangle(mesh, b, d, c);
                    }
                }
            };
        }

        private static void Triangle(MeshWriteData mesh, int a, int b, int c)
        {
            mesh.SetNextIndex((ushort)a);
            mesh.SetNextIndex((ushort)b);
            mesh.SetNextIndex((ushort)c);
        }

        private sealed class FrameLook
        {
            private readonly VisualElement _element;
            private Color _fill, _line, _accent, _bar;
            private Color _fromFill, _fromLine, _fromAccent, _fromBar;
            private Color _toFill, _toLine, _toAccent, _toBar;
            private float _cut;
            private float _easeStart;
            private bool _styled;
            private IVisualElementScheduledItem? _ease;

            public FrameLook(VisualElement element)
            {
                _element = element;
                element.generateVisualContent += Draw;
                element.RegisterCallback<CustomStyleResolvedEvent>(OnStyle);
            }

            private void OnStyle(CustomStyleResolvedEvent resolved)
            {
                ICustomStyle style = resolved.customStyle;
                style.TryGetValue(FillProperty, out Color fill);
                style.TryGetValue(LineProperty, out Color line);
                style.TryGetValue(AccentProperty, out Color accent);
                style.TryGetValue(BarProperty, out Color bar);
                style.TryGetValue(CutProperty, out _cut);
                if (!_styled)
                {
                    _styled = true;
                    _fill = _toFill = fill;
                    _line = _toLine = line;
                    _accent = _toAccent = accent;
                    _bar = _toBar = bar;
                    _element.MarkDirtyRepaint();
                    return;
                }
                if (fill == _toFill && line == _toLine && accent == _toAccent && bar == _toBar)
                    return;
                _fromFill = _fill;
                _fromLine = _line;
                _fromAccent = _accent;
                _fromBar = _bar;
                _toFill = fill;
                _toLine = line;
                _toAccent = accent;
                _toBar = bar;
                _easeStart = Time.unscaledTime;
                if (_ease == null)
                    _ease = _element.schedule.Execute(Ease).Every(15);
                else
                    _ease.Resume();
            }

            private void Ease()
            {
                float k = Mathf.Clamp01((Time.unscaledTime - _easeStart) / EaseSeconds);
                _fill = Color.Lerp(_fromFill, _toFill, k);
                _line = Color.Lerp(_fromLine, _toLine, k);
                _accent = Color.Lerp(_fromAccent, _toAccent, k);
                _bar = Color.Lerp(_fromBar, _toBar, k);
                _element.MarkDirtyRepaint();
                if (k >= 1f)
                    _ease!.Pause();
            }

            private void Draw(MeshGenerationContext context)
            {
                float width = _element.layout.width, height = _element.layout.height;
                if (!_styled || width < 4f || height < 4f)
                    return;
                float cut = Mathf.Min(_cut, Mathf.Min(width, height) * 0.5f);
                // Half a pixel in, so a one-pixel line lies whole inside the element.
                const float edge = 0.5f;
                Painter2D painter = context.painter2D;
                painter.lineJoin = LineJoin.Miter;
                painter.lineCap = LineCap.Butt;

                if (_fill.a > 0f || _line.a > 0f)
                {
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(cut, edge));
                    painter.LineTo(new Vector2(width - edge, edge));
                    painter.LineTo(new Vector2(width - edge, height - cut));
                    painter.LineTo(new Vector2(width - cut, height - edge));
                    painter.LineTo(new Vector2(edge, height - edge));
                    painter.LineTo(new Vector2(edge, cut));
                    painter.ClosePath();
                    if (_fill.a > 0f)
                    {
                        painter.fillColor = _fill;
                        painter.Fill();
                    }
                    if (_line.a > 0f)
                    {
                        painter.strokeColor = _line;
                        painter.lineWidth = 1f;
                        painter.Stroke();
                    }
                }

                if (_bar.a > 0f)
                {
                    painter.fillColor = _bar;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(0f, cut));
                    painter.LineTo(new Vector2(BarWidth, cut - BarWidth));
                    painter.LineTo(new Vector2(BarWidth, height));
                    painter.LineTo(new Vector2(0f, height));
                    painter.ClosePath();
                    painter.Fill();
                }

                if (_accent.a > 0f)
                {
                    float reach = Mathf.Min(BracketLength, Mathf.Min(width, height) * 0.5f - cut);
                    if (reach < 0f)
                        reach = 0f;
                    painter.strokeColor = _accent;
                    painter.lineWidth = 2f;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(cut + reach, 1f));
                    painter.LineTo(new Vector2(cut, 1f));
                    painter.LineTo(new Vector2(1f, cut));
                    painter.LineTo(new Vector2(1f, cut + reach));
                    painter.Stroke();
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(width - cut - reach, height - 1f));
                    painter.LineTo(new Vector2(width - cut, height - 1f));
                    painter.LineTo(new Vector2(width - 1f, height - cut));
                    painter.LineTo(new Vector2(width - 1f, height - cut - reach));
                    painter.Stroke();
                }
            }
        }
    }
}
