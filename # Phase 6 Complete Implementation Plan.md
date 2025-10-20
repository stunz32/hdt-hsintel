

## Revisions (Repo-Aligned Updates) � Read First

This section supersedes parts of the original plan to ensure zero build breaks and full alignment with our current repository, guardrails, and log/behavior constraints. Treat these items as authoritative where they differ later.

- Data providers (no new deps in Engine): use embedded static C# tables for curated effects; no JSON parsing inside HSIntel.Engine.
- Hero attacks: implement as an internal branch in BoardSimulator.SimulateAttack using an engine-only metadata flag; handle durability, retaliation, Taunt/Frozen, and keep AttackSummary format.
- Direct damage / hero power: apply curated damage in SimulatePlayCard/SimulateHeroPower with proper hand/mana updates; no extra log tags.
- Secrets risk: do not change StateEvaluator or EvaluationResult; apply as ordering/priority adjustment (MoveGenerator or enqueue-time) and gate by config.
- Sequencing: enforce via MoveGenerator ordering first; add cheap legality dry-run only when reordering heterogeneous actions.
- RNG: curated resolvers only; cap to min(50ms, MaxComputeMs/5); when disabled, log the single existing RNG skipped line.
- Config: no edits to HSIntel.Core in this phase; use internal Engine toggles and existing EnableRollouts/RolloutSamples.
- Logging: keep AttackSummary and BeamResult formats exactly; Secrets logs remain concise.
- Canonical paths: use HDT/HSIntel.Engine; avoid mirroring unless necessary.
- Snippets: code blocks are illustrative; integrate with existing methods and types (no pseudo helpers).

## 1. Scope & Goals

### 1.1 What We're Adding Now

**Primary Features:**
1. **Hero attacks in lethal math** - weapon swings and hero power damage
2. **Hand direct damage** - curated spell allowlist for lethal calculations
3. **Secret risk penalties** - scoring adjustments based on unproven opponent secrets
4. **RNG rollouts** - 64-sample Monte Carlo for probabilistic lethal
5. **Sequencing enforcement** - reordering actions per `/docs/sequencing.md` rules

**Supporting Infrastructure:**
- Curated JSON data files (`/data/*.json`) for deterministic card effect lookups
- Internal `RiskAdjuster` wrapper for secret penalties (no public API changes)
- Internal `ActionSequencer` for reordering (toggleable)
- Internal `IRandomResolver` registry for RNG effects

### 1.2 Non-Goals for This Phase

- Complex on-board auras (e.g., Stormwind Champion buffs) beyond what's needed for correctness
- Full spell rules engine (only curated direct damage)
- UI changes for probability display (log-only for now)
- Performance improvements to BeamSearch itself (maintain current behavior)
- Localized card text parsing (strictly ID-based)

---

## 2. Architecture Strategy

### 2.1 Hero Attacks (Option A - Preferred)

**Problem:** `GameAction.Attacker` is an `Entity` reference; heroes aren't in the minion entity list.

**Solution:** Engine-internal metadata flag without changing Core models.

```csharp
// Internal to HSIntel.Engine only
internal class GameActionMetadata
{
    public bool IsHeroAttack { get; set; }
    public int WeaponDamage { get; set; }
}

// Attach to GameAction via internal dictionary (keyed by action instance)
internal static class ActionMetadataRegistry
{
    private static ConditionalWeakTable<GameAction, GameActionMetadata> _metadata = new();
    
    public static void SetHeroAttack(GameAction action, int weaponDamage) 
    {
        var meta = _metadata.GetOrCreateValue(action);
        meta.IsHeroAttack = true;
        meta.WeaponDamage = weaponDamage;
    }
    
    public static bool IsHeroAttack(GameAction action, out int damage)
    {
        if (_metadata.TryGetValue(action, out var meta) && meta.IsHeroAttack)
        {
            damage = meta.WeaponDamage;
            return true;
        }
        damage = 0;
        return false;
    }
}
```

**Usage in `BoardSimulator.SimulateAttack`:**
```csharp
if (ActionMetadataRegistry.IsHeroAttack(action, out int weaponDamage))
{
    // Handle hero swing
    var target = action.Target; // minion or enemy hero
    ApplyDamageToTarget(target, weaponDamage, fromHero: true);
    LogAttackSummary("Friendly Hero", target, weaponDamage);
}
else
{
    // Existing minion attack logic
}
```

### 2.2 Direct Damage (Curated Tables, No JSON in Engine)

Use internal static dictionaries for allowlisted direct-damage spells (e.g., Fireball, Frostbolt) with fields: cardId, damage, target(face|minion|flex), manaCost. Apply only when legal and mana allows; ignore unknown cards.

Integrate in Simulator:
- SimulatePlayCard: remove card from hand, spend mana, apply curated damage; no new log tags.
- RNG spells (e.g., Arcane Missiles): include only when rollouts are enabled and within the time budget via a curated resolver; otherwise, skip in fast-path.

### 2.3 Hero Powers (Curated Tables)

Provide a small internal table of damage-dealing hero powers (e.g., Mage ping, Hunter steady shot) with fields: heroClass/cardId, damage, target, manaCost=2. Extend SimulateHeroPower to spend mana and apply curated damage. Do not attempt non-damage powers in this phase.

### 2.4 RNG System

**Interface:**
```csharp
internal interface IRandomResolver
{
    string EffectName { get; }
    int[] ResolveDeterministic(GameContext context, Card card); // [min, max] damage
    LethalProbability ResolveWithRollouts(GameContext context, Card card, int samples);
}

internal class LethalProbability
{
    public float Probability { get; set; } // 0.0 - 1.0
    public int[] DamageRange { get; set; } // [min, max] observed
    public int SamplesRun { get; set; }
}
```

**Registry:**
```csharp
internal class RandomResolverRegistry
{
    private Dictionary<string, IRandomResolver> _resolvers = new();
    
    public void Register(string cardId, IRandomResolver resolver)
    {
        _resolvers[cardId] = resolver;
    }
    
    public bool TryResolve(string cardId, out IRandomResolver resolver)
    {
        return _resolvers.TryGetValue(cardId, out resolver);
    }
}
```

**Example Resolver:**
```csharp
internal class ArcaneMissilesResolver : IRandomResolver
{
    public string EffectName => "Arcane Missiles (3 random)";
    
    public int[] ResolveDeterministic(GameContext context, Card card)
    {
        // Worst case: all hit taunts with high HP
        // Best case: all 3 hit face
        int enemyTaunts = context.Board.EnemyMinions.Count(m => m.HasTaunt);
        return enemyTaunts > 0 ? new[] {0, 3} : new[] {3, 3};
    }
    
    public LethalProbability ResolveWithRollouts(GameContext context, Card card, int samples)
    {
        var rng = new Random();
        int lethalCount = 0;
        int[] range = {int.MaxValue, 0};
        
        for (int i = 0; i < samples; i++)
        {
            int faceDamage = SimulateMissiles(context, rng);
            range[0] = Math.Min(range[0], faceDamage);
            range[1] = Math.Max(range[1], faceDamage);
            
            if (context.OpponentHero.Health - faceDamage <= 0)
                lethalCount++;
        }
        
        return new LethalProbability
        {
            Probability = (float)lethalCount / samples,
            DamageRange = range,
            SamplesRun = samples
        };
    }
    
    private int SimulateMissiles(GameContext context, Random rng)
    {
        // Simplified: count valid targets, randomly distribute 3 hits
        var targets = new List<DamageTarget>();
        targets.Add(new DamageTarget { IsFace = true, Health = context.OpponentHero.Health });
        foreach (var minion in context.Board.EnemyMinions)
            targets.Add(new DamageTarget { IsFace = false, Health = minion.Health });
        
        int faceDamage = 0;
        for (int i = 0; i < 3; i++)
        {
            var target = targets[rng.Next(targets.Count)];
            if (target.IsFace)
                faceDamage++;
            else
            {
                target.Health--;
                if (target.Health <= 0)
                    targets.Remove(target);
            }
        }
        return faceDamage;
    }
}
```

### 2.5 Secret Risk System

**No Changes to EvaluationResult or StateEvaluator Public API**

