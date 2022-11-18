using Verse;

namespace HolyWasher;

// ReSharper disable once ClassNeverInstantiated.Global
public class HolyWasherMod : Mod
{
    public HolyWasherMod(ModContentPack content) : base(content)
    {
        Log.Message("[HolyWasher]: loaded");
    }
    
    internal static JobDef HolyWash => DefDatabase<JobDef>.GetNamed("HolyWash");
}