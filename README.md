# FocusBalance (GameOptimizer)

> **A tiny, honest gaming helper for Windows 11.** It does two things — and deliberately *nothing* else.

**FocusBalance** temporarily lowers the priority of CPU‑hungry **background** processes while you game, so your
6‑core CPU spends its time on the game instead of on updaters, indexers, antivirus scans, and chatty
background apps. It also has a one‑click **DPC/ISR latency check** (LatencyMon‑style) to tell you whether a
*driver* is the source of your stutter.

It is built on the opposite philosophy of most "optimizer" tools: instead of piling on dozens of tweaks that
mostly do nothing, every idea here was researched and **most were rejected as placebo, harmful, or already
covered by Windows**. What survived is small, safe, and reversible.

**What it does NOT do (by design):**
- ❌ Never touches your **game** or anything you've used — any process that has been in the foreground is
  permanently left alone, so it **never opens a modify handle to a game** (nothing for an anti‑cheat to flag).
  Anti‑cheat services (Vanguard, EAC, BattlEye, ACE, GameGuard, FACEIT) are on a hard never‑touch list.
- ❌ Only ever **lowers** background priority, never kills a process, never raises priority.
- ❌ No registry/timer/power "tweaks", no debloating, no telemetry, **no network access** — it's all local.
- ❌ It does **not** magically add FPS. It reduces CPU contention. If nothing in the background misbehaves,
  it does nothing — and that is correct.

