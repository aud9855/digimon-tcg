using System;
using System.Collections.Generic;
using System.Linq;
using DigimonTcg.Core.Engine.Effects;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine;

/// <summary>
/// v0.37.0 미지원 문장 11차 묶음 — 엔진 쪽:
/// · 메모리 조건 지속 효과 (StaticContext.Engine · 메모리가 바뀌면 지속 효과를 다시 계산)
/// · DP 소멸 효과의 상한 +N (공식 Q&amp;A BT12-001: "DP 소멸 효과에 기술된 수치의 상한을 플러스") — 효과를 처리하는 동안만 <see cref="DeletionCap"/>
/// · "can't play (or move) Digimon with N DP or less" (Q&amp;A EX3-012: 상대의 행동이나 효과로 등장시킬 수 없다 · EX7-014: 이동도)
/// · 색 추가 ("This Digimon is also treated as yellow" — Q&amp;A BT3-014: 진화 조건 「옐로 Lv.5」로도 진화할 수 있다)
/// · 상대의 디지몬 전부가 얻는 키워드 ("none of your opponent's Digimon can suspend" · "~ can activate [On Play] effects")
/// · DNA 진화에서 다른 이름 · Lv.으로도 취급 (Q&amp;A BT20-025: 배틀 에어리어에 있는 동안만)
/// </summary>
public partial class GoldfishEngine
{
    /// <summary>방금 처리한 시큐리티 디지몬의 [시큐리티] 효과가 "Play this card without battling"이었는지 (ResolveSecurityDigimonEffect가 채운다)</summary>
    internal bool SecurityPlayWithoutBattle { get; private set; }

    /// <summary>배틀 없이 등장한 시큐리티 디지몬 수 (시뮬레이션 · 진단용)</summary>
    public int SecurityPlayWithoutBattleTotal { get; private set; }

    /// <summary>[시큐리티] "Play this card without battling and without paying the cost" (BT5-112 · EX2-054 · BT24-015) — 트래시에 놓인 이 카드를 효과로 등장 (막히면 배틀한다)</summary>
    private bool PlaySecurityDigimonWithoutBattle(PlayerZones owner, CardData card)
    {
        SecurityPlayWithoutBattle = false;
        if (!owner.Trash.Contains(card)) return false;
        if (!TryPlayByEffect(owner, card, PlayOrigin.Security, () => owner.Trash.Remove(card))) return false;
        SecurityPlayWithoutBattleTotal++;
        return true;
    }

    /// <summary>지속 효과의 색 추가 (값 = 색 비트)</summary>
    private static void ApplyExtraColors37(CardInstance inst, List<StaticEffect> statics, StaticContext ctx)
    {
        inst.ExtraColors.Clear();
        foreach (var e in statics)
        {
            if (e.Stat != StaticStat.ExtraColors) continue;
            int mask = e.Value(ctx);
            if (mask == 0) continue;
            foreach (CardColor col in Enum.GetValues(typeof(CardColor)))
                if ((mask & (1 << (int)col)) != 0) inst.ExtraColors.Add(col);
        }
    }

    /// <summary>상대 카드가 주는 키워드 ("none of your opponent's Digimon can suspend")</summary>
    private void ApplyEnemyKeywordAuras37(CardInstance inst, PlayerZones owner, PlayerZones enemy)
    {
        foreach (var (aura, auraCtx) in DigimonAuras(enemy, owner, StaticStat.EnemyKeyword).Concat(TamerAuras(enemy, owner, StaticStat.EnemyKeyword)))
            if (aura.Keyword != null && (aura.Subject == null || aura.Subject(inst)) && aura.Value(auraCtx) != 0)
                inst.ActiveKeywords.Add(aura.Keyword.Value);
    }

    /// <summary>owner 필드의 지속 효과가 enemy에 거는 "DP N 이하의 디지몬을 등장시킬 수 없다" (가장 큰 N, 없으면 0)</summary>
    private int StaticPlayDpLock37(PlayerZones owner, PlayerZones enemy)
    {
        int lockDp = 0;
        foreach (var (aura, auraCtx) in DigimonAuras(owner, enemy, StaticStat.OpponentPlayDpLock).Concat(TamerAuras(owner, enemy, StaticStat.OpponentPlayDpLock)))
            lockDp = Math.Max(lockDp, aura.Value(auraCtx));
        return lockDp;
    }

