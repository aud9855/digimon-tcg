using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.62.0 미지원 문장 36차 묶음 (엔진 쪽):
///  - "When this Digimon would unsuspend" (BT7-055가 주는 효과): 액티브 페이즈 · 효과로 액티브가 되려 할 때 그 디지몬 1마리마다 발휘 — 막으면 액티브가 되지 않는다
///    (공식 Q&amp;A BT7-055 Q1). 턴이 시작된 뒤 액티브가 되므로 (종합 룰 6-2-1) 그 효과가 있을 수 있으면 턴의 주인을 먼저 바꾸고 판단한다
///  - EX6-031 "Change ＜Security A. -＞ to ＜Security A. +＞": ≪시큐리티 어택≫의 몫을 효과마다 부호로 나눠 센다 (CardInstance.SaNeg62 · AttackSaParts62)
///  - BT8-059 "Players can't ignore digivolution requirements" (공식 Q&amp;A BT8-059 Q1): 진화 조건(카드의 진화 코스트 칸 · 공식 〔진화〕 조건)을 만족하지 않는 진화를 막는다
///  - 이 턴 동안 조건에 맞는 자신의 디지몬 전부가 얻는 키워드 · 효과 문장 (BT4-101 · BT9-102 — 나중에 나온 디지몬도, 공식 Q&amp;A BT9-102 Q1)
///  - 효과로 받은 키워드 지속 효과 문장 (BT13-108 "[Opponent's Turn] This Digimon isn't affected by your opponent's Option cards")
/// </summary>
public partial class GoldfishEngine
{
    // =====================================================================
    // "When this Digimon would unsuspend" (BT7-055)
    // =====================================================================

    /// <summary>지금 필드에 "액티브가 되려 할 때" 효과가 있을 수 있는지 (그 효과를 가진 카드 · 그 효과를 주는 지속 효과 · 효과로 받은 것)</summary>
    internal bool WouldUnsuspendPossible62()
    {
        var reg = SentenceParser.WouldUnsuspendCards62;
        return Side(State.Player) || Side(State.DummyOpponent);

        // v0.63.0 효과를 처리하지 않고 놓인 카드도 스크립트를 먼저 만들어 본다 (GoldfishEngine.Batch63)
        bool Card(CardData c)
        {
            EnsureScript63(c);
            return reg.ContainsKey(c.CardId) || (CardScripts.For(c)?.Statics.Any(s => s.Grant61 != null && reg.ContainsKey(s.Grant61.CardId)) ?? false);
        }
        bool Side(PlayerZones z)
        {
            foreach (var d in z.BattleArea)
            {
                if (Card(d.TopCard) || d.EvolutionSources.Any(Card)) return true;
                if (d.GrantedEffects.Any(g => reg.ContainsKey(g.Effect.CardId))) return true;
                if (_sideAuras62.Any(a => a.Grant != null && reg.ContainsKey(a.Grant.CardId))) return true;
            }
            return z.Tamers.Any(Card);
        }
    }

    /// <summary>inst가 지금 "액티브가 되려 할 때" 효과를 가졌는지 (맨 위 카드 · 진화원 · 효과로 받은 것 · 지속 효과로 얻은 것)</summary>
    private static bool HasWouldUnsuspend62(GoldfishEngine e, CardInstance inst)
    {
        bool Has(CardData c) => CardScripts.For(c)?.Effects.Any(x => x.Trigger == EffectTrigger.ThisDigimonWouldUnsuspend62) ?? false;
        return Has(inst.TopCard) || inst.EvolutionSources.Any(Has) || e.ActiveGrantedEffects(inst).Any(Has);
    }

    /// <summary>owner의 inst가 액티브가 되려 할 때: "액티브가 되려 할 때" 효과를 발휘하고 액티브가 되는 것을 막았으면 true</summary>
    internal bool WouldUnsuspendPrevented62(PlayerZones owner, CardInstance inst)
    {
        if (State.IsGameOver || !owner.BattleArea.Contains(inst) || inst.TamerProxyOf != null || !HasWouldUnsuspend62(this, inst)) return false;
        var ctx = RunEffects(EffectTrigger.ThisDigimonWouldUnsuspend62, owner, inst, inst.TopCard);
        Diag46.Hit(ctx.PreventUnsuspend62 ? "wouldUnsuspendPrevented62" : "wouldUnsuspendPaid62");
        return ctx.PreventUnsuspend62;
    }