### Requirements
- Windows 11 (x64), .NET 8 Desktop Runtime, run **as administrator** (needed to lower other processes' priority).

### How to use it (important — one session per game)
FocusBalance is meant to run for **one gaming session at a time**:

1. **Launch FocusBalance first**, *before* you start your game (Run as administrator).
2. **Start your game.** FocusBalance is already active and will suppress background hogs. The game itself is
   never touched.
3. **When you finish, close the game and close FocusBalance** — closing its window stops it and restores every
   process's original priority.
4. **For a different game, start fresh:** close FocusBalance and launch it again *before* starting the next
   game. Run **one FocusBalance session per game** — don't keep a single instance running across several
   different games. (Launch FB → game 1 → close both; launch FB → game 2 → close both, and so on.)

While it runs you can watch the **Action log** (what got restrained / restored), the **Currently restrained**
list, and tune the threshold (default **8 %** of total CPU ≈ one full thread on a 12‑thread CPU) and strength
(BelowNormal / Idle). Press **Measure DPC latency** any time for a read‑only driver‑latency report.

### ⚠️ Disclaimer
This software is provided **"AS IS", with no warranty and no liability** (see [LICENSE](LICENSE)). It runs with
administrator rights and changes process priorities on your system. **You use it at your own risk.** While it is
designed to never touch games or anti‑cheat processes, the author is **not responsible** for any anti‑cheat
action, data loss, instability, or other damage. If you play competitive games with aggressive anti‑cheat and
have any doubt, don't run it.

---

(아래는 한국어 개발 문서 / Korean development notes below.)

# GameOptimizer (한국어)

근거 기반 Windows 11 게임 최적화 모음. **효과를 *주장*하지 않고 사용자가 *측정·되돌리기*** 할 수 있게 만드는 것이 원칙입니다. 과장·플라시보 기능은 의도적으로 넣지 않습니다.

대상 환경: Ryzen 9600X (single-CCD) + RTX 4070, Windows 11.

리서치 근거: [research/windows11-gaming-optimization-evidence.md](research/windows11-gaming-optimization-evidence.md)
— 23개 소스 → 82개 주장 → 25개 적대적 검증(2/3 반박 시 폐기)로 만든 "뭐가 진짜 효과 있나" 분류표.

---

## 모듈

### FocusBalance — 백그라운드 격리 (ProBalance식) ✅ MVP 완성
`src/focusbalance/`

게임(포그라운드 앱)을 쓰는 동안 **CPU를 많이 먹는 다른(백그라운드) 프로세스의 우선순위를 일시적으로 낮춰** 경합을 줄입니다. 진정되거나, 포그라운드로 오거나, 종료되거나, 도구를 끄면 **원래 우선순위로 자동 복원**합니다.

**왜 이걸 먼저?** 리서치 결론: single-CCD 9600X에서는 수동 코어 친화도 고정보다 백그라운드 격리가 실효적. (멀티-CCD가 아니라 코어 간 이동 페널티가 없음 → 친화도 이득이 작음.)

**안전 설계 (Rynez 철학 그대로)**
- 우선순위를 **낮추기만** 함. 절대 프로세스를 죽이거나, 원래보다 높이지 않음.
- 변경 전 원래 값을 저장 → **잠잠해지거나/포그라운드 복귀/프로세스 종료/앱 닫을 때 전량 복원** (스모크 테스트로 검증).
- **보호 목록**: 시스템/세션/셸(Explorer)/보안(Defender)/오디오 프로세스 + 현재 게임 + 자기 자신은 절대 안 건드림.
- 모든 동작을 로그로 기록 (`logs/focusbalance-YYYYMMDD.log`).

**정직한 한계**: 이건 경합을 줄이는 것이지 **FPS를 마법처럼 더하지 않습니다.** 백그라운드에 말썽 부리는 프로세스가 없으면 아무것도 안 하며, 그게 정상입니다.

#### 빌드 & 실행
```powershell
cd src\focusbalance
dotnet build -c Release
# 산출물: src\focusbalance\bin\Release\net8.0-windows\focusbalance.exe
# 우클릭 → "관리자 권한으로 실행" (다른 프로세스 우선순위 변경에 관리자 필요)
```
**실행 = 자동 활성** (CPU 감지·억제 즉시 시작). 토글 버튼·트레이 없음 — 게임 세션 동안 켜두고 끝나면 **창을 닫으면(X) 정지 + 전량 복원 + 종료**.

**게임/안티치트는 아예 안 건드림 (안티치트 안전)**: 한 번이라도 포그라운드였던 프로세스(=게임·당신이 쓴 앱)는 **영구히 우선순위 변경 시도 안 함** → 게임 프로세스에 modify 핸들을 절대 안 엶(안티치트가 탐지하는 행위 자체를 안 함). 안티치트 서비스(Vanguard/EAC/BattlEye/ACE 등)도 보호목록으로 제외. 게임은 어차피 포그라운드라 이미 CPU를 받으므로 손댈 필요 없음 — **진짜 백그라운드만 억제.**

**DPC 측정 버튼 내장**: 창의 `Measure DPC latency`(15/30/60s) 버튼이 DPC Latency Monitor 엔진을 그대로 호출해 측정 후 결과 표를 팝업으로 띄움(읽기 전용, 백그라운드 억제는 계속 동작). 같은 엔진을 `src/dpclatency` 콘솔판과 **소스 공유**.

#### 설정 (창에서 실시간 변경)
- **Lower to**: `BelowNormal` (부드러움, 기본) / `Idle` (가장 강함)
- **Restrain above (% CPU)**: 백그라운드 프로세스를 격리하기 시작하는 총 CPU 사용률 임계값 (기본 8%)

#### DPC 측정 (FocusBalance 창의 `Measure DPC latency` 버튼)
NT 커널 ETW 세션을 열어 **드라이버별 DPC/ISR 실행 시간**을 측정합니다(LatencyMon식). DPC/ISR이 오래 걸리는 드라이버는 FPS 카운터로 안 잡히는 **스터터·오디오 끊김의 전형적 소프트웨어 원인**. **읽기 전용** — 시스템 변경 없음.
- 리서치에서 "진단용 PROVEN". 핵심 가치는 stall 측정이 아니라 **"어느 드라이버가 범인인지" 귀속** (Microsoft TraceEvent 사용).
- 버튼 → 15/30/60s 선택 → 측정 후 결과 팝업(드라이버별 DPC 횟수/최대/평균 µs, ISR, 최악 스파이크 + 해석). 측정 중 **실제로 스터터 나는 작업**을 하세요.
- 판정은 절대값이 아니라 **반복되는 긴 DPC(>0.5/1ms 횟수)** 기준 — 단발 스파이크 오탐 방지. (엔진: `DpcLatencyMonitor.cs`)

> **폐기됨 — Defender 폴더 자동 예외:** 만들었다가 제거. 자동 추가가 게임 도중(60초 시점) PowerShell+Defender 재설정을 실행해 **오히려 일회성 hitch**를 유발했고(사용자 체감 "더 끊김"), 애초에 Defender는 검증된 병목이 아니었음(실효는 백그라운드 억제). 효과 미검증 + 부작용 + 보안 트레이드오프 → 폐기. 게임 폴더 예외가 필요하면 Windows 보안에서 수동으로.

> **만들지 않음 — "선호 코어 끄고 균등 분배":** 요청받았으나 거절. AMD 선호 코어는 부스트용 설계라, 균등 분배하면 스레드 마이그레이션↑ → 캐시 thrash → 게임 끊김 **악화** 가능. 깔끔한 SW 토글도 없음(BIOS `CPPC Preferred Cores`뿐). 단일 CCD 9600X에선 이득 없고 손해만. 폐기한 트윅과 동류.

> **폐기된 모듈 — Game Tweaks (HAGS/FSO/전원계획 토글):** 만들었다가 제거함. 이유: (1) 전부 Windows에 이미 있는 토글의 복제일 뿐 새로 *잡아내는* 게 없고, (2) 토글 자체가 리서치상 거의 플라시보/상황부 — 특히 전원계획은 현대 Ryzen+Win11에서 Balanced≈High perf로 **오차 범위**. 효과를 측정 안 하고 켜고 끄는 건 "플라시보 최적화 툴" 행동이라 [완성도 위해 만들지 말 것] 원칙에 따라 폐기.

---

## 검증
```powershell
cd test\engine-smoke
dotnet run -c Release
# 자기 소유 부하 프로세스로 탐지→격리(Idle)→완전 복원(Normal)을 확인. 비관리자로 실행 가능.
```
결과: `RESULT: PASS (detect → restrain → full restore)`

---

## 로드맵 (리서치 기반, 만들 가치가 있는 것만)
- **A/B 성능 측정** (토글 전/후 FPS·1% low·프레임타임 실측) — "주장"을 "측정"으로 바꾸는 마지막 조각. Game Tweaks와 짝.

**만들지 않을 것** (리서치: 플라시보/유해): 타이머 해상도 조작, 레지스트리 클리너, 공격적 디블로터, standby 메모리 강제 flush.

---

## 폴더 구조
```
GameOptimizer/
├─ README.md
├─ research/      리서치 리포트 (근거)
├─ src/
│  └─ focusbalance/   단일 앱: 백그라운드 억제 + DPC 측정 버튼 (.NET 8 WinForms)
└─ test/
   └─ engine-smoke/   ProBalance 엔진 스모크 테스트
```

단일 앱으로 통합됨 (별도 dpclatency 콘솔·Game Tweaks는 제거). 실행=활성, 창 닫으면 정지+전량 복원.