```csharp
// Internal wrapper in HSIntel.Engine/Evaluation/
internal class RiskAdjuster
{
    private SecretHandler _secretHandler;
    private HSIntelConfig _config;
    
    public EvaluationResult AdjustForSecrets(
        EvaluationResult rawResult, 
        GameAction[] actionSequence,
        GameContext context)
    {
        if (!_config.EnableSecretRiskPenalties)
            return rawResult;
        
        float penalty = _secretHandler.GetRiskForActionSequence(actionSequence, context);
        
        // Return original result but track adjusted score internally
        // BeamSearch will read adjustedScore from internal dictionary
        float adjustedScore = rawResult.Score - penalty;
        
        InternalScoreRegistry.SetAdjustedScore(rawResult, adjustedScore);
        
        return rawResult; // Unchanged for logging
    }
}

internal static class InternalScoreRegistry
{
    private static ConditionalWeakTable<EvaluationResult, AdjustedScoreHolder> _scores = new();
    
    public static void SetAdjustedScore(EvaluationResult result, float adjusted)
    {
        var holder = _scores.GetOrCreateValue(result);
        holder.AdjustedScore = adjusted;
    }
    
    public static float GetAdjustedScore(EvaluationResult result)
    {
        if (_scores.TryGetValue(result, out var holder))
            return holder.AdjustedScore;
        return result.Score; // Fallback to raw
    }
}

internal class AdjustedScoreHolder { public float AdjustedScore; }
```

**In BeamSearch (internal change only):**
```csharp
// When sorting nodes for beam pruning:
var sortedNodes = currentLayer
    .OrderByDescending(n => InternalScoreRegistry.GetAdjustedScore(n.Evaluation))
    .Take(_beamWidth)
    .ToList();

// But log with raw score:
Trace.WriteLine($"[HSIntel][Engine] BeamResult: depth={depth} score={n.Evaluation.Score:F2}");
```

### 2.6 Sequencing Enforcement

**ActionSequencer (internal class):**
```csharp
internal class ActionSequencer
{
    public GameAction[] Reorder(GameAction[] actions, GameContext context)
    {
        if (actions.Length <= 1)
            return actions;
        
        var reordered = new List<GameAction>();
        
        // Rule 1: Information first (draw/discover)
        reordered.AddRange(actions.Where(IsInformationAction));
        
        // Rule 2: Buffs before summons
        reordered.AddRange(actions.Where(IsBuffAction));
        reordered.AddRange(actions.Where(IsSummonAction));
        
        // Rule 3: Small spells first (Counterspell probes)
        var spells = actions.Where(IsSpellAction).OrderBy(GetManaCost).ToList();
        reordered.AddRange(spells);
        
        // Rule 4: Taunts before face
        var attacks = actions.Where(a => a.Type == GameActionType.Attack).ToList();
        reordered.AddRange(attacks.Where(TargetsTaunt));
        reordered.AddRange(attacks.Where(TargetsFace));
        
        // Validation: Does reordering violate mana/legality?
        if (!IsLegalSequence(reordered, context))
        {
            Trace.WriteLine("[HSIntel][Engine] Sequencing: reverted (illegal after reorder)");
            return actions; // Fallback to original
        }
        
        return reordered.ToArray();
    }
    
    private bool IsLegalSequence(List<GameAction> sequence, GameContext context)
    {
        // Dry-run simulation to check mana/targets still valid
        var testContext = context.Clone();
        int manaRemaining = testContext.CurrentMana;
        
        foreach (var action in sequence)
        {
            if (action.Type == GameActionType.PlayCard)
            {
                int cost = GetManaCost(action);
                if (cost > manaRemaining)
                    return false;
                manaRemaining -= cost;
            }
            
            // Check target still exists (e.g., didn't kill taunt earlier)
            if (!testContext.IsValidTarget(action))
                return false;
        }
        
        return true;
    }
}
```

---

## 3. Changes by File/Module

### 3.1 `HDT/HSIntel.Engine/Lethal/LethalSolver.cs`

**Existing:** Minion-only lethal with taunt clears.

**Add:**

```csharp
// New methods (internal):
private int CalculateHeroPowerDamage(GameContext context)
{
    if (context.HeroPowerUsedThisTurn || context.CurrentMana < 2)
        return 0;
    
    var heroPower = _heroPowerProvider.GetPower(context.FriendlyHero.Class);
    if (heroPower != null && heroPower.Damage > 0)
        return heroPower.Damage;
    
    return 0;
}

private int CalculateWeaponDamage(GameContext context)
{
    if (context.FriendlyHero.Weapon == null || context.FriendlyHero.IsFrozen)
        return 0;
    
    // Can only swing if no untargetable taunts block
    var taunts = context.Board.EnemyMinions.Where(m => m.HasTaunt).ToList();
    if (taunts.Any(t => !CanHeroAttack(context.FriendlyHero, t)))
        return 0;
    
    return context.FriendlyHero.Weapon.Attack;
}

private List<DirectDamagePlay> GetDirectDamageOptions(GameContext context)
{
    var options = new List<DirectDamagePlay>();
    
    foreach (var card in context.Hand)
    {
        if (_directDamageProvider.TryGetDamage(card.CardId, out var damageCard))
        {
            if (damageCard.ManaCost <= context.CurrentMana &&
                (damageCard.Target == TargetType.Face || damageCard.Target == TargetType.Flex))
            {
                options.Add(new DirectDamagePlay
                {
                    Card = card,
                    Damage = damageCard.Damage,
                    ManaCost = damageCard.ManaCost,
                    IsRandom = damageCard.Target == TargetType.Random
                });
            }
        }
    }
    
    return options.OrderByDescending(o => o.Damage).ToList();
}

public LethalPlan? TryFindLethal(GameContext context, HSIntelConfig config)
{
    // Phase 1: Calculate guaranteed damage sources
    int minionDamage = CalculateMinionFaceDamage(context); // existing
    int weaponDamage = config.EnableHeroAttackInFastPath ? CalculateWeaponDamage(context) : 0;
    int heroPowerDamage = CalculateHeroPowerDamage(context);
    
    var spellOptions = config.EnableDirectDamageInFastPath 
        ? GetDirectDamageOptions(context)
        : new List<DirectDamagePlay>();
    
    // Phase 2: Check simple lethal (no taunts or minimal clears)
    int guaranteedDamage = minionDamage + weaponDamage + heroPowerDamage;
    int spellDamage = spellOptions.Where(s => !s.IsRandom).Sum(s => s.Damage);
    
    if (guaranteedDamage + spellDamage >= context.OpponentHero.Health)
    {
        return BuildLethalPlan(context, weaponDamage > 0, heroPowerDamage > 0, spellOptions);
    }
    
    // Phase 3: Check with taunt removal (existing logic + spells)
    // ... existing taunt-clear logic, now include weapon/spells ...
    
    return null; // No lethal found
}

private LethalPlan BuildLethalPlan(
    GameContext context, 
    bool includeWeapon, 
    bool includeHeroPower,
    List<DirectDamagePlay> spells)
{
    var actions = new List<GameAction>();
    
    // Order: taunt clears → spells → minion face → weapon → hero power
    
    // 1. Clear taunts (existing)
    actions.AddRange(GetTauntClearActions(context));
    
    // 2. Direct damage spells (largest first for sequencing)
    foreach (var spell in spells.OrderByDescending(s => s.ManaCost))
    {
        actions.Add(new GameAction
        {
            Type = GameActionType.PlayCard,
            Card = spell.Card,
            Target = context.Board.OpponentHero
        });
    }
    
    // 3. Minion face swings (existing)
    actions.AddRange(GetMinionFaceActions(context));
    
    // 4. Weapon swing
    if (includeWeapon)
    {
        var weaponAction = new GameAction
        {
            Type = GameActionType.Attack,
            Attacker = null, // Hero attack signal
            Target = context.Board.OpponentHero
        };
        ActionMetadataRegistry.SetHeroAttack(weaponAction, context.FriendlyHero.Weapon.Attack);
        actions.Add(weaponAction);
    }
    
    // 5. Hero power
    if (includeHeroPower)
    {
        actions.Add(new GameAction
        {
            Type = GameActionType.HeroPower,
            Target = context.Board.OpponentHero
        });
    }
    
    return new LethalPlan
    {
        Actions = actions.ToArray(),
        TotalDamage = CalculateTotalDamage(actions, context),
        IsGuaranteed = !spells.Any(s => s.IsRandom)
    };
}
```

**Performance Budget:** Keep under 2ms by:
- Hard cap on spell permutations: max 3 spells considered
- Greedy ordering, no exhaustive search
- Early exit when first lethal found

### 3.2 `HDT/HSIntel.Engine/Simulator/BoardSimulator.cs`

**Existing:** `SimulateAttack` for minion combat.

**Modify:**

