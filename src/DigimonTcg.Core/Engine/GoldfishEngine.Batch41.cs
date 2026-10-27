using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>v0.41.0 시뮬레이션 진단 — 새 동작이 실제로 일어난 횟수</summary>
public static class Diag41
{
    public static int NextEvoReduction, OwnAttackSwitch, AppLinkEffect, LinkCostReduced, AiLink, AiLinkSevenCode, OppUnderOpp, TrashTop, ActivateEffect, Misc;
    public static void Hit(ref int counter) => System.Threading.Interlocked.Increment(ref counter);
    public static void Reset() => NextEvoReduction = OwnAttackSwitch = AppLinkEffect = LinkCostReduced = AiLink = AiLinkSevenCode = OppUnderOpp = TrashTop = ActivateEffect = Misc = 0;
}

public partial class GoldfishEngine
{
    // =====================================================================
    // 이 턴 동안의 진화 코스트 감소 ("The next time one of your Digimon would digivolve this turn, reduce the digivolution cost by 1" — ST12-15 · BT1-109 · BT5-109 · EX1-033 · EX5-029 · BT3-103 · BT14-013)
    // =====================================================================

    private sealed record TurnEvoMod41(PlayerZones Owner, EvoCostModifier Mod, CardData Card, CardInstance? Holder, int Turn, bool OneShot);

    private readonly List<TurnEvoMod41> _turnEvoMods41 = new();

    /// <summary>효과가 건 이 턴 동안의 진화 코스트 감소 (oneShot = 다음 1번 진화에만 — 공식 BT1-109 "이 턴 동안, 다음에 ~ 진화할 때")</summary>
    internal void AddTurnEvoCostModifier41(PlayerZones owner, EvoCostModifier mod, CardData card, CardInstance? holder, bool oneShot)
    {
        _turnEvoMods41.RemoveAll(x => x.Turn != State.TurnSequence);
        _turnEvoMods41.Add(new TurnEvoMod41(owner, mod, card, holder, State.TurnSequence, oneShot));
    }

    /// <summary>지금 걸리는 이 턴 동안의 감소 (효과, 그 효과를 가진 디지몬, 효과가 적힌 카드)</summary>
    private IEnumerable<(EvoCostModifier Mod, CardInstance? Holder, CardData Card)> TurnEvoCostModifierSources41(PlayerZones owner) =>
        _turnEvoMods41.Where(x => ReferenceEquals(x.Owner, owner) && x.Turn == State.TurnSequence).Select(x => (x.Mod, x.Holder, x.Card)).ToList();

    /// <summary>한 번만 쓰는 감소를 썼다 — 지운다</summary>
    private void ConsumeTurnEvoCostModifier41(EvoCostModifier mod)
    {
        int i = _turnEvoMods41.FindIndex(x => ReferenceEquals(x.Mod, mod) && x.Turn == State.TurnSequence);
        if (i < 0) return;
        Diag41.Hit(ref Diag41.NextEvoReduction);
        if (_turnEvoMods41[i].OneShot) _turnEvoMods41.RemoveAt(i);
    }

    // =====================================================================
    // 링크 코스트 감소 (BT25-004 · BT25-045 — "When a [Social], [Tool] or [Game] trait card would link to this Digimon, you may reduce the cost by 1")
    // =====================================================================

    private readonly Dictionary<(LinkCostModifier41, CardInstance), int> _linkCostModUsed41 = new();

    /// <summary>
    /// target에 card를 링크할 때 낼 코스트 (target의 맨 위 카드 효과 · 진화원 효과의 감소). apply = 실제로 링크한다 ("you may"는 묻고, [턴에 1회]를 기록) —
    /// false면 미리보기 (묻지 않는다)
    /// </summary>
    internal int ReduceLinkCost41(PlayerZones owner, CardInstance target, CardData card, int cost, bool apply)
    {
        if (cost <= 0) return Math.Max(0, cost);
        var mods = new List<(LinkCostModifier41 Mod, CardData Holder)>();
        foreach (var m in CardScripts.For(target.TopCard)?.LinkCostModifiers41 ?? Enumerable.Empty<LinkCostModifier41>())
            if (m.Source == EffectSource.Main) mods.Add((m, target.TopCard));
        foreach (var src in target.FaceUpSources)
            foreach (var m in CardScripts.For(src)?.LinkCostModifiers41 ?? Enumerable.Empty<LinkCostModifier41>())
                if (m.Source == EffectSource.Inherited) mods.Add((m, src));
        foreach (var (mod, holder) in mods)
        {
            if (cost <= 0) break;
            if (!mod.Which(card) || (mod.YourTurnOnly && !IsTurnOf(owner))) continue;
            if (mod.OncePerTurn && _linkCostModUsed41.TryGetValue((mod, target), out int used) && used == State.TurnSequence) continue;
            if (apply && mod.Optional && !AskYesNo(owner, target, $"[{holder.Name}] [{card.Name}]의 링크 코스트를 {mod.Reduce} 줄일까요?", recommended: true)) continue;
            int r = Math.Min(mod.Reduce, cost);
            cost -= r;
            if (!apply) continue;
            if (mod.OncePerTurn) _linkCostModUsed41[(mod, target)] = State.TurnSequence;
            Diag41.Hit(ref Diag41.LinkCostReduced);
            EffectLog(owner, $"[{holder.Name}] [{card.Name}] 링크 코스트 -{r} (코스트 {cost})");
        }
        return cost;
    }

