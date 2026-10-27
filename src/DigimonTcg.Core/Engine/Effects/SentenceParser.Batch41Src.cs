using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.41.0 진화원 조작 동작 — 원래 규칙이 읽지 못한 동작만 (ParseAction41) · 링크 코스트 감소 절</summary>
internal static partial class SentenceParser
{
    /// <summary>"[Your Turn] [Once Per Turn] When a [Social], [Tool] or [Game] trait card would link to this Digimon, you may reduce the cost by 1" (BT25-004 · BT25-045 — 링크 코스트)</summary>
    internal static readonly Regex LinkCostRe41 = new(
        @"^(?<labels>(?:\[[^\]]+\]\s*)*)When (?:an?|1) (?<pick>.+?) would (?:be )?link(?:ed)? to this Digimon, (?<may>you may )?reduce the (?:link |linking )?cost by (?<n>\d+)\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"[A|B|C]" (영어 데이터의 묶은 표기) → "[A], [B] or [C]"</summary>
    internal static string PipeList41(string s) => Regex.Replace(s, @"\[(?<x>[^\]|]+(?:\|[^\]|]+)+)\]", m =>
    {
        var xs = m.Groups["x"].Value.Split('|').Select(x => $"[{x.Trim()}]").ToList();
        return xs.Count == 2 ? $"{xs[0]} or {xs[1]}" : string.Join(", ", xs.Take(xs.Count - 1)) + " or " + xs[^1];
    });

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Func<CardData, bool>?> DetachSpecCache41 = new(StringComparer.Ordinal);

    /// <summary>top 카드의 ≪분리 (지정)≫이 card를 지정 링크 카드로 받는지 (AI 링크가 쓴다)</summary>
    internal static bool DetachAccepts41(CardData top, CardData card)
    {
        var spec = DetachSpecCache41.GetOrAdd(top.CardId, _ =>
            Regex.Match(top.EffectText ?? "", @"＜Detach \((?<arg>[^＞]+)\)＞", I) is { Success: true } m ? ParseSpecifiedCard(m.Groups["arg"].Value) : null);
        return spec != null && spec(card);
    }

    /// <summary>"＜Succession ([Ceresmon])＞ (This Digimon gains all effects other than ＜Succession＞ on its topmost specified digivolution card.)" (BT26-032 — ≪계승≫)</summary>
    internal static readonly Regex SuccessionRe41 = new(@"^＜Succession \((?<arg>.+?)\)＞(?:\s*\(.*\))?\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool SuccessionClause41(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        var m = SuccessionRe41.Match(rawClause.Trim());
        if (!m.Success || inherited || card.Type != CardType.Digimon) return false;
        if (ParseSpecifiedCard(m.Groups["arg"].Value) is not { } spec) return false;
        def?.Script41.SuccessionSpecs41.Add(spec);
        return true;
    }

    private static bool LinkCostClause41(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string c = PipeList41(Regex.Replace(rawClause.Trim(), @"^Inherited Effect\s*", "", I));
        var m = LinkCostRe41.Match(c);
        if (!m.Success) return false;
        var labels = Regex.Matches(m.Groups["labels"].Value, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value.Trim().ToLowerInvariant()).ToList();
        if (labels.Any(l => l is not ("your turn" or "once per turn"))) return false;
        string pickText = "1 " + Regex.Replace(m.Groups["pick"].Value, @"\bcards?$", "card", I);
        if ((ParseCardPick(pickText) ?? ParseNamePick(pickText)) is not { } pick) return false;
        int n = int.Parse(m.Groups["n"].Value);
        var source = inherited ? EffectSource.Inherited : EffectSource.Main;
        // 진화원 효과에 [자신의 턴]이 빠진 표기(진화원 칸)도 같은 효과로 본다 — 메인 칸의 "Inherited Effect [Your Turn] ~"가 있으면 그것 (BT25-004)
        bool yourTurn = labels.Contains("your turn") || Regex.IsMatch(card.EffectText, @"Inherited Effect \[Your Turn\][^\n]*would link to this Digimon", I) && inherited;
        def?.LinkCostModifier41(new LinkCostModifier41(source, pick.Match, n, labels.Contains("once per turn"), yourTurn, m.Groups["may"].Success,
            $"{source}|{Regex.Replace(pickText.ToLowerInvariant(), @"\s+", " ")}|{n}"));
        return true;
    }

    /// <summary>
    /// "Place 1 of your opponent's Digimon under 1 of your opponent's other Digimon as its bottom digivolution card (or under 1 of their Tamers)" (BT11-088 · BT12-083) ·
    /// "(You may) place 1 of your opponent's Digimon as any of their other Digimon's bottom digivolution card or under any of their Tamers" (EX10-056) —
    /// 공식 BT11-088 "상대의 디지몬 1마리를 다른 상대의 디지몬 1마리의 진화원 아래에 놓는다" · EX10-056 "~ 또는 테이머 아래에 놓을 수 있다"
    /// (종합 룰 4-7-7: 아래에 놓이는 디지몬은 맨 위 카드만 놓이고 그 진화원은 트래시)
    /// </summary>
    internal static readonly Regex OppUnderOppRe41 = new(
        @"^Place (?<t>1 of your opponent's (?:[^,]*? )?Digimon(?: with [^,]+?)?) (?:under (?:1|any) of (?:your opponent's|their) other Digimon as its bottom digivolution card|as (?:1|any) of (?:your opponent's|their) other Digimon's bottom digivolution card)(?<tamer> or under (?:1|any) of (?:your opponent's|their) Tamers)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Action<EffectContext>? OpponentUnderOpponent41(string s, List<EffectTrigger> triggers)
    {
        var m = OppUnderOppRe41.Match(s);
        if (!m.Success) return null;
        var spec = TargetSpec.Parse(m.Groups["t"].Value);
        if (spec == null || !spec.Opponent) return null;
        bool tamerOk = m.Groups["tamer"].Success;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        return c =>
        {
            var enemy = c.Enemy;
            // 놓일 곳이 있는 대상만 (다른 디지몬 또는 테이머)
            var victims = Pick(spec, c, false).Where(v => enemy.BattleArea.Contains(v) && (enemy.BattleArea.Any(h => !ReferenceEquals(h, v)) || (tamerOk && enemy.Tamers.Count > 0))).ToList();
            if (victims.Count == 0)
            {
                // Pick이 고른 대상에 놓일 곳이 없으면 놓일 곳이 있는 다른 대상
                var all = enemy.BattleArea.Where(v => spec.Filters.All(f => f(v)) && spec.ContextFilters.All(f => f(c, v))
                    && (enemy.BattleArea.Any(h => !ReferenceEquals(h, v)) || (tamerOk && enemy.Tamers.Count > 0))).ToList();
                victims = all.Count == 0 ? all : c.Engine.ChooseTargets(c.Owner, all, 1, $"[{c.Card.Name}] 진화원 아래에 놓을 상대의 디지몬을 고르세요.", optional: false, preselected: null).Take(1).ToList();
            }
            var v = victims.FirstOrDefault();
            if (v == null) { c.LastStepResolved = false; Emit(c, label, "놓을 수 있는 상대의 디지몬이 없습니다"); return; }
            var hosts = enemy.BattleArea.Where(h => !ReferenceEquals(h, v)).OrderByDescending(h => h.TopCard.Level).ToList();
            bool underTamer = hosts.Count == 0 || (tamerOk && enemy.Tamers.Count > 0
                && c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] [{v.TopCard.Name}]을(를) 상대의 테이머 아래에 놓을까요? (아니오 = 다른 상대의 디지몬의 진화원 아래)", recommended: false));
            string name = v.TopCard.Name;
            bool ok;
            string where;
            if (underTamer)
            {
                if (!tamerOk || enemy.Tamers.Count == 0) { c.LastStepResolved = false; return; }
                ok = c.Engine.PlaceDigimonUnderTamers41(enemy, v);
                where = "상대의 테이머 아래";
            }
            else
            {
                var host = hosts.Count == 1 ? hosts[0]
                    : c.Engine.ChooseTargets(c.Owner, hosts, 1, $"[{c.Card.Name}] [{name}]을(를) 진화원 아래에 놓을 상대의 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                if (host == null) { c.LastStepResolved = false; return; }
                ok = c.Engine.PlaceDigimonUnder(enemy, v, host, canPrevent: true);
                where = $"[{host.TopCard.Name}]의 진화원 아래";
            }
            if (!ok) { c.LastStepResolved = false; Emit(c, label, $"[{name}]을(를) 놓지 못했습니다"); return; }
            Diag41.Hit(ref Diag41.OppUnderOpp);
            Emit(c, label, $"상대의 [{name}]을(를) {where}에 놓았습니다");
            c.RecalculateFieldBuffs();
        };
    }

    /// <summary>
    /// "Trash the top card of this Digimon" (BT9-083 · BT13-058 · BT13-091 · EX10-022) · "Trash the top card of 1 of your Digimon with the [Armor Form] trait" (BT8-110) —
    /// 맨 위 카드를 파기하고 바로 아래 진화원이 맨 위 카드가 된다 (≪아머 퍼지≫와 같은 처리). 진화원이 없으면 하지 않는다
    /// </summary>
    private static Action<EffectContext>? TrashTopCard41(string s, List<EffectTrigger> triggers)
    {
        var m = Regex.Match(s, @"^Trash the top card of (?<t>this Digimon|1 of your .+)$", I);
        if (!m.Success) return null;
        bool self = m.Groups["t"].Value.Equals("this Digimon", StringComparison.OrdinalIgnoreCase);
        TargetSpec? spec = null;
        if (!self && (spec = TargetSpec.Parse(m.Groups["t"].Value)) is not { Opponent: false, AnySide: false }) return null;
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        return c =>
        {
            CardInstance? d;
            if (self) d = c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? c.Self : null;
            else
            {
                var pool = c.Owner.BattleArea.Where(x => x.EvolutionSources.Count > 0 && spec!.Filters.All(f => f(x)) && spec.ContextFilters.All(f => f(c, x))).ToList();
                d = pool.Count <= 1 ? pool.FirstOrDefault() : c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] 맨 위 카드를 파기할 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
            }
            if (d == null || !c.Engine.TrashTopCardOf41(c.Owner, d, out var top)) { c.LastStepResolved = false; Emit(c, label, "맨 위 카드를 파기할 수 없습니다 (진화원 없음)"); return; }
            c.LastTargets = new List<CardInstance> { d };
            Diag41.Hit(ref Diag41.TrashTop);
            Emit(c, label, $"[{top!.Name}]을(를) 파기 — [{d.TopCard.Name}](으)로 남습니다");
        };
    }
}