```csharp
public GameContext SimulateAttack(GameContext context, GameAction action)
{
    var newContext = context.Clone();
    
    // Check for hero attack
    if (ActionMetadataRegistry.IsHeroAttack(action, out int weaponDamage))
    {
        var target = action.Target;
        
        if (target.IsHero)
        {
            newContext.OpponentHero.Health -= weaponDamage;
            LogAttackSummary("Friendly Hero", "Enemy Hero", weaponDamage);
        }
        else
        {
            var targetMinion = newContext.Board.EnemyMinions.First(m => m.EntityId == target.EntityId);
            targetMinion.Health -= weaponDamage;
            
            // Hero takes counter-damage
            newContext.FriendlyHero.Health -= targetMinion.Attack;
            
            LogAttackSummary("Friendly Hero", targetMinion.Name, weaponDamage);
            
            if (targetMinion.Health <= 0)
                newContext.Board.EnemyMinions.Remove(targetMinion);
        }
        
        // Weapon durability (if tracking)
        if (newContext.FriendlyHero.Weapon != null)
        {
            newContext.FriendlyHero.Weapon.Durability--;
            if (newContext.FriendlyHero.Weapon.Durability <= 0)
                newContext.FriendlyHero.Weapon = null;
        }
        
        return newContext;
    }
    
    // Existing minion attack logic
    // ...
}

public GameContext SimulateSpell(GameContext context, GameAction action)
{
    var newContext = context.Clone();
    
    if (_directDamageProvider.TryGetDamage(action.Card.CardId, out var damageCard))
    {
        if (damageCard.Target == TargetType.Random)
        {
            // In fast-path: apply worst-case deterministic
            // In rollout: use RNG resolver
            if (_config.EnableRollouts)
            {
                // Handled by RNG system
                return ApplyRandomDamage(newContext, action, damageCard);
            }
            else
            {
                // Worst case: all damage wasted on high-HP targets
                Trace.WriteLine("[HSIntel][Engine] RNG: skipped (disabled)");
                return newContext; // No damage applied
            }
        }
        else
        {
            // Deterministic damage
            var target = action.Target;
            if (target.IsHero)
            {
                newContext.OpponentHero.Health -= damageCard.Damage;
            }
            else
            {
                var minion = newContext.Board.EnemyMinions.First(m => m.EntityId == target.EntityId);
                minion.Health -= damageCard.Damage;
                if (minion.Health <= 0)
                    newContext.Board.EnemyMinions.Remove(minion);
            }
        }
    }
    
    newContext.CurrentMana -= action.Card.Cost;
    newContext.Hand.Remove(action.Card);
    
    return newContext;
}

private void LogAttackSummary(string attacker, string target, int damage)
{
    Trace.WriteLine($"[HSIntel][Engine] AttackSummary: {attacker} -> {target} ({damage} dmg)");
}
```

### 3.3 `HDT/HSIntel.Engine/MoveGen/MoveGenerator.cs`

**Add hero swing and spell generation (gated):**

```csharp
public IEnumerable<GameAction> GenerateActions(GameContext context, HSIntelConfig config)
{
    // Existing: minion attacks, card plays, hero power, end turn
    
    // NEW: Hero weapon attack
    if (config.EnableHeroAttackInFastPath && 
        context.FriendlyHero.Weapon != null && 
        !context.FriendlyHero.IsFrozen)
    {
        foreach (var target in GetValidHeroTargets(context))
        {
            var action = new GameAction
            {
                Type = GameActionType.Attack,
                Attacker = null,
                Target = target
            };
            ActionMetadataRegistry.SetHeroAttack(action, context.FriendlyHero.Weapon.Attack);
            yield return action;
        }
    }
    
    // NEW: Direct damage spells (only when safe/relevant)
    if (config.EnableDirectDamageInFastPath)
    {
        foreach (var card in context.Hand)
        {
            if (_directDamageProvider.TryGetDamage(card.CardId, out var damageCard) &&
                damageCard.ManaCost <= context.CurrentMana)
            {
                foreach (var target in GetValidSpellTargets(context, damageCard))
                {
                    yield return new GameAction
                    {
                        Type = GameActionType.PlayCard,
                        Card = card,
                        Target = target
                    };
                }
            }
        }
    }
}

private IEnumerable<Entity> GetValidHeroTargets(GameContext context)
{
    // Can attack any valid target (minions or face) unless taunts block
    var taunts = context.Board.EnemyMinions.Where(m => m.HasTaunt).ToList();
    
    if (taunts.Any())
    {
        // Must attack taunts
        foreach (var taunt in taunts)
            yield return taunt;
    }
    else
    {
        // Can go face or trade
        yield return context.Board.OpponentHero;
        foreach (var minion in context.Board.EnemyMinions)
            yield return minion;
    }
}
```

### 3.4 `HDT/HSIntel.Engine/Secrets/SecretHandler.cs`

**Extend with risk calculation:**

```csharp
internal class SecretRisk
{
    public string SecretName { get; set; }
    public float Penalty { get; set; }
    public string TriggerCondition { get; set; }
}

private Dictionary<string, SecretRisk> _secretRisks;

public SecretHandler(string dataPath)
{
    LoadSecretsRiskData(dataPath); // /data/secrets_risk.json
}

public float GetRiskForActionSequence(GameAction[] actions, GameContext context)
{
    if (!context.Board.OpponentSecrets.Any())
        return 0f;
    
    float totalPenalty = 0f;
    
    foreach (var secret in context.Board.OpponentSecrets)
    {
        if (_secretRisks.TryGetValue(secret.CardId, out var risk))
        {
            // Check if action sequence triggers this secret
            if (SequenceTriggersSecret(actions, risk, context))
            {
                totalPenalty += risk.Penalty;
                Trace.WriteLine($"[HSIntel][Engine] Secrets: risk={risk.Penalty:F2} ({risk.SecretName})");
            }
        }
    }
    
    return totalPenalty;
}

private bool SequenceTriggersSecret(GameAction[] actions, SecretRisk risk, GameContext context)
{
    // Example: Counterspell triggered by first expensive spell
    if (risk.SecretName == "Counterspell")
    {
        var firstSpell = actions.FirstOrDefault(a => a.Type == GameActionType.PlayCard);
        if (firstSpell != null && firstSpell.Card.Cost >= 3)
            return true;
    }
    
    // Example: Explosive Trap triggered by face attack
    if (risk.SecretName == "Explosive Trap")
    {
        var faceAttack = actions.FirstOrDefault(a => 
            a.Type == GameActionType.Attack && 
            a.Target.IsHero);
        if (faceAttack != null)
            return true;
    }
    
    // ... more secret logic ...
    
    return false;
}
```

### 3.5 `HDT/HSIntel.Engine/Search/BeamSearch.cs`

**Internal modification only (no signature change):**

```csharp
private RiskAdjuster _riskAdjuster;
private ActionSequencer _sequencer;

public BeamSearch(/* existing params */)
{
    // ... existing init ...
    _riskAdjuster = new RiskAdjuster(_secretHandler, _config);
    _sequencer = new ActionSequencer();
}

public SearchResult Search(GameContext initialContext, HSIntelConfig config)
{
    // ... existing search loop ...
    
    foreach (var node in currentLayer)
    {
        var actions = _moveGenerator.GenerateActions(node.State, config);
        
        // NEW: Apply sequencing if enabled
        if (config.EnableSequencingEnforcement)
        {
            actions = _sequencer.Reorder(actions.ToArray(), node.State);
        }
        
        foreach (var action in actions)
        {
            var newState = _simulator.Simulate(node.State, action);
            var rawEval = _evaluator.Evaluate(newState);
            
            // NEW: Apply secret risk adjustment
            var adjustedEval = _riskAdjuster.AdjustForSecrets(rawEval, node.GetActionPath().Append(action), newState);
            
            var newNode = new SearchNode
            {
                State = newState,
                Action = action,
                Evaluation = adjustedEval, // Store original for logging
                Parent = node
            };
            
            nextLayer.Add(newNode);
        }
    }
    
    // Sort by adjusted score (internal lookup)
    currentLayer = nextLayer
        .OrderByDescending(n => InternalScoreRegistry.GetAdjustedScore(n.Evaluation))
        .Take(_beamWidth)
        .ToList();
    
    // Log with RAW score (unchanged format)
    Trace.WriteLine($"[HSIntel][Engine] BeamResult: depth={depth} score={currentLayer[0].Evaluation.Score:F2}");
}
```

### 3.6 `HDT/HSIntel.Engine/Services/DecisionEngineCoordinator.cs`

**Add config toggles:**

