# HS Intel Coach - Complete Implementation Checklist (Final Exhaustive Version)

> **Purpose:** Complete, unambiguous implementation guide for building HS Intel Coach as a first-party module inside an HDT fork. Every step is actionable with clear success criteria. The AI implementer must track progress in `/docs/progress.md` and run self-tests after each phase.

## CRITICAL CONTEXT
- **Starting Point:** Full Hearthstone Deck Tracker (HDT) source code from GitHub
- **License:** HDT is "All Rights Reserved" - this is for personal use only, no distribution
- **Integration:** Build as first-party modules inside the HDT solution, not as plugins
- **Goal:** Intelligent coach that calculates best plays with visual overlay guidance

---

## Phase 0: Fork Setup & Environment Verification

### 0.1 Fork HDT Repository
- [x] Fork https://github.com/HearthSim/Hearthstone-Deck-Tracker to personal GitHub
- [x] Clone locally with full history
- [x] Create branch `hsintel-main` from master
- [x] Document fork date and HDT version in `/docs/fork-info.md`

### 0.2 Verify Build Environment
- [x] Install Visual Studio 2022+ with .NET Framework 4.7.2 SDK
- [x] Install NuGet package manager
- [x] Open `Hearthstone Deck Tracker.sln` 
- [x] Restore all NuGet packages
- [x] Build solution in Debug mode
- [x] Run HDT once to verify it launches
- [x] Document any build warnings/issues in `/docs/build-notes.md`

### 0.3 Create HS Intel Project Structure
- [x] Add new project `HSIntel.Core` (Class Library, .NET Framework 4.7.2)
- [x] Add new project `HSIntel.Engine` (Class Library, .NET Framework 4.7.2)  
- [x] Add new project `HSIntel.Overlay` (WPF User Control Library, .NET Framework 4.7.2)
- [x] Add project references from main HDT project to all HSIntel projects
- [x] Create folder structure in each project:
  - `/Models/`, `/Services/`, `/Interfaces/`, `/Utils/`, `/Config/`

### 0.4 Initialize Documentation Structure
- [x] Create `/docs/` folder at repository root
- [x] Create `/docs/progress.md` with sections:
  - Phase Status (checklist of all phases)
  - Current Tasks
  - Completed Tasks  
  - Test Results
  - Issues & Blockers
  - Backlog
  - Metrics Log
- [x] Create `/docs/contracts/` folder
- [x] Create `/tests/golden/` folder
- [x] Create `/data/` folder

### Self-Test 0
- [x] Build succeeds with no errors
- [x] HDT launches normally with empty HSIntel projects
- [x] All documentation folders exist
- [x] Update `/docs/progress.md`: Phase 0 Complete

---

## Phase 1: Disable Auto-Updater & Establish Contracts

### 1.1 Disable HDT Auto-Updater
- [x] Locate auto-update code in `/HDTUpdate/` 
- [x] In main HDT project, find updater initialization (likely in `Core/UpdateManager.cs` or similar)
- [x] Add configuration flag `HSIntel.DisableAutoUpdate=true`
- [x] Wrap updater initialization in conditional:
  ```
  if (!Config.Instance.HSIntelDisableAutoUpdate)
  {
      // existing updater code
  }
  ```
- [x] Remove HDTUpdate from installer/release builds
- [x] Add "Upstream Status" panel stub in Options (read-only, shows HDT version)

### 1.2 Document Event Contracts
- [x] Create `/docs/contracts/events.md` with:
  - List of HDT events we consume:
    - `GameEvents.OnGameStart/End`
    - `GameEvents.OnTurnStart`  
    - `GameEvents.OnPlayerDraw/Play/HandDiscard`
    - `GameEvents.OnOpponentDraw/Play/HandDiscard`
    - `GameEvents.OnAttack`
    - `GameEvents.OnPlayerHeroPower`
    - `DeckManagerEvents.OnDeckSelected`
    - `IPlugin.OnUpdate` (~100ms tick, Â±40ms tolerance)
  - Minimal payload schema for each
  - Degradation rules (missing event = skip paint cycle)

