using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.42.0 "대신" — 앞 문장의 효과를 바꾸는 문장:
///  - "A. If C, B instead." (BT14-089 — 공식 "DP 6000 이하의 상대의 디지몬 1마리를 소멸시킨다. 명칭에 「그레이몬」을 포함하는 자신의 디지몬이 있다면, 대신 가장 DP가 낮은 상대의 디지몬 1마리를 소멸시킨다") —
///    A를 처리하기 전에 C를 보고, C이면 A 대신 B (P-088 · BT7-109)
///  - "A. By X, B instead." · "A. By X, instead B." (EX3-072 · BT19-092 · BT22-034 — 공식 "자신의 디지몬 1마리를 소멸시키는 것으로, 대신 ~") — X를 내면 A 대신 B (내지 않으면 A)
///  - "A. If no Digimon was deleted by this effect, B instead." (BT13-111 — 공식 "이 효과로 소멸하지 않았다면, ~") — A 뒤에 조건을 본다 (이 효과의 결과)
/// 두 문장을 한 문장(표시 ⟦instead⟧)으로 합쳐 읽는다 (MergeSentences42 → InsteadStep42). 렌더러도 같은 합친 문장을 옮긴다
/// </summary>
internal static partial class SentenceParser
{
    internal const string InsteadMark42 = " ⟦instead⟧ ";

    internal static readonly Regex InsteadIfRe42 = new(@"^If (?<c>[^,]+), (?<b>.+?),? instead\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex InsteadByRe42 = new(@"^By (?<cost>[^,]+), (?:(?<pre>instead,? )(?<b>.+?)|(?<b>.+?),? instead)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"A. If C, B instead." · "A. By X, B instead." → 한 문장 (A ⟦instead⟧ 뒤 문장)</summary>
    private static List<string> MergeSentences42(List<string> sentences)
    {
        if (!sentences.Any(s => Regex.IsMatch(s, @"\binstead\b|\breduce\b", I))) return sentences;
        var result = new List<string>();
        for (int i = 0; i < sentences.Count; i++)
        {
            // "A. For every ~, reduce this effect's digivolution cost by 1." — 앞 문장과 한 문장으로 (ReduceStep42 — SentenceParser.Batch42Reduce.cs)
            if (i + 1 < sentences.Count && IsReduceSentence42(sentences[i + 1]) && !sentences[i].Contains(InsteadMark42) && ReducedSentence42(sentences[i], 1) != null)
            {
                result.Add(sentences[i].Trim().TrimEnd('.') + ReduceMark42 + sentences[i + 1].Trim());
                i++;
                continue;
            }
            // "If C, B instead." — 원래 규칙(앞 문장 대신)이 읽는다. B만 앞 문장에 맞춘다 ("2 such Digimon" · "1 opposing Digimon" · 빠진 "from your trash")
            if (i > 0 && InsteadIfRe42.Match(sentences[i].Trim()) is { Success: true } im && !result[^1].Contains(InsteadMark42))
            {
                string b = InsteadB42(result[^1].Trim().TrimEnd('.'), im.Groups["b"].Value);
                string orig = im.Groups["b"].Value.Trim().TrimEnd('.');
                if (!string.Equals(b, char.ToUpperInvariant(orig[0]) + orig[1..], StringComparison.Ordinal))
                {
                    result.Add($"If {im.Groups["c"].Value}, {char.ToLowerInvariant(b[0])}{b[1..]} instead.");
                    continue;
                }
            }
            // "By X, B instead." — 원래 규칙이 읽지 못한다: 앞 문장과 한 문장으로 (InsteadStep42)
            if (i + 1 < sentences.Count && !sentences[i].Contains(InsteadMark42)
                && InsteadByRe42.IsMatch(sentences[i + 1].Trim())
                && !Regex.IsMatch(sentences[i], @"\binstead\b", I))
            {
                result.Add(sentences[i].Trim().TrimEnd('.') + InsteadMark42 + sentences[i + 1].Trim());
                i++;
                continue;
            }
            result.Add(sentences[i]);
        }
        return result;
    }

    /// <summary>뒤 문장의 B를 앞 문장 A에 맞춘다 ("Delete 2 such Digimon" — P-088 · "1 opposing Digimon" — BT14-089 · 빠진 "from your trash" — BT7-109)</summary>
    internal static string InsteadB42(string a, string b)
    {
        b = b.Trim().TrimEnd('.');
        if (b.Length > 0) b = char.ToUpperInvariant(b[0]) + b[1..];
        b = Regex.Replace(b, @"\b(?<n>\d+) opposing Digimon\b", "${n} of your opponent's Digimon", I);
        var such = Regex.Match(b, @"^(?<v>Delete|Return|Suspend) (?<n>\d+) such Digimon(?<rest>.*)$", I);
        var am = Regex.Match(a, @"^(?:Then, )?(?<v>Delete|Return|Suspend) 1 of (?<t>.+?)(?<rest> to (?:the|its owner's) (?:hand|bottom of the deck))?$", I);
        if (such.Success && am.Success)
            b = $"{such.Groups["v"].Value} {such.Groups["n"].Value} of {am.Groups["t"].Value}{am.Groups["rest"].Value}{such.Groups["rest"].Value}";
        // "you may play 1 ~ without paying its memory cost" — A가 "from your trash"면 같은 곳 (BT7-109 — 공식 "대신 ~ 디지몬 카드 1장을 코스트를 지불하지 않고 등장시킬 수 있다" — 트래시에서)
        var aFrom = Regex.Match(a, @"^Play .+? (?<from>from your (?:trash|hand))\b", I);
        if (aFrom.Success && Regex.IsMatch(b, @"^(?:You may )?play \d+ .+? without paying", I) && !Regex.IsMatch(b, @"\bfrom your\b", I))
            b = Regex.Replace(b, @"(?= without paying)", " " + aFrom.Groups["from"].Value, I);
        return b;
    }

    private static Step? InsteadStep42(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        int at = sentence.IndexOf(InsteadMark42, StringComparison.Ordinal);
        if (at < 0) return null;
        string a = sentence[..at].Trim(), second = sentence[(at + InsteadMark42.Length)..].Trim();
        var stepA = ParseStep(a, card, triggers);
        if (stepA.Error != null || stepA.Step == null) return null;
        var sa = stepA.Step;
        Match m;
        if ((m = InsteadByRe42.Match(second)).Success)
        {
            string costText = m.Groups["cost"].Value;
            string b = InsteadB42(a, m.Groups["b"].Value);
            var stepB = ParseStep(b, card, triggers);
            if (stepB.Error != null || stepB.Step == null) return null;
            if (ParseCost(costText, card) is not { } pay) return null;
            var sb = stepB.Step;
            string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
            return new Step(null, c =>
            {
                if (pay.CanPay(c) && c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] {pay.Describe} 대신 \"{b}\"를 처리할까요?", pay.Recommend(c)))
                {
                    c.CostFailed = false;
                    pay.Pay(c);
                    if (!c.CostFailed)
                    {
                        Diag42.Hit(ref Diag42.InsteadAlt);
                        RunStep(sb, c);
                        return;
                    }
                    c.CostFailed = false;
                }
                RunStep(sa, c);
            }, false, sentence);
        }
        return null;
    }
}