```csharp
public async Task<SearchResult> ComputeDecisionAsync(GameContext context)
{
    var config = _configProvider.GetConfig();
    
    // Existing: Try lethal fast-path first
    if (config.EnableHeroAttackInFastPath || config.EnableDirectDamageInFastPath)
    {
        var lethalPlan = _lethalSolver.TryFindLethal(context, config);
        if (lethalPlan != null)
        {
            // Apply plan via simulator to build SearchResult
            var finalState = ApplyPlan(context, lethalPlan);
            var result = new SearchResult
            {
                BestLine = lethalPlan.Actions,
                FinalState = finalState,
                IsLethal = true,
                Confidence = lethalPlan.IsGuaranteed ? 1.0f : 0.85f
            };
            
            RaiseDecisionComputed(result);
            return result;
        }
    }
    
    // Existing: Run beam search if no lethal
    var searchResult = await _beamSearch.SearchAsync(context, config);
    
    // NEW: Check if RNG rollout needed
    if (config.EnableRollouts && ShouldRunRollouts(searchResult, config))
    {
        searchResult = await RunRolloutsAsync(context, searchResult, config);
    }
    
    RaiseDecisionComputed(searchResult);
    return searchResult;
}

private bool ShouldRunRollouts(SearchResult result, HSIntelConfig config)
{
    // Trigger rollouts if:
    // 1. Near-lethal with RNG cards involved
    // 2. High-swing scenario (score delta >= 6.0)
    
    var hasRngCards = result.BestLine.Any(a => 
        a.Type == GameActionType.PlayCard && 
        _rngRegistry.HasResolver(a.Card.CardId));
    
    if (!hasRngCards)
        return false;
    
    var remainingBudget = config.MaxComputeMs - result.ComputeTimeMs;
    return remainingBudget >= 50; // Need at least 50ms for rollouts
}

private async Task<SearchResult> RunRolloutsAsync(GameContext context, SearchResult deterministicResult, HSIntelConfig config)
{
    var rolloutBudget = Math.Min(50, config.MaxComputeMs / 5);
    var samples = config.RolloutSamples; // default 64
    
    Stopwatch sw = Stopwatch.StartNew();
    int successCount = 0;
    
    for (int i = 0; i < samples && sw.ElapsedMilliseconds < rolloutBudget; i++)
    {
        var rolloutContext = context.Clone();
        
        // Apply deterministic actions, resolve RNG randomly
        foreach (var action in deterministicResult.BestLine)
        {
            if (action.Type == GameActionType.PlayCard && 
                _rngRegistry.TryResolve(action.Card.CardId, out var resolver))
            {
                // Resolve with random sample
                rolloutContext = resolver.ApplyRandomOutcome(rolloutContext, action, i);
            }
            else
            {
                rolloutContext = _simulator.Simulate(rolloutContext, action);
            }
        }
        
        if (rolloutContext.OpponentHero.Health <= 0)
            successCount++;
    }
    
    int samplesRun = Math.Min(samples, (int)(sw.ElapsedMilliseconds / (rolloutBudget / (float)samples)));
    float probability = (float)successCount / samplesRun;
    
    Trace.WriteLine($"[HSIntel][Engine] RNG: probability={probability:F2} samples={samplesRun}");
    
    return new SearchResult
    {
        BestLine = deterministicResult.BestLine,
        FinalState = deterministicResult.FinalState,
        IsLethal = probability >= 0.5f,
        Confidence = probability,
        RolloutSamples = samplesRun
    };
}
```

### 3.7 Data Files

Create `/data/direct_damage.json`:
```json
{
  "version": "1.0",
  "cards": [
    {"cardId": "CS2_029", "name": "Fireball", "damage": 6, "target": "flex", "manaCost": 4},
    {"cardId": "CS2_024", "name": "Frostbolt", "damage": 3, "target": "flex", "manaCost": 2},
    {"cardId": "EX1_277", "name": "Arcane Missiles", "damage": 3, "target": "random", "manaCost": 1},
    {"cardId": "CS2_037", "name": "Frost Shock", "damage": 1, "target": "flex", "manaCost": 1},
    {"cardId": "EX1_238", "name": "Lightning Bolt", "damage": 3, "target": "flex", "manaCost": 1},
    {"cardId": "CS2_093", "name": "Consecration", "damage": 2, "target": "aoe", "manaCost": 4},
    {"cardId": "DS1_185", "name": "Arcane Shot", "damage": 2, "target": "flex", "manaCost": 1},
    {"cardId": "EX1_539", "name": "Kill Command", "damage": 3, "target": "flex", "manaCost": 3, "notes": "Base damage, ignores beast conditional"}
  ]
}
```

Create `/data/hero_powers.json`:
```json
{
  "version": "1.0",
  "powers": [
    {"heroClass": "MAGE", "cardId": "CS2_034", "name": "Fireblast", "damage": 1, "target": "flex", "manaCost": 2},
    {"heroClass": "HUNTER", "cardId": "DS1h_292", "name": "Steady Shot", "damage": 2, "target": "face", "manaCost": 2},
    {"heroClass": "DRUID", "cardId": "CS2_017", "name": "Shapeshift", "damage": 1, "target": "hero_buff", "manaCost": 2},
    {"heroClass": "PRIEST", "cardId": "CS1h_001", "name": "Lesser Heal", "damage": -2, "target": "flex", "manaCost": 2, "notes": "Negative = healing"}
  ]
}
```

Create `/data/secrets_risk.json`:
```json
{
  "version": "1.0",
  "secrets": [
    {
      "class": "MAGE",
      "secretId": "EX1_287",
      "name": "Counterspell",
      "triggers": ["first_spell_cost_3_plus"],
      "penalty": 8.0,
      "probeHint": "Play cheap spell first"
    },
    {
      "class": "HUNTER",
      "secretId": "EX1_554",
      "name": "Explosive Trap",
      "triggers": ["face_attack"],
      "penalty": 6.0,
      "probeHint": "Attack with low-value minion first"
    },
    {
      "class": "PALADIN",
      "secretId": "EX1_130",
      "name": "Noble Sacrifice",
      "triggers": ["face_attack"],
      "penalty": 3.0,
      "probeHint": "Attack with any minion to trigger"
    },
    {
      "class": "ROGUE",
      "secretId": "EX1_287",
      "name": "Evasion",
      "triggers": ["face_damage"],
      "penalty": 5.0,
      "probeHint": "Ping hero with cheap damage first"
    }
  ]
}
```

Create `/data/rng_effects.json`:
```json
{
  "version": "1.0",
  "effects": [
    {
      "cardId": "EX1_277",
      "name": "Arcane Missiles",
      "type": "random_split",
      "resolver": "ArcaneMissilesResolver",
      "params": {"missiles": 3, "damage_each": 1}
    },
    {
      "cardId": "NEW1_007",
      "name": "Avenging Wrath",
      "type": "random_split",
      "resolver": "AvengingWrathResolver",
      "params": {"missiles": 8, "damage_each": 1}
    }
  ]
}
```

### 3.8 `HDT/Hearthstone Deck Tracker/HSIntel/HdtGameStateSource.cs`

**No changes required** (snapshot provider already aligned in Phase 6 initial work).

---

## 4. Backwards Compatibility and Risk Mitigation

### 4.1 No Breaking Changes

- **HSIntel.Core:** No public API modifications
- **HSIntel.Overlay:** No changes
- **HSIntel.Engine public surface:** BeamSearch signature unchanged; all new classes are `internal`

### 4.2 Config Toggles (Conservative Defaults)

```csharp
// In HSIntelConfig
public bool EnableHeroAttackInFastPath { get; set; } = true;  // Safe, well-tested
public bool EnableDirectDamageInFastPath { get; set; } = false; // Opt-in
public bool EnableSecretRiskPenalties { get; set; } = false;    // Opt-in
public bool EnableSequencingEnforcement { get; set; } = false;  // Opt-in
public bool EnableRollouts { get; set; } = false;               // Opt-in
public int RolloutSamples { get; set; } = 64;
```

### 4.3 Unknown Cards/Effects: Graceful Degradation

```csharp
// If card not in allowlist, ignore rather than crash
if (!_directDamageProvider.TryGetDamage(cardId, out var damage))
{
    // Fall back to existing v1 behavior (don't include in lethal calc)
    continue;
}
```

### 4.4 Telemetry (Unchanged Log Formats)

- `[HSIntel][Engine] AttackSummary: ...` - format unchanged
- `[HSIntel][Engine] BeamResult: ...` - format unchanged
- **New (additive):**
  - `[HSIntel][Engine] Secrets: risk={penalty:F2} probe={hint}`
  - `[HSIntel][Engine] RNG: probability={prob:F2} samples={n}`
  - `[HSIntel][Engine] Sequencing: applied|reverted`