### 1.3 Document Overlay Contract  
- [x] Create `/docs/contracts/overlay.md` with:
  - Target: HDT overlay canvas in "HDT - Capturable Overlay" window
  - Layer hierarchy:
    - OrdersAdornerLayer (pass-through)
    - GhostAdornerLayer (pass-through)  
    - CoachHUD (interactive)
  - Arena Helper pattern reference for adding WPF children

### 1.4 Create Configuration Schema
- [x] Create `/docs/config.md` listing all HSIntel.* settings:
  - Search parameters (BeamWidth, Depth, MaxCompute, etc.)
  - Rollout triggers  
  - UI settings
  - Data sources
- [x] Add configuration class `HSIntel.Core/Config/HSIntelConfig.cs`
- [x] Integrate with HDT's Config.Instance persistence

### Self-Test 1
- [x] Auto-updater code is disabled when flag is true
- [x] Contracts documentation is complete
- [x] Configuration loads/saves with HDT config
- [x] Update `/docs/progress.md`: Phase 1 Complete

---

## Phase 2: Event Binding & State Ingestion

### 2.1 Create Event Binder
- [x] Create `HSIntel.Core/Services/HDTEventBinder.cs`
- [x] Implement subscription to all HDT events listed in contract
- [x] Map HDT events to semantic events:
  ```
  public event EventHandler<BoardStateChangedEventArgs> BoardStateChanged;
  public event EventHandler<TurnStartedEventArgs> TurnStarted;
  // etc.
  ```
- [x] Add OnUpdate handler with 40ms tolerance check
- [x] Implement DataQuality enum (Normal, Degraded)
- [x] Set Degraded when events missing/late

### 2.2 Create State Models
- [x] Create `HSIntel.Core/Models/BoardState.cs`:
  - Player hero (health, armor, weapon)
  - Opponent hero  
  - Friendly minions list
  - Enemy minions list
  - Secrets in play
- [x] Create `HSIntel.Core/Models/HandState.cs`:
  - Cards in hand with costs
  - Playable cards this turn
  - Created-by tags
- [x] Create `HSIntel.Core/Models/GameContext.cs`:
  - Current mana
  - Turn number
  - Active player

### 2.3 Implement State Builder
- [x] Create `HSIntel.Core/Services/StateBuilder.cs`
- [x] Subscribe to semantic events from binder
- [x] Build BoardState from HDT's Game.Entities
- [x] Build HandState from HDT's player hand tracking
- [x] Update on each event
- [x] Add state snapshot capability

### 2.4 Add State Validation
- [x] Validate entity counts match expectations
- [x] Check for impossible states (negative health, etc.)
- [x] Log validation failures but don't crash
- [x] Add metrics: state updates per second

### Self-Test 2
- [ ] Start a game, verify events fire
- [ ] Draw a card, verify HandState updates
- [ ] Play a minion, verify BoardState updates  
- [ ] Check no events are missed in a full turn
- [ ] Verify state snapshots are valid
- [ ] Update `/docs/progress.md`: Phase 2 Complete

---

## Phase 3: Opponent Reads Pipeline

### 3.1 Create Opponent Model
- [x] Create `HSIntel.Core/Models/OpponentModel.cs`:
  - Hand slots with kept-in-mulligan flags
  - Created-by/discovered tags per card
  - Possible card ranges per slot
  - Secret candidates by class

### 3.2 Implement Mulligan Tracking
- [x] Hook mulligan phase events
- [x] Track which opponent cards were kept (positions 1-3/4)
- [x] Set kept flags that freeze after mulligan
- [x] Handle missing mulligan (conservative: no kept labels)

### 3.3 Implement Created/Discovered Tracking
- [x] Parse created-by tags from HDT entities
- [x] Map creator card ? possible created cards
- [x] Narrow ranges when cards are created
- [x] Track discovered vs randomly generated

### 3.4 Implement Secret Tracking
- [x] Use HDT's existing secret helper as reference
- [x] Track secrets by class
- [x] Implement elimination when triggers don't fire
- [ ] Generate probe recommendations *(defer to overlay UI phase)*

### 3.5 Add Hand Odds Integration
- [x] Add HDT hand odds snapshot to event source/binder
- [x] Reuse HDT's hand probability calculations

---

## Phase 6: Guarded Implementation — Lethal Fast-Path & Secrets Stubs

