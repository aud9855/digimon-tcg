using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.59.0 미지원 문장 33차 묶음 (엔진 쪽):
///  - ≪디지버스트≫로 파기할 진화원은 주인이 고른다 (종합 룰 16-14-1 "지정된 장수의 진화원을 이 디지몬에서 파기" — 위 · 아래 지정이 없다.
///    "위에서" · "아래에서"가 적힌 처리(4-27-2 · 4-27-3)와 다르다. 공식 Q&amp;A ST6-13 Q1: ≪디지버스트≫로 파기한 진화원의 Lv.3 카드를 곧바로 등장)
///  - 상대의 효과를 받지 않는 동안 (BT27-103 어택 중 · "isn't affected by your opponent's effects"): 상대 카드의 지속 효과(DP · ≪시큐리티 어택≫ · 키워드)와
///    이미 받은 상대의 DP 효과도 영향이 없다 — 종합 룰 15-15-5-1 "효과에 의해 발생한 영향을 받지 않는다" · 15-15-5-2 (받지 않는 동안에는 DP -3000이 없고, 다시 받게 된 시점에 -3000)
///  - BT20-037 상대의 디지몬 · 테이머는 기간 동안 【등장 시】를 발휘할 수 없다 (PlayerZones.OnPlayBlockedUntil59)
///  - ST17-13 [시큐리티] "At the end of the battle, 1 of your Digimon may digivolve into this card without paying the digivolution cost" — 배틀이 끝난 뒤 (진화 조건은 지킨다: 공식 Q&amp;A ST17-13 Q1)
/// </summary>
public partial class GoldfishEngine
{
    // =====================================================================
    // ≪디지버스트≫ — 파기할 진화원 고르기
    // =====================================================================

    /// <summary>
    /// ≪디지버스트 N≫으로 파기할 진화원 N장을 고르고 진화원에서 뺀다. 진화원이 N장 이하면 전부.
    /// 추천(AI · 묻지 않을 때): "≪디지버스트≫로 파기되었을 때" 효과가 있는 카드 → 아래(오래된 카드)부터 — 원래(아래에서 N장)와 같은 순서에 그 카드만 앞으로
    /// </summary>
    private List<CardData> ChooseDigiBurstSources59(PlayerZones owner, CardInstance self, int n)
    {
        var bottomUp = self.EvolutionSources.Reverse().ToList();
        if (n <= 0 || bottomUp.Count == 0) return new List<CardData>();
        if (bottomUp.Count <= n) return self.RemoveBottomSources(n);
        var order = Enumerable.Range(0, bottomUp.Count)
            .OrderBy(i => BurstTrashedEffect59(bottomUp[i]) ? 0 : BurstUse60?.Invoke(bottomUp[i]) == true ? 1 : 2).ThenBy(i => i).ToList();   // v0.60.0 뒤의 효과가 트래시에서 쓰는 카드 (GoldfishEngine.Batch60)
        var ranked = order.Select(i => bottomUp[i]).ToList();
        var picked = ChooseCardIndexes(owner, CardChoiceZone.DigivolutionCards, ranked, n, upTo: false,
            $"[{self.TopCard.Name}] ≪디지버스트 {n}≫ — 파기할 진화원 {n}장을 고르세요.");
        var positions = picked.Select(i => order[i]).Distinct().ToHashSet();
        if (positions.Count != n) positions = order.Take(n).ToHashSet();
        if (!order.Take(n).ToHashSet().SetEquals(positions)) Diag46.Hit("digiBurstChosen59");
        var removed = new List<CardData>();
        var rest = new List<CardData>();
        for (int i = 0; i < bottomUp.Count; i++) (positions.Contains(i) ? removed : rest).Add(bottomUp[i]);
        self.ReplaceSources59(rest);
        return removed;
    }

    private static bool BurstTrashedEffect59(CardData card) =>
        CardScripts.For(card)?.Effects.Any(e => e.Trigger == EffectTrigger.DigiBurstTrashedThis) == true;

    // =====================================================================
    // 상대의 효과를 받지 않는 동안 — 지속 효과 · 이미 받은 DP 효과
    // =====================================================================