    /// <summary>방금 등장한 디지몬의 【등장 시】를 상대의 지속 효과가 막는지</summary>
    private bool OnPlayBlocked37(PlayerZones owner, CardInstance instance)
    {
        if (!owner.BattleArea.Contains(instance)) return false;
        var enemy = EnemyOf(owner);
        return DigimonAuras(enemy, owner, StaticStat.EnemyKeyword).Concat(TamerAuras(enemy, owner, StaticStat.EnemyKeyword))
            .Any(a => a.Effect.Keyword == Keyword.CannotActivateOnPlay && (a.Effect.Subject == null || a.Effect.Subject(instance)) && a.Effect.Value(a.Context) != 0);
    }

    /// <summary>
    /// 지금 owner가 발휘하는 DP 소멸 효과의 상한 +N: 자신의 필드의 "Add N to the maximum DP you can choose with DP-based deletion effects" (자신이 발휘하는 효과 전부) +
    /// 효과를 발휘한 디지몬(self)의 "add N to this Digimon's DP-based deletion effects' maximums"
    /// </summary>
    internal int DeletionCapBonus(PlayerZones owner, CardInstance? self)
    {
        int bonus = DigimonAuras(owner, EnemyOf(owner), StaticStat.DeleteCapAll).Concat(TamerAuras(owner, EnemyOf(owner), StaticStat.DeleteCapAll)).Sum(a => a.Effect.Value(a.Context));
        if (self != null && owner.BattleArea.Contains(self))
        {
            var ctx = new StaticContext { Engine = this, State = State, Owner = owner, Enemy = EnemyOf(owner), Digimon = self, OwnersTurn = self.IsOwnersTurn };
            bonus += StaticEffectsOf(self).Where(e => e.Stat == StaticStat.DeleteCapSelf).Sum(e => e.Value(ctx));
        }
        if (bonus > 0) DeletionCapAppliedTotal++;
        return Math.Max(0, bonus);
    }

    /// <summary>DP 소멸 효과의 상한이 늘어난 채로 처리한 소멸 효과 수 (시뮬레이션 · 진단용)</summary>
    public int DeletionCapAppliedTotal { get; private set; }

    /// <summary>DNA 진화 조건을 볼 때 이 디지몬의 모습 — 맨 위 카드 + "also treated as Lv.6 [Slayerdramon] for [Examon]'s DNA digivolution" (dnaCard 이름이 맞을 때)</summary>
    internal static IEnumerable<CardData> DnaFacets(CardInstance d, CardData dnaCard)
    {
        yield return d.TopCard;
        if (CardScripts.For(d.TopCard) is not { DnaAliases.Count: > 0 } script) yield break;
        foreach (var (forName, asName, level) in script.DnaAliases)
            if (CardNames.Is(dnaCard, forName))
                yield return new CardData
                {
                    CardId = d.TopCard.CardId + "#DNA", Name = asName, EnglishName = asName, Type = CardType.Digimon, Level = level, Color = d.TopCard.Color, SecondaryColor = d.TopCard.SecondaryColor,
                    DP = d.TopCard.DP, PlayCost = d.TopCard.PlayCost, Traits = d.TopCard.Traits,
                };
    }
}

public static partial class EffectRules
{
    /// <summary>v0.37.0 zones가 지금 card(디지몬)를 등장시킬 수 없는지 — "can't play Digimon with N DP or less" (행동 · 효과 모두, Q&amp;A EX3-012)</summary>
    public static bool PlayDpBlocked(GameState state, PlayerZones zones, CardData card) =>
        card.Type == CardType.Digimon && !card.IsDual
        && ((zones.StaticPlayDpLock > 0 && card.DP <= zones.StaticPlayDpLock) || zones.PlayDpLocks.Any(l => IsActive(l.Until, state) && card.DP <= l.Dp));

    /// <summary>v0.37.0 육성 에어리어의 이 디지몬을 배틀 에어리어로 이동시킬 수 없는지 — "can't play or move Digimon with N DP or less" (EX7-014)</summary>
    public static bool MoveDpBlocked(GameState state, PlayerZones zones, CardInstance digimon) =>
        zones.PlayDpLocks.Any(l => l.Move && IsActive(l.Until, state) && digimon.GetCurrentDP() <= l.Dp);
}

/// <summary>
/// v0.37.0 DP 소멸 효과를 처리하는 동안 상한에 더할 값 (TargetSpec의 "with N DP or less"가 본다). 효과 처리 밖에서는 0
/// </summary>
internal static class DeletionCap
{
    [ThreadStatic] private static int _bonus;
    public static int Bonus => _bonus;

    /// <summary>bonus를 켠 채로 run (끝나면 원래대로)</summary>
    public static T With<T>(int bonus, Func<T> run)
    {
        int prev = _bonus;
        _bonus = bonus;
        try { return run(); }
        finally { _bonus = prev; }
    }
}