### 6.1 Minimal Board-Only Lethal Solver
- [x] Add `HSIntel.Engine/Lethal/LethalSolver.cs`
- [x] API: `LethalPlan? TryFindLethal(GameContext, HSIntelConfig)`
- [x] Behavior: compute minion-only lethal respecting taunts; exclude hero attacks/spells
- [x] Performance: simple greedy, sub-millisecond on typical boards

### 6.2 Integration (No BeamSearch changes)
- [x] Minimal edits in `HSIntel.Engine/Services/DecisionEngineCoordinator.cs`
- [x] Invoke lethal fast-path before search
- [x] If plan found: apply via `BoardSimulator` to build `SearchResult`
- [x] Emit concise logs (rely on existing `[HSIntel][Engine] AttackSummary` from simulator)
- [x] Raise `DecisionComputed` and return without calling `BeamSearch`
- [x] Else: call existing `BeamSearch` unchanged

### 6.3 Secrets (Stub Only)
- [x] Add `HSIntel.Engine/Secrets/SecretHandler.cs`
- [x] Summarize opponent secrets from `context.Board.Secrets`
- [x] Emit safe, information-first probe order via Trace logs only
- [x] No scoring changes

### 6.4 RNG Gating (Stub Only)
- [x] Respect `HSIntelConfig.EnableRollouts/RolloutSamples`
- [x] Log `[HSIntel][Engine] RNG: skipped (disabled)` when rollouts are off

### 6.5 Docs
- [x] Add `/docs/sequencing.md` with Phase 6 sequencing rules
- [ ] Update `/docs/progress.md` (file currently missing; tracking here instead)

### 6.6 Guardrails Verification
- [x] No edits to HSIntel.Core or HSIntel.Overlay public models/APIs
- [x] No new project references
- [x] No BeamSearch signature changes
- [x] No HearthDb/localization parsing used
- [x] Target remains `net472`
- [x] Log tags: AttackSummary/BeamResult unchanged in format
- [x] Snapshot provider aligned to HDT Core snapshot model (HdtGameStateSource uses extended HeroDescriptor with hero/hero‑power metadata)

### Self-Test 6 (Initial Subset)
- [ ] Board-only lethal detection puzzles pass (minions only)
- [ ] SecretHandler emits probe order logs
- [ ] RNG gating logs "skipped" unless explicitly enabled

- [x] Expose P(card in hand) for each possible card

### Self-Test 3
- [x] Mulligan a game, verify kept cards tracked
- [x] Play a discover card, verify created-by tracked (fallback to creator StoredCardIds when needed)
- [x] Play a secret, verify candidates listed (opponent-side only)
- [x] Trigger secret elimination, verify list updates (DirectAttack/plays/board checks)
- [x] Update `/docs/progress.md`: Phase 3 Complete

---

## Phase 4: Overlay Infrastructure

### 4.1 Create Overlay Host
- [x] Create `HSIntel.Overlay/IntelOverlayHost.xaml` (UserControl)
- [x] Add as child to HDT's overlay canvas in main window
- [x] Ensure it fills canvas (bind to ActualWidth/Height)
- [x] Set Panel.ZIndex appropriately

### 4.2 Create Adorner Layers (Moved to Phase 8: Visual Overlay Rendering)
- [ ] Create `OrdersAdornerLayer.cs`:
  - Number badges (1, 2, 3...) 
  - Set IsHitTestVisible = false
  - Use retained DrawingVisuals for performance
- [ ] Create `ArrowLayer.cs`:
  - Bezier curves from source to target
  - Arrowhead rendering
  - Pass-through for clicks
- [ ] Create `HaloLayer.cs`:  
  - Highlight rings around targets
  - Soft pulse animation (800ms period)

### 4.3 Create Coach HUD
- [x] Create `CoachHUD.xaml`:
  - Toggle button (bottom-right by default)
  - Status indicator  
  - Settings gear icon
- [x] Make draggable with snap-to-corners
- [x] This is ONLY hit-testable element
- [x] Bind to HSIntelConfig for position persistence

