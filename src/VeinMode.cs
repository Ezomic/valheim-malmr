using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// Whether vein mining is on, and the key that switches it.
    ///
    /// <b>A tap toggles it</b>, Robbin's answer of 2026-09-26: tap once and it stays on until
    /// tapped again, across swings, tool changes and deposits. A key rather than always-on
    /// because the bar takes over every blow on an open metal, and there are times you want a
    /// few chunks by hand - a bit of copper for one repair, or a chunk that is in the way.
    ///
    /// <b>A tap is a press AND a release</b>, not the key going down. Alt is the key most often
    /// pressed on the way to something else, and two of those would otherwise flip the mode
    /// behind the player's back:
    ///
    ///  - Alt+Tab. The press arrives while the game has focus, and the release arrives after it
    ///    has gone. Toggling on the press flipped vein mining every time somebody tabbed out to
    ///    read a wiki with a pickaxe in hand.
    ///  - Taum's follow toggle is Alt held with E on a boar. With a pickaxe out that is still a
    ///    press of Alt.
    ///
    /// So it counts when the key comes back up within TapSeconds of going down, the game had
    /// focus the whole time, and neither Use nor the inventory key (Tab, the other half of
    /// Alt+Tab) went down in between. The cost is that the mode changes on the release rather
    /// than the press, which is a tenth of a second nobody will notice on a toggle.
    ///
    /// <b>Only with a pickaxe out.</b> Jafna's height hold is Left Alt with a hoe, and none of
    /// the game's own keyboard bindings is Alt by default (its AltPlace is Left Shift), so tying
    /// this to the Pickaxes skill keeps every other tool's Alt as it was.
    ///
    /// Not saved. It is a switch in the player's hands for this session, and a mode that came
    /// back on by itself after a restart would be the one thing a player could not see coming.
    /// </summary>
    internal static class VeinMode
    {
        /// <summary>How long the key may be held and still count as a tap.</summary>
        private const float TapSeconds = 0.5f;

        internal static bool On { get; private set; }

        private static bool _armed;
        private static float _downAt;

        /// <summary>From the console, and from the key through Tick.</summary>
        internal static void Set(bool on)
        {
            if (On == on) return;
            On = on;

            if (MalmrConfig.Verbose.Value)
                MalmrPlugin.Log.LogInfo("Vein mining " + (on ? "on." : "off."));
        }

        /// <summary>From the plugin's Update.</summary>
        internal static void Tick()
        {
            if (!MalmrConfig.Enabled.Value)
            {
                _armed = false;
                return;
            }

            KeyCode key = MalmrConfig.VeinToggleKey.Value;
            if (key == KeyCode.None) return;

            // logWarning: false - ZInput grumbles about every KeyCode it cannot map, and a key
            // somebody chose is a configuration, not a fault. ZInput rather than Unity's own
            // Input, because the game runs on the new Input System and the legacy class does not
            // see every key here - Furrow lost a round trip to that.
            if (ZInput.GetKeyDown(key, false))
            {
                _armed = Application.isFocused && !Elsewhere();
                _downAt = Time.unscaledTime;
            }

            if (!_armed) return;

            if (!Application.isFocused
                || ZInput.GetButtonDown("Use")
                || ZInput.GetButtonDown("Inventory")
                || Time.unscaledTime - _downAt > TapSeconds)
            {
                _armed = false;
                return;
            }

            if (!ZInput.GetKeyUp(key, false)) return;
            _armed = false;

            if (Elsewhere()) return;

            Player player = Player.m_localPlayer;
            if (player == null || !PickaxeOut(player)) return;

            Set(!On);
        }

        /// <summary>Whether the player is holding a tool that trains Pickaxes.</summary>
        internal static bool PickaxeOut(Player player)
        {
            if (player == null) return false;

            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            return weapon != null
                   && weapon.m_shared != null
                   && weapon.m_shared.m_skillType == Skills.SkillType.Pickaxes;
        }

        /// <summary>
        /// Whether the keystroke belongs to something other than the world: a text field - chat,
        /// the console, a sign - or a window over the view. Without the first, an Alt typed into
        /// chat reaches the mode, which reads as it switching by itself. Without the second, a
        /// tap while sorting the inventory would flip a mode the player cannot see the marker of.
        /// </summary>
        private static bool Elsewhere()
        {
            if (Chat.instance != null && Chat.instance.HasFocus()) return true;
            if (Console.IsVisible() || TextInput.IsVisible()) return true;
            return InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen();
        }
    }
}
