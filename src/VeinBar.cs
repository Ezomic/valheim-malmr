using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// The look of the vein bar and the vein mode marker, and nothing else.
    ///
    /// Kept to this one class on purpose: Robbin will pick the look from mockups, and whichever
    /// he picks replaces what is below without touching what decides when it shows or what it
    /// says (Focus) or where the numbers come from (Ledger). The default until then is his
    /// sketch from 2026-09-26: a bar under the crosshair reading "Copper vein 45%".
    ///
    /// IMGUI, so the mod stays one DLL with no asset bundle. Opaque, and never smaller than
    /// 12px, which are the suite's two rules for anything drawn over the game. Sized off the
    /// screen height against 1080, because a fixed 240 pixels is a sliver across a 4K screen.
    /// </summary>
    internal static class VeinBar
    {
        private static readonly Color Track = new Color(0.12f, 0.10f, 0.08f, 1f);
        private static readonly Color Edge = new Color(0.36f, 0.29f, 0.20f, 1f);
        private static readonly Color Fill = new Color(0.80f, 0.52f, 0.20f, 1f);
        private static readonly Color Text = new Color(0.96f, 0.92f, 0.84f, 1f);
        private static readonly Color Shadow = new Color(0f, 0f, 0f, 0.85f);

        private static Texture2D _track, _edge, _fill;
        private static GUIStyle _text, _shadow;
        private static float _builtFor = -1f;

        /// <summary>
        /// Draws the marker, or the bar when there is one. The bar is itself the sign that vein
        /// mining is on, so the marker is not drawn under it as well.
        /// </summary>
        internal static void Draw(bool bar, string label, float fraction)
        {
            // Follow the game's own idea of whether the interface is up: hidden with the HUD,
            // and while a menu, the map, a container or the console is over the view.
            if (Hud.IsUserHidden() || InventoryGui.IsVisible() || Menu.IsVisible()
                || Minimap.IsOpen() || Console.IsVisible()) return;

            float scale = Mathf.Max(1f, Screen.height / 1080f);
            Build(scale);

            float centreX = Screen.width * 0.5f;

            // Below the crosshair, clear of it and of the hover name vanilla prints beside it.
            float top = Screen.height * 0.5f + 40f * scale;

            if (bar)
            {
                float w = 240f * scale;
                float h = 22f * scale;
                var outer = new Rect(centreX - w * 0.5f, top, w, h);

                GUI.DrawTexture(outer, _edge);

                float border = Mathf.Max(1f, Mathf.Round(scale));
                var inner = new Rect(outer.x + border, outer.y + border, outer.width - border * 2f,
                                     outer.height - border * 2f);

                GUI.DrawTexture(inner, _track);

                float filled = inner.width * Mathf.Clamp01(fraction);
                if (filled > 0f) GUI.DrawTexture(new Rect(inner.x, inner.y, filled, inner.height), _fill);

                Label(outer, label);
                return;
            }

            // The marker: a small tag in the same place and colours, so the bar grows out of it.
            float mw = 110f * scale;
            float mh = 20f * scale;
            var tag = new Rect(centreX - mw * 0.5f, top, mw, mh);

            GUI.DrawTexture(tag, _edge);

            float b = Mathf.Max(1f, Mathf.Round(scale));
            GUI.DrawTexture(new Rect(tag.x + b, tag.y + b, tag.width - b * 2f, tag.height - b * 2f), _track);

            Label(tag, "Vein mining");
        }

        /// <summary>Text with a one-pixel shadow, so it reads over the fill and the track alike.</summary>
        private static void Label(Rect area, string text)
        {
            float offset = Mathf.Max(1f, Mathf.Round(_builtFor));
            GUI.Label(new Rect(area.x + offset, area.y + offset, area.width, area.height), text, _shadow);
            GUI.Label(area, text, _text);
        }

        /// <summary>Styles exist only inside OnGUI, so they are built on first paint and again when the screen changes size.</summary>
        private static void Build(float scale)
        {
            // The textures are checked as well as the style. They are marked to outlive a scene
            // change, but a Unity object can still be destroyed under a static reference, and
            // DrawTexture with a dead one logs an error every frame.
            bool textures = _track != null && _edge != null && _fill != null;
            if (_text != null && textures && Mathf.Approximately(_builtFor, scale)) return;
            _builtFor = scale;

            if (_track == null) _track = Solid(Track);
            if (_edge == null) _edge = Solid(Edge);
            if (_fill == null) _fill = Solid(Fill);

            // 12 is the floor, not the size: the smallest that reads on Robbin's setup. 14 at
            // 1080 and growing with the screen, so it keeps its size beside a bar that grows too.
            int font = Mathf.Max(12, Mathf.RoundToInt(14f * scale));

            _text = new GUIStyle(GUI.skin.label)
            {
                fontSize = font,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Text },
            };

            _shadow = new GUIStyle(_text) { normal = { textColor = Shadow } };
        }

        private static Texture2D Solid(Color colour)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, colour);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }
    }
}
