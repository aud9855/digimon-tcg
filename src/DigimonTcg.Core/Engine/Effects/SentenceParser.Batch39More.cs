using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.39.0 (더): 상대가 트래시에서 등장 (EX5-060 · BT15-078) · "with a level less than or equal to it" (EX5-060 — 공식 "등장한 디지몬의 Lv. 이하의") ·
/// 그 디지몬의 진화원 아래에 놓기 (BT19-082 · EX8-067 · EX11-065)
/// </summary>
internal static partial class SentenceParser
{
    internal static readonly Regex OppPlaysRe39 = new(@"^Your opponent plays (?<pick>.+?) from their (?<z>trash|hand) (?<sus>suspended )?without paying (?:the|its) (?:memory )?cost(?<noplay> ⟦no-onplay⟧)?$", RegexOptions.IgnoreCase);

    internal static readonly Regex LevelLeItRe39 = new(@"^Play (?<pick>.+?) with a level (?:less than or equal to|as high or lower than|equal to or lower than) (?:it|that Digimon)(?:'s level)? from your (?<z>trash|hand|hand or trash) without paying (?:the|its) (?:memory )?cost$", RegexOptions.IgnoreCase);

    internal static readonly Regex PlaceToThatRe39 = new(@"^Place (?<pick>.+?) from your (?<from>hand or trash|hand and/or trash|trash|hand) (?:as (?:that|those|any of those) Digimon's bottom digivolution cards?|under (?:that Digimon|it) as its bottom digivolution cards?)$", RegexOptions.IgnoreCase);

    internal static readonly Regex AtTurnEndRe39 = new(@"^At (?:the )?(?<next>next )?end of (?<w>your opponent's|their|the|this|your) turn, (?<v>return|delete) (?<t>it|that Digimon|that token|those Digimon|them)(?<hand> to (?:the|its owner's|your|their owners'?) hands?)?$", RegexOptions.IgnoreCase);

