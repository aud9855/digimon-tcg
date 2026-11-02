using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.79.0 미지원 문장 53차 묶음 (원래 규칙이 읽지 못한 절만):
///  - EX5-058 "If there are 4 or more total Digimon, play 1 [Fujitsumon] Token suspended to your battle area. (~) If there are 3 or fewer, play it suspended to your opponent's battle area."
///    — 서로의 배틀 에어리어의 디지몬 합계 (처리할 때)로 자신 · 상대의 배틀 에어리어에 레스트 상태로. 상대의 배틀 에어리어에 등장한 토큰은 상대의 디지몬 (공식 Q&amp;A EX5-058 Q1).
///    토큰 효과 "[All Turns] This Digimon doesn't unsuspend" — 이 디지몬은 액티브가 되지 않는다 (효과로도 — BT12-057 "don't unsuspend"와 같은 지속 효과)
///  - BT23-013 "~ play 1 [Atho, René &amp; Por] Token (~) or, from your hand or trash, 1 Digimon card with [Sistermon] in its name without paying the cost. This effect can't play cards with the same names as any of your Digimon."
///    — 토큰 1마리 또는 패 · 트래시의 카드 1장 중 하나. 자신의 디지몬(배틀 에어리어)과 같은 명칭의 것은 고를 수 없다
///  - BT19-091 "Play 1 [WarGrowlmon] Token (~), 1 [Taomon] Token (~), and 1 [Rapidmon] Token (~). This effect can't play tokens with the same names as your Digimon. Then, 1 of your level 5 Digimon gains ＜Alliance＞ twice for the turn and attacks."
///    — 자신의 디지몬과 같은 명칭의 토큰은 빼고 등장. ≪연계≫ 2개 = 어택했을 때 ≪연계≫를 2번 (각각 다른 디지몬 1마리를 레스트 — 종합 룰 15-?? 같은 키워드 효과는 각각 발휘)
///  - BT26-086 "link up to 7 [Appmon] trait cards with different names from this Digimon's digivolution cards to this Digimon" — 명칭이 서로 다른 카드만
/// </summary>
internal static partial class SentenceParser
{
    private const RegexOptions I79 = RegexOptions.IgnoreCase;

    internal const string ByTotalMarker79 = "⟦bytotal79⟧";
    internal const string NoSameNameMarker79 = "⟦nosame79⟧";
    internal const string AllianceTwiceMarker79 = "⟦alliance2x79⟧";
    internal const string OptionToSecurityMarker79 = "⟦optsec79⟧";

    /// <summary>"[All Turns] This Digimon doesn't unsuspend." · "~ can't unsuspend." (EX5-058 토큰)</summary>
    internal static readonly Regex SelfUnsuspendLockRe79 = new(@"^\[All Turns\] This Digimon (?:doesn't|does not|can't|cannot) unsuspend\.?$", I79 | RegexOptions.Compiled);

    /// <summary>원래 규칙이 읽지 못한 절 → 같은 뜻의 다른 표기 (바꾸지 못하면 null)</summary>
    internal static string? Rewrite79(string rawClause)
    {
        string t = rawClause.Trim();
        // EX5-058
        string a = Regex.Replace(t, @"\bIf there are 4 or more total Digimon, play 1 (?<x>\[[^\]]+\]) Token suspended to your battle area\.(?<spec>\s*\(Digimon/(?:[^()]|\([^()]*\))*\))\s*If there are 3 or fewer, play it suspended to your opponent's battle area\.?$",
            m => $"Play 1 {m.Groups["x"].Value} Token {ByTotalMarker79}.{m.Groups["spec"].Value}", I79);
        // BT23-013 · BT19-091 "This effect can't play cards/tokens with the same names as (any of) your Digimon." — 앞 문장 끝에 표식
        a = Regex.Replace(a, @"(?<p>without paying the costs?|\))\.\s*This effect can't play (?:cards|tokens|Digimon) with the same names? as (?:any of )?your Digimon\.",
            m => $"{m.Groups["p"].Value} {NoSameNameMarker79}.", I79);
        // BT19-091 "gains ＜Alliance＞ twice for the turn and attacks"
        a = Regex.Replace(a, @"\bgains ＜Alliance＞ twice for the turn and attacks\b", $"gains ＜Alliance＞ for the turn and attacks. Then, {AllianceTwiceMarker79}", I79);
        // BT10-041 "~ paying its memory cost. Place the Option card used with this effect on top of your security stack face down instead of placing it in your trash."
        a = Regex.Replace(a, @"(?<p>paying its memory cost)\.\s*Place the Option card used with this effect on top of your security stack face down instead of placing it in your trash\.?$",
            m => $"{m.Groups["p"].Value} {OptionToSecurityMarker79}.", I79);
        return a == t ? null : a;
    }

