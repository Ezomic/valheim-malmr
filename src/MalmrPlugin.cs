using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Ezomic.Core;
using HarmonyLib;

namespace Malmr
{
    /// <summary>
    /// Malmr. Tap Left Alt with a pickaxe out and vein mining is on: your blows on a deposit
    /// fill a bar instead of breaking chunks, and when the bar reaches the whole deposit's
    /// health, every chunk breaks at once, buried ones too. Each metal opens at its own Pickaxes
    /// level, late, and once its biome's boss has been beaten at one star through Vandi.
    ///
    /// That is Robbin's design of 2026-09-26, in his words: "you enable it by pressing alt, and
    /// u keep mining a piece and a progress bar will show up that goes up to 100 till u have done
    /// enough damage to account for the whole ore deposit and then it breaks all at once". It
    /// replaced the first mechanic, which took up to four extra chunks beside the struck one on
    /// every swing, always on, and charged each a share of the swing's wear.
    ///
    /// Vein mining already exists and it is popular, and the reason is the same one Skaft
    /// answers for repair: a copper deposit is dozens of chunks and taking them one at a time is
    /// tedium, not difficulty. The popular version breaks the whole deposit on one blow while you
    /// hold a key. That deletes the deposit as a job, and the key makes it a setting rather than
    /// something the character has learned. This one keeps the job and takes away the fiddle:
    ///
    ///  - The deposit costs the swings it would by hand, roughly. A blow puts into the bar
    ///    exactly what it would have dealt the chunk it struck - the deposit's resistances, its
    ///    tool tier, the game's own damage number - and the bar is full only at the summed
    ///    health of every chunk. Stamina, pickaxe wear and the skill are charged by the swing
    ///    itself, as vanilla charges them. What it saves is the walking between chunks and the
    ///    digging after buried ones.
    ///  - Metal by metal, late. Tin at Pickaxes 40 up to bloodgold at 90, so every metal has been
    ///    mined by hand for a long while first. The level is the one you earned; a bonus from
    ///    gear or an effect does not open a metal early.
    ///  - And the boss of the metal's biome, beaten at one star, which is Vandi's count of your
    ///    kills reaching two. See Gate for the rule and Bosses for why only Vandi can answer it.
    ///  - A switch you choose to flip. It stays on until tapped again, a marker says it is on,
    ///    and with it off every blow is vanilla - for the times you want one chunk by hand.
    ///
    /// <b>Where each part runs.</b> The swing is decided on the machine that swings, because
    /// only that machine knows the swinger's skill - skills live in the player profile, not on
    /// any ZDO. The bar is kept on the deposit, because it must survive a logout and a friend
    /// must be able to finish it - and only the deposit's owner may write that, so the blow
    /// travels there as a message, the road a vanilla blow takes (Vein, then Owner). The owner
    /// counts it and, at 100%, breaks the deposit through the deposit's own damage path
    /// (Collapse). The one thing read from outside either machine is Vandi's kill count, which
    /// lives in the world's global keys and so is already on every client.
    ///
    /// There is deliberately no BepInProcess attribute. A dedicated server runs
    /// valheim_server.exe, and it can own deposits: it has to load this to fill their bars.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    // Soft, not hard. A hard dependency that is absent does not degrade - the plugin never
    // loads at all - and a mod should not need a second install for something it can do
    // without. Core is exactly that: what Malmr loses without it is the host's say and the
    // check that everybody connected has Malmr, not the mod. Soft still buys the load-order
    // guarantee when Core is present, which is all that registering with the gate needs.
    [BepInDependency(CoreGuid, BepInDependency.DependencyFlags.SoftDependency)]
    // Hard, and the opposite argument. Vandi is not something Malmr can do without: the rule is
    // that a metal opens once its biome's boss has been beaten at one star, and the only record
    // of who beat what at how many stars is Vandi's. Soft would have left two bad choices when
    // it is missing - every metal with a boss shut forever, which is a mod that loads and does
    // nothing while its log says it is ready, or the boss half waved through, which is the
    // generous version this mod exists not to be. A plugin BepInEx refuses to load, with a line
    // naming the missing dependency, is the honest one of the three. Through a mod manager it
    // is still one install: the manifest lists Vandi, so it comes along.
    [BepInDependency(VandiGuid, BepInDependency.DependencyFlags.HardDependency)]
    public class MalmrPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ezomic.valheim.malmr";
        public const string PluginName = "Malmr";
        public const string PluginVersion = "0.1.0";
        public const string PluginAuthor = "Robbin Thijssen";

        /// <summary>Core's plugin GUID. Optional - see TryRegisterWithCore.</summary>
        private const string CoreGuid = "ezomic.valheim.core";

        /// <summary>Vandi's plugin GUID. Required - see the attribute above and Bosses.</summary>
        private const string VandiGuid = "ezomic.valheim.vandi";

        internal static ManualLogSource Log;

        /// <summary>
        /// Whether Core answered at load. Worth keeping even when nothing reads it yet: the
        /// difference between gated and ungated is invisible to a player otherwise, and this
        /// is what a warning on spawn would be driven by.
        /// </summary>
        internal static bool CorePresent;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            // Config first. Registering absorbs every entry the mod has bound, so anything
            // bound after this line is carried only because Core re-absorbs at manifest
            // time - and depending on the order of two lines in an Awake is not a thing
            // worth relying on.
            MalmrConfig.Bind(Config);

            TryRegisterWithCore();

            // One named type at a time, never the whole assembly, and each on its own so a
            // game update that renames one target costs that feature and not the rest.
            // PatchAll(Type) patches exactly the type it is handed and does not recurse into
            // nested ones, which is why the console hook is named on its own.
            _harmony = new Harmony(PluginGuid);
            Apply("deposit hit", typeof(DamagePatches));
            Apply("deposit message", typeof(ListenPatches));
            Apply("unlock message", typeof(Announce));
            Apply("console command", typeof(DevConsole.Hook));

