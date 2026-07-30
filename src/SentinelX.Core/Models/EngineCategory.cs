namespace SentinelX.Core.Models;

/// <summary>
/// The subsystem categories Sentinel X phase 1 covers. Only a subset of the full product-spec
/// category list (hardware, drivers, security, ETW/minidump, etc.) is represented here — this
/// phase is scoped to the streaming/studio-facing modules. Scoring renormalizes over whatever
/// is currently registered, so more categories can be added later without changing the scoring
/// pipeline (same pattern used by the sibling CrashSight project).
/// </summary>
public enum EngineCategory
{
    Obs,
    SplitCam,
    StreamValidation
}
