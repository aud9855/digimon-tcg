using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.67.0 미지원 문장 41차 묶음 (파서 쪽) — 원래 규칙(40차까지)이 읽지 못한 절만:
///  - BT8-040 패 1장을 파기하여 이 턴 동안 이 디지몬의 색은 파기한 카드의 색으로도 취급 (⟦handcolors67⟧ — CardInstance.TimedColors67)
///  - BT11-043 · BT14-097 · EX13-031 상대의 디지몬 1마리를 원래 명칭 「스카몬」 · 화이트 · DP 3000으로 변경 (⟦chg67:K⟧ — 종합 룰 15-12-2, CardInstance.Changed67 + BaseColor66)
///  - BT12-086 덱 위 공개 — 색이 서로 다른 카드 N장까지를 패에 (⟦revdiffcol67:K⟧ — 서로 같은 색을 갖지 않는다: 23차 EX7-037과 같은 판단)
///  - EX4-072 자신의 Lv.6 디지몬 1마리를 고르고, 그 명칭을 포함하는 명칭이 다른 패의 Lv.6 디지몬 카드로 진화 조건을 무시하고 코스트 없이 (⟦namedevo67:K⟧ — 공식 Q&amp;A EX4-072 Q1)
///  - BT5-089 어택한 디지몬이 공개한 카드로 코스트 없이 진화 (⟦revevothat67:K⟧)
///  - P-070 [시큐리티] 배틀 종료 시 덱 위 1장 공개 — 조건에 맞으면 등장시킬 수 있다, 아니면 패에 (⟦revtop67:K⟧ — 공식 Q&amp;A P-070 Q1)
///  - ST9-08 진화원 효과의 조그레스 진화 — 앞의 규칙이 읽는 표기로
/// </summary>
internal static partial class SentenceParser
{
    /// <summary>바꿔 쓴 절을 다시 읽는 중 (같은 규칙으로 또 바꿔 쓰지 않는다)</summary>
    [ThreadStatic] private static int _depth67;

    private static string? TryParse67(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string? err = TryParse66(rawClause, inherited, card, def);
        if (err == null || _depth67 > 0) return err;
        string t = Normalize58(rawClause);
        string alt = ClauseRewrite67(t);
        if (alt == t) return err;
        _depth67++;
        try
        {
            if (TryParse66(alt, inherited, card, null) != null) return err;
            if (def != null) TryParse66(alt, inherited, card, def);
            return null;
        }
        finally { _depth67--; }
    }

    // ---- 표시 (값 묶음은 번호로) ----

    private static readonly ConcurrentDictionary<string, int> SpecIds67 = new();
    private static readonly ConcurrentDictionary<int, string[]> Specs67 = new();

    internal static string[]? Spec67(int id) => Specs67.TryGetValue(id, out var s) ? s : null;

    private static string Mark67(string kind, params string[] values)
    {
        string key = kind + "\u0001" + string.Join("\u0001", values);
        int id = SpecIds67.GetOrAdd(key, _ => { int k; lock (Specs67) { k = Specs67.Count + 1; Specs67[k] = values; } return k; });
        return $"⟦{kind}67:{id}⟧";
    }

    private const string Col67 = "red|blue|yellow|green|black|purple|white";