    // =====================================================================
    // AI 링크 — 패의 카드를 링크 코스트를 내고 자신의 디지몬에 링크한다 (메모리를 상대 쪽으로 넘기지 않는 것만).
    // ≪분리≫의 지정 링크 카드(예: [Seven Code])를 그 디지몬에 먼저 — ≪분리≫가 실제로 쓰이도록 (BT26-010 · BT26-019 등)
    // =====================================================================

    /// <summary>AI가 링크할 (카드, 디지몬, 코스트). 없으면 null</summary>
    internal (CardData Card, CardInstance Target, int Cost)? ChooseAiLink41(PlayerZones owner, int memory)
    {
        (CardData, CardInstance, int)? best = null;
        int bestScore = int.MinValue;
        foreach (var card in owner.Hand.Distinct().ToList())
        {
            if (LinkCostOf(card) is not int baseCost) continue;
            foreach (var d in owner.BattleArea)
            {
                if (d.LinkedCards.Count >= MaxLinks(d) || MeetsLinkRequirement(card, d) != true) continue;
                int cost = ReduceLinkCost41(owner, d, card, baseCost, apply: false);
                if (cost > memory) continue;
                bool detach = Detach40Specified(d, card);
                int score = (detach ? 100 : 0) + LinkDp(card) / 1000 * 5 + d.TopCard.Level - cost * 3;
                if (score > bestScore) { bestScore = score; best = (card, d, cost); }
            }
        }
        return best;
    }

    /// <summary>AI: 링크 하나를 한다 (했으면 true). ctx.Memory에서 코스트를 낸다</summary>
    private bool AiTryLink41(AiTurnContext ctx)
    {
        var owner = State.DummyOpponent;
        if (ChooseAiLink41(owner, ctx.Memory) is not { } pick) return false;
        var (card, target, _) = pick;
        if (!owner.Hand.Remove(card)) return false;
        int cost = ReduceLinkCost41(owner, target, card, LinkCostOf(card) ?? 0, apply: true);
        int before = ctx.Memory;
        ctx.Memory -= cost;
        Diag41.Hit(ref Diag41.AiLink);
        if (Detach40Specified(target, card)) Diag41.Hit(ref Diag41.AiLinkSevenCode);
        Log($"🤖 [AI 링크] [{card.Name}] → [{target.TopCard.Name}] (코스트 {cost}, AI 메모리: {before} → {ctx.Memory})");
        AddOpponentEvent(OpponentActionKind.Tamer, "링크", $"{card.Name} → {target.TopCard.Name} · 메모리 {before} → {ctx.Memory}", card);
        AddEffectEvent(card, CaptureEffectLogs(() => LinkCard(owner, target, card)));
        return true;
    }

    // =====================================================================
    // 진화원 조작 (SentenceParser.Batch41Src)
    // =====================================================================

    /// <summary>
    /// 디지몬(맨 위 카드)을 주인의 테이머 아래에 놓는다 (EX10-056 · BT12-083 — "or under any of their Tamers"). 진화원은 트래시 (종합 룰 4-7-7과 같다).
    /// "떠나려 할 때" 효과로 막혔거나 테이머가 없으면 false
    /// </summary>
    internal bool PlaceDigimonUnderTamers41(PlayerZones zones, CardInstance moving)
    {
        if (!zones.BattleArea.Contains(moving) || zones.Tamers.Count == 0)
            return false;
        if (TryPreventLeave(zones, moving, deletion: false, inBattle: false, canPrevent: true, LeaveDestination.Other))
            return false;
        if (!zones.BattleArea.Remove(moving))
            return false;
        TrashLinks(zones, moving);
        ApplyOverflow(zones, moving.GetEvolutionStackBottomUp().ToList());
        while (moving.EvolutionSources.Count > 0)
            zones.Trash.Add(moving.EvolutionSources.Pop());
        if (!IsToken(moving.TopCard)) zones.UnderTamers.Add(moving.TopCard);
        PurgeTokens(zones);
        NotifyLeftBattleArea(zones, moving);
        RecalculateFieldBuffs();
        return true;
    }

