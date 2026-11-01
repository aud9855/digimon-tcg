using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.61.0 미지원 문장 35차 묶음 (엔진 쪽):
///  - LM-020 "선언한 카드 종류의 효과를 받지 않는다 (이 디지몬의 효과 이외)": 받지 않는 동안에는 이미 받은 그 종류의 효과(이 턴 · 기간 DP, 기간 ≪시큐리티 어택≫,
///    이 어택 동안의 ≪시큐리티 어택≫, 기간 키워드, "~ 할 수 없다" 기간 효과, 효과로 받은 효과)도 영향이 없다 — 종합 룰 15-15-5-1 · 15-15-5-2 · 15-15-5-4.
///    v0.59.0 · v0.60.0 상대의 효과 내성(BT27-103 어택 중 · "isn't affected by your opponent's effects")과 같은 방식: 효과가 줄 때 그 효과가 적힌 카드의 종류를 기록하고,
///    받지 않는 상태가 끝나면 그 시점부터 다시 영향을 받는다 (기록은 지우지 않는다)
/// </summary>
public partial class GoldfishEngine
{
    // =====================================================================
    // LM-020 — 효과가 적힌 카드의 종류 기록
    // =====================================================================

    /// <summary>지금 처리 중인 효과가 target에게 어느 카드 종류의 효과인지 (이 디지몬 자신의 효과 · 카드가 없는 처리는 null — "other than this Digimon's effects")</summary>
    private CardType? EffectCategory61(CardInstance target)
    {
        var card = CurrentEffectCard;
        if (card == null || ReferenceEquals(card, target.TopCard) || target.EvolutionSources.Contains(card)) return null;
        return card.IsDual ? CardType.Option : card.Type;   // 듀얼 카드의 효과는 옵션 쪽으로 (UnaffectedCategory50 · UnaffectedByKeyword34와 같다)
    }

    /// <summary>받은 효과의 종류를 기록한다 (종류를 모르면 잠금만 기록 — always)</summary>
    private void NoteCat61(CardInstance d, int kind, int value, int until, bool always = false)
    {
        var type = EffectCategory61(d);
        if (type == null && !always) return;
        d.CatGiven61.Add((type, IsOpponentEffectFor(OwnerOf(d)), kind, value, until));
    }

    /// <summary>효과로 준 효과의 종류를 기록한다</summary>
    internal void NoteGrantedCat61(CardInstance d, CardData granted)
    {
        if (EffectCategory61(d) is { } type) d.GrantedCat61.Add((granted, type));
    }

    /// <summary>받지 않는 종류의 카드가 준 효과인지</summary>
    private static bool GrantedCatShielded61(CardInstance self, CardData effect)
    {
        if (self.CatShield61 is not { } t || self.GrantedCat61.Count == 0) return false;
        foreach (var g in self.GrantedCat61)
            if (ReferenceEquals(g.Effect, effect) && g.Type == t) return Diag46.HitTrue("catShieldGranted61");
        return false;
    }

    /// <summary>지금 받지 않는 카드 종류를 채운다 (LM-020 이 턴)</summary>
    private void SetCatShield61(CardInstance inst)
    {
        inst.CatShield61 = inst.UnaffectedCategory50 is { } t && EffectRules.IsActive(inst.UnaffectedCategory50Until, State) ? t : null;
        if (inst.CatShield61 != null && inst.CatGiven61.Any(g => g.Type == inst.CatShield61 && (g.Until >= State.TurnSequence || g.Kind is 0 or 3)))
            Diag46.Hit("catShieldGiven61");
    }

    /// <summary>
    /// 받지 않는 카드 종류가 있을 때의 기간 키워드 (AddTimedKeywordsSplit60과 같고, 그 종류가 준 키워드는 더하지 않는다). 상대의 효과로 받은 것은 돌려준다
    /// </summary>
    private static List<Keyword>? AddTimedKeywordsSplit61(CardInstance inst)
    {
        var skip = inst.CatGiven61.Where(g => g.Kind == 4 && g.Type == inst.CatShield61).Select(g => ((Keyword)g.Value, g.Until)).ToList();
        var opp = inst.OppTimedKeywords60.ToList();
        var deferred = new List<Keyword>();
        foreach (var t in inst.TimedKeywords)
        {
            int i = opp.IndexOf(t);
            if (i >= 0) opp.RemoveAt(i);
            int j = skip.IndexOf(t);
            if (j >= 0) { skip.RemoveAt(j); Diag46.Hit("catShieldTimedKeyword61"); continue; }
            if (i >= 0) deferred.Add(t.Keyword);
            else inst.ActiveKeywords.Add(t.Keyword);
        }
        return deferred.Count == 0 && inst.OppTimedKeywords60.Count == 0 ? null : deferred;
    }
}