    private static (Action<EffectContext>? Act, string? Error)? MoreAction39(string s, string label, bool security)
    {
        Match m;
        // "At the next end of your opponent's turn, return it to the hand" (BT16-084 · BT17-025 — 공식 "다음 상대의 턴 종료 시, 그 디지몬을 패로 되돌린다") ·
        // "At the next end of your opponent's turn, delete it" (BT16-074) · "At the end of your opponent's turn, delete that token" (P-165) — 이 효과로 등장한 그 디지몬
        if ((m = AtTurnEndRe39.Match(s)).Success && (m.Groups["v"].Value.Equals("delete", StringComparison.OrdinalIgnoreCase) != m.Groups["hand"].Success))
        {
            bool del = m.Groups["v"].Value.Equals("delete", StringComparison.OrdinalIgnoreCase);
            string w = m.Groups["w"].Value.ToLowerInvariant();
            var d = w is "your opponent's" or "their" ? (m.Groups["next"].Success ? Duration.OpponentsNextTurnEnd : Duration.OpponentsTurnEnd) : w == "your" ? Duration.YourTurnEnd : Duration.ThisTurn;
            return (c =>
            {
                var list = c.PlayedThisEffect.Where(x => c.Owner.BattleArea.Contains(x)).ToList();
                if (list.Count == 0) list = c.LastTargets.Where(x => c.Owner.BattleArea.Contains(x)).ToList();
                if (list.Count == 0) { c.LastStepResolved = false; Emit(c, label, "이 효과로 등장한 디지몬이 없습니다"); return; }
                int until = Until(c, d);
                foreach (var x in list)
                {
                    if (del) { c.Engine.ScheduleDeletionAtTurnEnd(x, c.Owner, until); Diag39.Hit(ref Diag39.ScheduledDelete); }
                    else c.Engine.ScheduleReturnToHand39(x, c.Owner, until);
                }
                Emit(c, label, $"[{string.Join(", ", list.Select(x => x.TopCard.Name))}] 정해진 턴 종료 시 {(del ? "소멸" : "패로 되돌림")}");
            }, null);
        }
        // "Your opponent plays 1 level 4 or lower Digimon card from their trash suspended without paying the cost." (EX5-060 — 공식 "상대는 본인의 트래시에서 ~ 레스트 상태로 등장시킨다") — 상대가 고른다
        if ((m = OppPlaysRe39.Match(s)).Success && ParseCardPick(m.Groups["pick"].Value) is { } op)
        {
            var (count, match) = op;
            bool fromTrash = m.Groups["z"].Value.Equals("trash", StringComparison.OrdinalIgnoreCase), suspended = m.Groups["sus"].Success, noOnPlay = m.Groups["noplay"].Success;
            var origin = fromTrash ? PlayOrigin.Trash : PlayOrigin.Hand;
            return (c =>
            {
                var zone = fromTrash ? c.Enemy.Trash : c.Enemy.Hand;
                var played = new List<string>();
                var playedInst = new List<CardInstance>();
                for (int i = 0; i < count; i++)
                {
                    // 상대가 고른다 — 고르는 쪽(상대)의 추천: Lv. · DP가 높은 것
                    var pool = zone.Where(x => x.Type == CardType.Digimon && match(x) && c.Engine.CanPlayByEffect(c.Enemy, x, origin))
                        .OrderByDescending(x => x.Level).ThenByDescending(x => x.DP).ToList();
                    if (pool.Count == 0 || c.Enemy.BattleArea.Count >= 12) break;
                    var pick = c.Engine.ChooseCards(c.Enemy, fromTrash ? CardChoiceZone.Trash : CardChoiceZone.Hand, pool, 1, false, $"[{c.Card.Name}] 상대의 효과로 등장시킬 카드를 고르세요.").FirstOrDefault();
                    if (pick == null) break;
                    var before = c.Enemy.BattleArea.ToList();
                    if (!c.Engine.TryPlayByEffect(c.Enemy, pick, origin, () => zone.Remove(pick), triggerOnPlay: !noOnPlay, suspended: suspended)) continue;
                    played.Add(pick.Name);
                    playedInst.AddRange(c.Enemy.BattleArea.Except(before));
                }
                if (played.Count == 0) { c.LastStepResolved = false; Emit(c, label, "상대가 등장시킬 카드가 없습니다"); return; }
                c.LastTargets = playedInst;   // "Then, you may switch the target of attack to that Digimon" (BT15-078)
                Diag39.Hit(ref Diag39.LevelLeIt);
                Emit(c, label, $"상대가 [{string.Join(", ", played)}]을(를) {(suspended ? "레스트 상태로 " : "")}등장시켰습니다");
            }, null);
        }
        // "you may play 1 purple Digimon card with a level less than or equal to it from your trash without paying the cost" (EX5-060 — "it" = 등장한 상대의 디지몬)
        if ((m = LevelLeItRe39.Match(s)).Success && ParseCardPick(m.Groups["pick"].Value) is { } lp)
        {
            var (count, match) = lp;
            string z = m.Groups["z"].Value.ToLowerInvariant();
            bool hand = z.Contains("hand"), trash = z.Contains("trash");
            return (c =>
            {
                var refD = c.LastTargets.FirstOrDefault() ?? c.Other;
                int? lv = refD?.TopCard.Level;
                if (lv == null || lv <= 0) { c.LastStepResolved = false; Emit(c, label, "비교할 디지몬이 없습니다"); return; }
                Func<CardData, bool> ok = x => x.Type is CardType.Digimon && match(x) && x.Level <= lv;
                var pool = (hand ? PoolFrom(c.Owner.Hand, ok) : Enumerable.Empty<PoolCard>()).Concat(trash ? PoolFrom(c.Owner.Trash, ok) : Enumerable.Empty<PoolCard>())
                    .Where(p => c.Engine.CanPlayByEffect(c.Owner, p.Card, OriginOf(c, p))).OrderByDescending(p => p.Card.Level).ThenByDescending(p => p.Card.DP).ToList();
                var played = new List<string>();
                foreach (var p in ChooseFromPool(c, hand && trash ? CardChoiceZone.HandOrTrash : hand ? CardChoiceZone.Hand : CardChoiceZone.Trash, pool, Math.Min(count, pool.Count), $"[{c.Card.Name}] Lv.{lv} 이하의 등장시킬 카드를 고르세요."))
                {
                    if (c.Owner.BattleArea.Count >= 12) break;
                    var p0 = p;
                    if (!c.Engine.TryPlayByEffect(c.Owner, p.Card, OriginOf(c, p), () => TakeFromPool(c, p0))) continue;
                    played.Add(p.Card.Name);
                }
                if (played.Count == 0) { c.LastStepResolved = false; Emit(c, label, "등장시킬 카드가 없습니다"); return; }
                Diag39.Hit(ref Diag39.LevelLeIt);
                Emit(c, label, $"[{string.Join(", ", played)}] 코스트 없이 등장 (Lv.{lv} 이하)");
            }, null);
        }
        // "place 1 ~ from your hand as that Digimon's bottom digivolution card" (BT19-082) · "place up to 2 ~ from your trash as that Digimon's bottom digivolution cards" (EX8-067) ·
        // "~ from your hand or trash as any of those Digimon's bottom digivolution card" (EX11-065) — 사건의 그 디지몬
        if ((m = PlaceToThatRe39.Match(s)).Success && ParseCardPick(m.Groups["pick"].Value) is { } pp)
        {
            var (count, match) = pp;
            bool upTo = IsUpTo(m.Groups["pick"].Value);
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool hand = from.Contains("hand"), trash = from.Contains("trash");
            return (c =>
            {
                var host = c.LastTargets.FirstOrDefault(d => c.Owner.BattleArea.Contains(d)) ?? (c.Other != null && c.Owner.BattleArea.Contains(c.Other) ? c.Other : null)
                           ?? c.PlayedThisEffect.FirstOrDefault(d => c.Owner.BattleArea.Contains(d));
                if (host == null) { c.LastStepResolved = false; Emit(c, label, "놓을 디지몬이 없습니다"); return; }
                Func<CardData, bool> ok = x => match(x) && !ReferenceEquals(x, c.Card);
                var pool = (hand ? PoolFrom(c.Owner.Hand, ok) : Enumerable.Empty<PoolCard>()).Concat(trash ? PoolFrom(c.Owner.Trash, ok) : Enumerable.Empty<PoolCard>())
                    .OrderByDescending(p => p.Card.InheritedEffectText.Trim().Length > 0).ThenBy(p => p.Card.Level).ToList();
                var idx = c.Engine.ChooseCardIndexes(c.Owner, hand && trash ? CardChoiceZone.HandOrTrash : hand ? CardChoiceZone.Hand : CardChoiceZone.Trash,
                    pool.Select(p => p.Card).ToList(), Math.Min(count, pool.Count), upTo, $"[{c.Card.Name}] [{host.TopCard.Name}]의 진화원 아래에 놓을 카드를 고르세요.");
                int placed = 0;
                foreach (var i in idx)
                {
                    if (!TakeFromPool(c, pool[i])) continue;
                    host.AddBottomSource(pool[i].Card);
                    placed++;
                }
                c.PaidCount = placed;
                c.LastTargets = new List<CardInstance> { host };
                if (placed == 0) { c.LastStepResolved = false; Emit(c, label, "놓을 카드가 없습니다"); return; }
                c.RecalculateFieldBuffs();
                Diag39.Hit(ref Diag39.SourcePlace);
                Emit(c, label, $"[{host.TopCard.Name}]의 진화원 아래에 {placed}장 놓았습니다");
            }, null);
        }
        return null;
    }
}

internal static partial class SentenceParser
{
    internal static readonly Regex AtTurnEndActRe39 = new(@"^At the end of (?:your opponent's|their) (?<next>next )?turn, (?<x>.+)$", RegexOptions.IgnoreCase);
}