    /// <summary>
    /// 추천: 패 1장을 파기해서라도 액티브로 할지 — 패가 2장 이상이거나, 그 디지몬이 Lv.5 이상 · 진화원 2장 이상 (어택 · 블록에 쓸 디지몬)일 때
    /// </summary>
    internal bool RecommendUnsuspendDiscard62(PlayerZones owner, CardInstance inst) =>
        owner.Hand.Count >= 2 || inst.TopCard.Level >= 5 || inst.EvolutionSources.Count >= 2;

    // =====================================================================
    // EX6-031 ≪시큐리티 어택 -≫ → ≪시큐리티 어택 +≫
    // =====================================================================

    /// <summary>inst의 ≪시큐리티 어택 -N≫이 지금 +N으로 바뀌는지와 그 -N의 합 (ApplyContinuousEffects가 시큐리티 어택을 계산한 뒤 — 같은 몫을 부호별로 다시 센다)</summary>
    private void ApplySaFlip62(CardInstance inst, PlayerZones owner, PlayerZones enemy, List<StaticEffect> statics, StaticContext ctx)
    {
        inst.SaFlip62 = false;
        inst.SaNeg62 = 0;
        bool flip = false;
        foreach (var (aura, auraCtx) in DigimonAuras(owner, enemy, StaticStat.AllySaFlip62))
            if (aura.AppliesTo(auraCtx, inst) && !CategoryShield51(inst, auraCtx) && aura.Value(auraCtx) != 0) { flip = true; break; }
        if (!flip) return;
        int neg = 0;
        void N(int v) { if (v < 0) neg -= v; }
        N(inst.TopCard.SecurityAttackBonus);
        foreach (var s in inst.FaceUpSources) N(s.InheritedSecurityAttackBonus);
        // 기간 ≪시큐리티 어택 ±N≫ — 받지 않는 몫(상대의 효과 · 카드 종류)은 빼고
        var applied = inst.TimedSecurityAttackBuffs.Select(x => x.Count).ToList();
        if (inst.OppShield59) foreach (var x in inst.OppTimedSa60) applied.Remove(x.Count);
        if (inst.CatShield61 is { } ct)
            foreach (var g in inst.CatGiven61)
                if (g.Kind == 2 && g.Type == ct && !(inst.OppShield59 && g.Opp)) applied.Remove(g.Value);
        foreach (var v in applied) N(v);
        foreach (var e in statics) if (e.Stat == StaticStat.SecurityAttack) N(e.Value(ctx));
        foreach (var link in inst.LinkedCards) N(CardScripts.For(link)?.LinkSecurityAttack43 ?? 0);
        foreach (var (aura, auraCtx) in DigimonAuras(owner, enemy, StaticStat.AllySecurityAttack).Concat(TamerAuras(owner, enemy, StaticStat.AllySecurityAttack)))
            if (aura.AppliesTo(auraCtx, inst) && !CategoryShield51(inst, auraCtx)) N(aura.Value(auraCtx));
        foreach (var (aura, auraCtx) in DigimonAuras(enemy, owner, StaticStat.EnemySecurityAttack).Concat(TamerAuras(enemy, owner, StaticStat.EnemySecurityAttack)))
            if (aura.AppliesTo(auraCtx, inst) && !CategoryShield51(inst, auraCtx) && !OppAuraShield59(inst, auraCtx)) N(aura.Value(auraCtx));
        inst.SaFlip62 = true;
        inst.SaNeg62 = neg;
        if (neg > 0 || inst.AttackSaParts62.Count > 0) Diag46.Hit("saFlip62");
    }

    // =====================================================================
    // BT8-059 진화 조건을 무시할 수 없다
    // =====================================================================

    /// <summary>필드(서로의 배틀 에어리어)에 "Players can't ignore digivolution requirements"가 있는지 → 모든 디지몬 · 육성 에어리어에 표시</summary>
    private void ApplyNoIgnoreReq62()
    {
        bool locked = State.Player.BattleArea.Any(d => d.TamerProxyOf == null && CardScripts.HasKeyword(d, Keyword.NoIgnoreEvoRequirements62))
                      || State.DummyOpponent.BattleArea.Any(d => d.TamerProxyOf == null && CardScripts.HasKeyword(d, Keyword.NoIgnoreEvoRequirements62));
        foreach (var z in new[] { State.Player, State.DummyOpponent })
        {
            foreach (var d in z.BattleArea) d.NoIgnoreReq62 = locked;
            if (z.BreedingArea != null) z.BreedingArea.NoIgnoreReq62 = locked;
        }
        _noIgnoreReq62 = locked;
    }

    private bool _noIgnoreReq62;

    /// <summary>지금 서로 진화 조건을 무시할 수 없는지 (BT8-059 — 테이머 진화 · 효과 진화의 판단)</summary>
    internal bool NoIgnoreReq62 => _noIgnoreReq62;