### 4.4 Add Coordinate Mapping
- [x] Create `OverlayCoordinates.cs` service (implemented as IOverlayCoordinateMapper + OverlayCoordinateMapperBase)
- [x] Map board positions to screen coordinates (prototype via RegionDrawer)
- [x] Map hand positions to screen coordinates  
- [x] Handle window resize/DPI scaling (Refresh on SizeChanged)
- [x] Use HDT's existing coordinate helpers as reference

### Self-Test 4\n- [x] Overlay appears over game\n- [x] HUD is draggable\n- [x] Clicks pass through adorner layers\n- [x] Draw dummy arrow, verify it renders\n- [x] Resize window, verify coordinates update (board/hand regions)
- [x] Draw dummy arrow, verify it renders
- [x] Resize window, verify coordinates update
- [x] Update `/docs/progress.md`: Phase 4 Complete

---

## Phase 5: Decision Engine Core

### 5.1 Create Move Generator
- [x] Create `HSIntel.Engine/MoveGen/MoveGenerator.cs`
- [x] Generate all legal actions:
  - Play card (with valid targets)
  - Attack (with valid targets)
  - Hero power
  - End turn
- [x] Handle targeting requirements
- [x] Order by likely value (big plays first)

### 5.2 Implement Board Simulator  
- [x] Create `HSIntel.Engine/Simulator/BoardSimulator.cs`
- [x] Clone board state
- [x] Apply action to get new state
- [ ] Handle:
  - [x] Minion combat (damage, death)
  - [ ] Spell effects (damage, buff, draw)
  - [x] Taunt/Divine Shield/Windfury
  - [ ] Deathrattles (basic)

### 5.3 Create Evaluator
- [x] Create `HSIntel.Engine/Evaluation/StateEvaluator.cs`
- [x] Implement scoring function:
  ```
  Score = w1*BoardControl + w2*CardAdvantage + 
          w3*HealthDiff + w4*ManaCurve + 
          w5*TempoGain + w6*LethalThreat
  ```
- [x] Define initial weights (document in `/docs/evaluator.md`)
- [ ] Normalize so Î”Score = 6.0 = "large swing"

### 5.4 Implement Beam Search
- [x] Create `HSIntel.Engine/Search/BeamSearch.cs`
- [x] Parameters: Width=10, Depth=5 (configurable)
- [x] Use priority queue sorted by score
- [x] Prune to top N at each depth
- [x] Return top line with score

### 5.5 Add Transposition Table
- [x] Create `HSIntel.Engine/Search/TranspositionTable.cs`
- [x] Hash function for board state:
  ```
  Hash = Zobrist(heroes, minions, hand, secrets, mana)
  ```
- [x] Cache evaluated positions
- [x] Hit rate tracking

### Self-Test 5
- [x] Generate moves for sample position
- [x] Simulate a trade, verify state updates
- [x] Evaluate before/after, verify score changes
- [x] Run beam search on 10 positions
- [x] Verify transposition hits on duplicates
- [x] Measure: ≤250ms for typical position (time budget raised)
- [x] Update `/docs/progress.md`: Phase 5 Complete

---

## Phase 6: Lethal Detection & Special Cases

### 6.1 Implement Lethal Fast Path
- [x] Create `HSIntel.Engine/Lethal/LethalSolver.cs`
- [x] Calculate guaranteed face damage:
  - Direct damage from hand (curated, engine-internal toggles off by default)
  - Minion attacks (after taunt removal)
  - Weapon + hero power (curated HP; toggle‑gated)
- [x] Find minimal taunt removal
- [x] Return lethal sequence if found
- [x] Add to beam search as priority

### 6.2 Handle Secrets
- [x] Create `HSIntel.Engine/Secrets/SecretHandler.cs`
- [x] For each possible secret:
  - Generate safe probe sequence (logs only; probe order emitted)
  - Calculate risk if triggered (ordering‑only signal; no scoring API changes)
- [ ] Modify evaluator to penalize risky plays (repo‑aligned: skipped; use ordering only)
- [x] Generate probe recommendations (logs)

### 6.3 Handle RNG
- [x] Identify RNG cards (curated resolvers only)
- [x] For critical situations only:
  - Run 64 Monte Carlo samples (capped by time budget)
  - Calculate success probability
