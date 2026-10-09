using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using HarmonyLib;
using StardewValley;

namespace TimeEase;

internal sealed class SaveObserver
{
    // Inspected macOS 1.6.15 build 24354: the inner state machine's terminal
    // 100 follows both final file replacements. Unknown builds stay reminders-only.
    private const string InspectedBody = "B2498A08102EB7ACF9C7AE37B50300296B081EB8AC47EAE612253A249C70B8C4";
    private static SaveObserver? instance;
    private readonly string owner;
    private readonly Action<string> unsupported;
    private MethodInfo? factory;
    private MethodInfo? moveNext;
    private SaveEvidence? active;
    private int steps;
    private string? observationError;
    internal bool Supported { get; private set; }
    internal int ObservedSteps => Volatile.Read(ref steps);

    internal SaveObserver(string owner, Action<string> unsupported)
    { this.owner = owner; this.unsupported = unsupported; }

    internal void Install()
    {
        try
        {
            factory = typeof(SaveGame).GetMethod(nameof(SaveGame.getSaveEnumerator), Type.EmptyTypes)
                ?? throw new MissingMethodException("缺少原生保存枚举器。");
            moveNext = factory.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType
                .GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingMethodException("无法识别原生保存状态机。");
            byte[] body = moveNext.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
            if (Game1.version != "1.6.15" || Convert.ToHexString(SHA256.HashData(body)) != InspectedBody)
                throw new NotSupportedException("当前游戏保存实现未核实。");
            if (HasForeignPatch()) throw new NotSupportedException("其他 Mod 修改了保存枚举器。");
            instance = this;
            // Observe the method which actually writes and advances. An iterator
            // factory can be inlined into an already compiled native caller, so
            // patching only getSaveEnumerator can miss the entire save.
            new Harmony(owner).Patch(moveNext,
                prefix: new HarmonyMethod(typeof(SaveObserver), nameof(BeforeStep)) { priority = Priority.First },
                postfix: new HarmonyMethod(typeof(SaveObserver), nameof(AfterStep)) { priority = Priority.Last },
                finalizer: new HarmonyMethod(typeof(SaveObserver), nameof(StepFailed)));
            Supported = true;
        }
        catch (Exception error)
        {
            new Harmony(owner).UnpatchAll(owner);
            Reject(error.Message);
        }
    }

    // Called only by the main-screen SMAPI before-save event, before native Save
    // starts. Worker callbacks receive this token without reading Game1 or UI.
    internal void Begin(SaveEvidence evidence)
    {
        Interlocked.Exchange(ref active, evidence);
        Interlocked.Exchange(ref steps, 0);
        Interlocked.Exchange(ref observationError, null);
    }

    internal bool Check()
    {
        try
        {
            string? error = Volatile.Read(ref observationError);
            if (Supported && error != null) Reject(error);
            else if (Supported && HasForeignPatch()) Reject("其他 Mod 修改了保存枚举器，无法确认本次保存证据。");
        }
        catch (Exception error) { Reject(error.Message); }
        return Supported;
    }
    private bool HasForeignPatch() => new[] { factory, moveNext }.Where(m => m != null)
        .Any(m => Harmony.GetPatchInfo(m!)?.Owners.Any(id => id != owner) == true);
    private void Reject(string reason) { Supported = false; unsupported(reason); }

    private static void BeforeStep(object __instance, out SaveEvidence? __state)
    {
        __state = null;
        var observer = instance;
        var evidence = observer == null ? null : Volatile.Read(ref observer.active);
        if (observer == null || !observer.Supported || evidence == null) return;
        // Refuse to attach a new attempt to an already advancing old iterator.
        // The inspected generated iterator starts with Current == 0.
        if (!evidence.Observed && ((IEnumerator<int>)__instance).Current != 0) return;
        if (!evidence.Observe(__instance))
        {
            if (evidence.SourceConflict)
                Interlocked.CompareExchange(ref observer.observationError, "同一保存尝试出现多个原生保存状态机，本次仅提醒。", null);
            return;
        }
        Interlocked.Increment(ref observer.steps);
        __state = evidence;
    }

    private static void AfterStep(object __instance, bool __result, SaveEvidence? __state)
    {
        if (__state == null) return;
        // __instance is the exact, fingerprint-checked native inner iterator.
        // Never use the outer Save iterator's 100 or an event as disk evidence.
        if (__result && ((IEnumerator<int>)__instance).Current == 100) __state.Complete();
        else if (!__result && !__state.Succeeded) __state.Fail();
    }

    private static void StepFailed(Exception? __exception, SaveEvidence? __state)
    {
        if (__exception != null) __state?.Fail();
        // A void finalizer preserves the original exception and native recovery.
    }
}
