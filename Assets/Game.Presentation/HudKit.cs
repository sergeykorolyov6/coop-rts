using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // Building blocks of the in-game UI, shared by the panels: plates with cut corners, command
    // buttons (icon, name, price, hotkey), bars, stat cells and price rows. The look lives in
    // Assets/UI/Hud.uss; this only builds the trees and names the classes.
    // Icons are Resources/Icons/<id>.png — white glyphs on transparency, tinted by USS; an id
    // without a picture shows its first letter instead, so new content works before its art.
    public static class HudKit
    {
        private const string IconFolder = "Icons/";
        private static readonly Dictionary<string, Texture2D?> Icons = new Dictionary<string, Texture2D?>();

        public static Texture2D? Icon(string id)
        {
            if (!Icons.TryGetValue(id, out Texture2D? texture))
            {
                texture = UnityEngine.Resources.Load<Texture2D>(IconFolder + id);
                Icons[id] = texture;
            }
            return texture;
        }

        // A plate of the HUD: dark, framed, with cut corners (MenuDraw reads the frame from USS).
        public static VisualElement Plate(string name, params string[] classes)
        {
            var plate = new VisualElement { name = name };
            MakePlate(plate, classes);
            return plate;
        }

        public static void MakePlate(VisualElement element, params string[] classes)
        {
            element.AddToClassList("hud-plate");
            foreach (string c in classes)
                element.AddToClassList(c);
            MenuDraw.Frame(element);
        }

        public static VisualElement Child(VisualElement parent, string className)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            parent.Add(element);
            return element;
        }

        public static Label Text(VisualElement parent, string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        // The picture of a unit, building, ability or resource; without one, its first letter.
        public static VisualElement IconView(VisualElement parent, string id, string fallback, string className)
        {
            var view = new VisualElement { pickingMode = PickingMode.Ignore };
            view.AddToClassList("hud-icon");
            view.AddToClassList(className);
            SetIcon(view, id, fallback);
            parent.Add(view);
            return view;
        }

        public static void SetIcon(VisualElement view, string id, string fallback)
        {
            if (view.userData as string == id)
                return;
            view.userData = id;
            view.Clear();
            Texture2D? texture = Icon(id);
            view.EnableInClassList("hud-icon--letter", texture == null);
            if (texture != null)
            {
                view.style.backgroundImage = Background.FromTexture2D(texture);
                return;
            }
            view.style.backgroundImage = StyleKeyword.None;
            var letter = new Label(fallback.Length > 0 ? fallback.Substring(0, 1).ToUpperInvariant() : "?") { pickingMode = PickingMode.Ignore };
            letter.AddToClassList("hud-icon__letter");
            view.Add(letter);
        }

        // "Основной 30 + второй 10" as two marks with numbers; secondary only when it costs.
        public static VisualElement Price(VisualElement parent, string className)
        {
            VisualElement row = Child(parent, "hud-price");
            row.AddToClassList(className);
            return row;
        }

        public static void SetPrice(VisualElement row, int primary, int secondary, bool primaryShort, bool secondaryShort, string? time = null)
        {
            string key = $"{primary}|{secondary}|{primaryShort}|{secondaryShort}|{time}";
            if (row.userData as string == key)
                return;
            row.userData = key;
            row.Clear();
            if (primary > 0 || secondary == 0)
                Amount(row, Game.Sim.Resources.Primary, primary.ToString(), primaryShort);
            if (secondary > 0)
                Amount(row, Game.Sim.Resources.Secondary, secondary.ToString(), secondaryShort);
            if (time != null)
                Text(row, time, "hud-price__time");
        }

        public static void Amount(VisualElement row, int resource, string text, bool shortOf)
        {
            VisualElement mark = Child(row, "hud-res");
            mark.AddToClassList(resource == Game.Sim.Resources.Secondary ? "hud-res--secondary" : "hud-res--primary");
            Label value = Text(row, text, "hud-price__value");
            value.EnableInClassList("hud-price__value--short", shortOf);
        }

        // A stat cell: small caption above, the value under it ("УРОН" / "12").
        public static void Stat(VisualElement parent, string caption, string value)
        {
            VisualElement cell = Child(parent, "hud-stat");
            Text(cell, caption, "hud-stat__caption");
            Text(cell, value, "hud-stat__value");
        }

        public static Bar AddBar(VisualElement parent, string className)
        {
            VisualElement track = Child(parent, "hud-bar");
            track.AddToClassList(className);
            VisualElement fill = Child(track, "hud-bar__fill");
            return new Bar(track, fill);
        }

        public sealed class Bar
        {
            private float _shown = -1f;
            private Color _color;

            public Bar(VisualElement track, VisualElement fill)
            {
                Track = track;
                Fill = fill;
            }

            public VisualElement Track { get; }
            public VisualElement Fill { get; }

            public void Set(float share)
            {
                share = Mathf.Clamp01(share);
                if (Mathf.Abs(share - _shown) < 0.001f)
                    return;
                _shown = share;
                Fill.style.width = Length.Percent(share * 100f);
            }

            public void SetColor(Color color)
            {
                if (color == _color)
                    return;
                _color = color;
                Fill.style.backgroundColor = color;
            }
        }

        // Green at full health, amber at half, red near death.
        public static Color HealthColor(float share)
        {
            Color high = new Color(0.47f, 0.90f, 0.55f), mid = new Color(1.00f, 0.77f, 0.36f), low = new Color(1.00f, 0.38f, 0.34f);
            return share > 0.5f ? Color.Lerp(mid, high, (share - 0.5f) * 2f) : Color.Lerp(low, mid, share * 2f);
        }
    }
}
