using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    // A square button of a command card: hotkey in the corner, the icon, the name and the price.
    // A cooldown darkens it from the top; a badge in the other corner counts (queue, ready).
    public sealed class CommandButton
    {
        private readonly Label _hotkey;
        private readonly Label _badge;
        private readonly VisualElement _icon;
        private readonly Label _name;
        private readonly VisualElement _cooldown;
        private readonly Label _cooldownText;
        private string _shownBadge = "";
        private string _shownCooldown = "";
        private float _shownShare = -1f;

        public CommandButton(System.Action onClick, string iconId, string name, string hotkey)
        {
            Button = new Button(onClick);
            Button.AddToClassList("hud-cmd");
            MenuDraw.Frame(Button);
            _icon = HudKit.IconView(Button, iconId, name, "hud-cmd__icon");
            _name = HudKit.Text(Button, name, "hud-cmd__name");
            Price = HudKit.Price(Button, "hud-cmd__price");
            _cooldown = HudKit.Child(Button, "hud-cmd__cooldown");
            _cooldownText = HudKit.Text(Button, "", "hud-cmd__cooldown-text");
            _hotkey = HudKit.Text(Button, hotkey, "hud-cmd__hotkey");
            _hotkey.style.display = hotkey.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _badge = HudKit.Text(Button, "", "hud-cmd__badge");
            _badge.style.display = DisplayStyle.None;
            SetCooldown(0f, "");
        }

        public Button Button { get; }
        public VisualElement Price { get; }

        public void SetName(string name)
        {
            if (_name.text != name)
                _name.text = name;
        }

        public void SetIcon(string id, string fallback) => HudKit.SetIcon(_icon, id, fallback);

        public void SetHotkey(string hotkey)
        {
            if (_hotkey.text == hotkey)
                return;
            _hotkey.text = hotkey;
            _hotkey.style.display = hotkey.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetBadge(string text)
        {
            if (text == _shownBadge)
                return;
            _shownBadge = text;
            _badge.text = text;
            _badge.style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // share: how much of the wait is left, 0..1; text over the icon ("4 с").
        public void SetCooldown(float share, string text)
        {
            share = Mathf.Clamp01(share);
            if (Mathf.Abs(share - _shownShare) >= 0.005f)
            {
                _shownShare = share;
                _cooldown.style.height = Length.Percent(share * 100f);
                _cooldown.style.display = share > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (text != _shownCooldown)
            {
                _shownCooldown = text;
                _cooldownText.text = text;
                _cooldownText.style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // Cannot do it now (no money, full queue, not ready): dimmed, still clickable — the sim says why.
        public void SetBlocked(bool blocked) => Button.EnableInClassList("hud-cmd--blocked", blocked);

        public void SetActive(bool active) => Button.EnableInClassList("hud-cmd--active", active);
    }
}
