# Evidence-Based, Gap-Filling Software Ideas for Reducing In-Game Stutter / 1% Lows
## Target: Ryzen 9600X (6-core single-CCD) + RTX 4070, Windows 11, user/admin-mode, reversible

Date: 2026-06-21
Method: 21 claims survived 3-vote adversarial verification; merged and synthesized below.

---

## Executive summary

After honest scrutiny, most of the "obvious" candidates are either (a) impossible to replicate from a
user/admin-mode tool because the real mechanism is engine-side, or (b) already covered by Windows
built-ins or mature free tools. NVIDIA Reflex helps latency a lot — but only when GPU-bound, only via
in-game SDK integration, and it targets latency, not stutter/1% lows. DX12 shader pre-compilation is
genuinely the right problem, but the platform answer (Microsoft Advanced Shader Delivery / State Object
Database) is developer- and store-gated with no external-tool path. CPU affinity has no proven benefit
on a single-CCD 9600X and fights the scheduler. MMCSS tuning only affects threads the game itself
registered. The single most defensible NEW direction is in MEASUREMENT: animation error (sim-vs-display
pacing mismatch) is a real, formula-defined gap that classic frametime/1%-low metrics miss — though
Intel PresentMon 2.0 is already closing it.

---

## Finding 1 — NVIDIA Reflex: large latency win, but GPU-bound-only, engine-gated, NOT a stutter fix
Confidence: HIGH (multiple primary vendor + independent hardware-sensor sources, unanimous votes)

Mechanism (claims 5, 6): Reflex Low Latency Mode aligns game-engine work to complete just-in-time for
rendering, eliminating the GPU render queue and reducing CPU back pressure in GPU-bound scenes. This is
a render-queue/back-pressure mechanism, NOT a frame-pacing or stutter mechanism — NVIDIA's own docs
never mention stutter, and independent testing shows it does not reliably improve 1% lows (and can
worsen frametime consistency in some configs, e.g. with frame generation or Reflex+VSync).

Evidence (claims 0, 1): TechPowerUp LDAT v2 hardware mouse-to-photon review — Overwatch Epic/200% res
(GPU-bound, ~60 FPS): 82.3 ms -> 35.0 ms (>57% reduction). Overwatch High (256 FPS, 210 W): 25.7 ms ->
16.8 ms at no FPS or GPU-power cost. But at Low settings (CPU-bound, 400 FPS cap) latency stayed ~13.3 ms
whether Off/On/On+Boost — "if you don't find yourself in a GPU-bound scenario, NVIDIA Reflex Low Latency
Mode serves no purpose."

Buildability (claims 4): NEGATIVE for a user-mode tool. The core benefit requires in-game SDK
integration; driver-only Ultra Low Latency Mode is explicitly weaker because it cannot remove CPU-side
back pressure. RTSS/DXVK-NVAPI can inject Reflex markers, but that is a mature third-party tool (already
covered) and injection cannot fully manage the render queue without engine cooperation.

Verdict: Real and well-measured, but out of scope twice over — it targets latency not stutter, and it is
not buildable as a novel user/admin tool. Do not build.

Sources:
- https://www.techpowerup.com/review/nvidia-reflex-review-test-ldat-v2/3.html (primary, hardware sensor)
- https://www.nvidia.com/en-us/geforce/news/reflex-low-latency-platform/ (primary vendor)
- https://developer.nvidia.com/performance-rendering-tools/reflex (primary vendor)

---

## Finding 2 — Frame pacing / latent sync is tunable, but already fully covered by Special K & RTSS
Confidence: HIGH (Blur Busters primary + independent coverage, unanimous votes)

Mechanism (claims 2, 3): Special K's Latent Sync is an open-source frame limiter for VSYNC-off rendering
that steers the tearline off-screen ("lagless VSYNC") across D3D9/11/12 and OpenGL — a complete
replacement for RTSS Scanline Sync. It is a two-phase limiter (wait before and after Present) that
distributes idle time between waiting for the vertical-blank (pacing stability) and delaying next-frame
start (input latency). It exposes a "Bias Pre/Post-Sync Delays" control plus optional Adaptive Tearing,
making frame pacing a tunable parameter rather than fixed.