    /// <summary>상대 카드가 주는 키워드를 더하기 전의 키워드 (더한 것을 알아내려고)</summary>
    private static Keyword[]? EnemyKeywordProbe59(CardInstance inst) => inst.ActiveKeywords.Count == 0 ? Array.Empty<Keyword>() : inst.ActiveKeywords.ToArray();

    /// <summary>상대 카드의 지속 효과로 새로 더해진 키워드 (없으면 null)</summary>
    private static List<Keyword>? EnemyKeywordAdded59(CardInstance inst, Keyword[]? before)
    {
        if (before == null || inst.ActiveKeywords.Count == before.Length) return null;
        return inst.ActiveKeywords.Where(k => Array.IndexOf(before, k) < 0).ToList();
    }

    /// <summary>지금 상대의 효과를 받지 않는지 채우고, 받지 않으면 상대 카드가 준 키워드를 뺀다</summary>
    private void ApplyOppShield59(CardInstance inst, List<Keyword>? enemyKeywords)
    {
        bool shield = CardScripts.HasKeyword(inst, Keyword.UnaffectedByOpponentEffects)
                      || AttackInProgress57 && CardScripts.HasKeyword(inst, Keyword.UnaffectedByOpponentDuringAttacks58);
        if (shield && !inst.OppShield59 && (inst.OppTempDp59 != 0 || inst.OppTimedDp59.Count > 0)) Diag46.Hit("oppShieldGivenDp59");
        inst.OppShield59 = shield;
        if (!shield || enemyKeywords == null) return;
        foreach (var k in enemyKeywords) inst.ActiveKeywords.Remove(k);
        Diag46.Hit("oppShieldKeywordAura59");
    }

    /// <summary>상대 카드의 지속 효과(DP · ≪시큐리티 어택≫)를 받지 않는지 — 상대의 효과 전부 · 상대의 디지몬의 효과 (테이머가 주는 것은 받는다)</summary>
    private bool OppAuraShield59(CardInstance inst, StaticContext auraCtx)
    {
        bool hit = inst.OppShield59
                   || auraCtx.Digimon != null && auraCtx.Digimon.TamerProxyOf == null && CardScripts.HasKeyword(inst, Keyword.UnaffectedByOpponentDigimonEffects);
        return hit && Diag46.HitTrue("oppShieldAura59");
    }

    /// <summary>어택 선언 · 어택 대상이 정해졌을 때: 어택 중에만 상대의 효과를 받지 않는 디지몬이 있으면 지속 효과를 다시 계산 (BT27-103)</summary>
    private void RefreshAttackShield59()
    {
        if (State.Player.BattleArea.Concat(State.DummyOpponent.BattleArea).Any(d => CardScripts.HasKeyword(d, Keyword.UnaffectedByOpponentDuringAttacks58)))
            RecalculateFieldBuffs();
    }

    // =====================================================================
    // BT20-037 【등장 시】를 발휘할 수 없다 (기간)
    // =====================================================================

    /// <summary>owner의 디지몬 · 테이머가 지금 【등장 시】를 발휘할 수 없는지 (상대의 기간 효과)</summary>
    private bool OnPlayPeriodBlocked59(PlayerZones owner) =>
        EffectRules.IsActive(owner.OnPlayBlockedUntil59, State) && Diag46.HitTrue("onPlayPeriodBlocked59");

    // =====================================================================
    // ST17-13 배틀 종료 시 이 카드로 진화
    // =====================================================================

    private readonly List<(PlayerZones Owner, Action Run)> _afterBattle59 = new();

    /// <summary>시큐리티 효과가 "배틀 종료 시"에 할 일을 맡긴다</summary>
    internal void QueueAfterBattle59(PlayerZones owner, Action run) => _afterBattle59.Add((owner, run));

    /// <summary>시큐리티 디지몬과의 배틀이 끝났을 때 맡긴 일을 처리한다</summary>
    private void RunAfterBattle59()
    {
        if (_afterBattle59.Count == 0) return;
        var list = _afterBattle59.ToList();
        _afterBattle59.Clear();
        foreach (var (_, run) in list)
        {
            if (State.IsGameOver) return;
            run();
        }
        RecalculateFieldBuffs();
    }
}
