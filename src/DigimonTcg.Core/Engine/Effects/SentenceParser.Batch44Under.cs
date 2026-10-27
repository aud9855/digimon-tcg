using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.44.0 18차 묶음 — 상대의 디지몬/테이머 아래의 카드:
///  - "trash any 3 cards under 1 of your opponent's Digimon or Tamers" (BT16-069 — 공식 "상대의 디지몬/테이머 1마리(명) 아래에서 선택하여 3장 파기한다") ·
///    "Trash any 4 cards under your opponent's Digimon or Tamers" (EX12-033) — 진화원(뒷면 포함) · 테이머 아래 카드 중에서 고른다. 겹쳐진 카드 보호(BT21-060)를 지킨다
///  - "For each one, (you may) trash any 1 card under ~" (LM-005 · RB1-014 · RB1-016 — 공식 "파기한 1장마다 ~ 1장 파기한다") · "For each card trashed by this effect, ~" (P-089)
/// </summary>
internal static partial class SentenceParser
{
    internal static readonly Regex TrashUnderRe44 = new(
        @"^(?<may>you may )?trash (?:any )?(?<n>\d+) cards? under (?<one>(?:1|one) of )?your opponent's Digimon or Tamers$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static (Action<EffectContext>? Act, string? Error)? TrashUnderAction44(string s, string label)
    {
        var m = TrashUnderRe44.Match(s);
        if (!m.Success) return null;
        int n = int.Parse(m.Groups["n"].Value);
        bool one = m.Groups["one"].Success, may = m.Groups["may"].Success;
        return (c => TrashUnder44(c, n, one, may, label), null);
    }

    /// <summary>상대의 디지몬 · 테이머 아래 카드 n장을 고른 뒤 파기한다 (one = 디지몬/테이머 1마리(명)에서만). 파기한 매수</summary>
    private static int TrashUnder44(EffectContext c, int n, bool one, bool may, string label)
    {
        var z = c.Enemy;
        // 후보: (어느 디지몬 · null = 테이머 아래, 카드) — 추천: DP가 높은 디지몬의 위쪽 진화원부터, 그다음 테이머 아래
        var hosts = z.BattleArea.Where(d => d.TamerProxyOf == null && d.EvolutionSources.Count > 0 && !c.Engine.IsUnaffectedBy(c.Owner, d))
            .OrderByDescending(d => d.GetCurrentDP()).ThenByDescending(d => d.EvolutionSources.Count).ToList();
        var groups = hosts.Select(d => (Host: (CardInstance?)d, Cards: d.GetEvolutionStackBottomUp().AsEnumerable().Reverse().ToList())).ToList();
        if (z.UnderTamers.Count > 0 && z.Tamers.Count > 0) groups.Add((null, z.UnderTamers.ToList()));
        groups = groups.Where(g => g.Cards.Count > 0 && (g.Host == null || !c.Engine.StackProtected42(z, g.Host))).ToList();
        if (groups.Count == 0 || n <= 0) { c.LastStepResolved = false; Emit(c, label, "파기할 아래의 카드가 없습니다"); return 0; }
        if (may && !c.Engine.AskYesNo(c.Owner, null, $"[{c.Card.Name}] 상대의 디지몬/테이머 아래의 카드를 {n}장 파기할까요?", recommended: true)) { c.LastStepResolved = false; return 0; }
        if (one)
        {
            // 1마리(명)를 먼저 고른다 — 플레이어는 디지몬을 고르고(테이머 아래면 고르지 않음), AI는 가장 많이 파기할 수 있는 곳
            var best = groups.OrderByDescending(g => Math.Min(n, g.Cards.Count)).ThenByDescending(g => g.Host?.GetCurrentDP() ?? 0).First();
            var digis = groups.Where(g => g.Host != null).Select(g => g.Host!).ToList();
            if (digis.Count > 0 && ReferenceEquals(c.Owner, c.State.Player))
            {
                var pickHost = c.Engine.ChooseTargets(c.Owner, digis, 1, $"[{c.Card.Name}] 아래의 카드를 파기할 상대의 디지몬을 고르세요.", optional: groups.Any(g => g.Host == null), best.Host).FirstOrDefault();
                best = pickHost == null ? groups.First(g => g.Host == null) : groups.First(g => ReferenceEquals(g.Host, pickHost));
            }
            groups = new() { best };
        }
        var pool = groups.SelectMany(g => g.Cards.Select(x => (g.Host, Card: x))).ToList();
        var picks = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.DigivolutionCards, pool.Select(p => p.Card).ToList(), Math.Min(n, pool.Count), upTo: false,
            $"[{c.Card.Name}] 파기할 상대의 디지몬/테이머 아래의 카드를 고르세요 ({Math.Min(n, pool.Count)}장).").Distinct().Where(i => i >= 0 && i < pool.Count).Select(i => pool[i]).ToList();
        int done = 0;
        var trashed = new List<CardData>();
        foreach (var (host, x) in picks)
        {
            if (host != null)
            {
                if (!z.BattleArea.Contains(host) || host.TakeSource(y => ReferenceEquals(y, x)) == null) continue;
            }
            else
            {
                if (!z.UnderTamers.Remove(x)) continue;
                z.FaceDownUnderTamers.Remove(x);
            }
            z.Trash.Add(x);
            trashed.Add(x);
            done++;
        }
        if (trashed.Count > 0) c.Engine.ApplyOverflowPublic34(z, trashed);
        c.RecalculateFieldBuffs();
        c.PaidCount = done;
        if (done == 0) c.LastStepResolved = false;
        else Diag44.Hit("trashUnder");
        Emit(c, label, done > 0 ? $"상대의 디지몬/테이머 아래의 카드 {done}장을 파기했습니다" : "파기하지 못했습니다");
        return done;
    }

    /// <summary>"For each one, (you may) trash any 1 card under ~" · "For each card trashed by this effect, you may trash 1 card under 1 of ~" — 앞 문장에서 파기한 매수 (PaidCount)</summary>
    private static Step? TrashUnderForEach44(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        var m = Regex.Match(sentence.Trim().TrimEnd('.'), @"^(?:Then, )?For (?:each one|each card trashed(?: by this effect)?), (?<core>.+)$", I);
        if (!m.Success) return null;
        string core = m.Groups["core"].Value;
        var um = TrashUnderRe44.Match(core);
        if (!um.Success || um.Groups["n"].Value != "1") return null;
        bool may = um.Groups["may"].Success, one = um.Groups["one"].Success;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        return new Step(null, c =>
        {
            int k = Math.Min(c.PaidCount, 20);
            if (k <= 0) { c.LastStepResolved = false; return; }
            // 1장씩 k번 (각각 다른 디지몬/테이머에서 골라도 된다) — "1 of ~"면 1장마다 대상을 고른다
            if (may && !c.Engine.AskYesNo(c.Owner, null, $"[{c.Card.Name}] 상대의 디지몬/테이머 아래의 카드를 {k}장까지 파기할까요?", recommended: true)) { c.LastStepResolved = false; return; }
            int total = 0;
            if (one) for (int i = 0; i < k; i++) total += TrashUnder44(c, 1, true, false, label);
            else total = TrashUnder44(c, k, false, false, label);
            c.PaidCount = total;
            c.LastStepResolved = total > 0;
        }, false, sentence.Trim());
    }
}