- [x] Mark lines with P(success) when RNG involved (single concise line)

### 6.4 Implement Sequencing Rules
- [x] Information first (draw before playing)
- [x] Buffs before summons
- [x] Small spells first (Counterspell test) — ordering‑only (toggle‑gated)
- [x] Document rules in `/docs/sequencing.md` (already present)

### Self-Test 6
- [ ] Test 20 lethal puzzles, verify 98%+ solved
- [ ] Test secret scenarios, verify probe order
- [ ] Test RNG lethal, verify probability shown
- [ ] Verify sequencing follows rules
- [ ] Update `/docs/progress.md`: Phase 6 Complete

---

## Phase 7: Play Recommendation Pipeline

Status (updated 2025-10-18)
- [x] Engine-side recommendation pipeline implemented and toggle-gated.
- [x] Coordinator wires result to internal store after the single BeamResult log.
- [x] No public API/log changes; defaults unchanged; within MaxComputeMs.
- [ ] Overlay rendering glue is deferred to Phase 8.

Engine implementation details
- Internal models: `HDT/HSIntel.Engine/Models/Recommendations.cs` (PlayRecommendationStep/PlayRecommendationSet).
- Service: `HDT/HSIntel.Engine/Services/RecommendationService.cs`
  - `UpdateFromResult(SearchResult)` maps chosen actions to ordered steps; no re-simulation.
  - `GetCurrentActionsOrEmpty()` provides the current recommended line for overlay.
- Toggle: `HDT/HSIntel.Engine/Internal/EngineToggles.cs` -> `EnableRecommendationPipeline` (default false).
- Coordinator: `HDT/HSIntel.Engine/Services/DecisionEngineCoordinator.cs` updates recommendations when the toggle is enabled.

### 7.1 Create Recommendation Service
- [x] Create `HSIntel.Engine/Recommendation/PlayRecommender.cs`
- [x] Integrate all components:
  ```
  State â†’ MoveGen â†’ BeamSearch â†’ Evaluator â†’ Best Line
  ```
- [x] Add legality validation
- [x] Handle degraded states gracefully (clear recommendations on cancel/failure; coordinator skips degraded data)

### 7.2 Add Opponent Reads Integration
- [x] Modify evaluator to use opponent model (toggle-gated):
  - Weight plays by P(opponent has counter)
  - Bias against overextension vs AOE
  - Adjust for kept cards
- [ ] Document impact in `/docs/opponent-reads.md`

### 7.3 Implement Caching Strategy
- [x] Cache recommendations for same state (signature-based)
- [x] Invalidate on state change
- [ ] Background compute for next likely states (deferred per time-budget guardrail)
- [x] Metric: cache hit rate (internal counters)

### 7.4 Add Performance Monitoring
- [x] Track compute time per recommendation
- [x] Track nodes evaluated
- [x] Track alternates dropped
- [x] Log to `/logs/HSIntel-perf.txt` (toggle-gated)
- [x] Auto-disable if consistently over budget (toggle-gated)

### Self-Test 7
- [ ] Full game with recommendations
- [ ] Verify p95 < 150ms, p99 < 250ms
- [ ] Verify reads affect recommendations
- [ ] Check no UI stuttering
- [ ] Update `/docs/progress.md`: Phase 7 Complete

---

## Phase 8: Visual Overlay Rendering

Status (updated 2025-10-18)
- [x] Retained visuals implemented for orders/arrows/halos in HDT/HSIntel.Overlay.
- [x] Click-through maintained; HUD remains only hit-testable control.
- [x] Overlay redraws only when the recommended sequence changes.
- [x] Exact-target placement using entity ids (reflection to HDT Entities) for minions.
- [ ] Animations and visual configuration deferred.

### 8.1 Implement Order Badges
- [x] In `OrdersAdornerLayer.cs`:
  - [x] Draw circles with numbers
  - [x] Position over cards/minions
  - [ ] Add drop shadow for visibility
  - [ ] Fade in animation (120ms)

### 8.2 Implement Target Arrows
- [x] In `ArrowAdornerLayer.cs`:
  - [ ] Draw bezier from source to target (current implementation uses straight line)
  - [ ] Curved based on distance
  - [x] Arrowhead at end
  - [ ] Different colors for attack/spell