    /// <summary>디지몬의 맨 위 카드를 파기한다 — 바로 아래 진화원이 맨 위 카드가 된다 (≪아머 퍼지≫와 같은 처리). 진화원이 없으면 false</summary>
    internal bool TrashTopCardOf41(PlayerZones owner, CardInstance inst, out CardData? top)
    {
        top = null;
        if (!owner.BattleArea.Contains(inst) || inst.EvolutionSources.Count == 0 || StackProtected42(owner, inst) || inst.TrashTopCard() is not { } t)   // v0.42.0 StackProtected42
            return false;
        top = t;
        ApplyOverflow(owner, new[] { t });
        owner.Trash.Add(t);
        RecalculateFieldBuffs();
        TrashExposedFaceDownTops();
        return true;
    }
}

public partial class GoldfishEngine
{
    private int _activateDepth41;

    /// <summary>card의 메인 효과 중 그 시점의 것이 있는지</summary>
    internal static bool HasEffectsFor41(CardData card, IReadOnlySet<EffectTrigger> triggers) =>
        CardScripts.For(card)?.Effects.Any(e => e.Source == EffectSource.Main && triggers.Contains(e.Trigger)) == true;

    /// <summary>
    /// card의 그 시점 메인 효과를 host(효과를 발휘하는 디지몬 — 테이머면 null)의 효과로 발휘한다 (onlyOne = 1개만 — 여러 개면 고른다). 발휘한 효과 수.
    /// 효과 안에서 다시 효과를 발휘하는 것은 3단계까지 (끝없이 이어지지 않게)
    /// </summary>
    internal int ActivateEffectsAs41(PlayerZones owner, CardInstance? host, CardData card, IReadOnlySet<EffectTrigger> triggers, bool onlyOne)
    {
        if (_activateDepth41 >= 3 || CardScripts.For(card) is not { } script) return 0;
        var effects = script.Effects.Where(e => e.Source == EffectSource.Main && triggers.Contains(e.Trigger)).ToList();
        if (effects.Count == 0) return 0;
        if (onlyOne && effects.Count > 1)
        {
            int i = ChooseOption(owner, $"[{card.Name}]의 어느 효과를 발휘할까요?", effects.Select((e, k) => $"효과 {k + 1} ({e.Trigger})").ToList(), 0);
            effects = new List<CardEffect> { effects[Math.Clamp(i, 0, effects.Count - 1)] };
        }
        _activateDepth41++;
        _effectDepth++;
        var outerEffectOwner = CurrentEffectOwner;
        CurrentEffectOwner = owner;
        try
        {
            foreach (var e in effects)
            {
                var ctx = new EffectContext(this, owner, host, null, null) { Trigger = e.Trigger };
                Resolve(ctx, card, e);
            }
        }
        finally
        {
            _effectDepth--;
            _activateDepth41--;
            CurrentEffectOwner = outerEffectOwner;
        }
        if (_effectDepth == 0) FlushPendingTriggers();
        return effects.Count;
    }

    /// <summary>d의 ≪분리≫ 지정 링크 카드인지</summary>
    private static bool Detach40Specified(CardInstance d, CardData card) => SentenceParser.DetachAccepts41(d.TopCard, card);
}

public partial class GoldfishEngine
{
    /// <summary>
    /// v0.41.0 링크 조건 "[Appmon] trait" · 「어플몬」: 영어 데이터는 어플몬의 특징을 "Search (App Name)"처럼 앱 이름만 적고 "Appmon"을 빠뜨린 카드가 많다
    /// (공식 한글 데이터가 있는 어플몬 디지몬은 모두 특징 「어플몬」) — 특징에 "(App Name)"이 있는 디지몬도 어플몬으로 본다
    /// </summary>
    internal static bool IsAppmonByAppName41(CardData card, string trait) =>
        (trait.Equals("Appmon", StringComparison.OrdinalIgnoreCase) || trait == "어플몬")
        && card.Traits.Any(t => t.Contains("(App Name)", StringComparison.OrdinalIgnoreCase));
}
