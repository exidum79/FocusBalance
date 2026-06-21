# Windows 11 게임 최적화 — 근거 기반 분류 리포트

> 대상: Ryzen 9600X / single-CCD 데스크톱, 범용 게이밍
> 방법: 다중 소스 웹검색 → 23개 소스 fetch → 82개 주장 추출 → 25개 적대적 검증(3표제, 2/3 반박 시 폐기) → 20개 확정 / 5개 폐기
> 작성일: 2026-06-21
> 원칙: 정직한 한계 + 되돌리기 가능성. 효과 과장 금지.

---

## TL;DR — 분류표

| 항목 | 분류 | 한 줄 요약 |
|---|---|---|
| **DPC/ISR 레이턴시 진단 (LatencyMon)** | ✅ 진단용 PROVEN | FPS를 올리진 않음. 끊김(스터터)의 원인을 *찾는* 도구. |
| **MMCSS (멀티미디어 스케줄러)** | 🟡 SITUATIONAL | 레이턴시/우선순위엔 효과, FPS엔 거의 무관. |
| **CPU 친화도/우선순위 (Process Lasso)** | 🟡 SITUATIONAL | single-CCD 9600X에선 효과 제한적. 백그라운드 격리(ProBalance)가 핵심. |
| **AMD CPPC2 preferred-core** | ✅ PROVEN (드라이버가 담당) | Process Lasso가 아니라 **AMD 칩셋 드라이버**가 처리. |
| **고성능 전원 계획 / C-state / 코어파킹** | 🟡 SITUATIONAL | AMD는 권장하지만 "FPS 증명"이 아니라 최대성능용. |
| **HAGS (하드웨어 GPU 스케줄링)** | 🟡 SITUATIONAL | 게임별로 이득/손해 갈림. 범용 부스트 아님. |
| **전체화면 최적화(FSO) / 테두리없음 vs 독점전체화면** | 🟡 거의 PLACEBO(FPS) | MS 텔레메트리: 평균적으로 FSE와 동등 이상. 차이는 FPS가 아니라 레이턴시. |
| **타이머 해상도 조작 (0.5ms 등)** | ❌ OBSOLETE/HARMFUL | Win10 2004부터 per-process화. 깊은 C-state 차단 → 전력 10~25%↑. |
| **레지스트리 클리너** | ❌ PLACEBO | MS 공식 미지원. 성능 향상 근거 없음. |
| **공격적 디블로터 / 대량 텔레메트리 차단** | ❌ HARMFUL | Windows Update·Defender 파손. FPS 이득은 저사양에서만. |
| **시작프로그램 수동 정리** | ✅ 안전 | 디블로터 대신 권장되는 안전한 방법. |

범례: ✅ 효과/안전 입증 · 🟡 상황부(조건부) · ❌ 플라시보 또는 유해

---

## 항목별 상세

