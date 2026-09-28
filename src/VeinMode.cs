using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    /// <b>A tap is a press AND a release of the key on its own, with the game in front the whole
    /// time</b>, not the key going down. Alt is the key most often pressed on the way to
    /// something else, and each of these would otherwise flip the mode behind the player's back:
    ///
    ///  - Alt+Tab, leaving. The press arrives while the game has focus. Toggling on the press
    ///    flipped vein mining every time somebody tabbed out to read a wiki with a pickaxe in hand.
    ///  - Alt+Tab, coming back. Windows can hand the window back while Alt is still held, and the
    ///    Input System, catching up on a keyboard it stopped watching, can then report Alt going
    ///    down in the game's first frames. The release that follows lands in front of a focused
    ///    game, so it looks exactly like a tap the player made. This one is inferred, not seen:
    ///    it is the road that fits Jafna's line below, since Jafna counts a press, not a tap.
    ///  - Taum's follow toggle is Alt held with E on a boar. With a pickaxe out that is still a
    ///    press of Alt.
    ///
    /// <b>Why it is this strict.</b> The first version already counted on the release, asked
    /// Application.isFocused at the press, on every frame between and at the release, and refused
    /// a tap with Use or the inventory key (Tab) inside it. The first scenario run, on 2026-09-28,
    /// switched vein mining on anyway when Robbin tabbed away, and it stayed on for the rest of the
    /// session; Jafna, which reads the same Left Alt with a hoe, logged a height hold in that run
    /// with nobody playing. Polling cannot see either Alt+Tab. Tab need not reach the game at all,
    /// since Windows takes it for the switcher. And the game can hear of a focus change a frame
    /// or so after the keystrokes around it, so the frame that sees Alt come up can still read
    /// focused, and a press handed back with the window arrives after focus already reads true.
    /// Which of the two that run hit is not known, and the rule below closes both.
    ///
    /// So a tap counts only when all of this holds:
    ///  - The press came more than FocusSettleSeconds (and FocusSettleFrames) after the game last
    ///    gained or lost focus. That refuses the press Windows hands back with the window.
    ///  - No other key went down while it was held: any key ZInput can read, not only E and Tab.
    ///  - The game kept focus from the press to the release, judged by every focus change the
    ///    plugin is told of (OnApplicationFocus) as well as by polling, and the key came back up
    ///    within TapSeconds.
    ///  - The game still has focus a moment after the release. The switch happens then rather
    ///    than on the release itself, so a focus loss the game hears of a frame late still
    ///    cancels it.
    ///
    /// The costs: the mode changes a little after the release, a fraction of a second nobody
    /// will notice on a toggle, and a tap with any other key pressed inside it (a strafe, say)
    /// does not count and has to be made again.
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

        /// <summary>
        /// How long after a focus change a press or a release is not trusted, in both seconds and
        /// frames. Frames because the game hears of a focus change on a frame boundary, a frame
        /// or two late; seconds so that a fast machine, which runs through three frames in a few
        /// milliseconds, still waits long enough for Windows to finish switching.
        /// </summary>
        private const float FocusSettleSeconds = 0.15f;
        private const int FocusSettleFrames = 3;

        internal static bool On { get; private set; }

        private static bool _armed;
        private static float _downAt;
        private static int _downFocusChanges;

        private static bool _pending;
        private static float _upAt;
        private static int _upFrame;
        private static int _upFocusChanges;

        private static int _focusChanges;
        private static float _focusChangedAt = float.NegativeInfinity;
        private static int _focusChangedFrame = int.MinValue / 2;
        private static bool _wasFocused = true;

        private static KeyCode _othersFor = KeyCode.None;
        private static KeyCode[] _others;
        private static bool _othersFailed;

        /// <summary>From the console, and from the key through Tick.</summary>
        internal static void Set(bool on)
        {
            if (On == on) return;
            On = on;

            if (MalmrConfig.Verbose.Value)
                MalmrPlugin.Log.LogInfo("Vein mining " + (on ? "on." : "off."));
        }

        /// <summary>
        /// From the plugin's OnApplicationFocus, and from Tick when polling sees the focus flip.
        /// Both, because neither covers every case alone: a game that stops running frames in the
        /// background polls neither the loss nor the return, and the poll is a free backstop to a
        /// message nobody has yet watched arrive in this game.
        /// </summary>
        internal static void FocusChanged()
        {
            _focusChanges++;
            _focusChangedAt = Time.unscaledTime;
            _focusChangedFrame = Time.frameCount;
        }

        /// <summary>From the plugin's Update.</summary>
        internal static void Tick()
        {
            bool focused = Application.isFocused;
            if (focused != _wasFocused)
            {
                _wasFocused = focused;
                FocusChanged();
            }

            if (!MalmrConfig.Enabled.Value)
            {
                _armed = false;
                _pending = false;
                return;
            }

            Settle(focused);

            KeyCode key = MalmrConfig.VeinToggleKey.Value;
            if (key == KeyCode.None) return;

            // logWarning: false - ZInput grumbles about every KeyCode it cannot map, and a key
            // somebody chose is a configuration, not a fault. ZInput rather than Unity's own
            // Input, because the game runs on the new Input System and the legacy class does not
            // see every key here - Furrow lost a round trip to that.
            if (ZInput.GetKeyDown(key, false))
            {
                _armed = focused && !JustFocused() && !Elsewhere();
                _downAt = Time.unscaledTime;
                _downFocusChanges = _focusChanges;
            }

            if (!_armed) return;

            // Checked on the press frame too, so Alt and another key going down together is
            // already not a tap. Use and Inventory stay beside the any-key look because they are
            // buttons, not keys: rebound to a mouse button or pressed on a gamepad, the keyboard
            // look would not see them.
            if (!focused
                || _focusChanges != _downFocusChanges
                || ZInput.GetButtonDown("Use")
                || ZInput.GetButtonDown("Inventory")
                || OtherKeyDown(key)
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

            _pending = true;
            _upAt = Time.unscaledTime;
            _upFrame = Time.frameCount;
            _upFocusChanges = _downFocusChanges;
        }

        /// <summary>
        /// Switches the mode for a tap that has already been released, once the focus has held
        /// long enough after the release to trust it. A focus change in the meantime, or the game
        /// not being in front now, drops the tap instead. The count compared is the one taken at
        /// its press, kept apart from the next press's, since the tap is only good if nothing
        /// changed from its press onwards and a quick second tap must not vouch for the first.
        /// </summary>
        private static void Settle(bool focused)
        {
            if (!_pending) return;

            if (!focused || _focusChanges != _upFocusChanges)
            {
                _pending = false;
                return;
            }

            if (Time.frameCount - _upFrame < FocusSettleFrames) return;
            if (Time.unscaledTime - _upAt < FocusSettleSeconds) return;

            _pending = false;
            Set(!On);
        }

        /// <summary>Whether the game gained or lost focus a moment ago, too recently to trust a press.</summary>
        private static bool JustFocused()
        {
            return Time.frameCount - _focusChangedFrame < FocusSettleFrames
                   || Time.unscaledTime - _focusChangedAt < FocusSettleSeconds;
        }

        /// <summary>Whether any keyboard key other than the toggle key went down this frame.</summary>
        private static bool OtherKeyDown(KeyCode key)
        {
            KeyCode[] others = Others(key);
            if (others == null) return false;

            foreach (KeyCode other in others)
            {
                if (ZInput.GetKeyDown(other, false)) return true;
            }

            return false;
        }

        /// <summary>
        /// Every keyboard KeyCode ZInput can read, one per physical key, leaving out the toggle
        /// key's own. Built once per toggle key and only ever walked while a tap is armed, which
        /// is half a second at a time.
        ///
        /// Taken from ZInput's private KeyCode-to-Key map rather than from Enum.GetValues or a
        /// list written here. Asking ZInput about a KeyCode that is not in that map throws inside
        /// it (the Input System's keyboard has no key for it), so the map is the list of what can
        /// safely be asked, and it is what the game itself reads. The same physical key under two
        /// KeyCodes (AltGr and RightAlt, the Windows and Command keys) is walked once, and a
        /// toggle key's twin is left out with it: otherwise binding RightAlt would see its own
        /// press as AltGr's and never count a tap.
        ///
        /// Bound lazily and in a try, as reflection is everywhere in this suite. If a game update
        /// renames the field, the tap keeps its other guards (focus, Use, Tab, the time limit),
        /// loses only this one, and the log says so once.
        /// </summary>
        private static KeyCode[] Others(KeyCode key)
        {
            if (_others != null && _othersFor == key) return _others;
            if (_othersFailed) return null;

            try
            {
                FieldInfo field = typeof(ZInput).GetField("s_keyCodeToKeyMap",
                    BindingFlags.NonPublic | BindingFlags.Static);
                IDictionary map = field == null ? null : field.GetValue(null) as IDictionary;
                if (map == null) throw new MissingFieldException("ZInput", "s_keyCodeToKeyMap");

                object own = map.Contains(key) ? map[key] : null;
                var walked = new HashSet<object>();
                var others = new List<KeyCode>();

                foreach (DictionaryEntry entry in map)
                {
                    if (!(entry.Key is KeyCode) || entry.Value == null) continue;

                    // Key.None is 0, and the keyboard has no key at that index.
                    if (Convert.ToInt32(entry.Value) <= 0) continue;
                    if (own != null && own.Equals(entry.Value)) continue;
                    if (!walked.Add(entry.Value)) continue;

                    others.Add((KeyCode)entry.Key);
                }

                _others = others.ToArray();
                _othersFor = key;
                return _others;
            }
            catch (Exception error)
            {
                _othersFailed = true;
                MalmrPlugin.Log.LogWarning("Could not read ZInput's key list, so a vein mining tap "
                    + "with another key pressed inside it still counts (Use and Tab still stop "
                    + "one). Said once: " + error.Message);
                return null;
            }
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