    // BT11-043 "change 1 of your opponent's Digimon into a white Digimon with 3000 DP and an original name of [Sukamon] until the end of your opponent's turn."
    internal static readonly Regex ChangeIntoRe67 = new($@"\bchange 1 of (?:your opponent's|their) Digimon into an? (?<col>{Col67}) Digimon with (?<dp>\d+) DP and an original name of \[(?<n>[^\]]+)\] until the end of (?:your opponent's|their) turn\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT14-097 "Until the end of your turn, change 1 of your opponent's Digimon into being white and having 3000 DP and an original name of [Sukamon]"
    internal static readonly Regex ChangeBeingRe67 = new($@"\bUntil the end of your turn, change 1 of (?:your opponent's|their) Digimon into being (?<col>{Col67}) and having (?<dp>\d+) DP and an original name of \[(?<n>[^\]]+)\]\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // EX13-031 "you may change the base name, color and DP of 1 of your opponent's Digimon to [Sukamon], white and 3000 until their turn ends."
    internal static readonly Regex ChangeBaseRe67 = new($@"\byou may change the base name, colou?r and DP of 1 of (?:your opponent's|their) Digimon to \[(?<n>[^\]]+)\], (?<col>{Col67}) and (?<dp>\d+) until (?:their|your opponent's) turn ends\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT12-086 "Reveal the top 3 cards of your deck. Add up to 2 Digimon cards with different colors and ＜Save＞ in their texts among them to your hand. Place the rest at the bottom of your deck in any order."
    internal static readonly Regex RevealDiffColorsRe67 = new(@"\bReveal the top (?<n>\d+) cards of your deck\. Add up to (?<k>\d+) Digimon cards with different colou?rs and (?<x>＜[^＞]+＞|\[[^\]]+\]) in their texts among them to (?:the|your) hand\. (?:Place the rest at the bottom of your deck in any order|Return the rest to the bottom of the deck)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // EX4-072 "Choose 1 of your level 6 Digimon. It may digivolve into a level 6 Digimon card in your hand with a different name that includes the name of the chosen Digimon, ignoring digivolution requirements and without paying the cost."
    internal static readonly Regex NamedEvoRe67 = new(@"\bChoose 1 of your level (?<lv>\d+) Digimon\. It may digivolve into a level (?<lv2>\d+) Digimon card in your hand with a different name that includes the name of the chosen Digimon, ignoring (?:its )?digivolution requirements and without paying the cost\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // BT5-089 "[Your Turn] When you attack with a level 5 green Digimon, you may suspend this Tamer to reveal 3 cards from the top of your deck. You may digivolve 1 green level 6 Digimon card among them onto the attacking Digimon without paying its memory cost. Place the remaining cards at the bottom of your deck in any order."
    internal static readonly Regex AttackRevealEvoRe67 = new($@"^(?<lab>\[Your Turn\]\s*)When you attack with a level (?<lv>\d+) (?<col>{Col67}) Digimon, you may suspend this Tamer to reveal (?<n>\d+) cards from the top of your deck\. You may digivolve (?<p>1 [^.]+?) among them onto the attacking Digimon without paying its memory cost\. Place the remaining cards at the bottom of your deck in any order\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // P-070 "[Security] At the end of the battle, reveal the top card of your deck. If that card is a black Digimon card with a play cost of 4 or less, you may play it without paying its memory cost. Add any remaining cards to your hand. Then, add this card to its owner's hand."
    internal static readonly Regex RevealTopPlayRe67 = new($@"\bAt the end of the battle, reveal the top card of your deck\. If that card is an? (?<col>{Col67}) Digimon card with a play cost of (?<n>\d+) or less, you may play it without paying its memory cost\. Add any remaining cards to your hand\. Then, add this card to its owner's hand\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>같은 뜻 다른 표기 · 이 묶음의 동작 표시 — 절 전체에 차례로 (렌더러도 쓴다)</summary>
    internal static readonly Func<string, string>[] ClauseRules67 =
    {
        // BT8-040 "You may trash 1 card in your hand to treat this Digimon as also having the colors of the trashed card for the turn."
        s => Regex.Replace(s, @"\bYou may trash 1 card in your hand to treat this Digimon as also having the colou?rs of the trashed card for the turn\.", "⟦handcolors67⟧.", RegexOptions.IgnoreCase),
        s => ChangeIntoRe67.Replace(s, m => Mark67("chg", m.Groups["n"].Value, m.Groups["col"].Value, m.Groups["dp"].Value, "opp", "") + "."),
        s => ChangeBeingRe67.Replace(s, m => Mark67("chg", m.Groups["n"].Value, m.Groups["col"].Value, m.Groups["dp"].Value, "own", "") + "."),
        s => ChangeBaseRe67.Replace(s, m => Mark67("chg", m.Groups["n"].Value, m.Groups["col"].Value, m.Groups["dp"].Value, "opp", "may") + "."),
        s => RevealDiffColorsRe67.Replace(s, m => Mark67("revdiffcol", m.Groups["n"].Value, m.Groups["k"].Value, "1 Digimon card with " + m.Groups["x"].Value + " in its text") + "."),
        s => NamedEvoRe67.Replace(s, m => Mark67("namedevo", m.Groups["lv"].Value, m.Groups["lv2"].Value) + "."),
        s => AttackRevealEvoRe67.Replace(s, m => $"{m.Groups["lab"].Value}When one of your level {m.Groups["lv"].Value} {m.Groups["col"].Value} Digimon attacks, by suspending this Tamer, " + Mark67("revevothat", m.Groups["n"].Value, m.Groups["p"].Value) + "."),
        s => RevealTopPlayRe67.Replace(s, m => "At the end of the battle, " + Mark67("revtop", m.Groups["col"].Value, m.Groups["n"].Value) + ". Then, add this card to the hand."),
        // ST9-08 "You may DNA digivolve this Digimon and one of your other Digimon in play into a Digimon card in your hand by paying its DNA digivolve cost." — 코스트를 지불하는 보통 조그레스 진화
        s => Regex.Replace(s, @"\bYou may DNA digivolve this Digimon and one of your other Digimon in play into a Digimon card in your hand by paying its DNA digivolve cost\.?$",
            "This Digimon and any of your other Digimon may DNA digivolve into a Digimon card in your hand.", RegexOptions.IgnoreCase),
        // BT8-043 "When you would play this card from your hand, you may delete 1 of your purple [Cherubimon] to reduce this card's play cost by 8." — 등장 코스트 감소 비용 (공식 한글 카드 문장: 패의 이 카드를 등장시킬 때)
        s => Regex.Replace(s, $@"^When you would play this card from your hand, you may delete 1 of your (?<c>{Col67}) \[(?<n>[^\]]+)\] to reduce this card's play cost by (?<r>\d+)\.?$",
            "When this card would be played from your hand, by deleting 1 of your ${c} [${n}] Digimon, reduce the play cost by ${r}.", RegexOptions.IgnoreCase),
        // BT7-039 "Then, ＜Draw 1＞ (Draw 1 card from your deck.) for each Digimon card you placed." — 놓은 카드는 모두 디지몬 카드
        s => Regex.Replace(s, @"(?<d>＜Draw 1＞(?: \([^)]*\))?) for each Digimon card you placed\.?$", "${d} for each card placed.", RegexOptions.IgnoreCase),
        // BT9-079 "You may digivolve 1 of your other Digimon in play into a Digimon card with the [Undead] or [Dark Animal] trait from your trash without paying its memory cost." (공식 Q&A BT9-079 Q1: 진화 조건은 무시하지 않는다)
        s => Regex.Replace(s, @"\bYou may digivolve 1 of your other Digimon in play into (?<p>an? [^.]+?) from your trash without paying its memory cost\.?$", "1 of your other Digimon may digivolve into ${p} from your trash without paying the cost.", RegexOptions.IgnoreCase),
        // LM-068 "~ from your trash to the field without paying the cost."
        s => Regex.Replace(s, @"\bfrom your trash to the field without paying the cost\b", "from your trash without paying the cost", RegexOptions.IgnoreCase),
        // BT20-096 [트래시] "by paying 6 cost, return this card to the bottom of the deck and delete 1 of your opponent's unsuspended Digimon." — 코스트를 지불했을 때만 둘 다
        s => Regex.Replace(s, @"(?<pre>\bby paying \d+ cost), return this card to the bottom of the deck and (?<rest>delete [^.]+)\.?$", "${pre}, ⟦selfbottom67⟧. If you do, ${rest}.", RegexOptions.IgnoreCase),
    };

    /// <summary>기록에 보이는 문장의 동작 표시를 읽을 수 있는 문장으로 (P-070 "배틀이 끝나면 발휘합니다: ~")</summary>
    internal static string UnMark67(string text) =>
        text.IndexOf('⟦') < 0 ? text
        : Regex.Replace(Regex.Replace(text, @"⟦revtop67:\d+⟧", "reveal the top card of your deck (play it if it matches, otherwise add it to your hand)"), @"⟦[^⟧]*⟧", "(effect)");

    internal static string ClauseRewrite67(string rawClause)
    {
        string s = rawClause;
        foreach (var f in ClauseRules67) s = f(s);
        return s;
    }

    /// <summary>이 묶음의 동작 (원래 규칙이 읽지 못한 문장만 — ParseStep에서 앞의 묶음 다음)</summary>
    private static (Action<EffectContext>? Act, string? Error)? ParseAction67(string s, CardData card, List<EffectTrigger> triggers)
    {
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        s = Regex.Replace(s.Trim(), @"^Then,?\s+", "", RegexOptions.IgnoreCase);
        if (s == "⟦selfbottom67⟧")
            return (c => SelfToBottom67(c, label), null);
        if (s == "⟦handcolors67⟧")
            return card.Type == CardType.Digimon ? (c => HandColors67(c, label), null) : (null, "미지원: 디지몬이 아닌 카드의 색");
        var m = Regex.Match(s, @"^⟦(?<k>[a-z]+)67:(?<id>\d+)⟧$");
        if (!m.Success || Spec67(int.Parse(m.Groups["id"].Value)) is not { } v) return null;
        switch (m.Groups["k"].Value)
        {
            case "chg":
            {
                var col = Enum.Parse<CardColor>(v[1], ignoreCase: true);
                int dp = int.Parse(v[2]);
                var dur = v[3] == "own" ? Duration.YourTurnEnd : Duration.OpponentsTurnEnd;
                bool may = v[4] == "may";
                string name = v[0];
                return (c => Change67(c, label, name, col, dp, dur, may), null);
            }
            case "revdiffcol":
            {
                var pick = ParseCardPick(v[2]) ?? ParseNamePick(v[2]);
                if (pick == null) return (null, $"미지원 공개 조건 ({v[2]})");
                int n = int.Parse(v[0]), k = int.Parse(v[1]);
                var match = RevealAlias66.Wrap(pick.Value.Match);
                return (c => RevealDiffColors67(c, label, n, k, match), null);
            }
            case "namedevo":
            {
                int lv = int.Parse(v[0]), lv2 = int.Parse(v[1]);
                return (c => NamedEvo67(c, label, lv, lv2), null);
            }
            case "revevothat":
            {
                var pick = ParseCardPick(v[1]) ?? ParseNamePick(v[1]);
                if (pick == null) return (null, $"미지원 공개 조건 ({v[1]})");
                int n = int.Parse(v[0]);
                var match = RevealAlias66.Wrap(pick.Value.Match);
                return (c => RevealEvoThat67(c, label, n, match), null);
            }
            case "revtop":
            {
                var col = Enum.Parse<CardColor>(v[0], ignoreCase: true);
                int max = int.Parse(v[1]);
                return (c => RevealTopPlay67(c, label, col, max), null);
            }
        }
        return null;
    }

    // ---- BT8-040 ----

    /// <summary>
    /// 패 1장을 파기할 수 있다 — 파기했으면 이 턴 동안 이 디지몬의 색은 파기한 카드의 색으로도 취급한다 (공식 Q&amp;A BT8-040 Q1: 옐로 · 레드 2색 디지몬으로 취급).
    /// 추천: 이 디지몬에 없는 색의 카드가 있으면 파기 (Lv. · 코스트가 낮은 것부터), 없으면 파기하지 않는다
    /// </summary>
    private static void HandColors67(EffectContext c, string label)
    {
        var self = c.Self;
        if (self == null || !c.Owner.BattleArea.Contains(self)) { c.LastStepResolved = false; return; }
        if (c.Owner.Hand.Count == 0) { c.LastStepResolved = false; Emit(c, label, "파기할 패가 없습니다"); return; }
        var have = self.Colors;
        var ranked = c.Owner.Hand.Distinct().OrderBy(x => x.AllColors.Any(col => !have.Contains(col)) ? 0 : 1).ThenBy(x => x.Level).ThenBy(x => x.PlayCost).ToList();
        bool rec = ranked[0].AllColors.Any(col => !have.Contains(col));
        if (!c.Engine.AskYesNo(c.Owner, self, $"[{c.Card.Name}] 패 1장을 파기하여 이 턴 동안 이 디지몬을 그 카드의 색으로도 취급할까요?", rec)) { c.LastStepResolved = false; return; }
        var x = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, ranked, 1, upTo: false, $"[{c.Card.Name}] 파기할 패 1장을 고르세요 (그 카드의 색으로도 취급).").FirstOrDefault() ?? ranked[0];
        if (!c.Owner.Hand.Remove(x)) { c.LastStepResolved = false; return; }
        c.Owner.Trash.Add(x);
        c.Engine.NotifyHandTrashed(c.Owner, new[] { x });
        int until = Until(c, Duration.ThisTurn);
        foreach (var col in x.AllColors)
            if (!self.TimedColors67.Any(t => t.Color == col && t.Until >= until)) self.TimedColors67.Add((col, until));
        c.LastCard = x;
        c.RecalculateFieldBuffs();
        Diag46.Hit("handColors67");
        Emit(c, label, $"패의 [{x.Name}] 파기 → 이 턴 동안 [{self.TopCard.Name}]은(는) {string.Join("/", x.AllColors.Select(ColorKo66))}로도 취급 (지금 {string.Join("/", self.Colors.Select(ColorKo66))})");
    }

    // ---- BT11-043 · BT14-097 · EX13-031 ----

    /// <summary>
    /// 상대의 디지몬 1마리의 원래 명칭 · 색 · DP를 바꾼다 (기간 동안). 종합 룰 15-12-2: 정보를 바꾸는 효과 — 특징 · Lv. · 효과는 그대로, 늦게 바뀐 정보가 앞의 것을 덮어쓴다.
    /// DP ± 효과는 바뀐 DP에 더한다 (2-5-2). 추천: DP가 가장 높은 상대의 디지몬
    /// </summary>
    private static void Change67(EffectContext c, string label, string name, CardColor col, int dp, Duration dur, bool may)
    {
        var pool = c.Enemy.BattleArea.Where(d => d.TamerProxyOf == null && !c.Engine.IsUnaffectedBy(c.Owner, d)).OrderByDescending(d => d.GetCurrentDP()).ToList();
        if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, "대상이 없습니다"); return; }
        if (may && !c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 상대의 디지몬 1마리를 원래 명칭 [{name}] · {ColorKo66(col)} · DP {dp}로 바꿀까요?", true)) { c.LastStepResolved = false; return; }
        var d = pool.Count == 1 ? pool[0] : c.Engine.ChooseTargets(c.Owner, pool, 1, $"[{c.Card.Name}] 원래 명칭 [{name}] · {ColorKo66(col)} · DP {dp}로 바꿀 상대의 디지몬을 고르세요.", optional: false, preselected: pool[0]).FirstOrDefault() ?? pool[0];
        int until = Until(c, dur);
        string before = d.TopCard.Name;
        d.Changed67 = (name, dp, until);
        d.BaseColor66 = (col, until);
        d.ChangeSrc68 = d.ColorSrc68 = c.Engine.ChangeSource68(d);   // v0.68.0 받지 않는 동안에는 원래대로 (15-15-5-2)
        c.LastTargets = new List<CardInstance> { d };
        c.RecalculateFieldBuffs();
        Diag46.Hit("changeName67");
        Emit(c, label, $"상대의 [{before}] → 원래 명칭 [{name}] · {ColorKo66(col)} · DP {dp} ({(dur == Duration.YourTurnEnd ? "자신의 턴 종료까지" : "상대의 턴 종료까지")})");
    }

    // ---- BT12-086 ----

    /// <summary>덱 위 n장 공개 — 조건에 맞고 서로 같은 색을 갖지 않는 카드 k장까지를 패에, 나머지는 덱 아래로. 추천: Lv.가 높은 카드부터</summary>
    private static void RevealDiffColors67(EffectContext c, string label, int n, int k, Func<CardData, bool> match)
    {
        var deck = c.Owner.MainDeck;
        var revealed = deck.Take(n).ToList();
        deck.RemoveRange(0, revealed.Count);
        Diag46.Hit("revealDiffColors67");
        var taken = new List<CardData>();
        while (taken.Count < k)
        {
            var pool = revealed.Where(x => !taken.Contains(x) && match(x) && !x.AllColors.Any(col => taken.Any(t => t.AllColors.Contains(col))))
                .Distinct().OrderByDescending(x => x.Level).ThenByDescending(x => x.DP).ToList();
            if (pool.Count == 0) break;
            var x = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, pool, 1, upTo: true, $"[{c.Card.Name}] 공개한 카드 중 패에 추가할 카드를 고르세요 ({taken.Count + 1}/{k} — 이미 고른 카드와 색이 다른 카드).").FirstOrDefault();
            if (x == null) break;
            taken.Add(x);
            revealed.Remove(x);
        }
        deck.AddRange(revealed);
        c.Owner.Hand.AddRange(taken);
        if (taken.Count == 0) c.LastStepResolved = false;
        Emit(c, label, $"덱 위 {n}장 공개 → {(taken.Count == 0 ? "패에 추가할 카드가 없습니다" : string.Join(", ", taken.Select(x => $"[{x.Name}]")) + " 패에 추가")}{(revealed.Count > 0 ? $" · 나머지 {revealed.Count}장 덱 아래로" : "")}");
    }

    // ---- EX4-072 ----

    /// <summary>진화할 수 있는 패의 카드: Lv.lv2 디지몬 카드 · 명칭이 고른 디지몬의 명칭을 포함하고 다른 명칭 (공식 Q&amp;A EX4-072 Q1: 「워그레이몬」 → 「블랙워그레이몬」 등)</summary>
    private static List<CardData> NamedEvoPool67(EffectContext c, CardInstance d, int lv2)
    {
        if (EffectRules.IsDigivolveLocked(c.State, d)) return new List<CardData>();
        string nm = CardNames.MatchName(d);
        return c.Owner.Hand.Distinct().Where(x => x.Type == CardType.Digimon && x.Level == lv2 && CardNames.Contains(x, nm)
            && !string.Equals(CardNames.MatchName(x), nm, StringComparison.OrdinalIgnoreCase) && CardScripts.EvoAllowed(d, x)).OrderByDescending(x => x.DP).ToList();
    }

    private static void NamedEvo67(EffectContext c, string label, int lv, int lv2)
    {
        var mine = c.Owner.BattleArea.Where(d => d.TamerProxyOf == null && d.TopCard.Level == lv).ToList();
        if (mine.Count == 0) { c.LastStepResolved = false; Emit(c, label, $"고를 Lv.{lv} 디지몬이 없습니다"); return; }
        var ranked = mine.OrderByDescending(d => NamedEvoPool67(c, d, lv2).Count > 0 ? 1 : 0).ThenByDescending(d => d.EvolutionSources.Count).ToList();
        var chosen = ranked.Count == 1 ? ranked[0] : c.Engine.ChooseTargets(c.Owner, ranked, 1, $"[{c.Card.Name}] 자신의 Lv.{lv} 디지몬 1마리를 고르세요.", optional: false, preselected: ranked[0]).FirstOrDefault() ?? ranked[0];
        c.LastTargets = new List<CardInstance> { chosen };
        var pool = NamedEvoPool67(c, chosen, lv2);
        Diag46.Hit("namedEvo67");
        if (pool.Count == 0) { c.LastStepResolved = false; Emit(c, label, $"[{chosen.TopCard.Name}] 선택 → 진화할 수 있는 카드가 없습니다"); return; }
        var x = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, pool, 1, upTo: true, $"[{c.Card.Name}] [{chosen.TopCard.Name}]을(를) 진화 조건을 무시하고 코스트 없이 진화시킬 카드를 고르세요 (명칭에 「{CardNames.MatchName(chosen)}」 포함 · 명칭이 다른 Lv.{lv2}).").FirstOrDefault();
        if (x == null) { c.LastStepResolved = false; return; }
        string before = chosen.TopCard.Name;
        c.Engine.EvolveByEffect(c.Owner, chosen, x, 0);
        bool done = ReferenceEquals(chosen.TopCard, x);
        if (!done) c.LastStepResolved = false;
        Emit(c, label, done ? $"[{before}] → [{x.Name}] 진화 조건을 무시하고 코스트 없이 진화" : $"[{before}]은(는) [{x.Name}]으로 진화할 수 없습니다");
    }

    // ---- BT5-089 ----

    /// <summary>어택한 디지몬 (사건의 디지몬) — 덱 위 n장 공개, 그중 조건에 맞는 카드 1장으로 그 디지몬이 코스트 없이 진화할 수 있다 (보통 진화 조건). 나머지는 덱 아래로</summary>
    private static void RevealEvoThat67(EffectContext c, string label, int n, Func<CardData, bool> match)
    {
        var d = c.Other ?? c.Engine.CurrentAttacker;
        var deck = c.Owner.MainDeck;
        var revealed = deck.Take(n).ToList();
        deck.RemoveRange(0, revealed.Count);
        Diag46.Hit("revealEvoThat67");
        bool live = d != null && c.Owner.BattleArea.Contains(d) && !EffectRules.IsDigivolveLocked(c.State, d);
        var pool = live ? revealed.Where(x => x.Type == CardType.Digimon && match(x) && ActionValidator.ValidateEvolution(d!, x).CanExecute).Distinct().OrderByDescending(x => x.Level).ThenByDescending(x => x.DP).ToList() : new List<CardData>();
        var taken = pool.Count == 0 ? null : c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, pool, 1, upTo: true, $"[{c.Card.Name}] 공개한 카드 중 [{d!.TopCard.Name}]이(가) 코스트 없이 진화할 카드를 고르세요.").FirstOrDefault();
        if (taken != null) revealed.Remove(taken);
        deck.AddRange(revealed);   // 나머지를 먼저 덱 아래로 (공개 효과의 처리 순서 — SentenceParser.ParseReveal과 같다)
        string rest = revealed.Count > 0 ? $" · 나머지 {revealed.Count}장 덱 아래로" : "";
        if (taken == null) { c.LastStepResolved = false; Emit(c, label, $"덱 위 {n}장 공개 → 진화할 카드가 없습니다{rest}"); return; }
        string before = d!.TopCard.Name;
        var from = new List<CardData> { taken };
        c.Engine.EvolveByEffect(c.Owner, d, taken, 0, from);
        if (from.Count > 0) { deck.Add(taken); c.LastStepResolved = false; Emit(c, label, $"덱 위 {n}장 공개 → [{taken.Name}]으로 진화할 수 없음{rest}"); return; }
        Emit(c, label, $"덱 위 {n}장 공개 → [{before}] → [{taken.Name}] 코스트 없이 진화{rest}");
    }

    // ---- BT20-096 ----

    /// <summary>[트래시] 효과: 트래시의 이 카드를 덱 아래로 되돌린다 (트래시에 없으면 처리하지 않는다)</summary>
    private static void SelfToBottom67(EffectContext c, string label)
    {
        if (!c.Owner.Trash.Remove(c.Card)) { c.LastStepResolved = false; Emit(c, label, "이 카드가 트래시에 없습니다"); return; }
        c.Owner.MainDeck.Add(c.Card);
        Diag46.Hit("selfToBottom67");
        Emit(c, label, $"트래시의 [{c.Card.Name}]을(를) 덱 아래로");
    }

    // ---- P-070 ----

    /// <summary>덱 위 1장 공개 — 그 색의 등장 코스트 max 이하 디지몬 카드라면 코스트 없이 등장시킬 수 있다. 등장시키지 않은 카드는 패에 (공식 Q&amp;A P-070 Q1)</summary>
    private static void RevealTopPlay67(EffectContext c, string label, CardColor col, int max)
    {
        var deck = c.Owner.MainDeck;
        if (deck.Count == 0) { c.LastStepResolved = false; Emit(c, label, "덱이 없습니다"); return; }
        var x = deck[0];
        deck.RemoveAt(0);
        Diag46.Hit("revealTopPlay67");
        bool ok = x.Type == CardType.Digimon && x.AllColors.Contains(col) && x.PlayCost <= max && c.Owner.BattleArea.Count < 12 && c.Engine.CanPlayByEffect(c.Owner, x, PlayOrigin.Revealed);
        if (ok && c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 공개한 [{x.Name}]을(를) 코스트 없이 등장시킬까요? (아니면 패에)", true)
            && c.Engine.TryPlayByEffect(c.Owner, x, PlayOrigin.Revealed, null))
        {
            c.LastCard = x;
            Emit(c, label, $"덱 위 [{x.Name}] 공개 → 코스트 없이 등장");
            return;
        }
        c.Owner.Hand.Add(x);
        c.LastCard = x;
        Emit(c, label, $"덱 위 [{x.Name}] 공개 → 패에 추가");
    }
}
