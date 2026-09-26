using System.Runtime.CompilerServices;

namespace Malmr
{
    /// <summary>
    /// The only file in Malmr that names a Vandi type, and it is shaped that way on purpose.
    ///
    /// Vandi is a soft dependency since Robbin's call of 2026-09-26, so Vandi.dll may simply not
    /// be there. The runtime does not look for an assembly when Malmr loads. It looks when the
    /// JIT first compiles a method whose body names something in it, and if the file is missing
    /// that compile throws FileNotFoundException or TypeLoadException - at whatever call site
    /// happened to reach the method first, which in the classic version of this trap is the
    /// plugin's Awake or a Harmony patch, and the whole mod goes with it.
    ///
    /// So three rules, each closing one road to that compile:
    ///
    ///  - Every VandiApi call sits in a method of its own here, and nowhere else. Bosses calls
    ///    in only after the plugin found Vandi in BepInEx's list of loaded plugins, so on a
    ///    machine without Vandi these bodies are never compiled at all.
    ///  - NoInlining on each. An inlined body is compiled as part of its caller, which would
    ///    move the Vandi reference into Bosses and make compiling Bosses the thing that throws.
    ///  - Nothing but strings, ints and bools in the signatures, and no fields. Loading this
    ///    class, which happens the first time anything names it, only resolves what its
    ///    signatures use. A mod that walks every type in every assembly - a config manager
    ///    does - therefore loads this class safely too.
    ///
    /// The caller wraps each call in a try anyway. The build compiles against one Vandi, and a
    /// player might run another whose VandiApi lacks a method; that throws MissingMethodException
    /// here, and it must cost the boss half, never the class.
    /// </summary>
    internal static class VandiBridge
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int LocalBossKills(string bossKey)
        {
            return Vandi.VandiApi.LocalBossKills(bossKey);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool CountsKillsOf(string bossKey)
        {
            return Vandi.VandiApi.CountsKillsOf(bossKey);
        }
    }
}
