using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.45.0 공개한 카드 묶음의 처리 상태 (EffectContext.Rv45) — 중간 문장들이 "among them"으로 본다</summary>
internal sealed class RevealState45
{
    public required PlayerZones Zone { get; init; }
    public required List<CardData> Cards { get; init; }
    public int Played { get; set; }
    public int Added { get; set; }
}

/// <summary>
/// v0.45.0 덱 위 공개 묶음 — "Reveal the top N cards of your deck. ~ among them ~. Return the revealed cards to the top or bottom of the deck." 를 한 문장(⟦rv45⟧)으로 묶어,
/// 중간 문장이 공개한 카드를 보게 한다 (원래 공개 규칙이 읽지 못한 묶음만):
///  - "For each [D-Brigade] or [DigiPolice] trait card among them, ~" (BT16-060) · "＜De-Digivolve 1＞ ~ for each Digimon card among them" (BT14-065) ·
///    "For each of your opponent's Digimon, you may play 1 ~ among the revealed cards ~" (BT8-068)
///  - "Choose 1 ~ among them, and delete ~ whose play cost is less than or equal to the chosen card's play cost" (BT9-105) · "~ up to its play cost's total worth ~" (BT14-067)
///  - "~ with as high or lower a play cost as 1 Digimon card among them" (BT16-065)
///  - "If all of them are black, ~" (BT4-096) · "Place 1 of them on top of your security stack face down" + "Add the remaining card to your hand" (BT6-100)
///  - "You may play any number of ~ whose play costs add up to 15 or less among them ~" + "~ for each Digimon played with this effect" (BT8-106)
///  - "Add all ~ among them to your hand" + "Then, ~ for each card added to your hand by this effect" (BT4-107)
/// </summary>
internal static partial class SentenceParser
{
    internal const string RevealMark45 = "⟦rv45⟧";

