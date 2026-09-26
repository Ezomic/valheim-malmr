using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// The look of the vein mode marker and the vein bar, and nothing else.
    ///
    /// <b>Mockup A</b>, the one Robbin picked on 2026-09-26, and it is a spec, matched element
    /// by element:
    ///
    ///  - While vein mode is on and a pickaxe is out: the word Vein in gold, #d9a441, 12px,
    ///    centred just above the crosshair.
    ///  - While mining a deposit that is open to you: a bar centred just under the crosshair,
    ///    120 by 8 pixels at 1080p, black inside a 1 pixel #6b5a3a border, filled in the same
    ///    gold from the left in proportion to the progress.
    ///  - Under the bar, centred, in white 12px: "Copper vein 45%".
    ///
    /// Both at once while mining - the marker does not give way to the bar. The rebuild before
    /// the pick drew something else: a boxed "Vein mining" tag UNDER the crosshair in the bar's
    /// place, and a 240 by 22 bar with its label inside it. All of that is gone.
    ///
    /// Everything scales with the screen height against 1080, the text included, and the text
    /// never goes below 12: the suite's floor, the smallest that reads on Robbin's setup.
    ///
    /// <b>One thing not in the mockup:</b> each line of text has a one-pixel black shadow under
    /// it. Without one, white on a snowfield and gold on a lit sky are unreadable, and vanilla's
    /// own hover text carries an outline for the same reason. If the mockup is to be matched
    /// to the pixel, the shadow is the two lines in Text and nothing else changes.
    ///
    /// IMGUI, so the mod stays one DLL with no asset bundle. Kept to this one class: what
    /// decides when it shows and what it says is Focus, and where the numbers come from is
    /// Ledger, so a new look is a change here and nowhere else.
    /// </summary>
    internal static class VeinBar
    {
        private static readonly Color Gold = new Color32(0xd9, 0xa4, 0x41, 0xff);
        private static readonly Color Border = new Color32(0x6b, 0x5a, 0x3a, 0xff);
        private static readonly Color Back = Color.black;
        private static readonly Color White = Color.white;
        private static readonly Color Shadow = new Color(0f, 0f, 0f, 0.85f);

        // The mockup's numbers, at 1080p.
        private const float BarWidth = 120f;
        private const float BarHeight = 8f;
        private const float TextSize = 12f;

        /// <summary>
        /// How far the marker's baseline sits above the crosshair's middle, and the bar's top
        /// below it. "Just above" and "just under": clear of vanilla's crosshair, which is a
        /// small ring, and no further.
        /// </summary>
        private const float Gap = 12f;

        /// <summary>Between the bar's bottom edge and the top of the text under it.</summary>
        private const float TextGap = 3f;

        private static Texture2D _gold, _border, _back;
        private static GUIStyle _marker, _label, _shadow;
        private static float _builtFor = -1f;

        /// <summary>
        /// The marker always; the bar and its text too when there is a deposit to show.
        /// Called only while vein mode is on with a pickaxe out - Focus decides that.
        /// </summary>
        internal static void Draw(bool bar, string label, float fraction)
        {
            // IMGUI calls OnGUI once per event, and only the repaint draws anything.
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            // Follow the game's own idea of whether the interface is up: hidden with the HUD,
            // and while a menu, the map, a container or the console is over the view.
            if (Hud.IsUserHidden() || InventoryGui.IsVisible() || Menu.IsVisible()
                || Minimap.IsOpen() || Console.IsVisible()) return;

            float scale = Screen.height / 1080f;
            Build(scale);

            float centreX = Screen.width * 0.5f;
            float centreY = Screen.height * 0.5f;
            float line = Mathf.Ceil(_marker.fontSize * 1.4f);

            // Wide boxes and centred text, so the words centre on the crosshair whatever their
            // length and never wrap.
            float textWidth = 400f * Mathf.Max(1f, scale);

            // The marker, its bottom edge Gap above the crosshair.
            var marker = new Rect(centreX - textWidth * 0.5f, centreY - Gap * scale - line, textWidth, line);
            Text(marker, "Vein", _marker);

            if (!bar) return;

            // The bar: whole pixels, so the one-pixel border is one pixel and not a smear.
            float w = Mathf.Round(BarWidth * scale);
            float h = Mathf.Max(3f, Mathf.Round(BarHeight * scale));
            float edge = Mathf.Max(1f, Mathf.Round(scale));

            var outer = new Rect(Mathf.Round(centreX - w * 0.5f), Mathf.Round(centreY + Gap * scale), w, h);
            var inner = new Rect(outer.x + edge, outer.y + edge, outer.width - edge * 2f, outer.height - edge * 2f);

            GUI.DrawTexture(outer, _border);
            GUI.DrawTexture(inner, _back);

            float filled = Mathf.Round(inner.width * Mathf.Clamp01(fraction));
            if (filled > 0f) GUI.DrawTexture(new Rect(inner.x, inner.y, filled, inner.height), _gold);

            var under = new Rect(centreX - textWidth * 0.5f, outer.yMax + TextGap * scale, textWidth, line);
            Text(under, label, _label);
        }

        /// <summary>A line of text with its one-pixel shadow - see the class comment.</summary>
        private static void Text(Rect area, string text, GUIStyle style)
        {
            float offset = Mathf.Max(1f, Mathf.Round(_builtFor));
            GUI.Label(new Rect(area.x + offset, area.y + offset, area.width, area.height), text, _shadow);
            GUI.Label(area, text, style);
        }

        /// <summary>Styles exist only inside OnGUI, so they are built on first paint and again when the screen changes size.</summary>
        private static void Build(float scale)
        {
            // The textures are checked as well as the styles. They are marked to outlive a scene
            // change, but a Unity object can still be destroyed under a static reference, and
            // DrawTexture with a dead one logs an error every frame.
            bool textures = _gold != null && _border != null && _back != null;
            if (_marker != null && textures && Mathf.Approximately(_builtFor, scale)) return;
            _builtFor = scale;

            if (_gold == null) _gold = Solid(Gold);
            if (_border == null) _border = Solid(Border);
            if (_back == null) _back = Solid(Back);

            // 12 at 1080 and growing with the screen, never smaller: a 720p window keeps 12.
            int size = Mathf.Max(12, Mathf.RoundToInt(TextSize * scale));

            _marker = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Gold },
            };

            _label = new GUIStyle(_marker) { normal = { textColor = White } };
            _shadow = new GUIStyle(_marker) { normal = { textColor = Shadow } };
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
