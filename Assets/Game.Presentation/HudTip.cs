using System.Collections.Generic;

namespace Game.Presentation
{
    // What a button's tip says (Hud.AttachTip): a title with the hotkey, the price, what it is for,
    // numbers, and a note when something is in the way ("не хватает ресурсов").
    public sealed class HudTip
    {
        public string Title = "";
        public string Hotkey = "";
        public int Primary;
        public int Secondary;
        public bool HasPrice;
        public bool PrimaryShort;
        public bool SecondaryShort;
        public string? Time;
        public string Body = "";
        public string Note = "";
        public readonly List<(string Caption, string Value)> Stats = new List<(string, string)>();

        public static HudTip Text(string body) => new HudTip { Body = body };

        public HudTip WithPrice(int primary, int secondary, bool primaryShort = false, bool secondaryShort = false, string? time = null)
        {
            HasPrice = true;
            Primary = primary;
            Secondary = secondary;
            PrimaryShort = primaryShort;
            SecondaryShort = secondaryShort;
            Time = time;
            return this;
        }
    }
}