    private static readonly Regex RevealHeadRe45 = new(@"^(?:Then, )?(?<may>You may )?(?:[Rr]eveal the top (?<n>\d+) cards of your deck|(?<opp>Your opponent reveals the top (?<n>\d+) cards of their deck))$", RegexOptions.Compiled);
    private static readonly Regex RevealEndRe45 = new(
        @"^(?:(?<tb>(?:Return|Place) the (?:revealed )?cards (?:to|at) (?:either )?the top or (?:the )?bottom of the deck(?: in any order)?)|(?<trash>Trash the (?:revealed cards|rest|remaining cards?))|(?<top>Place the (?:cards|rest|remaining cards) (?:at|on) the top of (?:the|your) deck in any order)|(?<bottom>(?:Place|Return) the (?:rest|remaining cards) (?:at|to) the bottom of (?:the|your) deck(?: in any order)?)|(?<hand>Add the (?:remaining cards?|rest) to (?:the|your) hand))$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RevealPostRe45 = new(@"^Then, (?<inner>.+) for each card added to (?:the|your) hand by this effect$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>절 규칙: 공개 묶음을 한 문장으로 (원래 규칙이 읽지 못한 절만 — ClauseRules45)</summary>
    private static string JoinRevealGroup45(string t)
    {
        var lead = Regex.Match(t, @"^(?<labels>(?:Inherited Effect\s*|Security Effect\s*)?(?:\[[^\]]+\]\s*)*)(?<body>.*)$", RegexOptions.Singleline);
        string labels = lead.Groups["labels"].Value, body = lead.Groups["body"].Value;
        // "＜De-Digivolve 1＞ 1 of your opponent's Digimon. (Trash the top card. You can't trash past level 3 cards.) for each Digimon card among them." (BT14-065)
        body = Regex.Replace(body, @"＜De-Digivolve (?<n>\d+)＞ (?<t>1 of your opponent's Digimon)\.?\s*\(Trash the top card\. You can't trash past level 3 cards\.\)\s*for each (?<x>[^.]+?) among them\.", "For each ${x} among them, ＜De-Digivolve ${n}＞ ${t}.", I);
        var ss = Regex.Split(body.Trim(), @"(?<=\.)\s+(?=[A-Z＜・•])").ToList();
        for (int i = 0; i < ss.Count; i++)
        {
            if (!RevealHeadRe45.IsMatch(ss[i].Trim().TrimEnd('.'))) continue;
            for (int j = i + 2; j < ss.Count && j <= i + 4; j++)
            {
                if (!RevealEndRe45.IsMatch(ss[j].Trim().TrimEnd('.'))) continue;
                int last = j;
                if (j + 1 < ss.Count && RevealPostRe45.IsMatch(ss[j + 1].Trim().TrimEnd('.'))) last = j + 1;
                string joined = string.Join(" " + RevealMark45 + " ", ss.Skip(i).Take(last - i + 1).Select(x => x.Trim().TrimEnd('.'))) + ".";
                ss.RemoveRange(i, last - i + 1);
                ss.Insert(i, joined);
                return labels + string.Join(" ", ss);
            }
        }
        return t;
    }

    private static readonly ConcurrentDictionary<string, Step?> RuntimeSteps45 = new();

    /// <summary>처리 중에 값을 채운 문장을 읽는다 (같은 문장은 한 번만)</summary>
    private static Step? RuntimeStep45(string sentence, CardData card, List<EffectTrigger> triggers) =>
        RuntimeSteps45.GetOrAdd(card.CardId + "|" + sentence, _ =>
        {
            _gate45++; _gate44++;
            try { var r = ParseStep(sentence, card, triggers); return r.Error == null ? r.Step : null; }
            finally { _gate45--; _gate44--; }
        });

    private static (int Count, Func<CardData, bool> Match)? RevealPick45(string x)
    {
        string p = Regex.IsMatch(x, @"^(?:\d+|an?) ", I) ? Regex.Replace(x, @"^an? ", "1 ", I) : "1 " + x;
        return ParseCardPick(p) ?? ParseNamePick(p);
    }

    private static Step? RevealGroup45(string s, CardData card, List<EffectTrigger> triggers)
    {
        var parts = s.Split(RevealMark45).Select(x => x.Trim().TrimEnd('.').Trim()).ToList();
        if (parts.Count < 3) return null;
        var hm = RevealHeadRe45.Match(parts[0]);
        if (!hm.Success) return null;
        int n = int.Parse(hm.Groups["n"].Value);
        bool opp = hm.Groups["opp"].Success, may = hm.Groups["may"].Success;
        Action<EffectContext>? post = null;
        int endIdx = parts.Count - 1;
        if (RevealPostRe45.Match(parts[^1]) is { Success: true } pm)
        {
            var inner = ParseStep(Cap44(pm.Groups["inner"].Value) + ".", card, triggers);
            if (inner.Error != null || inner.Step == null) return null;
            var st = inner.Step;
            post = c => { if (c.Rv45 is { } rv) for (int k = 0; k < Math.Min(rv.Added, 20); k++) RunStep(st, c); };
            endIdx--;
        }
        var em = RevealEndRe45.Match(parts[endIdx]);
        if (!em.Success || endIdx < 2) return null;
        if (em.Groups["hand"].Success && opp) return null;
        var mids = new List<Action<EffectContext>>();
        foreach (var mid in parts.Skip(1).Take(endIdx - 1))
        {
            if (RevealMid45(mid, card, triggers, opp) is not { } act) return null;
            mids.Add(act);
        }
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        return new Step(null, c =>
        {
            var zone = opp ? c.Enemy : c.Owner;
            if (may && !c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 덱 위에서부터 {n}장 공개할까요?", true)) { c.LastStepResolved = false; return; }
            var deck = zone.MainDeck;
            var revealed = deck.Take(n).ToList();
            deck.RemoveRange(0, revealed.Count);
            var outer = c.Rv45;
            var rv = new RevealState45 { Zone = zone, Cards = revealed };
            c.Rv45 = rv;
            Diag45.Hit("revealGroup");
            Emit(c, label, $"{(opp ? "상대의 " : "")}덱 위 {revealed.Count}장 공개: [{string.Join(", ", revealed.Select(x => x.Name))}]");
            try
            {
                foreach (var m in mids) m(c);
            }
            finally
            {
                var rest = rv.Cards.ToList();
                rv.Cards.Clear();
                if (rest.Count > 0)
                {
                    if (em.Groups["trash"].Success) zone.Trash.AddRange(rest);
                    else if (em.Groups["hand"].Success) zone.Hand.AddRange(rest);
                    else if (em.Groups["top"].Success) deck.InsertRange(0, rest);
                    else if (em.Groups["bottom"].Success) deck.AddRange(rest);
                    else if (c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 공개한 카드 {rest.Count}장을 덱 위로 되돌릴까요? (아니오 = 덱 아래)", false)) deck.InsertRange(0, rest);
                    else deck.AddRange(rest);
                    c.Log($"[{c.Card.Name}] 공개한 카드 {rest.Count}장: {(em.Groups["trash"].Success ? "파기" : em.Groups["hand"].Success ? "패에 추가" : "덱으로")}");
                }
            }
            post?.Invoke(c);
            c.Rv45 = outer;
            c.LastStepResolved = true;
        }, Text: s.Replace(" " + RevealMark45 + " ", ". "));
    }

    /// <summary>공개 묶음의 중간 문장 하나</summary>
    private static Action<EffectContext>? RevealMid45(string mid, CardData card, List<EffectTrigger> triggers, bool opp)
    {
        Match m;
        // "For each X among them, ~"
        if ((m = Regex.Match(mid, @"^For each (?<x>.+?) among (?:them|the revealed cards), (?<inner>.+)$", I)).Success
            && RevealPick45(m.Groups["x"].Value) is { } fp)
        {
            var inner = ParseStep(Cap44(m.Groups["inner"].Value) + ".", card, triggers);
            if (inner.Error != null || inner.Step == null) return null;
            var st = inner.Step;
            var match = fp.Match;
            return c =>
            {
                int k = c.Rv45?.Cards.Count(match) ?? 0;
                if (k > 0) Diag45.Hit("revealForEach");
                for (int i = 0; i < Math.Min(k, 20); i++) RunStep(st, c);
            };
        }
        // "For each of your opponent's Digimon, you may play 1 ~ among the revealed cards ~" (BT8-068)
        if ((m = Regex.Match(mid, @"^For each (?<counter>[^,]+), (?<inner>.+\bamong (?:them|the revealed cards)\b.*)$", I)).Success
            && ParseCounter(m.Groups["counter"].Value) is { } counter
            && RevealMid45(m.Groups["inner"].Value, card, triggers, opp) is { } each)
        {
            return c =>
            {
                int k = Math.Min(counter(c.Owner, c.Enemy, c.Self), 20);
                for (int i = 0; i < k; i++) each(c);
            };
        }
        // "Choose 1 X among them, and ~ (the chosen card's play cost | its play cost's total worth)"
        if ((m = Regex.Match(mid, @"^Choose 1 (?<x>.+?) among them, and (?<inner>.+)$", I)).Success
            && RevealPick45(m.Groups["x"].Value) is { } cp)
        {
            string tmpl = m.Groups["inner"].Value;
            string Fill(int v) => Regex.Replace(Regex.Replace(tmpl,
                @"whose play cost is less than or equal to the chosen card's play cost", $"with a play cost of {v} or less", I),
                @"\bup to its play cost's total worth of\b", $"up to {v} play cost's total worth of", I);
            if (Fill(99) == tmpl || RuntimeStep45(Cap44(Fill(5)) + ".", card, triggers) == null) return null;
            var match = cp.Match;
            return c =>
            {
                if (c.Rv45 is not { } rv) return;
                var pool = rv.Cards.Where(match).OrderByDescending(x => x.PlayCost).ToList();
                if (pool.Count == 0) { c.LastStepResolved = false; return; }
                var pick = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, pool, 1, false, $"[{c.Card.Name}] 공개한 카드 중 1장을 고르세요 (등장 코스트를 씁니다).");
                var chosen = pool[pick.Count > 0 ? pick[0] : 0];
                Diag45.Hit("revealChoose");
                if (RuntimeStep45(Cap44(Fill(chosen.PlayCost)) + ".", c.Card, triggers) is { } st) RunStep(st, c);
            };
        }
        // "~ with as high or lower a play cost as 1 Digimon card among them" (BT16-065)
        if ((m = Regex.Match(mid, @"^(?<a>.+?) with as high or lower a play cost as 1 (?<x>.+?) among them$", I)).Success
            && RevealPick45(m.Groups["x"].Value) is { } mp)
        {
            string a = m.Groups["a"].Value;
            if (RuntimeStep45(Cap44(a) + " with a play cost of 5 or less.", card, triggers) == null) return null;
            var match = mp.Match;
            return c =>
            {
                if (c.Rv45 is not { } rv || !rv.Cards.Any(match)) { c.LastStepResolved = false; return; }
                int v = rv.Cards.Where(match).Max(x => x.PlayCost);
                Diag45.Hit("revealMaxCost");
                if (RuntimeStep45(Cap44(a) + $" with a play cost of {v} or less.", c.Card, triggers) is { } st) RunStep(st, c);
            };
        }
        // "If all of them are black, ~" (BT4-096)
        if ((m = Regex.Match(mid, @"^If all of them are (?<c>red|blue|yellow|green|black|purple|white)(?: cards)?, (?<inner>.+)$", I)).Success)
        {
            var color = Enum.Parse<CardColor>(m.Groups["c"].Value, true);
            var inner = ParseStep(Cap44(m.Groups["inner"].Value) + ".", card, triggers);
            if (inner.Error != null || inner.Step == null) return null;
            var st = inner.Step;
            return c =>
            {
                if (c.Rv45 is not { Cards.Count: > 0 } rv || !rv.Cards.All(x => x.AllColors.Contains(color))) { c.LastStepResolved = false; return; }
                Diag45.Hit("revealIfAll");
                RunStep(st, c);
            };
        }
        // "Place 1 of them on top of your security stack face down" (BT6-100)
        if (!opp && Regex.IsMatch(mid, @"^Place 1 of them (?:on top of|at the top of) your security stack(?: face down)?$", I))
            return c =>
            {
                if (c.Rv45 is not { Cards.Count: > 0 } rv) return;
                var ranked = rv.Cards.OrderBy(x => x.Level).ThenBy(x => x.PlayCost).ToList();
                var k = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, ranked, 1, false, $"[{c.Card.Name}] 시큐리티 위에 놓을 카드를 고르세요.");
                var x = ranked[k.Count > 0 ? k[0] : 0];
                rv.Cards.Remove(x);
                c.Owner.SecurityStack.Insert(0, x);
                c.Engine.NotifySecurityAdded(c.Owner, x);
                Diag45.Hit("revealToSecurity");
                c.Log($"[{c.Card.Name}] 공개한 카드 1장을 시큐리티 위에 놓았습니다.");
            };
        // "(you may) play 1 X among them without paying the cost"
        if (!opp && (m = Regex.Match(mid, @"^(?<may>you may )?play 1 (?<x>.+?) among (?:them|the revealed cards) without paying (?:its|the) (?:memory |play )?cost$", I)).Success
            && RevealPick45(m.Groups["x"].Value) is { } pp)
        {
            var match = pp.Match;
            bool mayPlay = m.Groups["may"].Success;
            return c =>
            {
                if (c.Rv45 is not { } rv) return;
                var pool = rv.Cards.Where(x => match(x) && x.Type is CardType.Digimon or CardType.Tamer).OrderByDescending(x => x.Level).ThenByDescending(x => x.DP).ToList();
                if (pool.Count == 0) return;
                var k = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, pool, 1, mayPlay, $"[{c.Card.Name}] 공개한 카드 중 등장시킬 카드를 고르세요.");
                if (k.Count == 0) return;
                var x = pool[k[0]];
                if (c.Owner.BattleArea.Count >= 12) return;
                rv.Cards.Remove(x);
                if (!c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Revealed, null)) { rv.Cards.Add(x); return; }
                rv.Played++;
                Diag45.Hit("revealPlay");
            };
        }
        // "You may play any number of X whose play costs add up to 15 or less among them without paying their memory costs" (BT8-106)
        if (!opp && (m = Regex.Match(mid, @"^(?:you may )?play any number of (?<x>.+?) whose (?:total )?play costs add up to (?<n>\d+) or less among them without paying (?:its|the|their) (?:memory |play )?costs?$", I)).Success
            && RevealPick45(m.Groups["x"].Value) is { } bp)
        {
            var match = bp.Match;
            int budget = int.Parse(m.Groups["n"].Value);
            return c =>
            {
                if (c.Rv45 is not { } rv) return;
                int spent = 0;
                while (c.Owner.BattleArea.Count < 12)
                {
                    var pool = rv.Cards.Where(x => match(x) && x.Type is CardType.Digimon or CardType.Tamer && spent + x.PlayCost <= budget).OrderByDescending(x => x.PlayCost).ToList();
                    if (pool.Count == 0) break;
                    var k = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, pool, 1, true, $"[{c.Card.Name}] 공개한 카드 중 등장시킬 카드를 고르세요 (등장 코스트 합계 {spent}/{budget} — 고르지 않으면 끝).");
                    if (k.Count == 0) break;
                    var x = pool[k[0]];
                    rv.Cards.Remove(x);
                    if (!c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Revealed, null)) { rv.Cards.Add(x); break; }
                    spent += x.PlayCost;
                    rv.Played++;
                    Diag45.Hit("revealPlayBudget");
                }
            };
        }
        // "~ for each Digimon played with this effect" (BT8-106)
        if ((m = Regex.Match(mid, @"^(?<inner>.+) for each Digimon played (?:with|by) this effect$", I)).Success)
        {
            var inner = ParseStep(Cap44(m.Groups["inner"].Value) + ".", card, triggers);
            if (inner.Error != null || inner.Step == null) return null;
            var st = inner.Step;
            return c => { for (int i = 0; i < Math.Min(c.Rv45?.Played ?? 0, 20); i++) RunStep(st, c); };
        }
        // "Add all X among them to your hand" (BT4-107)
        if (!opp && (m = Regex.Match(mid, @"^Add all (?<x>.+?) among them to (?:the|your) hand$", I)).Success
            && RevealPick45(m.Groups["x"].Value) is { } ap)
        {
            var match = ap.Match;
            return c =>
            {
                if (c.Rv45 is not { } rv) return;
                var take = rv.Cards.Where(match).ToList();
                foreach (var x in take) rv.Cards.Remove(x);
                c.Owner.Hand.AddRange(take);
                rv.Added += take.Count;
                if (take.Count > 0) { Diag45.Hit("revealAddAll"); c.Log($"[{c.Card.Name}] [{string.Join(", ", take.Select(x => x.Name))}] 패에 추가"); }
            };
        }
        return null;
    }
}