---

## 5. Rollouts and RNG

### 5.1 When EnableRollouts=true

**Trigger Conditions:**
1. Near-lethal with RNG cards (e.g., Arcane Missiles when opponent at 3 HP)
2. High-swing scenarios (score delta >= 6.0) with RNG involved
3. Sufficient time budget remaining (>= 50ms)

**Process:**
```csharp
1. Run deterministic search (existing)
2. Identify RNG actions in best line
3. For each RNG action:
   - Invoke resolver.ResolveWithRollouts(context, card, 64)
   - Sample 64 random outcomes (or until 50ms budget exhausted)
   - Track lethal success rate
4. Aggregate probabilities
5. Log: "[HSIntel][Engine] RNG: probability=0.67 samples=64"
```

**Time Budget:**
- Max 50ms for all rollouts
- Or MaxComputeMs/5, whichever is smaller
- Early stop if confidence >= 0.95 after 32 samples

### 5.2 When EnableRollouts=false

- Log: `[HSIntel][Engine] RNG: skipped (disabled)` (already implemented)
- Use deterministic worst-case bounds for evaluation
- Mark lethal as "uncertain" in SearchResult

---

## 6. Sequencing Enforcement

### 6.1 Rules from `/docs/sequencing.md`

1. **Information first:** Draw/discover before playing
2. **Buffs before summons:** Buff cards before minions that benefit
3. **Small spells first:** Test for Counterspell with cheap spells
4. **Taunts before face:** Clear taunts before face attacks

### 6.2 Implementation

```csharp
// In ActionSequencer.Reorder()
var buckets = new Dictionary<string, List<GameAction>>
{
    ["info"] = actions.Where(IsInformationAction).ToList(),
    ["buffs"] = actions.Where(IsBuffAction).ToList(),
    ["summons"] = actions.Where(IsSummonAction).ToList(),
    ["spells"] = actions.Where(IsSpellAction).OrderBy(GetManaCost).ToList(),
    ["taunt_attacks"] = actions.Where(a => a.Type == GameActionType.Attack && TargetsTaunt(a)).ToList(),
    ["face_attacks"] = actions.Where(a => a.Type == GameActionType.Attack && TargetsFace(a)).ToList(),
    ["other"] = actions.Where(IsOtherAction).ToList()
};

var reordered = buckets["info"]
    .Concat(buckets["buffs"])
    .Concat(buckets["summons"])
    .Concat(buckets["spells"])
    .Concat(buckets["taunt_attacks"])
    .Concat(buckets["face_attacks"])
    .Concat(buckets["other"])
    .ToList();
```

### 6.3 Validation

- **Dry-run simulation** checks if reordering creates illegal state
- If illegal: revert to original order, log warning
- If legal: use reordered sequence

---

## 7. Testing and Acceptance Criteria

### 7.1 Unit Tests (Engine-only)

**Location:** `HSIntel.Engine.Tests/` (create if not exists)

```csharp
[TestClass]
public class LethalSolverTests
{
    [TestMethod]
    public void TryFindLethal_MinionsPlusWeapon_FindsLethal()
    {
        var context = TestContextBuilder.Create()
            .WithFriendlyHero(health: 20, weapon: new Weapon(3, 2))
            .WithFriendlyMinion(3, 2)
            .WithOpponentHero(health: 6)
            .Build();
        
        var config = new HSIntelConfig { EnableHeroAttackInFastPath = true };
        var solver = new LethalSolver(/* deps */);
        
        var plan = solver.TryFindLethal(context, config);
        
        Assert.IsNotNull(plan);
        Assert.AreEqual(2, plan.Actions.Length); // Minion + hero swing
        Assert.IsTrue(plan.IsGuaranteed);
    }
    
    [TestMethod]
    public void TryFindLethal_DirectDamageSpell_FindsLethal()
    {
        var context = TestContextBuilder.Create()
            .WithFriendlyHero(health: 20)
            .WithFriendlyMinion(2, 2)
            .WithOpponentHero(health: 8)
            .WithCardInHand("CS2_029") // Fireball (6 damage)
            .WithMana(5)
            .Build();
        
        var config = new HSIntelConfig { EnableDirectDamageInFastPath = true };
        var solver = new LethalSolver(/* deps */);
        
        var plan = solver.TryFindLethal(context, config);
        
        Assert.IsNotNull(plan);
        Assert.AreEqual(2, plan.Actions.Length); // Fireball + minion
        Assert.AreEqual(GameActionType.PlayCard, plan.Actions[0].Type);
    }
    
    // ... 10 more test cases covering:
    // - Hero power lethal
    // - Taunt removal + weapon
    // - Multiple spells combo
    // - RNG spell deterministic bounds
}

[TestClass]
public class BoardSimulatorTests
{
    [TestMethod]
    public void SimulateAttack_HeroSwing_DamagesTarget()
    {
        var context = TestContextBuilder.Create()
            .WithFriendlyHero(health: 20, weapon: new Weapon(3, 2))
            .WithOpponentMinion(id: 1, health: 5, attack: 2, hasStaunt: true)
            .Build();
        
        var action = new GameAction { Type = GameActionType.Attack, Target = context.Board.EnemyMinions[0] };
        ActionMetadataRegistry.SetHeroAttack(action, 3);
        
        var simulator = new BoardSimulator(/* deps */);
        var result = simulator.SimulateAttack(context, action);
        
        Assert.AreEqual(2, result.Board.EnemyMinions[0].Health); // 5 - 3
        Assert.AreEqual(18, result.FriendlyHero.Health); // 20 - 2 counter
    }
    
    [TestMethod]
    public void SimulateSpell_Fireball_DamagesFace()
    {
        var context = TestContextBuilder.Create()
            .WithOpponentHero(health: 15)
            .WithCardInHand("CS2_029") // Fireball
            .WithMana(5)
            .Build();
        
        var action = new GameAction 
        { 
            Type = GameActionType.PlayCard, 
            Card = context.Hand[0],
            Target = context.Board.OpponentHero
        };
        
        var simulator = new BoardSimulator(/* deps */);
        var result = simulator.SimulateSpell(context, action);
        
        Assert.AreEqual(9, result.OpponentHero.Health); // 15 - 6
        Assert.AreEqual(1, result.CurrentMana); // 5 - 4
    }
}

[TestClass]
public class SecretHandlerTests
{
    [TestMethod]
    public void GetRiskForActionSequence_Counterspell_PenalizesExpensiveSpell()
    {
        var context = TestContextBuilder.Create()
            .WithOpponentSecret("EX1_287") // Counterspell
            .WithCardInHand("CS2_029") // Fireball (4 mana)
            .Build();
        
        var actions = new[]
        {
            new GameAction { Type = GameActionType.PlayCard, Card = context.Hand[0] }
        };
        
        var handler = new SecretHandler("/data/secrets_risk.json");
        var risk = handler.GetRiskForActionSequence(actions, context);
        
        Assert.AreEqual(8.0f, risk); // Penalty from data file
    }
    
    [TestMethod]
    public void GetRiskForActionSequence_ProbeFirst_NoRisk()
    {
        var context = TestContextBuilder.Create()
            .WithOpponentSecret("EX1_287") // Counterspell
            .WithCardInHand("CS2_025") // Arcane Intellect (3 mana)
            .WithCardInHand("CS2_024") // Frostbolt (
```csharp
        .WithCardInHand("CS2_024") // Frostbolt (2 mana)
            .WithCardInHand("CS2_029") // Fireball (4 mana)
            .Build();
        
        var actions = new[]
        {
            new GameAction { Type = GameActionType.PlayCard, Card = context.Hand[1] }, // Probe with Frostbolt
            new GameAction { Type = GameActionType.PlayCard, Card = context.Hand[2] }  // Then Fireball
        };
        
        var handler = new SecretHandler("/data/secrets_risk.json");
        var risk = handler.GetRiskForActionSequence(actions, context);
        
        Assert.AreEqual(0f, risk); // No risk, probed first
    }
}

[TestClass]
public class ActionSequencerTests
{
    [TestMethod]
    public void Reorder_BuffsBeforeSummons_CorrectOrder()
    {
        var context = TestContextBuilder.Create().Build();
        
        var actions = new[]
        {
            new GameAction { Type = GameActionType.PlayCard, Card = CreateCard("Minion", 3) },
            new GameAction { Type = GameActionType.PlayCard, Card = CreateCard("Buff", 2) },
        };
        
        var sequencer = new ActionSequencer();
        var reordered = sequencer.Reorder(actions, context);
        
        Assert.AreEqual("Buff", reordered[0].Card.Type);
        Assert.AreEqual("Minion", reordered[1].Card.Type);
    }
    
