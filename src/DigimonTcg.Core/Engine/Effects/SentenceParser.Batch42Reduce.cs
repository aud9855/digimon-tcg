using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.42.0 앞 문장(진화 · 등장)의 코스트를 셈 · 조건만큼 더 줄인다:
///  - "~ may digivolve into ~ in the hand. For every 10 total cards in both players' trashes, reduce this effect's digivolution cost by 1." (BT21-076 — 공식 "서로의 트래시 합계 10장마다 이 효과로 지불하는 코스트 -1") ·
///    BT21-082 · AD1-022 · P-166 · EX11-064 · BT27-049 ("paid cost") · EX13-070 ("Reduce this effect's paid cost by 1 for each ~")
///  - "~ with the play cost reduced by 3. For each card trashed by this effect, further reduce it by 1." (EX5-062 — 공식 "이 효과로 파기한 1장마다 이 효과의 코스트 경감 수치 -1") · EX5-043 ("If ~, further reduce it by 3") · EX13-043
///  - "you may play 1 [ADVENTURE] trait card from your hand. For every 2 of your Tamers' colors, reduce this effect's play cost by 1." (AD1-019)
/// 두 문장을 한 문장(표시 ⟦reduce⟧)으로 합쳐 읽는다 — 줄이는 양 k마다 앞 문장을 "with the ~ cost reduced by k"로 다시 읽는다
/// </summary>
internal static partial class SentenceParser
{
    internal const string ReduceMark42 = " ⟦reduce⟧ ";

    internal static readonly Regex ReduceForRe42 = new(@"^(?:For (?:each|every) (?<cnt>[^,]+), (?:reduce this effect's (?:digivolution |paid |play |use )?cost|further reduce it) by (?<n>\d+)|Reduce this effect's (?:digivolution |paid |play |use )?cost by (?<n>\d+) for (?:each|every) (?<cnt>.+?))\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex ReduceIfRe42 = new(@"^If (?<c>[^,]+), further reduce it by (?<n>\d+)\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsReduceSentence42(string s) => ReduceForRe42.IsMatch(s.Trim()) || ReduceIfRe42.IsMatch(s.Trim());

    /// <summary>앞 문장 a를 코스트 k만큼 더 줄인 문장 (읽을 수 없는 형태면 null)</summary>
    internal static string? ReducedSentence42(string a, int k)
    {
        a = a.Trim().TrimEnd('.');
        var has = Regex.Match(a, @"with the (?<w>play |digivolution |memory )?cost reduced by (?<r>\d+)", I);
        if (has.Success)
            return a[..has.Index] + $"with the {has.Groups["w"].Value}cost reduced by {int.Parse(has.Groups["r"].Value) + k}" + a[(has.Index + has.Length)..];
        if (Regex.IsMatch(a, @"\bwithout paying\b|\bfor (?:a|its) (?:digivolution |memory )?cost\b", I)) return null;
        if (Regex.IsMatch(a, @"\bdigivolve into .+ (?:in|from) (?:the |your )?(?:hand|trash)$", I)) return $"{a} with the digivolution cost reduced by {k}";
        if (Regex.IsMatch(a, @"\bplay .+ from (?:your|the) (?:hand|trash)$", I)) return $"{a} with the play cost reduced by {k}";
        return null;
    }

    private static Step? ReduceStep42(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        int at = sentence.IndexOf(ReduceMark42, StringComparison.Ordinal);
        if (at < 0) return null;
        string a = sentence[..at].Trim(), second = sentence[(at + ReduceMark42.Length)..].Trim();
        // 줄이는 양 (효과 상황 — 앞 문장의 비용 · 동작이 옮긴 매수는 PaidCount)
        Func<EffectContext, int> amount;
        Match m;
        if ((m = ReduceForRe42.Match(second)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            string cnt = m.Groups["cnt"].Value.Trim();
            if (Regex.IsMatch(cnt, @"^(?:card|one) (?:trashed|placed|returned) by this effect$|^card this effect (?:trashed|placed|returned)$", I)) amount = c => n * Math.Max(0, c.PaidCount);
            else if (ParseCounter(cnt) is { } counter) amount = c => n * Math.Max(0, counter(c.Owner, c.Enemy, c.Self));
            else return null;
        }
        else if ((m = ReduceIfRe42.Match(second)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            if (ParseEventCondition(m.Groups["c"].Value) is not { } cond) return null;
            amount = c => cond(c) ? n : 0;
        }
        else return null;
        if (ReducedSentence42(a, 1) is not { } probe || ParseStep(probe, card, triggers) is not { Error: null, Step: not null } probeStep) return null;
        var byK = new Dictionary<int, Step?>();
        return probeStep.Step with
        {
            Condition = null, Optional = false, Text = sentence,
            Act = c =>
            {
                int k = Math.Clamp(amount(c), 0, 20);
                if (k > 0) Diag42.Hit(ref Diag42.CountNew);
                if (!byK.TryGetValue(k, out var st)) byK[k] = st = ReducedSentence42(a, k) is { } ak ? ParseStep(ak, card, triggers).Step : null;
                if (st != null) RunStep(st, c);
            },
        };
    }
}