Buildability/gap: This is exactly the frame-pacing/latent-sync idea — and it already exists, open source
and actively maintained. Rebuilding it would duplicate Special K and RTSS (both ruled out as "mature
tools already covered"). Caveats: cannot hold the tearline in some titles (e.g. Elden Ring), excluded
under DWM-composed windowed paths, no Vulkan support as of its 2021 release.

Verdict: Legitimate technique, zero real gap. Do not build; recommend Special K / RTSS instead.

Sources:
- https://forums.blurbusters.com/viewtopic.php?t=9375 (primary, dev announcement + Blur Busters)

---

## Finding 3 — DX12 shader pre-compilation is the right problem, but the platform path is dev/store-gated
Confidence: HIGH (multiple primary Microsoft specs + blog + independent hardware testing, unanimous)

Mechanism (claims 7, 8, 9, 10): Microsoft's Advanced Shader Delivery (GDC 2026) defines a State Object
Database (SODB) — a SQLite, version-2 schema recording the app's State Objects, PSOs, and DXIL shader
bytecode — used to build a precompiled cache (PSDB) with a runtime fallback when an object is missing.
Microsoft's cloud compiles the SODB per driver+GPU config; players then download fully compiled shaders
for their specific hardware instead of compiling at runtime, eliminating the classic DX12 first-run
compile stutter.

Evidence of impact: Tom's Hardware tested on RX 9070 XT across six games — up to 95% load-time
improvement and 33% faster 1% lows.

Buildability (claims 8, 9): NEGATIVE for a third-party tool. Storage/lookup is driven by app-side API
calls (StorePipelineStateDesc / FindPipelineStateDesc) with opaque app-defined keys — the runtime
mechanism is integrated into the game. The delivery path requires the DEVELOPER to integrate SODB
collection and submit through Xbox Partner Center; the registration API is installer/store-gated (Xbox
Store, Steam), and the PSDB compiler runs in Microsoft's cloud. There is no automatic OS-level or
external-tool path to pre-warm DX12 shaders. A "Non-Title Cooperative" gameplay-capture path exists but
still requires actual gameplay capture and store-side registration.

Risk: Even a clever third-party precompiler would be on a collision course with this shipping Microsoft
platform feature (Agility SDK 1.619).

Verdict: Correct target, but not buildable as a user/admin tool, and about to be solved at the platform
level. Do not build a shader precompiler.

Sources:
- https://microsoft.github.io/DirectX-Specs/d3d/StateObjectDatabase.html (primary spec)
- https://devblogs.microsoft.com/directx/advanced-shader-delivery-whats-new-at-gdc-2026/ (primary)
- https://microsoft.github.io/DirectX-Specs/d3d/ShaderCacheRegistrationAPI.html (primary spec)

---

## Finding 4 — MMCSS / scheduling-category tuning: real OS feature, but opt-in by the game only
Confidence: HIGH (Microsoft primary docs, unanimous votes)

Mechanism (claims 13, 14, 15): MMCSS is a built-in Windows service giving time-sensitive threads
prioritized CPU access. Apps opt in via AvSetMmThreadCharacteristics / AvSetMmMaxThreadCharacteristics
with a task profile. Windows ships a dedicated "Games" profile (one of seven) with per-task registry
params: Affinity, Background Only, BackgroundPriority, GPU Priority, Priority, Scheduling Category, SFIO
Priority (Games defaults: GPU Priority=8, Priority=6, Scheduling Category=High, SFIO=High).

Buildability/gap (claim 15): Limited. The priority boost only applies to threads the GAME ITSELF
registered with MMCSS, driven by the registered task's category/base priority/foreground status. An
external tool flipping the registry "Games" profile only affects games that already register under that
task — it cannot make an arbitrary unregistered process receive boosting. So a tool could edit the Games
profile defaults, but the effect is bounded to participating titles and is untested for 1%-low impact.

Verdict: Low-leverage. Editing the shared Games profile is reversible but speculative and only helps
MMCSS-registering games. Marginal at best; treat as low priority if attempted at all.

Sources:
- https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/ProcThread/multimedia-class-scheduler-service.md (primary)

---

## Finding 5 — Per-game CPU affinity on a single-CCD 9600X: no proven benefit, fights the scheduler
Confidence: HIGH (Intel primary developer guidance + corroboration; one 2-1 vote)

Mechanism/evidence (claims 16, 17): Intel's gaming-threading guidance: manually pinning threads on a
modern PC fights both the OS and hardware scheduler; developers should give the OS workload context and
let it place threads. Hard affinity should be used ONLY when work must never migrate off a core set, and
any such assumption must be benchmarked across SKUs — i.e. not a safe blanket optimization. Cases where
affinity DOES help are confined to HETEROGENEOUS topologies (dual-CCD Ryzen, X3D vs non-X3D CCDs, Intel
P/E split). For a single-CCD, homogeneous 6-core 9600X, benchmarks show no meaningful difference from
assigning affinities, and manual affinity is less necessary.

Buildability/gap: Process Lasso already exposes affinity (claim 12), and its docs offer no empirical
gaming benefit — only a caution that some games are sensitive to affinity changes. No gap, and the
target hardware is exactly the case where affinity is least useful.

Verdict: Likely within margin of error / counterproductive on this CPU. Do not build per-game affinity
automation. Skeptic flag: claims of affinity FPS gains almost always come from dual-CCD/X3D systems and
do not transfer to the 9600X.

Sources:
- https://www.intel.com/content/www/us/en/developer/articles/technical/optimizing-threading-for-gaming-performance.html (primary)
- https://bitsum.com/processlasso-docs/ (primary, for existing-coverage / no-benefit-claim)

---

## Finding 6 — ProBalance-style suppression: already built, and even the vendor publishes no frametime data
Confidence: HIGH (Bitsum primary docs, unanimous)

(Claim 11) Process Lasso's ProBalance suppresses background CPU contention to maintain responsiveness,
but Bitsum's own docs provide NO game-specific frametime / FPS / 1%-low / stutter benchmarks; one Bitsum
page concedes the benefit "can't be easily quantified in simple FPS benchmarks." This corresponds to a
feature the user has ALREADY built. Useful as a calibration point: even the category leader cannot
quantify the win, so set honest expectations for the existing background-suppression feature.

Sources: https://bitsum.com/processlasso-docs/ (primary)

---

## Finding 7 — THE REAL GAP: animation-error measurement (sim-vs-display pacing mismatch)
Confidence: HIGH (GamersNexus white paper + Intel PresentMon corroboration, unanimous)

Mechanism (claims 18, 19, 20): Standard metrics (avg FPS, 1%/0.1% lows, MsBetweenPresents) measure
DISPLAY smoothness but miss ANIMATION ERROR — frames can be displayed at perfectly even frametimes yet
depict jerky/wrong movement because game-state pacing is mismatched from display pacing. Animation error
is a distinct, formula-defined metric: (AnimationTime_N - AnimationTime_N-1) - MsBetweenDisplayChange_N,
where magnitude (distance from zero, positive=too soon, negative=too late) indicates stutter severity.
GamersNexus (with Intel's Tom Petersen) frames it as explaining WHY stutter feels bad, not just that it
exists — it complements rather than replaces frametime tools, i.e. a genuine measurement gap.

Buildability/gap: This is the ONE area with a real, evidence-backed gap relative to classic
present-timing tools. CAVEAT: Intel's open-source PresentMon 2.0 already exposes "Simulation Time Error /
AnimationError," so the gap is partially closed by a free tool, and producing the metric requires access
to the game's AnimationTime/simulation state (PresentMon estimates this; a from-scratch tool would need
to hook sim state, which is hard). The defensible product is not re-deriving the metric but SURFACING it
usefully: a focused 9600X+4070 overlay/logger that reads PresentMon 2.0's animation-error stream and
correlates spikes with the user's already-built DPC/ISR ETW latency data and background-contention
events — turning "why did it feel bad here" into an actionable, per-cause timeline.

Verdict: Highest rank by (evidence x real gap x buildability). Build a MEASUREMENT/correlation layer on
top of PresentMon 2.0 animation error + existing ETW DPC data; do NOT try to invent a new sensor.

Sources:
- https://gamersnexus.net/gpus-gn-extras-cpus/problem-gpu-benchmarks-reality-vs-numbers-animation-error-methodology-white (primary white paper)
- Intel PresentMon 2.0 (GameTechDev) — corroborating implementation

---

## Overall ranking (evidence x real gap x buildability)

1. Animation-error correlation/overlay on PresentMon 2.0 + existing ETW DPC data (Finding 7) — only real
   gap with credible evidence; buildable as a read-only measurement layer.
2. (Everything else is do-not-build.) Reflex (engine-gated, latency not stutter), Latent Sync/frame
   pacing (already in Special K/RTSS), DX12 shader precompile (dev/store-gated, platform feature
   incoming), MMCSS tuning (game-opt-in only, marginal), CPU affinity (no benefit on single-CCD, fights
   scheduler).

## Refuted / weak claims noted for transparency
- Reflex beating a manual FPS cap for latency (1-2). 
- Reflex injectable by an arbitrary external tool (0-3) — it is not.
- Cyberpunk Reflex+VSync+GSync lowest latency at frametime-consistency cost (1-2).
- Latent Sync near-best consistency with small latency penalty as general proof (1-2).