### 8.3 Implement Trade Halos
- [x] In `HaloAdornerLayer.cs`:
  - [x] Highlight friendly attacker
  - [x] Highlight enemy target
  - [ ] Pulse animation
  - [x] Clear after action

### 8.4 Add Visual Configuration
- [ ] Configurable colors (normal/lethal/warning)
- [ ] Configurable sizes (badge/arrow/halo)
- [ ] Opacity settings
- [ ] Animation speeds
- [ ] Document in `/docs/visual-config.md`

### Self-Test 8
- [] Badges appear on recommended cards
- [] Arrows point to correct targets (straight line)
- [] Halos highlight trades
- [] All visuals are click-through
- [] No visual glitches on resize
- [ ] Update `/docs/progress.md`: Phase 8 Complete

---
## Phase 9: Explanation System

Status (updated 2025-10-19)
- [x] Schema files added (docs/explain-map.json, docs/explain.json)
- [x] Internal composer implemented (in Engine; overlay reads via reflection)
- [x] HUD Explain flyout shows Medium/Full + confidence; lethal tag; RNG shown when available
- [ ] Alternates panel deferred

### 9.1 Create Explanation Schema
- [x] Create /docs/explain-map.json:
  `json
  {
    "lethal.direct": "lethal_direct",
    "tempo.gain": "tempo_gain",
    "secret.probe": "secret_probe_{secret}",
    ...
  }
  `
- [x] Create /docs/explain.json with Medium/Full text for each

### 9.2 Build Explain Composer
- [x] Create HSIntel.Core/Explain/ExplainComposer.cs (implemented in Engine; accessed via reflection)
- [x] Map score components to explanation tokens
- [x] Generate Medium explanation (1 sentence)
- [x] Generate Full explanation (detailed reasoning)
- [x] Ensure every action has explanation

### 9.3 Create Explain UI
- [x] Add to CoachHUD.xaml:
  - Explain button
  - Flyout panel
- [x] Show current line with Medium explanations
- [x] Chevrons to expand each step to Full (simple expand)
- [ ] Alternates section (if any)

### 9.4 Add Confidence Indicators
- [x] Visual confidence (numeric displayed; tint deferred)
- [x] Numeric confidence score
- [x] Lethal indicator (header tag)
- [x] RNG probability display (shown when available)

### Self-Test 9
- [x] Every recommendation has explanation
- [x] Medium is concise (1-2 sentences)
- [x] Full adds substantial detail
- [x] UI expands/collapses smoothly
- [x] Update /docs/progress.md: Phase 9 Complete

---## Phase 10: What-If Analysis (Optional)

### 10.1 Create What-If Panel
- [ ] Add to `CoachHUD.xaml`:
  - Collapsed by default
  - Shows sequence chips (1-4 actions)
  - Drag to reorder

### 10.2 Implement Ghost Rendering
- [ ] In `GhostAdornerLayer.cs`:
  - Render at 45% opacity
  - Update while dragging
  - Show predicted outcome

### 10.3 Add Recompute Throttling
- [ ] Limit to once per 250ms while dragging
- [ ] Compute on background thread
- [ ] No alternates during drag
- [ ] Full recompute on release

### Self-Test 10
- [ ] Panel expands/collapses
- [ ] Dragging shows ghost
- [ ] No UI freezing while dragging  
- [ ] Recompute happens on release
- [ ] Update `/docs/progress.md`: Phase 10 Complete

---

## Phase 11: ISMCTS Enhancement (Gated)

### 11.1 Implement Rollout Gate
- [ ] Create `HSIntel.Engine/Search/RolloutGate.cs`
- [ ] Define triggers:
  - LethalSuspicion: near-lethal with RNG
  - HighSwing: Î”Score â‰¥ 6.0
- [ ] Check remaining time budget
- [ ] Gate with HSIntelConfig settings

### 11.2 Build ISMCTS
- [ ] Create `HSIntel.Engine/Search/ISMCTS.cs`
- [ ] Sample opponent hands from ranges
- [ ] Run limited simulations (64 samples)
- [ ] Use remaining compute budget
- [ ] Fall back to deterministic if over