### 1. DPC/ISR 레이턴시 진단 — ✅ 진단용 PROVEN (검증 3-0)
- DPC/ISR 루틴은 높은 IRQL에서 실행되어 선점(preempt) 불가. MS 가이드라인은 **DPC < 100µs**.
- **LatencyMon**이 표준 진단 도구. 스터터/오디오 끊김의 원인 드라이버를 찾는 데 사용.
- 핵심: 이건 **FPS를 올리는 게 아니라**, 무엇이 프레임을 망치는지 *측정*하는 도구.
- 출처: [MS MMCSS docs](https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service), [LatencyMon](https://www.resplendence.com/latencymon)

### 2. MMCSS — 🟡 SITUATIONAL (검증 3-0)
- 멀티미디어/포그라운드 스레드 우선순위를 부스트 (High 23–26, Medium 16–22, Low 8–15).
- `SystemResponsiveness` 레지스트리 값으로 CPU 예약 (20 = 백그라운드에 20% 예약, 100 = 비활성).
- 효과는 **레이턴시/응답성**이지 FPS가 아님.
- 출처: [MS MMCSS docs](https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service)

### 3. CPU 친화도/우선순위 + AMD CPPC2 — ✅/🟡 (검증 2-1 / 3-0)
- **AMD CPPC2 preferred-core** 스케줄링은 **AMD 칩셋 드라이버 3.10.08.506+** (Win11 22000.189+)가 복원/관리. Process Lasso 같은 서드파티가 하는 일이 아님. UEFI에서 CPPC2가 실패할 수 있어 드라이버가 보정.
- 9600X는 **단일 CCD**라 코어 간 이동(cross-CCD latency) 문제가 없음 → 수동 친화도 고정의 이득이 멀티-CCD(7950X 등)보다 작음.
- 실질 가치는 친화도 고정보다 **ProBalance식 백그라운드 프로세스 디프라이오리타이즈**(포그라운드 게임이 백그라운드에 밀리지 않게).
- 출처: [AMD PA-400](https://www.amd.com/en/resources/support-articles/faqs/PA-400.html), [Bitsum ProBalance](https://bitsum.com/how-probalance-works/), [Bitsum: when affinity matters](https://bitsum.com/tips-and-tweaks/when-cpu-affinity-matters/)

### 4. 전원 계획 / C-state / 코어파킹 — 🟡 SITUATIONAL (검증 3-0)
- AMD는 Ryzen Master 가이드에서 '고성능' 계획을 권장 — 단 이건 **최대 성능/OC용 권장**이지 "FPS가 오른다"는 벤치 증명이 아님.
- 코어파킹/깊은 C-state를 끄면 부스트 반응성↑ 가능하지만 전력·발열 trade-off.
- 출처: [AMD Ryzen Master 가이드 68886](https://docs.amd.com/r/en-US/68886-ryzen-master-user-guide/Recommended-Power-Settings)

### 5. HAGS (Hardware-Accelerated GPU Scheduling) — 🟡 SITUATIONAL (검증 3-0)
- **범용 부스트가 아님.** 게임/드라이버별로 결과가 갈림:
  - 이득: Ghostwire Tokyo (DX12), Metro Exodus (DXR), Wolfenstein Youngblood (RT)
  - 손해(regression): Ghostwire raw-FPS, LEGO Builder's Journey, Quake 2 RTX
- MS 설명: 버퍼링이 스케줄링을 가려서 대부분 "투명"(차이 없음)해야 정상.
- ⚠️ 한계: 벤치는 2022년 **NVIDIA RTX 3080** 기준 — 당신의 실제 GPU가 아님. GPU/드라이버 의존적.
- 폐기된 과장 주장: "HAGS가 전용 GPU 스케줄링 프로세서로 오프로드한다" (0-3 반박), "GeForce에 비권장" (0-3 반박).
- 출처: [MS DirectX 블로그](https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/), [BabelTech 26게임 벤치](https://babeltechreviews.com/hardware-accelerated-gpu-scheduling-performance/)

### 6. 전체화면 최적화(FSO) / 테두리없음 vs 독점전체화면 — 🟡 거의 PLACEBO(FPS) (검증 3-0 / 2-1)
- FSO는 전체화면을 flip-model 테두리없음으로 처리 (진짜 FSE가 아님).
- MS 텔레메트리: **거의 모든 사용자에게 FSE와 동등 이상.** → "FSO 끄면 FPS 오른다"는 통념을 약화.
- 실제 차이는 FPS가 아니라 **레이턴시/입력 지연** 영역. 게임별 예외는 존재.
- 출처: [MS: FSO 해설](https://devblogs.microsoft.com/directx/demystifying-full-screen-optimizations/), [MS: 창모드 게임 최적화](https://support.microsoft.com/en-us/topic/optimizations-for-windowed-games-in-windows-11)

### 7. 타이머 해상도 조작 — ❌ OBSOLETE / HARMFUL (검증 3-0)
- **Win10 v2004부터 `timeBeginPeriod`는 per-process.** 서드파티 타이머 툴이 더 이상 전역 타이머를 강제하지 못함 (전역 레지스트리 키는 Win11/Server 2022+에만).
- 해상도를 높이면 **깊은 C-state 진입을 막아 전력 약 10~25% 증가** (idle 효율↓, 발열↑).
- 폐기된 주장: "0.507ms가 0.5ms보다 낫다" (1-2 반박), "timeBeginPeriod는 MMCSS로 대체된 레거시" (1-2 반박).
- 출처: [MS timeBeginPeriod](https://learn.microsoft.com/en-us/windows/win32/api/timeapi/nf-timeapi-timebeginperiod), [randomascii: The Great Rule Change](https://randomascii.wordpress.com/2020/10/04/windows-timer-resolution-the-great-rule-change/)

### 8. 레지스트리 클리너 — ❌ PLACEBO (검증 3-0)
- **MS 공식 미지원.** 성능 향상 근거 없음. 잘못 지우면 시스템 손상 위험.
- 출처: [MS 레지스트리 클리닝 정책](https://support.microsoft.com/en-us/topic/microsoft-support-policy-for-the-use-of-registry-cleaning-utilities-0485f4df-9520-3691-2461-7b0fd54e8b3a)

### 9. 공격적 디블로터 / 대량 텔레메트리 차단 — ❌ HARMFUL (검증 3-0 / 2-1, 신뢰도 medium)
- 원클릭 디블로터/대량 텔레메트리 차단은 **Windows Update**(BITS, Delivery Optimization, Update Medic, DiagTrack) 파손, **Defender** 무력화, 롤백/BSOD 유발.
- FPS 이득은 저사양 기기에서만 미미하게.
- 안전한 대안: **그룹 정책**으로 텔레메트리 축소, **시작프로그램 수동 정리**.
- ⚠️ 신뢰도 medium (디블로터 피해는 포럼 출처 비중).
- 출처: [MS 레지스트리 정책](https://support.microsoft.com/en-us/topic/microsoft-support-policy-for-the-use-of-registry-cleaning-utilities-0485f4df-9520-3691-2461-7b0fd54e8b3a), [WindowsForum: 디블로팅이 업데이트를 깨는 이유](https://windowsforum.com/threads/debloating-windows-11-why-a-cleaner-pc-can-break-updates)

### 10. Standby 메모리 "정리" — ❌ HARMFUL (배경 지식, 1차 소스 부족)
- standby list를 강제 flush하면 캐시된 데이터를 다시 디스크에서 읽어야 해 **오히려 손해**. 게임 텍스처 스트리밍 등에서 역효과.
- (이번 검증 라운드에서 standby 전용 1차 소스는 unreliable 등급으로 걸러짐 — 일반 합의 수준의 결론.)

---

## 검증에서 폐기된(REFUTED) 주장 — 조언 아님
- HAGS가 "전용 GPU 스케줄링 프로세서로 오프로드" → **0-3 반박**
- HAGS "GeForce 게이머에 비권장" → **0-3 반박**
- "0.507ms가 0.5ms보다 sleep 정밀도 우위" → **1-2 반박**
- "timeBeginPeriod는 MMCSS로 대체된 레거시" → **1-2 반박**
- "26게임 중 대부분 HAGS 유의차 없음" → **1-2 반박** (실제론 이득/손해가 갈림)

## 미해결 질문
- **이 빌드의 GPU가 무엇인가?** HAGS/FSO 동작은 GPU/드라이버 의존적이고, 어떤 벤치도 실제 9600X 빌드의 GPU를 쓰지 않음.

## 방법론 한계 (정직한 고지)
- 메커니즘/벤더 문서 주장은 신뢰도 높음. **게임 임팩트** 주장은 상대적으로 약함.
- HAGS = 2022 NVIDIA 단일 벤치. FSO = MS 2019 텔레메트리(예외 존재). 디블로터 피해 = 포럼 출처.
- 통계: 5각도 · 23소스 fetch · 82주장 추출 · 25검증 · 20확정 · 5폐기 · 최종 6개 핵심 묶음 · 105 에이전트 호출.

---

## 핵심 시사점 (코드 단계로 갈 때)
1. **진짜 가치는 "진단 + 안전한 토글 + 되돌리기"** 형태. 범용 원클릭 부스트는 근거 없음.
2. 9600X single-CCD에선 친화도 고정보다 **백그라운드 격리(ProBalance식)**가 실효적.
3. **타이머 조작·레지스트리 클리너·공격적 디블로팅은 만들지 말 것** (obsolete/placebo/harmful).
4. HAGS/FSO/전원계획은 **사용자가 A/B로 직접 측정**할 수 있게 토글+롤백 제공이 정직한 설계.
5. LatencyMon식 DPC 진단은 통합 가치 높음 (Rynez 철학과 일치).
