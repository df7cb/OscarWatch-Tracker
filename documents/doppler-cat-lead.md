# Doppler CAT lead — developer notes

Operator-facing help: [oscarwatch.org/docs/radio-doppler-tuning](https://oscarwatch.org/docs/radio-doppler-tuning) (Lead Doppler section).

This note captures **field symptoms**, **tuning knobs**, and **next steps** if lead is still ahead or behind after the phase-gated implementation.

## Current behaviour (v1)

Setting: `RigSettings.DopplerCatLeadEnabled` (Settings → Radio).

When enabled, `DopplerCatLead.ResolveRangeRates` is used by:

- `RigController` (CAT writes)
- `FrequencyOverlayViewModel` (Radio row + rig context)

Logic lives in `OscarWatch.Core/Radio/DopplerCatLead.cs`.

| Phase | Condition | CAT / Radio row |
|-------|-----------|-----------------|
| Gentle leg (typical AOS / LOS) | Slope below blend start | Snapshot range rate only — same as lead off |
| Ramp (approaching TCA) | Blend start ≤ slope &lt; full | Linear blend between snapshot and lead rate |
| Steep leg (TCA middle) | Slope ≥ full threshold | Full lead rate at `utc + leadMs` (per RX/TX leg in dual-radio) |

**Lead time:** `leadMs = min(CatDelayMs / 2, MaxLeadMs)`.

**Blend:** `blend = clamp((slope − SlopeBlendStart) / (Steep − Start), 0, 1)`; `rate = lerp(snapshot, leadRate, blend)`. Avoids a step when the gate turns on.

**Slope:** sample range rate at `utc` and `utc + RangeRateSlopeSampleSec`; slope = \|Δrr\| / sample interval.

**UI:** frequency overlay shows a dot beside **Radio** when lead is enabled and the satellite is **above the horizon** (dim = waiting, amber = actively blending). Hidden below 0° elevation.

### Constants (all in `DopplerCatLead.cs`)

| Constant | Default | Role |
|----------|---------|------|
| `MaxLeadMs` | `50` | Cap lead so high CI-V **pacing** delay is not treated as tune latency |
| `RangeRateSlopeSampleSec` | `1.0` | How far ahead we look to measure “how fast range rate is changing” |
| `SlopeBlendStartKmPerSec2` | `0.010` | Slope where lead blend begins (0 = snapshot only) |
| `SteepRangeRateSlopeKmPerSec2` | `0.016` | Slope where blend reaches 1 (full lead) |
| `RecedingAssistMaxBlend` | `0.55` | Post-TCA receding leg when slope &lt; blend start but range rate is positive and large |
| `TcaRangeRateTaperKmPerSec` | `0.35` | Near TCA, scale lead from 0 at rr=0 up to full strength at this |range rate| |

Calibrated against **FO-29** high-elevation pass in `DopplerCatLeadTests.Fo29_lead_applies_near_tca_not_on_gentle_aos_leg` (~79° pass, London test site, seed TLE).

**Post-TCA receding assist:** after TCA, range rate is positive while slope falls below `SlopeBlendStartKmPerSec2`. AOS at the same elevation has negative range rate, so lead stays off on approach. **TCA taper:** when |range rate| is small (Doppler Δ crossing ~0), forward lead is scaled down to avoid overshoot (RS-44 field report: recurring −100 Hz RX offset at crossover). See `Fo29_post_tca_low_slope_receding_leg_keeps_cat_lead`, `Rs44_at_tca_lead_blend_is_tapered`, and `Fo29_aos_approach_with_low_slope_stays_on_snapshot_rate`.

## Reported operator pattern (FO-29)

- **Lead off:** AOS and LOS stable; **middle near TCA** lags (CAT arrives “in the past”).
- **Whole-pass lead (old):** ends still OK; **middle felt ahead** (coarse −100 Hz RX offset corrections).
- **Phase-gated lead (now):** intended to help only in the middle window without touching gentle legs.

Pure propagation lead on FO-29 is only **a few Hz** peak (see `Fo29_high_pass_lead_overshoot_is_bounded_after_cap`). Repeated **−100 Hz** buttons likely mix threshold stepping, rig lag, and coarse manual trim — not lead Hz alone.

---

## If it is still **ahead** (radio tuned above the signal)

Symptom: need **−RX offset** (or passband trim down) during the **steep middle**; Radio row above where the signal sounds.

### Check first (field)

1. **Radio vs Sat row** on the overlay during the problem window. The Hz gap ≈ applied lead + any other correction. If gap is small but audio feels wrong, suspect rig/CAT lag or threshold stepping, not lead math.
2. **CAT delay (ms)** — should pace CI-V, not be confused with tune latency. High values (200+) used to inflate lead before `MaxLeadMs`.
3. **Doppler threshold SSB/CW (Hz)** — default 50 Hz; CAT jumps in chunks; operator may use ±100 Hz offset buttons to chase.
4. Confirm **NOR/REV** matches the satellite database mode.

### Code knobs (if still ahead on steep leg)

| Knob | Direction | File |
|------|-----------|------|
| Lower `MaxLeadMs` | Less lead (e.g. 50 → 25 ms) | `DopplerCatLead.cs` |
| Lower lead fraction | e.g. `catDelayMs / 4` instead of `/ 2` in `ResolveLeadMs` | `DopplerCatLead.cs` |
| Raise `SteepRangeRateSlopeKmPerSec2` | Lead activates on fewer seconds (narrower TCA window) | `DopplerCatLead.cs` |
| Shorter `RangeRateSlopeSampleSec` | Steeper gate (may flicker on/off) | `DopplerCatLead.cs` |

### Future settings (if constants are not enough)

- **`DopplerCatLeadMs`** — Settings → Radio, 0 = automatic; explicit ms without changing CAT pacing (`RigSettings.DopplerCatLeadMs`).
- **Lead gain** — Settings → Radio, 0–100% scales lead contribution (`RigSettings.DopplerCatLeadGainPercent`).
- **Elevation gate** — optional AND with slope (e.g. only when el > 20°).

---

## If it is still **behind** (radio tuned below the signal)

Symptom: need **+RX offset** during the **steep middle**; lag returns when lead is on but ends of pass are fine.

### Check first (field)

1. Lead may be **off** on that second (slope below threshold) — watch Radio vs Sat: if they match, gate did not fire.
2. **Actual tune latency** may exceed `MaxLeadMs` (slow CI-V / busy serial / ICOM SAT mode).
3. **CAT delay** too large → fewer writes; combined with 50 Hz threshold, radio stays behind the slope.

### Code knobs (if still behind on steep leg)

| Knob | Direction | File |
|------|-----------|------|
| Raise `MaxLeadMs` | More lead (careful: pacing delay ≠ latency) | `DopplerCatLead.cs` |
| Lower `SteepRangeRateSlopeKmPerSec2` | Lead on earlier/later (wider middle window) | `DopplerCatLead.cs` |
| Raise lead fraction | e.g. full `CatDelayMs` instead of half (aggressive) | `ResolveLeadMs` |

### Future settings

- Measured **round-trip lead ms** per rig family (IC-910 vs FT-817).
- **Scale lead by slope** — `leadMs * clamp(slope / refSlope, 0, 1)` instead of binary gate.

---

## If **AOS / LOS** regressed (should stay on snapshot rate)

Symptom: lead seems active on gentle legs; Radio ≠ Sat when elevation is low or rate is changing slowly.

| Knob | Direction |
|------|-----------|
| **Raise** `SteepRangeRateSlopeKmPerSec2` | Stricter gate — less lead on gentle legs |
| **Lower** `RangeRateSlopeSampleSec` | Can false-trigger on noisy TLE legs — usually raise threshold instead |

Re-run: `Fo29_lead_applies_near_tca_not_on_gentle_aos_leg`.

---

## If the **middle window is wrong** (too short / too long)

Symptom: lead kicks in too late after TCA, or stays on too long toward LOS.

- Threshold `SteepRangeRateSlopeKmPerSec2` controls **when**, not **how much** lead.
- Run slope scan (temporary test or debugger) over `tca ± 180 s` using `ComputeRangeRateSlopeKmPerSec2` — see `FindSteepestUtcNearTca` in `DopplerCatLeadTests.cs`.
- Fast LEO (ISS) vs slow FO-29: same threshold may need per-orbit tuning or a **scaled** gate later.

---

## Tests to run after changes

```bash
dotnet test OscarWatch.Tests/OscarWatch.Tests.csproj --filter "FullyQualifiedName~DopplerCatLead"
dotnet test OscarWatch.Tests/OscarWatch.Tests.csproj --filter "Cat_lead_radio"
```

Key tests:

| Test | What it guards |
|------|----------------|
| `Gentle_slope_returns_snapshot_without_lead_queries` | Gate off → one slope sample, no lead queries |
| `Fo29_lead_applies_near_tca_not_on_gentle_aos_leg` | FO-29 AOS gentle vs steep near TCA |
| `Fo29_high_pass_lead_overshoot_is_bounded_after_cap` | Peak Hz overshoot bounded on FO-29 |
| `High_cat_delay_lead_is_capped_at_max_ms` | `MaxLeadMs` cap |

Update help + `Settings.Radio.DopplerCatLeadNote` in `Strings.resx` (and `ja` / `zh-CN` / `pt-BR`) if behaviour or operator wording changes.

---

## Related code (not lead, but same symptom window)

If middle-of-pass error persists with lead **off**, investigate:

- `RigController` — `DopplerThresholdLinearHz`, `CanWriteRx` / CAT delay pacing, hands-off linear (`ShouldTrackDopplerAutomatically`)
- Instantaneous range rate in `TrackingOrchestrator` / `LookAngles` (no 1 s lag)
- Passband trim vs RX offset (`SyncManualFromMainDial`)

---

## Open ideas (not implemented)

- [ ] User-visible **lead ms** or **gain** (decoupled from CAT delay)
- [ ] **Slope-proportional** lead instead of on/off
- [ ] **Hysteresis** on steep gate to avoid flicker at threshold boundary
- [ ] Log steep/lead state in diagnostics for post-pass review
- [ ] Per-rig default lead ms in driver or rig profile