/// <summary>
/// v0.61.0 진화원 파기를 한 곳으로 — 효과가 디지몬의 진화원을 파기하는 처리(위 · 아래에서 N장, 고른 카드, 비용, ≪디지버스트≫)는 여기를 지난다.
///  - BT9-109 (진화원) "Effects can't trash [X Antibody] in this Digimon's digivolution cards": 그 카드는 파기되지 않는다. 위 · 아래에서 N장이면 파기할 수 없는 카드를 빼고 센다
///    (공식 Q&amp;A EX2-055 Q1 "파기할 수 없는 카드를 제외하고 진화원의 카드를 아래에서부터 7장 이상 파기할 수 있다면"), 고르는 효과 · 비용은 고를 수 없다
///  - BT10-084 【상대의 턴】 "When an effect would trash one of your other Digimon's digivolution cards, you may trash this Digimon's digivolution cards with that effect instead":
///    치환 — 같은 효과로 이 디지몬의 진화원을 같은 방법(위 · 아래에서 N장 / 고른 매수)으로 파기한다. 이 디지몬에 진화원이 없어도 대체할 수 있다 (공식 Q&amp;A BT10-084 Q1).
///    비용(자신의 효과의 "~를 파기하여" · ≪디지버스트≫)은 대체하지 않는다
/// 디지몬이 떠날 때 진화원이 함께 트래시로 가는 것(룰 처리)은 효과가 진화원을 파기하는 것이 아니므로 여기를 지나지 않는다
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>효과가 host의 진화원 x를 파기할 수 없는지 (BT9-109 — 앞면 「X항체」)</summary>
    internal bool SourceTrashProtected61(CardInstance host, CardData x) =>
        CardScripts.HasKeyword(host, Keyword.XAntibodySourcesProtected61) && !host.IsFaceDown(x) && CardNames.Is(x, "X Antibody") && Diag46.HitTrue("xAntibodyProtected61");

    /// <summary>효과가 파기할 수 있는 진화원 (보호된 카드를 뺀다 — 보호가 없으면 그대로)</summary>
    internal IEnumerable<CardData> TrashableSources61(CardInstance host, IEnumerable<CardData> xs) =>
        CardScripts.HasKeyword(host, Keyword.XAntibodySourcesProtected61) ? xs.Where(x => !SourceTrashProtected61(host, x)).ToList() : xs;

    /// <summary>효과가 파기할 수 있는 진화원 매수</summary>
    internal int TrashableSourceCount61(CardInstance host) =>
        CardScripts.HasKeyword(host, Keyword.XAntibodySourcesProtected61) ? host.EvolutionSources.Count(x => !SourceTrashProtected61(host, x)) : host.EvolutionSources.Count;

    /// <summary>BT10-084 치환: 효과가 zone의 host의 진화원을 파기하려 할 때, 그 주인의 다른 디지몬이 대신할 수 있으면 물어서 그 디지몬 (아니면 null)</summary>
    private CardInstance? SourceTrashReplacement61(PlayerZones zone, CardInstance host, int n)
    {
        if (CurrentEffectOwner == null && !_deletingByOpponentEffect) return null;   // 효과가 아니다
        CardInstance? r = null;
        foreach (var d in zone.BattleArea)
            if (!ReferenceEquals(d, host) && d.TamerProxyOf == null && CardScripts.HasKeyword(d, Keyword.ReplaceOtherSourceTrash61)) { r = d; break; }
        if (r == null) return null;
        // 추천: 이 디지몬에 진화원이 없으면 대신한다 (아무것도 파기되지 않는다). 있으면 그 디지몬이 진화원을 모두 잃게 되고 이 디지몬은 남을 때만
        //  (진화원이 없는 디지몬을 노리는 뒤의 효과 — BT8-031 "return 1 of your opponent's Digimon with no digivolution cards" 등 — 에 이 디지몬을 내주지 않게)
        int rs = TrashableSourceCount61(r), hs = TrashableSourceCount61(host);
        bool recommend = rs == 0 || hs <= n && rs > n;
        if (!AskYesNo(zone, r, $"[{r.TopCard.Name}] 효과가 [{host.TopCard.Name}]의 진화원을 파기하려 합니다. 대신 [{r.TopCard.Name}]의 진화원을 그 효과로 파기할까요?", recommended: recommend))
            return null;
        EffectLog(zone, $"[{r.TopCard.Name}]의 효과: [{host.TopCard.Name}] 대신 [{r.TopCard.Name}]의 진화원을 파기합니다.");
        Diag46.Hit("replaceSourceTrash61");
        return r;
    }

    /// <summary>
    /// 효과가 host의 진화원을 위(top) · 아래에서 n장 파기해 트래시로 (치환 · 보호). 반환: 파기한 카드 (위에서면 위부터, 아래에서면 아래부터).
    /// 보호 · 치환이 없으면 원래와 같다 (위: 하나씩 빼서 트래시 · 아래: RemoveBottomSources)
    /// </summary>
    internal List<CardData> TrashSourcesAt61(PlayerZones zone, CardInstance host, int n, bool top, bool cost = false)
    {
        if (n > 0 && host.EvolutionSources.Count > 0 && StackProtected42(zone, host)) return new List<CardData>();   // v0.62.0 겹쳐진 카드 보호도 여기서 (BT21-060 · EX11-070 — 공식 Q&A BT21-060 Q1)
        if (!cost && n > 0 && host.EvolutionSources.Count > 0 && SourceTrashReplacement61(zone, host, n) is { } r)
        {
            if (StackProtected42(zone, r)) return new List<CardData>();   // v0.62.0 대신 파기하는 디지몬의 겹쳐진 카드도 상대의 효과로 파기되지 않는다
            host = r;
        }
        if (!CardScripts.HasKeyword(host, Keyword.XAntibodySourcesProtected61))
        {
            if (!top)
            {
                var bottom = host.RemoveBottomSources(n);
                zone.Trash.AddRange(bottom);
                return bottom;
            }
            var removed = new List<CardData>();
            while (removed.Count < n && host.EvolutionSources.Count > 0)
                removed.Add(host.EvolutionSources.Pop());
            zone.Trash.AddRange(removed);
            return removed;
        }
        var order = top ? host.EvolutionSources.ToList() : host.GetEvolutionStackBottomUp().ToList();
        return RemoveSourcesToTrash61(zone, host, order.Where(x => !SourceTrashProtected61(host, x)).Take(n).ToList());
    }

    /// <summary>
    /// 효과가 고른 진화원 picks를 host에서 파기해 트래시로 (보호된 카드는 남는다). 치환이면 같은 매수를 대신 그 디지몬의 진화원에서 (고르는 사람은 효과의 주인).
    /// 보호 · 치환이 없으면 원래와 같다 (고른 순서대로 빼서 트래시). 반환: 파기한 카드
    /// </summary>
    internal List<CardData> TrashChosenSources61(PlayerZones zone, CardInstance host, IReadOnlyList<CardData> picks, bool cost = false)
    {
        if (picks.Count > 0 && StackProtected42(zone, host)) return new List<CardData>();   // v0.62.0 겹쳐진 카드 보호도 여기서 (BT21-060 · EX11-070)
        if (!cost && picks.Count > 0 && SourceTrashReplacement61(zone, host, picks.Count) is { } r)
        {
            if (StackProtected42(zone, r)) return new List<CardData>();   // v0.62.0
            var pool = TrashableSources61(r, r.EvolutionSources.ToList()).ToList();
            int k = Math.Min(picks.Count, pool.Count);
            if (k == 0) return new List<CardData>();
            var chooser = CurrentEffectOwner ?? EnemyOf(zone);
            var chosen = k == pool.Count ? pool : ChooseCards(chooser, CardChoiceZone.DigivolutionCards, pool, k, upTo: false, $"[{r.TopCard.Name}]의 진화원에서 파기할 카드 {k}장을 고르세요.");
            return RemoveSourcesToTrash61(zone, r, chosen);
        }
        return RemoveSourcesToTrash61(zone, host, CardScripts.HasKeyword(host, Keyword.XAntibodySourcesProtected61) ? picks.Where(x => !SourceTrashProtected61(host, x)) : picks);
    }

    /// <summary>≪디지버스트 N≫ — 파기할 수 없는 진화원이 있을 때 (그 카드를 빼고 ChooseDigiBurstSources59와 같은 추천 순서로 고른다)</summary>
    private List<CardData> ChooseDigiBurstProtected61(PlayerZones owner, CardInstance self, int n)
    {
        var bottomUp = self.GetEvolutionStackBottomUp().ToList();
        var order = Enumerable.Range(0, bottomUp.Count).Where(i => !SourceTrashProtected61(self, bottomUp[i]))
            .OrderBy(i => BurstTrashedEffect59(bottomUp[i]) ? 0 : BurstUse60?.Invoke(bottomUp[i]) == true ? 1 : 2).ThenBy(i => i).ToList();
        int k = Math.Min(n, order.Count);
        if (k <= 0) return new List<CardData>();
        var ranked = order.Select(i => bottomUp[i]).ToList();
        var picked = k == ranked.Count ? Enumerable.Range(0, k).ToList()
            : ChooseCardIndexes(owner, CardChoiceZone.DigivolutionCards, ranked, k, upTo: false, $"[{self.TopCard.Name}] ≪디지버스트 {n}≫ — 파기할 진화원 {k}장을 고르세요.");
        var positions = picked.Where(i => i >= 0 && i < order.Count).Select(i => order[i]).Distinct().ToHashSet();
        if (positions.Count != k) positions = order.Take(k).ToHashSet();
        var removed = new List<CardData>();
        var rest = new List<CardData>();
        for (int i = 0; i < bottomUp.Count; i++) (positions.Contains(i) ? removed : rest).Add(bottomUp[i]);
        self.ReplaceSources59(rest);
        return removed;
    }

    private static List<CardData> RemoveSourcesToTrash61(PlayerZones zone, CardInstance host, IEnumerable<CardData> picks)
    {
        var done = new List<CardData>();
        foreach (var x in picks)
            if (host.TakeSource(y => ReferenceEquals(y, x)) != null) { zone.Trash.Add(x); done.Add(x); }
        return done;
    }

    // =====================================================================
    // BT10-042 ≪시큐리티 어택≫을 가진 상대의 디지몬
    // =====================================================================

    /// <summary>≪시큐리티 어택 +N≫ · ≪시큐리티 어택 -N≫ 중 어느 것이든 가졌는지 (공식 Q&amp;A BT10-042 Q1) — 카드 · 진화원 · 테이머 · 효과의 몫 중 하나라도 0이 아니면</summary>
    public static bool HasSecurityAttackKeyword61(CardInstance d) =>
        d.TopCard.SecurityAttackBonus != 0 || d.GetInheritedSecurityAttackBonus() != 0 || d.TamerSecurityAttackBonus != 0 || d.TemporarySecurityAttackBonus != 0
        || d.EffectSecurityAttackBonus != 0 || d.AttackSecurityAttackBonus != 0;

    /// <summary>attacker가 target에게 어택할 수 없는지 — target이 "Your opponent's Digimon with ＜Security A.＞ can't attack this Digimon"을 가졌고 attacker가 ≪시큐리티 어택≫을 가졌다
    /// (attacker가 상대의 효과를 받지 않으면 어택할 수 있다 — 15-15-5-1)</summary>
    internal static bool SaHolderLocked61(CardInstance attacker, CardInstance target) =>
        CardScripts.HasKeyword(target, Keyword.CannotBeAttackedBySa61) && !attacker.OppShield59 && HasSecurityAttackKeyword61(attacker) && Diag46.HitTrue("saHolderLocked61");

    // =====================================================================
    // 다른 카드의 지속 효과로 얻는 효과 문장 (BT5-091 · BT8-031 · BT10-056)
    // =====================================================================

    /// <summary>inst가 지금 지속 효과로 얻는 효과 문장을 채운다 (자신의 카드 · 상대의 카드 — 받지 않는 효과 · 카드 종류는 뺀다)</summary>
    private void ApplyAuraGrants61(CardInstance inst, PlayerZones owner, PlayerZones enemy)
    {
        inst.AuraGranted61.Clear();
        if (!AnyGrantAura61(owner) && !AnyGrantAura61(enemy)) return;
        foreach (var (aura, ctx) in DigimonAuras(owner, enemy, StaticStat.AllyGrant61).Concat(TamerAuras(owner, enemy, StaticStat.AllyGrant61)))
            if (aura.Grant61 is { } g && aura.AppliesTo(ctx, inst) && !CategoryShield51(inst, ctx) && aura.Value(ctx) != 0 && !inst.AuraGranted61.Contains(g))
                inst.AuraGranted61.Add(g);
        foreach (var (aura, ctx) in DigimonAuras(enemy, owner, StaticStat.EnemyGrant61).Concat(TamerAuras(enemy, owner, StaticStat.EnemyGrant61)))
            if (aura.Grant61 is { } g && aura.AppliesTo(ctx, inst) && !CategoryShield51(inst, ctx) && !OppAuraShield59(inst, ctx) && aura.Value(ctx) != 0 && !inst.AuraGranted61.Contains(g))
                inst.AuraGranted61.Add(g);
        if (inst.AuraGranted61.Count > 0) Diag46.Hit("auraGranted61");
    }

    /// <summary>side의 배틀 에어리어 · 테이머 중 효과 문장을 주는 지속 효과를 가진 카드가 있는지 (빠른 확인)</summary>
    private static bool AnyGrantAura61(PlayerZones side)
    {
        var reg = SentenceParser.GrantAuraCards61;
        if (reg.IsEmpty) return false;
        foreach (var d in side.BattleArea)
        {
            if (reg.ContainsKey(d.TopCard.CardId)) return true;
            foreach (var s in d.EvolutionSources) if (reg.ContainsKey(s.CardId)) return true;
        }
        foreach (var t in side.Tamers)
            if (reg.ContainsKey(t.CardId)) return true;
        return false;
    }
}