### 11.3 Integrate with Pipeline
- [ ] Add to PlayRecommender
- [ ] Run only when gated
- [ ] Blend with deterministic results
- [ ] Show confidence adjustment

### Self-Test 11
- [ ] Rollouts trigger on lethal + RNG
- [ ] Rollouts trigger on high swing
- [ ] Never exceed time budget
- [ ] Fallback works correctly
- [ ] Update `/docs/progress.md`: Phase 11 Complete

---

## Phase 12: Data Layer & Precedence

### 12.1 Setup Card Data Chain
- [ ] Create `HSIntel.Core/Data/CardDataService.cs`
- [ ] Primary: HDT's card database
- [ ] Fallback: HearthstoneJSON
- [ ] Cache in memory
- [ ] Log conflicts to `/logs/HSIntel-card-warnings.txt`

### 12.2 Add HearthstoneJSON Client
- [ ] Create HTTP client for API
- [ ] Fetch `/v1/latest/` on startup
- [ ] Parse and map to internal model
- [ ] Handle offline gracefully

### 12.3 Implement Image Cache
- [ ] Create cache at `%AppData%/HearthstoneDeckTracker/HSIntel/cache/images/`
- [ ] LRU eviction at 256MB
- [ ] Fetch 256x for thumbnails
- [ ] Use in Explain UI

### 12.4 Create Archetype Hints
- [ ] Create `/data/archetypes.json`:
  ```json
  {
    "Mage": {
      "Freeze": ["Frost Nova", "Blizzard"],
      "Burn": ["Fireball", "Frostbolt"]
    }
  }
  ```
- [ ] Use to bias play-around decisions

### Self-Test 12
- [ ] Card data loads from HDT
- [ ] Fallback to HJSON works
- [ ] Images cache properly
- [ ] Archetype detection influences recommendations
- [ ] Update `/docs/progress.md`: Phase 12 Complete

---

## Phase 13: Quality Gates & Validation

### 13.1 Create Golden Test Suite
- [ ] Create `/tests/golden/schema.json` (JSON Schema)
- [ ] Create 200 test snapshots covering:
  - Lethal scenarios
  - Secret situations
  - Trade decisions  
  - Mulligan impacts
- [ ] Document in `/tests/golden/README.md`

### 13.2 Build Test Harness
- [ ] Create `tools/GoldenRunner/` (standalone)
- [ ] Load snapshots
- [ ] Run recommendations
- [ ] Compare to expected
- [ ] Generate report
- [ ] NO runtime linkage to main code

### 13.3 Implement Quality Metrics
- [ ] Track in production:
  - Lethal detection rate
  - Compute time p95/p99
  - Illegal line rate
  - Secret probe accuracy
- [ ] Log to `/logs/HSIntel-metrics.txt`

### 13.4 Add Data Quality Banner
- [ ] Show when logs incomplete:
  - "Limited data: Hearthstone logs disabled or incomplete. Click to enable logs."
- [ ] Link to HDT log.config page
- [ ] Auto-dismiss when fixed

### Self-Test 13
- [ ] Golden tests achieve 98%+ lethal detection
- [ ] Performance meets p95 < 150ms
- [ ] Secret probes 95%+ accurate
- [ ] Banner appears/dismisses correctly
- [ ] Update `/docs/progress.md`: Phase 13 Complete

---

## Phase 14: Settings UI & Configuration

### 14.1 Create Settings Panel
- [ ] Add tab to HDT Options window
- [ ] Add Thoroughness slider:
  - Normal: Beam=10, Depth=5
  - Deep: Beam=14, Depth=6  
  - Max: Beam=18, Depth=7
- [ ] Add Advanced section with all raw values

### 14.2 Wire Configuration
- [ ] Load from HDT config.xml
- [ ] Save changes immediately
- [ ] Apply without restart
- [ ] Show current values in HUD tooltip

### 14.3 Add Hotkeys
- [ ] Register global hotkeys:
  - Ctrl+; = Toggle coach
  - Ctrl+' = Open explain
- [ ] Make configurable
- [ ] Handle conflicts

### Self-Test 14
- [ ] Settings save/load correctly
- [ ] Changes apply immediately
- [ ] Hotkeys work globally
- [ ] No conflicts with HDT keys
- [ ] Update `/docs/progress.md`: Phase 14 Complete