    [TestMethod]
    public void Reorder_SmallSpellsFirst_CorrectOrder()
    {
        var context = TestContextBuilder.Create().Build();
        
        var actions = new[]
        {
            new GameAction { Type = GameActionType.PlayCard, Card = CreateCard("Fireball", 4) },
            new GameAction { Type = GameActionType.PlayCard, Card = CreateCard("Frostbolt", 2) },
        };
        
        var sequencer = new ActionSequencer();
        var reordered = sequencer.Reorder(actions, context);
        
        Assert.AreEqual(2, reordered[0].Card.Cost); // Frostbolt first
        Assert.AreEqual(4, reordered[1].Card.Cost); // Fireball second
    }
    
    [TestMethod]
    public void Reorder_TauntsBeforeFace_CorrectOrder()
    {
        var context = TestContextBuilder.Create()
            .WithEnemyMinion(id: 1, health: 2, attack: 1, hasTaunt: true)
            .Build();
        
        var actions = new[]
        {
            new GameAction { Type = GameActionType.Attack, Target = context.Board.OpponentHero },
            new GameAction { Type = GameActionType.Attack, Target = context.Board.EnemyMinions[0] },
        };
        
        var sequencer = new ActionSequencer();
        var reordered = sequencer.Reorder(actions, context);
        
        Assert.IsTrue(reordered[0].Target.IsMinion); // Taunt first
        Assert.IsTrue(reordered[1].Target.IsHero);   // Face second
    }
}

[TestClass]
public class RNGResolverTests
{
    [TestMethod]
    public void ArcaneMissiles_Deterministic_ReturnsRange()
    {
        var context = TestContextBuilder.Create()
            .WithOpponentHero(health: 10)
            .WithEnemyMinion(health: 5, attack: 2, hasTaunt: true)
            .Build();
        
        var resolver = new ArcaneMissilesResolver();
        var range = resolver.ResolveDeterministic(context, CreateCard("EX1_277"));
        
        Assert.AreEqual(0, range[0]); // Worst: all wasted on taunt
        Assert.AreEqual(3, range[1]); // Best: all hit face
    }
    
    [TestMethod]
    public void ArcaneMissiles_Rollouts_CalculatesProbability()
    {
        var context = TestContextBuilder.Create()
            .WithOpponentHero(health: 3) // Lethal if 3 hit face
            .WithEnemyMinion(health: 5, attack: 2)
            .Build();
        
        var resolver = new ArcaneMissilesResolver();
        var result = resolver.ResolveWithRollouts(context, CreateCard("EX1_277"), samples: 64);
        
        Assert.IsTrue(result.Probability > 0.0f && result.Probability < 1.0f);
        Assert.AreEqual(64, result.SamplesRun);
        Assert.AreEqual(3, result.DamageRange[1]); // Max possible
    }
}
```

### 7.2 Self-Test 6 Scenarios (Expanded)

**Location:** `/tests/SelfTest6_Scenarios.md`

```markdown
# Self-Test 6: Lethal Fast-Path + Secrets + RNG

## Scenario 1: Board + Weapon Lethal
- **Setup:** Friendly 3/2 minion, hero with 4/2 weapon, opponent at 7 HP, no taunts
- **Expected:** Find lethal (minion 3 + weapon 4 = 7)
- **Config:** EnableHeroAttackInFastPath = true
- **Pass Criteria:** LethalPlan returned with 2 actions

## Scenario 2: Direct Damage Spell Lethal
- **Setup:** Friendly 2/2 minion, Fireball in hand (4 mana), opponent at 8 HP, 5 mana available
- **Expected:** Find lethal (minion 2 + Fireball 6 = 8)
- **Config:** EnableDirectDamageInFastPath = true
- **Pass Criteria:** LethalPlan with PlayCard(Fireball) + Attack

## Scenario 3: Hero Power Lethal
- **Setup:** Mage, 2x 3/2 minions, opponent at 7 HP, 2 mana available, hero power unused
- **Expected:** Find lethal (minions 6 + ping 1 = 7)
- **Pass Criteria:** LethalPlan includes HeroPower action

## Scenario 4: Taunt + Weapon Clear
- **Setup:** Hero with 3/2 weapon, 2/2 minion, enemy 2/2 taunt, opponent at 3 HP behind taunt
- **Expected:** Find lethal (weapon kills taunt, minion goes face)
- **Pass Criteria:** Hero attacks taunt first, then minion attacks face

## Scenario 5: Multi-Spell Combo
- **Setup:** Frostbolt (3 dmg) + Fireball (6 dmg) in hand, opponent at 9 HP, 6 mana
- **Expected:** Find lethal (3 + 6 = 9)
- **Config:** EnableDirectDamageInFastPath = true
- **Pass Criteria:** 2x PlayCard actions in correct order

## Scenario 6: Counterspell Risk
- **Setup:** Opponent has Counterspell (unprobed), player has Frostbolt + Fireball
- **Expected:** Secret risk penalty applied to naive Fireball first line
- **Config:** EnableSecretRiskPenalties = true
- **Pass Criteria:** Log shows "Secrets: risk=8.0"

## Scenario 7: Explosive Trap Risk
- **Setup:** Opponent has Explosive Trap, player about to attack face with 1/1
- **Expected:** Risk penalty applied, alternative lines prioritized
- **Config:** EnableSecretRiskPenalties = true
- **Pass Criteria:** Face attack deprioritized vs other plays

## Scenario 8: Secret Probe Order
- **Setup:** Unknown Mage secret, player has cheap + expensive spells
- **Expected:** Probe order logged: "small spell first"
- **Pass Criteria:** SecretHandler emits probe recommendation

## Scenario 9: RNG Lethal (Arcane Missiles)
- **Setup:** Opponent at 3 HP, 1x enemy minion (5 HP), Arcane Missiles in hand
- **Expected:** Probability logged (likely 0.15-0.30 depending on board)
- **Config:** EnableRollouts = true, RolloutSamples = 64
- **Pass Criteria:** Log shows "RNG: probability=0.XX samples=64"

## Scenario 10: RNG Disabled Fallback
- **Setup:** Same as #9 but rollouts disabled
- **Expected:** Deterministic worst-case assumed (0 face damage)
- **Config:** EnableRollouts = false
- **Pass Criteria:** Log shows "RNG: skipped (disabled)"

## Scenario 11: Sequencing - Buffs Before Summons
- **Setup:** Hand Blessing of Kings (buff) + Boulderfist Ogre (minion)
- **Expected:** Actions reordered if sequencer enabled
- **Config:** EnableSequencingEnforcement = true
- **Pass Criteria:** Buff played before minion in recommended line

## Scenario 12: Sequencing - Small Spells First
- **Setup:** Counterspell suspected, hand has Frostbolt (2) + Fireball (4)
- **Expected:** Frostbolt recommended first
- **Config:** EnableSequencingEnforcement = true
- **Pass Criteria:** Lower-cost spell prioritized

## Scenario 13: Taunt Removal Before Face
- **Setup:** 2x friendly minions, enemy has 1 taunt + face lethal available after clear
- **Expected:** Taunt cleared first, then face attacks
- **Config:** EnableSequencingEnforcement = true
- **Pass Criteria:** Attack actions properly ordered

## Scenario 14: Complex Lethal (All Features)
- **Setup:** Opponent at 12 HP, player has:
  - 3/2 minion
  - 2/1 weapon
  - Frostbolt (3 dmg)
  - Fireball (6 dmg)
  - 8 mana, hero power unused (Mage)
  - Enemy taunt: 2/2
- **Expected:** Find lethal via:
  1. Weapon kills taunt (2 dmg, 2 HP left)
  2. Frostbolt face (3 dmg)
  3. Fireball face (6 dmg)
  4. Minion face (3 dmg)
  5. Hero power face (1 dmg)
  Total: 13 available, 12 needed
- **Config:** All features enabled
- **Pass Criteria:** Correct action sequence returned

## Scenario 15: No Lethal Available
- **Setup:** Insufficient damage sources
- **Expected:** TryFindLethal returns null, BeamSearch proceeds normally
- **Pass Criteria:** No crash, search result returned

## Scenario 16-20: Edge Cases
16. **Frozen Hero:** Hero has weapon but is frozen → weapon not included
17. **Used Hero Power:** Hero power already used this turn → not included
18. **Insufficient Mana:** Spells cost more than available → not included
19. **Windfury:** Minion with windfury attacks twice in lethal calc
20. **Divine Shield:** Taunt with divine shield requires 2 attacks to clear