    private static string? TryParse79(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse78(rawClause, inherited, card, def);
        if (err == null) return null;
        // 원래 규칙이 읽지 못한 절만
        if (!inherited && card.Type == CardType.Digimon && SelfUnsuspendLockRe79.IsMatch(rawClause.Trim()))
        {
            if (def != null)
            {
                def.Script41.Statics.Add(new StaticEffect(StaticStat.UnsuspendLock45, EffectSource.Main, _ => 1)   // 1 = 자신의 디지몬 (UnsuspendLock45)
                {
                    SubjectOf = (sc, x) => ReferenceEquals(x, sc.Digimon),
                });
                Diag46.Hit("selfUnsuspendLock79");
            }
            return null;
        }
        if (Rewrite79(rawClause) is not { } alt || TryParse78(alt, inherited, card, null) != null) return err;
        if (def != null)
        {
            TryParse78(alt, inherited, card, def);
            Diag46.Hit("rewrite79");
        }
        return null;
    }

    private static (Action<EffectContext>? Act, string? Error)? ParseAction79(string s, CardData card, List<EffectTrigger> triggers)
    {
        s = Regex.Replace(s.Trim().TrimEnd('.'), @"^Then,?\s+", "", I79);
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        Match m;
        // EX5-058 "Play 1 [Fujitsumon] Token⦃~⦄ ⟦bytotal79⟧" — 서로의 디지몬 합계 4 이상이면 자신의, 3 이하면 상대의 배틀 에어리어에 레스트 상태로
        if ((m = Regex.Match(s, @"^Play 1 \[(?<name>[^\]]+)\] Token⦃(?<spec>[^⦄]*)⦄ " + Regex.Escape(ByTotalMarker79) + "$", I79)).Success)
        {
            string name = m.Groups["name"].Value, spec = m.Groups["spec"].Value;
            if (MakeToken(name, spec) is not { } proto) return (null, $"미지원 토큰 정보: {DecodeTokenSpec(spec)}");
            if (KeywordDictionary.Analyze(proto).FirstOrDefault(x => x.Support != ClauseSupport.Supported) is { } bad) return (null, $"미지원 토큰 효과: {bad.Text}");
            return (c =>
            {
                int total = c.Owner.BattleArea.Count + c.Enemy.BattleArea.Count;
                var side = total >= 4 ? c.Owner : c.Enemy;
                bool ok = side.BattleArea.Count < 12 && c.Engine.TryPlayByEffect(side, MakeToken(name, spec)!, PlayOrigin.Token, null, suspended: true);
                if (!ok) c.LastStepResolved = false;
                Diag46.Hit(total >= 4 ? "tokenByTotalOwn79" : "tokenByTotalEnemy79");
                Emit(c, label, ok ? $"서로의 디지몬 {total}마리 — [{name} Token]을(를) {(total >= 4 ? "자신" : "상대")}의 배틀 에어리어에 레스트 상태로 등장" : "토큰을 등장시킬 수 없습니다");
                c.RecalculateFieldBuffs();
            }, null);
        }
        // BT19-091 "Play 1 [A] Token⦃~⦄, 1 [B] Token⦃~⦄, and 1 [C] Token⦃~⦄ ⟦nosame79⟧" — 자신의 디지몬과 같은 명칭의 토큰은 등장시키지 않는다
        if ((m = Regex.Match(s, @"^Play (?<list>(?:\d+|a|an) \[[^\]]+\] Tokens?⦃[^⦄]*⦄(?:(?:,|,? and) (?:\d+|a|an) \[[^\]]+\] Tokens?⦃[^⦄]*⦄)*) " + Regex.Escape(NoSameNameMarker79) + "$", I79)).Success)
        {
            var tokens = new List<(int N, string Name, string Spec)>();
            foreach (Match t in Regex.Matches(m.Groups["list"].Value, @"(?<n>\d+|a|an) \[(?<name>[^\]]+)\] Tokens?⦃(?<spec>[^⦄]*)⦄", I79))
            {
                if (MakeToken(t.Groups["name"].Value, t.Groups["spec"].Value) is not { } proto) return (null, $"미지원 토큰 정보: {DecodeTokenSpec(t.Groups["spec"].Value)}");
                if (KeywordDictionary.Analyze(proto).FirstOrDefault(x => x.Support != ClauseSupport.Supported) is { } bad) return (null, $"미지원 토큰 효과: {bad.Text}");
                tokens.Add((int.TryParse(t.Groups["n"].Value, out int tn) ? tn : 1, t.Groups["name"].Value, t.Groups["spec"].Value));
            }
            return (c =>
            {
                var done = new List<string>();
                var skipped = new List<string>();
                // 같은 명칭은 효과를 처리하기 시작할 때의 자신의 디지몬으로 본다 (이 효과로 등장한 토큰끼리는 서로 다른 명칭)
                var mine = c.Owner.BattleArea.ToList();
                foreach (var (tn, tname, tspec) in tokens)
                {
                    if (mine.Any(d => SameName79(d, tname))) { skipped.Add(tname); continue; }
                    int played = 0;
                    for (int i = 0; i < tn && c.Owner.BattleArea.Count < 12; i++, played++)
                        if (!c.Engine.TryPlayByEffect(c.Owner, MakeToken(tname, tspec)!, PlayOrigin.Token, null)) break;
                    if (played > 0) done.Add($"[{tname} Token] {played}장");
                }
                if (skipped.Count > 0) Diag46.Hit("tokenSameNameSkipped79");
                Emit(c, label, (done.Count > 0 ? string.Join(", ", done) + " 등장" : "등장시킨 토큰이 없습니다")
                               + (skipped.Count > 0 ? $" (자신의 디지몬과 같은 명칭이라 등장시키지 않음: {string.Join(", ", skipped)})" : ""));
            }, null);
        }
        // BT23-013 "play 1 [X] Token⦃~⦄ or, from your hand or trash, 1 Digimon card with [Sistermon] in its name without paying the cost ⟦nosame79⟧"
        if ((m = Regex.Match(s, @"^(?<may>you may )?play 1 \[(?<name>[^\]]+)\] Token⦃(?<spec>[^⦄]*)⦄ or, from your hand or (?:your )?trash, (?<pick>1 .+?) without paying (?:the|its) costs? " + Regex.Escape(NoSameNameMarker79) + "$", I79)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } pick)
        {
            string name = m.Groups["name"].Value, spec = m.Groups["spec"].Value;
            if (MakeToken(name, spec) is not { } proto) return (null, $"미지원 토큰 정보: {DecodeTokenSpec(spec)}");
            if (KeywordDictionary.Analyze(proto).FirstOrDefault(x => x.Support != ClauseSupport.Supported) is { } bad) return (null, $"미지원 토큰 효과: {bad.Text}");
            bool may = m.Groups["may"].Success;
            var match = pick.Match;   // 장수는 1장
            return (c =>
            {
                bool Same(string n) => c.Owner.BattleArea.Any(d => SameName79(d, n));
                var opts = new List<(string Text, CardData Card, PlayOrigin From)>();
                if (!Same(name) && c.Owner.BattleArea.Count < 12) opts.Add(($"[{name} Token] 토큰", proto, PlayOrigin.Token));
                foreach (var x in c.Owner.Hand.Where(x => x.Type == CardType.Digimon && match(x) && !Same(x.Name) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Hand)).Distinct())
                    opts.Add(($"패의 [{x.Name}] (Lv.{x.Level})", x, PlayOrigin.Hand));
                foreach (var x in c.Owner.Trash.Where(x => x.Type == CardType.Digimon && match(x) && !Same(x.Name) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Trash)).Distinct())
                    opts.Add(($"트래시의 [{x.Name}] (Lv.{x.Level})", x, PlayOrigin.Trash));
                if (opts.Count == 0) { c.LastStepResolved = false; Emit(c, label, "등장시킬 수 있는 토큰 · 카드가 없습니다 (자신의 디지몬과 같은 명칭은 등장시킬 수 없음)"); return; }
                // 추천: 트래시 → 패의 카드 중 Lv. · DP가 높은 것, 없으면 토큰
                int rec = opts.Select((o, i) => (o, i)).OrderByDescending(p => p.o.From == PlayOrigin.Token ? -1 : p.o.Card.Level * 100000 + p.o.Card.DP + (p.o.From == PlayOrigin.Trash ? 1 : 0)).First().i;
                var texts = opts.Select(o => o.Text).ToList();
                if (may) texts.Add("등장시키지 않는다");
                int k = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 토큰 1마리 또는 패 · 트래시의 카드 1장을 코스트 없이 등장시킵니다 (자신의 디지몬과 같은 명칭은 고를 수 없음).", texts, rec);
                if (k < 0 || k >= opts.Count) { c.LastStepResolved = false; Emit(c, label, "등장시키지 않았습니다"); return; }
                var (_, card2, from) = opts[k];
                bool ok = from switch
                {
                    PlayOrigin.Token => c.Engine.TryPlayByEffect(c.Owner, MakeToken(name, spec)!, PlayOrigin.Token, null),
                    PlayOrigin.Hand => c.Engine.TryPlayByEffect(c.Owner, card2, PlayOrigin.Hand, () => c.Owner.Hand.Remove(card2)),
                    _ => c.Engine.TryPlayByEffect(c.Owner, card2, PlayOrigin.Trash, () => c.Owner.Trash.Remove(card2)),
                };
                if (!ok) c.LastStepResolved = false;
                Diag46.Hit("tokenOrCardNoSame79");
                Emit(c, label, ok ? $"{opts[k].Text} 코스트 없이 등장" : "등장시킬 수 없습니다");
            }, null);
        }
        // BT19-091 "~ gains ＜Alliance＞ for the turn and attacks. Then, ⟦alliance2x79⟧" — ≪연계≫를 2개 (어택했을 때 2번). 앞 단계가 ≪연계≫를 주고 어택을 선언한 디지몬에
        if (s.Equals(AllianceTwiceMarker79, StringComparison.Ordinal))
        {
            return (c =>
            {
                // 효과 어택은 선언만 하고 【어택 시】 · ≪연계≫는 효과가 끝난 뒤 (GoldfishEngine.Batch57) — 선언한 디지몬에 ≪연계≫ 1개를 더한다
                if (c.Engine.CurrentAttacker is { } d && c.Owner.BattleArea.Contains(d) && CardScripts.HasKeyword(d, Keyword.Alliance))
                {
                    c.Engine.AddExtraAlliance79(d);
                    Diag46.Hit("allianceTwice79");
                }
            }, null);
        }
        // BT10-041 "use 1 Option card with [Plug-In] in its name or that's yellow and has a memory cost of 5 or less from your hand without meeting its color requirements or paying its memory cost ⟦optsec79⟧"
        //  — 색 조건을 무시하고 코스트 없이 사용, 사용한 옵션은 트래시 대신 자신의 시큐리티 맨 위에 뒷면으로 (시큐리티에 놓을 수 없으면 트래시 — 공식 Q&A BT10-041 Q1)
        if ((m = Regex.Match(s, @"^(?:you may )?use 1 Option card with (?<x>\[[^\]]+\]) in its name or that's (?<col>red|blue|yellow|green|black|purple|white) and has a (?:memory |use )?cost of (?<n>\d+) or less from your hand without (?:meeting its colou?r requirements or )?paying (?:its|the) (?:memory )?cost " + Regex.Escape(OptionToSecurityMarker79) + "$", I79)).Success)
        {
            string nm = m.Groups["x"].Value.Trim('[', ']');
            var col = Enum.Parse<CardColor>(m.Groups["col"].Value, ignoreCase: true);
            int maxCost = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                var pool = EffectRules.OptionUseBlocked(c.State, c.Owner) ? new List<CardData>()
                    : c.Owner.Hand.Where(x => x.Type == CardType.Option && (TargetSpec.NameHas(x, nm) || x.AllColors.Contains(col) && x.PlayCost <= maxCost)).Distinct().OrderByDescending(x => x.PlayCost).ToList();
                var pick = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, pool, 1, upTo: true, $"[{c.Card.Name}] 색 조건을 무시하고 코스트 없이 사용할 옵션 카드를 고르세요 (사용한 뒤 시큐리티 맨 위에 뒷면으로).").FirstOrDefault();
                if (pick == null || !c.Owner.Hand.Remove(pick)) { c.LastStepResolved = false; Emit(c, label, "사용할 수 있는 옵션 카드가 없습니다"); return; }
                c.Engine.UseOptionByEffectToSecurity79(c.Owner, pick);
                Diag46.Hit("optionToSecurity79");
            }, null);
        }
        // BT20-098 "you may play 1 [Ghost] trait Digimon card of each returned card's level from your trash without paying the costs" — 비용으로 되돌린 카드의 Lv.마다 1장씩 (같은 Lv.은 1장)
        if ((m = Regex.Match(s, @"^(?:you may )?play 1 (?<pick>.+?) of each returned card's level from your trash without paying (?:the|its) costs?$", I79)).Success
            && ParseCardPick("1 " + m.Groups["pick"].Value) is { } gp)
        {
            var match = gp.Match;
            return (c =>
            {
                var levels = c.PaidCards64.Where(x => x.Level > 0).Select(x => x.Level).Distinct().OrderBy(x => x).ToList();
                if (levels.Count == 0) { c.LastStepResolved = false; Emit(c, label, "되돌린 카드가 없습니다"); return; }
                var played = new List<string>();
                foreach (int lv in levels)
                {
                    var pool = c.Owner.Trash.Where(x => x.Type == CardType.Digimon && x.Level == lv && match(x) && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Trash))
                        .OrderByDescending(x => x.DP).ThenByDescending(x => x.PlayCost).ToList();
                    if (pool.Count == 0 || c.Owner.BattleArea.Count >= 12) continue;
                    var pick = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Trash, pool, 1, upTo: true, $"[{c.Card.Name}] 트래시에서 코스트 없이 등장시킬 Lv.{lv} 카드를 고르세요 (되돌린 카드의 Lv.마다 1장).").FirstOrDefault();
                    if (pick == null) continue;
                    if (c.Engine.TryPlayByEffect(c.Owner, pick, PlayOrigin.Trash, () => c.Owner.Trash.Remove(pick))) played.Add($"{pick.Name}(Lv.{lv})");
                }
                if (played.Count == 0) c.LastStepResolved = false;
                else Diag46.Hit("playByReturnedLevels79");
                Emit(c, label, played.Count > 0 ? $"되돌린 카드의 Lv. ({string.Join(", ", levels)})마다 [{string.Join(", ", played)}] 등장" : "등장시킬 카드가 없습니다");
            }, null);
        }
        // BT26-086 "link up to 7 [Appmon] trait cards with different names from this Digimon's digivolution cards to this Digimon without paying the costs"
        if ((m = Regex.Match(s, @"^(?:you may )?link up to (?<n>\d+) (?<pick>.+?) cards with different names from this Digimon's digivolution cards to this Digimon without paying (?:the|its) costs?$", I79)).Success
            && card.Type == CardType.Digimon
            && (ParseCardPick($"{m.Groups["n"].Value} {m.Groups["pick"].Value} cards") ?? ParseNamePick($"{m.Groups["n"].Value} {m.Groups["pick"].Value} cards")) is { } lp)
        {
            var (count, match) = lp;
            return (c =>
            {
                var d = c.Self;
                if (d == null || !c.Owner.BattleArea.Contains(d)) { c.LastStepResolved = false; Emit(c, label, "링크할 디지몬이 없습니다"); return; }
                var ranked = d.SourcesWhere(match).Where(x => GoldfishEngine.MeetsLinkRequirement(x, d) != false)
                    .OrderByDescending(x => GoldfishEngine.LinkDp(x)).ThenByDescending(x => x.Level).ToList();
                // 추천 순서: 명칭마다 1장씩 먼저 (AI는 앞에서부터 고른다)
                var firsts = ranked.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                var pool = firsts.Concat(ranked.Where(x => !firsts.Contains(x))).ToList();
                var picks = c.Engine.ChooseCards(c.Owner, CardChoiceZone.DigivolutionCards, pool, count, upTo: true, $"[{c.Card.Name}] [{d.TopCard.Name}]에 링크할 카드를 고르세요 (명칭이 서로 다른 카드 {count}장까지).");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var linked = new List<string>();
                foreach (var x in picks)
                {
                    if (!names.Add(x.Name)) continue;   // 명칭이 서로 다른 카드만
                    if (!c.Owner.BattleArea.Contains(d) || d.TakeSource(y => ReferenceEquals(y, x)) == null) continue;
                    c.Engine.LinkCard(c.Owner, d, x);
                    linked.Add(x.Name);
                }
                if (linked.Count == 0) { c.LastStepResolved = false; Emit(c, label, "링크할 카드가 없습니다"); return; }
                Diag46.Hit("linkDistinctNames79");
                Emit(c, label, $"[{string.Join(", ", linked)}]을(를) [{d.TopCard.Name}]에 링크 (명칭이 서로 다른 카드)");
            }, null);
        }
        return null;
    }

    /// <summary>
    /// 비용 (원래 규칙이 읽지 못한 것만 — 체인 맨 끝):
    ///  - BT20-098 "returning 9 levels' total worth of Digimon cards from your opponent's trash to the bottom of the deck" — Lv. 합계가 정확히 9 (공식 Q&amp;A BT20-098 Q1: 8 이하는 안 된다 —
    ///    "것으로" 비용은 일부만 실행할 수 없다). 되돌린 카드는 PaidCards64 (다음 문장 "each returned card's level")
    /// </summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost79(string cost, CardData card)
    {
        Match m;
        if ((m = Regex.Match(cost.Trim(), @"^returning (?<n>\d+) levels' total worth of Digimon cards from your opponent's trash to the bottom of (?:the|their|its owner's) deck$", I79)).Success)
        {
            int total = int.Parse(m.Groups["n"].Value);
            List<CardData> Able(EffectContext c) => c.Enemy.Trash.Where(x => x.Type == CardType.Digimon && x.Level > 0 && x.Level <= total).ToList();
            return (c => BestLevelSubset79(Able(c), total, _ => 0) != null,
                    c =>
                    {
                        var able = Able(c);
                        // 추천: 자신의 트래시에 같은 Lv.의 카드가 있는 Lv.을 많이 (다음 문장이 그 Lv.의 카드를 등장시킨다)
                        var ownLevels = c.Owner.Trash.Where(x => x.Type == CardType.Digimon).Select(x => x.Level).ToHashSet();
                        var best = BestLevelSubset79(able, total, lv => ownLevels.Contains(lv) ? 10 + lv : 0) ?? new List<CardData>();
                        var chosen = best;
                        if (c.Engine.IsAskingPlayer79(c.Owner))
                        {
                            var ranked = best.Concat(able.Where(x => !best.Contains(x))).ToList();
                            for (int tries = 0; tries < 3; tries++)
                            {
                                var idx = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, ranked, Math.Min(total, ranked.Count), upTo: true,
                                    $"[{c.Card.Name}] 상대의 트래시에서 Lv. 합계가 정확히 {total}{IGa79(total)} 되도록 덱 아래로 되돌릴 디지몬 카드를 고르세요.");
                                var pick = idx.Distinct().Where(i => i >= 0 && i < ranked.Count).Select(i => ranked[i]).ToList();
                                if (pick.Sum(x => x.Level) == total) { chosen = pick; break; }
                                c.Log($"[{c.Card.Name}] Lv. 합계가 {pick.Sum(x => x.Level)}입니다 — 정확히 {total}{(IGa79(total) == "가" ? "여야" : "이어야")} 합니다.");
                            }
                        }
                        foreach (var x in chosen) c.Enemy.Trash.Remove(x);
                        c.Enemy.MainDeck.AddRange(chosen);
                        c.PaidCards64 = chosen.ToList();
                        c.Log($"[{c.Card.Name}] 상대의 트래시의 [{string.Join(", ", chosen.Select(x => $"{x.Name}(Lv.{x.Level})"))}]을(를) 덱 아래로 되돌렸습니다 (Lv. 합계 {chosen.Sum(x => x.Level)})");
                        Diag46.Hit("returnLevelTotal79");
                    },
                    c =>
                    {
                        var ownLevels = c.Owner.Trash.Where(x => x.Type == CardType.Digimon).Select(x => x.Level).ToHashSet();
                        return BestLevelSubset79(Able(c), total, lv => ownLevels.Contains(lv) ? 10 + lv : 0) is { } b && b.Any(x => ownLevels.Contains(x.Level));
                    },
                    $"상대의 트래시에서 디지몬 카드를 Lv. 합계가 {total}{IGa79(total)} 되도록 덱 아래로 되돌려");
        }
        return null;
    }

    /// <summary>d가 명칭 n을 가졌는지 — 토큰은 카드 이름 뒤의 " Token"을 뺀 명칭 (「메가로그라우몬」 토큰의 명칭은 「메가로그라우몬」)</summary>
    private static bool SameName79(CardInstance d, string n) =>
        CardNames.Is(d, n) || GoldfishEngine.IsToken(d.TopCard) && d.TopCard.Name.Equals(n + " Token", StringComparison.OrdinalIgnoreCase);

    /// <summary>숫자 뒤 조사 이/가 (2 · 4 · 5 · 9로 끝나면 가)</summary>
    internal static string IGa79(int n) => (n % 10) is 2 or 4 or 5 or 9 ? "가" : "이";

    /// <summary>Lv. 합계가 정확히 total인 카드 묶음 중 점수(서로 다른 Lv.마다 score)가 가장 높은 것 (같으면 장수가 적은 것). 없으면 null</summary>
    internal static List<CardData>? BestLevelSubset79(List<CardData> cards, int total, Func<int, int> score)
    {
        var byLevel = cards.GroupBy(x => x.Level).Where(g => g.Key > 0).OrderByDescending(g => g.Key).Select(g => (Level: g.Key, Cards: g.ToList())).ToList();
        List<CardData>? best = null; int bestScore = int.MinValue;
        var cur = new List<CardData>();
        void Go(int i, int left)
        {
            if (left == 0)
            {
                int sc = cur.Select(x => x.Level).Distinct().Sum(score) * 100 - cur.Count;
                if (sc > bestScore) { bestScore = sc; best = cur.ToList(); }
                return;
            }
            if (i >= byLevel.Count) return;
            var (lv, xs) = byLevel[i];
            for (int k = Math.Min(xs.Count, left / lv); k >= 0; k--)
            {
                for (int j = 0; j < k; j++) cur.Add(xs[j]);
                Go(i + 1, left - k * lv);
                cur.RemoveRange(cur.Count - k, k);
            }
        }
        Go(0, total);
        return best;
    }
}