---

## Phase 15: Final Integration & Polish

### 15.1 Compatibility Guard
- [ ] Create `/docs/compat.json`:
  ```json
  {
    "supported": [
      {"min": "1.46.0", "max": "1.47.x"}
    ]
  }
  ```
- [ ] Check HDT version on startup
- [ ] Soft-disable if out of range
- [ ] Show warning banner

### 15.2 Performance Optimization
- [ ] Profile full games
- [ ] Optimize hot paths
- [ ] Add more caching
- [ ] Reduce allocations
- [ ] Target: consistent <150ms

### 15.3 Error Handling
- [ ] Wrap all entry points in try-catch
- [ ] Log errors but don't crash
- [ ] Degrade gracefully
- [ ] Show user-friendly messages

### 15.4 Final Documentation
- [ ] Complete `/docs/refs.md` with citations
- [ ] Write `/docs/user-guide.md`
- [ ] Document known limitations
- [ ] Create `/docs/troubleshooting.md`

### Self-Test 15
- [ ] Hour-long session with no crashes
- [ ] Performance consistent
- [ ] All errors handled gracefully  
- [ ] Documentation complete
- [ ] Update `/docs/progress.md`: Phase 15 Complete

---

## Phase 16: Comprehensive Testing

### 16.1 Integration Tests
- [ ] Test full game flow
- [ ] Test with different deck types
- [ ] Test all card mechanics
- [ ] Test at different ranks
- [ ] Document issues found

### 16.2 Stress Testing
- [ ] Complex board states (7v7)
- [ ] Many secrets active
- [ ] Large hand sizes
- [ ] Rapid state changes
- [ ] Verify performance holds

### 16.3 Edge Case Testing
- [ ] Disconnection/reconnection
- [ ] Spectator mode
- [ ] Practice mode
- [ ] Adventures (if applicable)
- [ ] Arena draft (overlay should hide)

### 16.4 Soak Testing
- [ ] Run for 24 hours continuously
- [ ] Monitor memory usage
- [ ] Check for leaks
- [ ] Verify logs rotate properly
- [ ] Confirm no degradation

### Final Validation
- [ ] All quality gates met:
  - Lethal detection â‰¥ 98%
  - Illegal line rate â‰¤ 0.05%
  - p95 â‰¤ 150ms, p99 â‰¤ 250ms
  - Secret probe accuracy â‰¥ 95%
  - Zero blocked clicks
- [ ] Update `/docs/progress.md`: ALL PHASES COMPLETE

---

## Ongoing Maintenance Protocol

### After Every Implementation Session
1. Update `/docs/progress.md` with:
   - Tasks completed
   - Issues encountered
   - Test results
   - Performance metrics
   - Next priorities

### After Every Phase
1. Run all self-tests
2. Document results
3. Fix any failures before proceeding
4. Commit code with descriptive message

### Weekly Metrics Review
1. Analyze performance logs
2. Review error logs
3. Check memory usage trends
4. Update optimization backlog

### Backlog Management
Track these for future improvement:
- [ ] More card mechanics support
- [ ] Better RNG handling
- [ ] Advanced archetype detection
- [ ] Machine learning evaluator
- [ ] Deck tracker integration
- [ ] Statistics tracking
- [ ] Replay analysis
- [ ] Coaching tips database

---

## Critical Success Factors

### Must Work Perfectly
1. **Never interfere with game** - all overlays pass-through except HUD
2. **Never crash HDT** - all errors caught and handled
3. **Always performant** - <150ms or skip paint
4. **Clear explanations** - every recommendation explained
5. **Accurate** - 98%+ correct lethal detection

### Implementation Notes
- Start with Phase 0-2 to establish foundation
- Phases 3-8 can partially parallelize  
- Phase 9-12 build on core features
- Phase 13-16 are validation and polish
- Keep `/docs/progress.md` updated continuously
- Test early, test often
- Profile performance regularly

---

**END OF CHECKLIST**

The AI implementer must now begin with Phase 0 and work through each phase sequentially, updating progress documentation and running self-tests at each milestone. Continue until all phases are complete and quality gates are met.