**Acceptance:** ≥18/20 scenarios pass (90% success rate)
```

### 7.3 Performance Criteria

**Fast-Path Lethal:**
- **Typical:** ≤2ms on boards with ≤7 minions per side
- **P95:** ≤5ms on complex boards (7v7 + multiple spells)
- **P99:** ≤10ms worst case

**BeamSearch with New Features:**
- **Typical:** Remain under MaxComputeMs=250ms on normal turns
- **timedOut=False:** ≥95% of evaluations complete without timeout
- **No Regression:** Compare to Phase 5 baseline; allow ≤10% slowdown

**RNG Rollouts:**
- **Budget:** ≤50ms or MaxComputeMs/5, whichever is smaller
- **Samples:** Target 64, early stop if time budget exhausted
- **Trigger Rate:** ≤5% of turns (only when RNG cards + near-lethal/high-swing)

**Memory:**
- **Peak:** ≤50MB additional heap allocation for all new systems
- **Leaks:** Zero ConditionalWeakTable leaks verified via 1000-turn soak test

---

## 8. Deliverables and Ordering (Micro-Phases)

### **Phase 6A: Hero Attack Foundation**
**Duration:** 2-3 days  
**Risk:** Low (isolated to LethalSolver + BoardSimulator)

**Tasks:**
1. Implement `ActionMetadataRegistry` (internal)
2. Extend `LethalSolver.CalculateWeaponDamage()`
3. Modify `BoardSimulator.SimulateAttack()` to handle hero swings
4. Add unit tests (5-6 test cases)
5. Update `MoveGenerator` to emit hero attack actions (gated)
6. Set `EnableHeroAttackInFastPath = true` by default

**Acceptance:**
- [ ] Hero weapon swings simulated correctly (damage + counter-damage + durability loss)
- [ ] Lethal solver includes weapon in calculations
- [ ] Frozen heroes excluded from weapon attacks
- [ ] Taunts block hero attacks appropriately
- [ ] Tests pass: weapon lethal, weapon + minions, weapon blocked by taunt

**Rollback Plan:**
- Set `EnableHeroAttackInFastPath = false` in config
- All code is isolated to internal Engine classes; no external dependencies

---

### **Phase 6B: Direct Damage + Hero Powers**
**Duration:** 3-4 days  
**Risk:** Low-Medium (data file dependency, curated allowlist)

**Tasks:**
1. Create `/data/direct_damage.json` with 20-30 core cards
2. Create `/data/hero_powers.json` with 9 hero classes
3. Implement `DirectDamageProvider` + `HeroPowerProvider`
4. Extend `LethalSolver` with spell + hero power logic
5. Modify `BoardSimulator.SimulateSpell()` for direct damage
6. Add `BoardSimulator.SimulateHeroPower()`
7. Add unit tests (8-10 test cases)
8. Keep `EnableDirectDamageInFastPath = false` by default (opt-in)

**Acceptance:**
- [ ] Fireball, Frostbolt, Lightning Bolt correctly applied
- [ ] Hero powers (Mage ping, Hunter steady shot, Druid claw) work
- [ ] Unknown cards gracefully ignored (no crash)
- [ ] Mana costs respected
- [ ] Tests pass: spell lethal, multi-spell combos, hero power lethal

**Rollback Plan:**
- Set `EnableDirectDamageInFastPath = false` (already default)
- Remove data files if they cause issues
- Lethal solver falls back to minions-only (Phase 6A state)

---

### **Phase 6C: Secret Risk Penalties**
**Duration:** 4-5 days  
**Risk:** Medium (scoring adjustments, BeamSearch internal changes)

**Tasks:**
1. Create `/data/secrets_risk.json` with 15-20 common secrets
2. Implement `SecretHandler.GetRiskForActionSequence()`
3. Implement `RiskAdjuster` wrapper
4. Add `InternalScoreRegistry` for adjusted scores
5. Modify `BeamSearch` to sort by adjusted scores (internal only)
6. Implement `ActionSequencer` with basic reordering rules
7. Add unit tests (6-8 test cases)
8. Add self-test scenarios for Counterspell, Explosive Trap
9. Keep `EnableSecretRiskPenalties = false` and `EnableSequencingEnforcement = false` by default

**Acceptance:**
- [ ] Secret risks correctly calculated from data file
- [ ] Adjusted scores used for beam node ordering
- [ ] Raw scores still logged (BeamResult format unchanged)
- [ ] Sequencer reorders actions per rules
- [ ] Illegal sequences reverted to original order
- [ ] Tests pass: Counterspell risk, probe-first scenarios, sequencing rules

**Rollback Plan:**
- Set both config flags to `false`
- `RiskAdjuster` becomes no-op (returns rawScore unchanged)
- `ActionSequencer` skipped entirely
- BeamSearch reverts to raw score sorting

---

### **Phase 6D: RNG Rollouts**
**Duration:** 5-6 days  
**Risk:** Medium-High (performance impact, complex sampling logic)

**Tasks:**
1. Create `/data/rng_effects.json` with 5-10 RNG cards
2. Implement `IRandomResolver` interface
3. Implement `ArcaneMissilesResolver` + 2-3 other resolvers
4. Implement `RandomResolverRegistry`
5. Add rollout logic to `DecisionEngineCoordinator`
6. Implement time budget enforcement (50ms cap)
7. Implement early stopping (confidence threshold)
8. Add unit tests (5-6 test cases)
9. Add self-test scenarios for probabilistic lethal
10. Keep `EnableRollouts = false` by default

**Acceptance:**
- [ ] Rollouts triggered only when RNG cards + budget available
- [ ] 64 samples run when time permits
- [ ] Early stop at 32 samples if confidence ≥0.95
- [ ] Time budget never exceeded (hard cap at 50ms)
- [ ] Probability logged: "RNG: probability=0.XX samples=NN"
- [ ] Deterministic fallback when disabled
- [ ] Tests pass: Arcane Missiles probability, time budget enforcement

**Rollback Plan:**
- Set `EnableRollouts = false` (already default)
- `DecisionEngineCoordinator` skips rollout branch entirely
- No performance impact when disabled

---

### **Phase 6E: Final Polish + Documentation**
**Duration:** 2-3 days  
**Risk:** Low (documentation + config tuning)

**Tasks:**
1. Expand `/docs/sequencing.md` with full rule descriptions
2. Document all config flags in `/docs/config.md`
3. Add examples to each data file (comments in JSON)
4. Write `/docs/Phase6_Implementation.md` summarizing changes
5. Update `/docs/progress.md` with Phase 6 completion
6. Run full Self-Test 6 suite (20 scenarios)
7. Run performance benchmark (100-turn stress test)
8. Generate Self-Test 6 report for checklist file
9. Create `/docs/Phase6_Rollback.md` with emergency procedures

**Acceptance:**
- [ ] All 20 Self-Test 6 scenarios documented
- [ ] ≥18/20 scenarios pass (90% success)
- [ ] Performance benchmarks within budget
- [ ] Documentation complete and accurate
- [ ] Config flags described with defaults and rationale

**Deliverables:**
- `/docs/Phase6_Implementation.md` (this plan)
- `/docs/Phase6_TestReport.md` (results)
- `/docs/Phase6_Rollback.md` (emergency procedures)
- Updated `/docs/progress.md`

---

## 9. Rollback Plan (Per Micro-Phase)

### **Emergency Rollback Procedure**

**If Phase 6A causes issues:**
```csharp
// In HSIntelConfig (emergency hotfix)
public bool EnableHeroAttackInFastPath { get; set; } = false; // ROLLBACK
```
- Lethal solver reverts to minions-only (Phase 5 behavior)
- No BeamSearch changes in 6A, so safe to disable

**If Phase 6B causes issues:**
```csharp
public bool EnableDirectDamageInFastPath { get; set; } = false; // Already default
```
- Spells ignored in lethal calculation
- Hero powers ignored
- Data files can be removed or renamed to `/data/disabled/`

**If Phase 6C causes issues:**
```csharp
public bool EnableSecretRiskPenalties { get; set; } = false; // Already default
public bool EnableSequencingEnforcement { get; set; } = false; // Already default
```
- `RiskAdjuster.AdjustForSecrets()` becomes no-op: `return rawResult;`
- `ActionSequencer.Reorder()` becomes no-op: `return actions;`
- BeamSearch reverts to raw score sorting
- No impact on lethal fast-path (6A/6B)

**If Phase 6D causes issues:**
```csharp
public bool EnableRollouts { get; set; } = false; // Already default
```
- Rollout branch never entered
- Deterministic search only
- RNG log: "skipped (disabled)"

**Nuclear Rollback (All of Phase 6):**
1. Set all Phase 6 config flags to `false`
2. Remove or rename `/data/*.json` files
3. System reverts to Phase 5 behavior:
   - Minion-only lethal (no weapon/spells/hero power)
   - No secret risk adjustments
   - No RNG rollouts
   - No sequencing enforcement
4. LethalSolver + DecisionEngineCoordinator continue to work (fast-path just returns null more often)

**Verification After Rollback:**
- Run Self-Test 5 (Phase 5 scenarios) to confirm system stable
- Check logs for "[HSIntel][Engine] RNG: skipped (disabled)" when rollouts off
- Verify BeamResult logs unchanged from Phase 5 format

---

## 10. Implementation Notes

### 10.1 HDT Tree Synchronization

**Canonical Location:** `HDT/HSIntel.Engine/`

**If Mirroring to `HSIntel.Engine/` Needed:**
- Add build step to copy files post-build
- Or use symbolic links (Windows: `mklink /D`)
- Or maintain manually with doc note in `/docs/tree-sync.md`

**Recommendation:** Keep HDT tree as source of truth; only mirror if external tools require standalone Engine project.

### 10.2 HearthDb Usage (If Needed)

**Already Available in HDT:** HearthDb package included

**Safe Usage:**
```csharp
// OK: Read stable IDs/tags
var card = Database.GetCardFromId("CS2_029");
int attack = card.Attack;
bool hasTaunt = card.Mechanics?.Contains("TAUNT") ?? false;

// NOT OK: Parse localized text
string text = card.Text; // Don't parse this for game logic
```

**Fallback:** If HearthDb insufficient, use curated JSON data files instead

### 10.3 New Public APIs (If Absolutely Necessary)

**If a Core/Overlay change is unavoidable:**

1. **Propose Micro-Phase 6F (Additive Only):**
   - Add new optional property to existing type
   - Provide default value (backwards compatible)
   - No constructor changes
   - Update `With(...)` method to include new property

2. **Example:**
```csharp
// In HSIntel.Core/Models/GameAction.cs (if needed)
public class GameAction
{
    // Existing properties...
    
    // NEW (Phase 6F): Optional metadata for extended action types
    public GameActionMetadata? Metadata { get; set; } = null; // Default null
    
    public GameAction With(/* existing params */, GameActionMetadata? metadata = null)
    {
        return new GameAction
        {
            // ... existing copies ...
            Metadata = metadata ?? this.Metadata
        };
    }
}
```

3. **Migration Plan:**
   - Version 1 code (Phase 5) ignores `Metadata` (null)
   - Version 2 code (Phase 6) can set `Metadata` for hero attacks
   - No breaking changes

**Current Plan:** Avoid this entirely by using `ConditionalWeakTable` for internal metadata (preferred).

### 10.4 Performance Profiling

**Before Phase 6:**
- Baseline Phase 5 performance (100-turn benchmark)
- Record P50/P95/P99 compute times
- Record typical BeamSearch node counts

**After Each Micro-Phase:**
- Run same 100-turn benchmark
- Compare to baseline
- Allow ≤10% regression per phase (cumulative ≤40% max)

**If Performance Degrades:**
- Profile with dotTrace or PerfView
- Identify hot paths
- Add caching (e.g., `LethalSolver` cache results for same board hash)
- Reduce beam width dynamically if over budget

**Optimization Opportunities:**
- Cache `DirectDamageProvider` lookups
- Lazy-load data files (don't parse until first use)
- Pool `GameContext` clones (object pool pattern)
- Reduce LINQ allocations in hot paths

---

## 11. Success Metrics

### 11.1 Functional Metrics

| Metric | Target | Phase |
|--------|--------|-------|
| Lethal Detection (Minions + Weapon) | ≥95% | 6A |
| Lethal Detection (+ Spells/Hero Power) | ≥95% | 6B |
| Secret Risk Accuracy | ≥90% | 6C |
| Sequencing Correctness | 100% (no illegal) | 6C |
| RNG Probability Accuracy | ±10% of theoretical | 6D |
| Overall Self-Test 6 Pass Rate | ≥90% (18/20) | 6E |

### 11.2 Performance Metrics

| Metric | Target | Baseline (Phase 5) |
|--------|--------|-------------------|
| Fast-Path Typical | ≤2ms | ~1ms |
| Fast-Path P95 | ≤5ms | ~2ms |
| BeamSearch Typical | ≤250ms | ~180ms |
| BeamSearch Timeout Rate | ≤5% | ~2% |
| Rollout Overhead | ≤50ms | N/A |
| Memory Overhead | ≤50MB | Baseline |

### 11.3 Quality Metrics

| Metric | Target |
|--------|--------|
| Code Coverage (Engine) | ≥80% |
| Unit Test Pass Rate | 100% |
| Self-Test Pass Rate | ≥90% |
| Zero Crashes | 1000-turn soak test |
| Log Format Compliance | 100% (existing tags only) |

---

## 12. Risk Assessment Matrix

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Performance regression | Medium | High | Profiling after each phase, config toggles |
| Data file errors | Low | Medium | Schema validation, graceful fallback |
| Secret risk over-penalizing | Medium | Medium | Tunable penalties in JSON, toggleable |
| RNG rollouts timeout | Low | High | Hard time budget, early stopping |
| Sequencing creates illegal moves | Low | High | Dry-run validation, revert on failure |
| BeamSearch internal changes break something | Low | Critical | Extensive unit tests, no public API changes |
| Unknown card handling | Medium | Low | Fail-safe defaults (ignore unknowns) |
| Memory leaks in ConditionalWeakTable | Low | Medium | Soak testing, weak reference validation |

**Overall Risk Level:** **Medium-Low** (most risks mitigated by toggles + fallbacks)

---

## 13. Final Checklist

**Before Starting Implementation:**
- [ ] Review Phase 5 completion status
- [ ] Verify HDT build environment stable
- [ ] Create feature branch `phase-6-complete` from `hsintel-main`
- [ ] Set up performance baseline metrics
- [ ] Document current Phase 5 behavior as rollback reference

**During Implementation:**
- [ ] Update `/docs/progress.md` after each micro-phase
- [ ] Run unit tests after each code change
- [ ] Profile performance weekly
- [ ] Review logs for format compliance
- [ ] Test with config toggles on/off

**Before Marking Phase 6 Complete:**
- [ ] All 5 micro-phases (6A-6E) delivered
- [ ] Self-Test 6 report shows ≥18/20 pass
- [ ] Performance benchmarks within budget
- [ ] 1000-turn soak test passes (zero crashes)
- [ ] Documentation complete
- [ ] Rollback procedures documented and tested
- [ ] Code review completed
- [ ] Commit with message: "Phase 6: Complete lethal + secrets + RNG implementation"

---

## 14. Post-Phase 6 Roadmap

**Immediate Next Steps (Phase 7+):**
- Phase 7: Recommendation pipeline integration (opponent reads impact)
- Phase 8: Visual overlay rendering (arrows, badges, halos)
- Phase 9: Explanation system (Medium/Full text)

**Technical Debt:**
- Evaluate if `ConditionalWeakTable` approach scales beyond Phase 6
- Consider formal metadata system if Phase 7+ needs similar patterns
- Optimize data file loading (lazy init, caching strategy)
- Expand curated card sets based on usage telemetry

**Known Limitations:**
- Hero power damage only covers direct damage types (healing excluded)
- Spell effects limited to direct damage (no complex mechanics)
- RNG resolvers cover only ~10 cards initially (expand in later phases)
- Secret risk penalties are heuristic-based (not game-tree optimal)

---

## Summary

This plan delivers **complete Phase 6 functionality** while maintaining **zero breaking changes** and **production safety**:

✅ **Hero attacks** (weapon + hero power) in lethal math  
✅ **Direct damage spells** from curated allowlist  
✅ **Secret risk penalties** via internal scoring adjustments  
✅ **RNG rollouts** with 64-sample Monte Carlo  
✅ **Sequencing enforcement** with fallback validation  

All features are **toggleable**, **performance-bound**, and **backwards-compatible**. The staged rollout (6A→6B→6C→6D→6E) minimizes risk, and comprehensive rollback procedures ensure production stability.

**Estimated Total Duration:** 16-21 days (3-4 weeks)  
**Risk Level:** Medium-Low (mitigated by toggles + extensive testing)  
**Success Criteria:** ≥90% Self-Test 6 pass rate + performance budgets met