            // The startup line every mod in the suite writes. It is how a log answers "which
            // build of what is actually loaded" without anyone guessing.
            Log.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " - ready.");
        }

        /// <summary>
        /// Once per world, the table of every deposit and what it counts as. Once a second, a
        /// look at which metals have just opened, for the message - see Opened for why a boss
        /// kill has to be looked for rather than heard. Every frame, the vein mode key, the next
        /// few chunks of any deposit this machine is breaking, and what the bar should say. Each
        /// is a few compares and a return when there is nothing to do.
        ///
        /// Each on its own try, so one throwing every frame costs its own feature and neither
        /// floods the log for the others nor stops them.
        /// </summary>
        private void Update()
        {
            Step("deposit survey", Deposits.SurveyTick);
            Step("unlock message", Opened.Tick);
            Step("vein mode key", VeinMode.Tick);
            Step("deposit break", Collapse.Tick);
            Step("vein bar", Focus.Tick);
        }

        /// <summary>The bar and the marker. IMGUI, so this is the only place they can be drawn.</summary>
        private void OnGUI()
        {
            Step("vein bar drawing", Focus.Draw);
        }

        private static readonly HashSet<string> Failed = new HashSet<string>();

        private static void Step(string what, Action step)
        {
            try
            {
                step();
            }
            catch (Exception error)
            {
                if (!Failed.Add(what)) return;
                Log.LogWarning("The " + what + " failed and will keep failing quietly for the rest "
                    + "of this session. Said once: " + error);
            }
        }

        private void Apply(string what, Type patches)
        {
            try
            {
                _harmony.PatchAll(patches);
            }
            catch (Exception error)
            {
                Log.LogError("Could not apply the " + what + " patches, so that part of Malmr "
                    + "is off for this session. The rest is unaffected. " + error.Message);
            }
        }

        /// <summary>
        /// Joins Core's version gate when Core is installed, and does nothing when it is not.
        ///
        /// Standing alone costs more since the bar than it did before it. Without Core nothing
        /// swaps a host's unlock table into a joining client, so on a shared server the levels
        /// are whatever each player wrote in their own file. And nothing refuses a player who
        /// does not have Malmr at all - whose machine, when it happens to own a deposit, drops
        /// every vein mining blow sent to it (see RegisterWithCore). Both are the server owner's
        /// choice to accept, which is why this logs rather than refusing to run.
        /// </summary>
        private void TryRegisterWithCore()
        {
            CorePresent = Chainloader.PluginInfos.ContainsKey(CoreGuid);

            if (!CorePresent)
            {
                Log.LogInfo("Core not installed - running standalone, without the version gate. "
                    + "In multiplayer every player and the server need Malmr, or vein mining blows "
                    + "on a deposit owned by a machine without it are lost.");
                return;
            }

            RegisterWithCore();
        }

        /// <summary>
        /// Kept separate and never inlined on purpose. The JIT resolves the assemblies a method
        /// needs when it first compiles that method, so a Suite call sitting directly in Awake
        /// would drag Ezomic.Core in before the check above could prevent it - and the
        /// missing-assembly exception would land during plugin load, which is the exact
        /// failure this arrangement exists to avoid. Isolating it means the type is only ever
        /// resolved on a machine that has Core.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RegisterWithCore()
        {
            // Everyone, since the bar - it was HostOnly before it, and the reason it changed is
            // the owner. The bar lives on the deposit's ZDO, and only the deposit's owner may
            // write that, so a vein mining blow is a message to the owner (Malmr_Vein1) and the
            // owner does the counting and the breaking. The owner is whoever the game picked -
            // any player near the rock, or the server - so every one of them must have the
            // handler. A machine without it logs "Failed to find rpc method" and the blow is
            // gone: the swinger paid the stamina and the wear, and neither the chunk nor the bar
            // moved. HostOnly would let exactly those players in.
            //
            // The first mechanic could be HostOnly because it sent nothing vanilla did not
            // already send. A claim of ownership would have kept that - the swinger takes the
            // rock and writes the bar itself - but a claim is not a lock: two players at one rock
            // would each claim, each add to the number they last saw and each write it back, and
            // one of every such pair of blows would vanish. The message is the owner doing its
            // own write, which cannot race.
            //
            // It costs what Everyone always costs: a player without Malmr is refused at a Core
            // server that runs it, and the server needs it too. Vandi already asks the same of
            // everybody, and Malmr brings Vandi, so on a server that runs both nothing new is
            // asked of anyone.
            Suite.Register(PluginGuid, PluginName, PluginVersion, Config, Requirement.Everyone);

            // Registering already absorbs the whole config file, so naming these is a formality.
            // It is worth writing anyway: these are the mod's balance, and saying out loud that
            // the host owns them is the point of putting Malmr on a server.
            Suite.Sync(MalmrConfig.Enabled, MalmrConfig.Unlocks, MalmrConfig.Deposits,
                       MalmrConfig.Bosses, MalmrConfig.BossKills);

            // The key, the message and the logging are the player's own. A KeyCode is exempt
            // from Core's sync anyway; saying so here keeps the three personal settings in one
            // line. A host reaching across to rebind somebody's key or turn their logging on is
            // not a thing anybody asked for.
            Suite.Local(MalmrConfig.VeinToggleKey, MalmrConfig.AnnounceUnlocks, MalmrConfig.Verbose);
        }

        private void OnDestroy()
        {
            // UnpatchSelf, never UnpatchAll(). The argumentless one unpatches every mod in
            // the process, not just this one.
            if (_harmony != null) _harmony.UnpatchSelf();
        }
    }
}
