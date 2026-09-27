using UnityEngine;

namespace Malmr
{
    /// <summary>
    /// Which deposit the bar is about, and what it says. The drawing is VeinBar's alone, so a
    /// different look is a change to that one class and nothing here.
    ///
    /// The bar shows while you are vein mining a deposit, which is Robbin's line and reads as
    /// two cases: the deposit under your crosshair, and the one you last put a blow into, for a
    /// few seconds after, while you are still standing at it. The second is what keeps the bar
    /// up when a swing's recoil or a step back takes the crosshair off the rock between blows -
    /// hover alone would make it flicker on every swing.
    ///
    /// Only for an open metal, and not for a deposit the Mistlands rule leaves to the hand. Those
    /// get their one line in the corner from Vein and no bar, because an empty bar over a rock you
    /// cannot vein mine would promise something it cannot deliver.
    ///
    /// Every client reads the same numbers off the deposit's ZDO (see Ledger), so the bar a
    /// player sees is the deposit's, not their own share of it: a friend's blows fill it too, and
    /// a chunk somebody takes by hand shrinks the total and moves the percentage on its own.
    /// </summary>
    internal static class Focus
    {
        /// <summary>How long the bar stays on the last deposit struck once the crosshair leaves it.</summary>
        private const float Linger = 5f;

        /// <summary>
        /// And only while you stay near it: within this many metres of where your last blow
        /// landed, which is on the rock's face and so a pickaxe's length from where you stand.
        ///
        /// Measured to that point and not to the deposit's pivot. Until review on 2026-09-27 it
        /// was 12 metres to the pivot, on the grounds that a pivot sits in a deposit's middle,
        /// and for an ore deposit a few metres across that was generous. Stone made cliffs and
        /// pillars into veins, whose pivot can be further than that from the face a player is
        /// working, and there the bar dropped out between blows and flickered, which is the one
        /// thing the linger is for. Eight is roughly what twelve to the middle of an ore deposit
        /// a few metres across left, counted from its face; no deposit's size has been measured.
        /// </summary>
        private const float Reach = 8f;

        /// <summary>How often the metal's gate is asked again for the same deposit. The numbers are read every frame.</summary>
        private const float Recheck = 0.25f;

        private static Component _struck;
        private static Vector3 _struckPoint;
        private static float _struckAt;

        private static Component _gated;
        private static float _nextGate;
        private static bool _open;
        /// <summary>One deposit as the screen names it, "Copper vein", looked up with the gate rather than every frame.</summary>
        private static string _noun = "";

        private static ZDOID _readUid = ZDOID.None;
        private static uint _readRevision;
        private static Ledger.Reading _reading;

        private static bool _marker;
        private static bool _bar;
        private static string _label = "";
        private static float _fraction;

        /// <summary>
        /// From Vein, on every blow sent into a bar, with the point it landed: the blow's
        /// m_point, which Attack sets on the struck chunk's collider for every melee hit.
        /// </summary>
        internal static void Struck(Component rock, Vector3 point)
        {
            _struck = rock;
            _struckPoint = point;
            _struckAt = Time.time;
        }

        /// <summary>From the plugin's Update. Decides; OnGUI only draws.</summary>
        internal static void Tick()
        {
            _marker = false;
            _bar = false;

            if (!MalmrConfig.Enabled.Value || !VeinMode.On) return;

            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead() || !VeinMode.PickaxeOut(player)) return;

            // The mode is on and a pickaxe is out: that alone earns the gold Vein above the
            // crosshair. Robbin asked for a small sign that it is on, and it matters most exactly
            // when there is no bar, since the next rock you hit will not break the way it used to.
            // It stays up while the bar shows as well: mockup A has both.
            _marker = true;

            Component target = Hovered(player);

            if (target == null && _struck != null && Time.time - _struckAt < Linger
                && Vector3.Distance(player.transform.position, _struckPoint) < Reach)
            {
                target = _struck;
            }

            if (target == null) return;

            ZNetView nview;
            if (!target.TryGetComponent(out nview) || !nview.IsValid()) return;

            if (target != _gated || Time.time >= _nextGate)
            {
                _gated = target;
                _nextGate = Time.time + Recheck;

                // The same two questions the swing asks, in the same order: where the deposit
                // stands, then what you have opened. A copper vein in the Mistlands gets no bar
                // for the same reason a shut metal gets none.
                Deposits.Kind kind = Deposits.Of(target);
                _open = kind != null && kind.Entry != null
                        && !Deposits.HandOnly(target, kind)
                        && Gate.For(kind, Vein.EarnedLevel(player)).Open;
                _noun = kind != null ? Deposits.Noun(kind.Metal) : "";
            }

            if (!_open) return;

            Ledger.Reading reading = Read(target, nview);
            if (reading == null || reading.Standing == 0) return;

            _bar = true;
            _fraction = reading.Fraction;
            _label = _noun + " " + reading.Percent + "%";
        }

        /// <summary>From the plugin's OnGUI.</summary>
        internal static void Draw()
        {
            if (!_marker) return;
            VeinBar.Draw(_bar, _label, _fraction);
        }

        /// <summary>The numbers, read again only when the deposit's ZDO has actually changed.</summary>
        private static Ledger.Reading Read(Component rock, ZNetView nview)
        {
            ZDO zdo = nview.GetZDO();

            if (_reading != null && zdo.m_uid == _readUid && zdo.DataRevision == _readRevision)
                return _reading;

            _reading = Ledger.Read(rock, nview);
            _readUid = zdo.m_uid;
            _readRevision = zdo.DataRevision;
            return _reading;
        }

        /// <summary>
        /// The deposit under the crosshair, through the game's own hover - which is the chunk's
        /// collider, a child of the deposit. Null when it is something else, or nothing.
        /// </summary>
        private static Component Hovered(Player player)
        {
            GameObject hover = player.GetHoverObject();
            if (hover == null) return null;

            MineRock5 vein = hover.GetComponentInParent<MineRock5>();
            if (vein != null) return vein;

            MineRock small = hover.GetComponentInParent<MineRock>();
            if (small != null) return small;

            return null;
        }
    }
}
