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

### 8.1 Implement Order Badges
- [ ] In `OrdersAdornerLayer.cs`:
  - Draw circles with numbers
  - Position over cards/minions
  - Add drop shadow for visibility
  - Fade in animation (120ms)

### 8.2 Implement Target Arrows
- [ ] In `ArrowLayer.cs`:
  - Draw bezier from source to target
  - Curved based on distance
  - Arrowhead at end
  - Different colors for attack/spell

### 8.3 Implement Trade Halos
- [ ] In `HaloLayer.cs`:
  - Highlight friendly attacker
  - Highlight enemy target
  - Pulse animation
  - Clear after action

### 8.4 Add Visual Configuration
- [ ] Configurable colors (normal/lethal/warning)
- [ ] Configurable sizes (badge/arrow/halo)
- [ ] Opacity settings
- [ ] Animation speeds
- [ ] Document in `/docs/visual-config.md`

### Self-Test 8
- [ ] Badges appear on recommended cards
- [ ] Arrows point to correct targets
- [ ] Halos highlight trades
- [ ] All visuals are click-through
- [ ] No visual glitches on resize
- [ ] Update `/docs/progress.md`: Phase 8 Complete

---

## Phase 9: Explanation System

### 9.1 Create Explanation Schema
- [ ] Create `/docs/explain-map.json`:
  ```json
  {
    "lethal.direct": "lethal_direct",
    "tempo.gain": "tempo_gain",
    "secret.probe": "secret_probe_{secret}",
    ...
  }
  ```
- [ ] Create `/docs/explain.json` with Medium/Full text for each

### 9.2 Build Explain Composer
- [ ] Create `HSIntel.Core/Explain/ExplainComposer.cs`
- [ ] Map score components to explanation tokens
- [ ] Generate Medium explanation (1 sentence)
- [ ] Generate Full explanation (detailed reasoning)
- [ ] Ensure every action has explanation

### 9.3 Create Explain UI
- [ ] Add to `CoachHUD.xaml`:
  - Explain button
  - Flyout panel
- [ ] Show current line with Medium explanations
- [ ] Chevrons to expand each step to Full
- [ ] Alternates section (if any)

### 9.4 Add Confidence Indicators
- [ ] Visual confidence (color/opacity)
- [ ] Numeric confidence score
- [ ] Lethal indicator (special styling)
- [ ] RNG probability display

### Self-Test 9
- [ ] Every recommendation has explanation
- [ ] Medium is concise (1-2 sentences)
- [ ] Full adds substantial detail
- [ ] UI expands/collapses smoothly
- [ ] Update `/docs/progress.md`: Phase 9 Complete

---

## Phase 10: What-If Analysis (Optional)

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