    /// <summary>target이 evoCard의 진화 조건을 만족하는지 — 카드의 진화 코스트 칸 (EvoRequirements48) · 공식 〔진화〕 조건 (종합 룰 2-3-5-3) · 카드에 적힌 다른 진화 조건</summary>
    internal static bool MeetsEvoRequirements62(CardInstance target, CardData evoCard) =>
        EvoRequirements48.NormalCost(target, evoCard) != null || KoreanEvolution47.Cost(evoCard, target) != null || CardScripts.MeetsAlternativeEvolution(evoCard, target);

    /// <summary>진화 조건을 무시하는 다른 진화의 코스트 (BT8-059가 있으면 공식 〔진화〕 조건만 — 그 밖의 "ignoring digivolution requirements" 진화는 할 수 없다)</summary>
    internal static int? SpecialEvolutionCost62(CardData evoCard, CardInstance target) =>
        target.NoIgnoreReq62 ? KoreanEvolution47.Cost(evoCard, target) : CardScripts.SpecialEvolutionCost(evoCard, target);

    // =====================================================================
    // 이 턴 동안 조건에 맞는 자신의 디지몬 전부가 얻는 것 (BT4-101 · BT9-102)
    // =====================================================================

    private sealed record SideAura62(PlayerZones Side, Func<CardInstance, bool> Filter, IReadOnlyList<Keyword> Keywords, CardData? Grant, int Until, CardType? Category);

    private readonly List<SideAura62> _sideAuras62 = new();

    /// <summary>"All of your ~ Digimon gain ＜X＞ and "~" for the turn" — 그 쪽의 조건에 맞는 디지몬 전부 (나중에 나온 디지몬도 — 공식 Q&amp;A BT9-102 Q1)</summary>
    internal void AddSideAura62(PlayerZones side, Func<CardInstance, bool> filter, IReadOnlyList<Keyword> keywords, CardData? grant, int until)
    {
        var card = CurrentEffectCard;
        _sideAuras62.Add(new SideAura62(side, filter, keywords, grant, until, card == null ? null : card.IsDual ? CardType.Option : card.Type));
        Diag46.Hit("sideAura62");
    }

    /// <summary>inst가 지금 얻는 것을 더한다 (ApplyAuraGrants61 뒤 — 받지 않는 카드 종류는 빼고)</summary>
    private void ApplySideAuras62(CardInstance inst, PlayerZones owner)
    {
        if (_sideAuras62.Count == 0) return;
        _sideAuras62.RemoveAll(a => a.Until < State.TurnSequence);
        foreach (var a in _sideAuras62)
        {
            if (!ReferenceEquals(a.Side, owner) || inst.TamerProxyOf != null || !a.Filter(inst)) continue;
            if (a.Category is { } t && inst.CatShield61 == t) continue;   // LM-020 그 카드 종류의 효과를 받지 않는다
            foreach (var k in a.Keywords) inst.ActiveKeywords.Add(k);
            if (a.Grant != null && !inst.AuraGranted61.Contains(a.Grant)) inst.AuraGranted61.Add(a.Grant);
        }
    }

    /// <summary>등장하는 디지몬이 【등장 시】 전에 이 턴 동안 얻는 것을 갖도록 (BT9-102 "[On Play] ~" — 얻은 효과로 발휘)</summary>
    private void RefreshSideAurasBeforeOnPlay62()
    {
        if (_sideAuras62.Count > 0 || _whole63.Count > 0) RecalculateFieldBuffs();   // v0.63.0 전체 처리도
    }

    // =====================================================================
    // 효과로 받은 키워드 지속 효과 문장 (BT13-108)
    // =====================================================================

    /// <summary>inst가 효과로 받은 효과 문장 중 키워드 지속 효과 ("[Opponent's Turn] This Digimon isn't affected by your opponent's Option cards") — 그 디지몬의 조건으로</summary>
    private void ApplyGrantedKeywordStatics62(CardInstance inst, StaticContext ctx)
    {
        if (inst.GrantedEffects.Count == 0) return;
        foreach (var g in inst.GrantedEffects)
        {
            if (CardScripts.For(g.Effect) is not { Statics.Count: > 0 } script) continue;
            if (!EffectRules.IsActive(g.Until, State) || (g.Granter != null && IsUnaffectedBy(g.Granter, inst)) || GrantedCatShielded61(inst, g.Effect)) continue;
            foreach (var e in script.Statics)
                if (e.Stat == StaticStat.Keyword && e.Keyword is { } k && e.Value(ctx) != 0) { inst.ActiveKeywords.Add(k); Diag46.Hit("grantedStaticKeyword62"); }
        }
    }
}
