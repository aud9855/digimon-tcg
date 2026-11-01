using System;
using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.58.0 미지원 문장 32차 묶음의 생성 한글 — 원래 규칙(31차까지)이 옮기지 못한 절만 (SentenceParser.Batch58과 같은 표)</summary>
public static partial class KoreanEffectRenderer
{
    [ThreadStatic] private static int _clauseDepth58;
    [ThreadStatic] private static int _koGate58;
    private static bool KoGate58 => _koGate58 > 0;

    private static string? RenderClause58(string rawClause)
    {
        if (_clauseDepth58 > 0) return null;
        string t = Regex.Replace(rawClause.Replace(' ', ' ').Replace('’', '\'').Trim(), @"^Inherited Effect\s*", "", IC);
        string alt = SentenceParser.ClauseRewrite58(t);
        bool special = ClauseSpecialKoRe58.IsMatch(t);
        // 32차 표현이 없으면 다시 옮기지 않는다 (같은 결과 — 시간만 든다)
        if (!special && alt == t && !SentenceParser.Hint58.IsMatch(t)) return null;
        _clauseDepth58++;
        _koGate58++;
        try
        {
            if (special) return ClauseSpecialKo58(t);
            return RenderClause(alt);
        }
        finally { _clauseDepth58--; _koGate58--; }
    }

    private static readonly Regex ClauseSpecialKoRe58 = new(
        $"{SentenceParser.TargetLockAllRe58}|{SentenceParser.DuringAttacksImmuneRe58}|{SentenceParser.GainTraitRe58}|^(?:\\[[^\\]]+\\]\\s*)*＜Digi-Burst up to \\d+＞|^\\[On Deletion\\]\\s*When this Digimon is deleted other than in battle, ",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>절 하나로 옮기는 것 (SentenceParser.ClauseSpecial58 · ≪디지버스트 N까지≫ 머리말)</summary>
    private static string? ClauseSpecialKo58(string t)
    {
        Match m;
        if ((m = SentenceParser.TargetLockAllRe58.Match(t)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return $"{LabelsKo57(m.Groups["q"].Value)}특징으로 {x}{J(x, "을", "를")} 가진 자신의 디지몬 전부는 어택의 대상이 변경되지 않는다.";
        }
        if ((m = SentenceParser.DuringAttacksImmuneRe58.Match(t)).Success)
            return $"{LabelsKo57(m.Groups["q"].Value)}어택 중에는, 이 디지몬은 상대의 효과를 받지 않는다.";
        if ((m = SentenceParser.GainTraitRe58.Match(t)).Success)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return $"{LabelsKo57(m.Groups["q"].Value)}이 디지몬은 특징 {x}{J(x, "을", "를")} 얻는다.";
        }
        // BT7-040 "[Main] [Once Per Turn] ＜Digi-Burst up to 4＞ (You may trash up to 4 of this Digimon's digivolution cards to activate the effect below.)"
        if ((m = Regex.Match(t, @"^(?<lab>(?:\s*\[[^\]]+\])*)\s*＜Digi-Burst up to (?<n>\d+)＞(?:\s*\([^)]*\))?\.?$", IC)).Success)
        {
            string n = m.Groups["n"].Value;
            return $"{LabelsKo57(m.Groups["lab"].Value)}《디지버스트 {n}까지》 (이 디지몬의 진화원을 {n}장까지 파기하는 것으로, 아래의 효과를 발휘한다)";
        }
        // P-142 "[On Deletion] When this Digimon is deleted other than in battle, ~"
        if ((m = Regex.Match(t, @"^\[On Deletion\]\s*When this Digimon is deleted other than in battle, (?<r>.+)$", IC)).Success
            && RenderClause("[On Deletion] " + Cap(m.Groups["r"].Value)) is { } od && od.StartsWith("【소멸 시】 ", StringComparison.Ordinal))
            return "【소멸 시】 이 디지몬이 배틀 이외로 소멸했을 때, " + od["【소멸 시】 ".Length..];
        return null;
    }

    /// <summary>문장 (SentenceParser.ParseAction58 · StepFallback58과 같은 표)</summary>
    private static string? SentenceFallback58(string s, bool nested)
    {
        if (!KoGate58) return null;
        s = s.Trim().TrimEnd('.').Trim();
        string pre = "";
        var then = Regex.Match(s, @"^Then,?\s+(?<r>.+)$", IC);
        if (then.Success) { pre = "그 후, "; s = then.Groups["r"].Value; }
        bool may = false;
        var mayM = Regex.Match(s, @"^you may (?<r>.+)$", IC);
        if (mayM.Success) { may = true; s = mayM.Groups["r"].Value; }
        Match m;
        if ((m = Regex.Match(s, @"^For each level of the Digimon deleted by this effect, (?<core>.+)$", IC)).Success && !may
            && Sentence(Cap(m.Groups["core"].Value), true) is { } fe)
            return $"{pre}이 효과로 소멸한 디지몬의 Lv. 1마다, {fe}";
        if ((m = Regex.Match(s, @"^reveal (?<p>1 .+?) from your hand and place it on (?:the )?top of your deck$", IC)).Success && PickKo(m.Groups["p"].Value) is { } pk)
            return $"{pre}자신의 패에서 {pk}{J(pk, "을", "를")} 공개하고, 덱 위에 {(may ? "놓을 수 있다" : "놓는다")}.";
        if ((m = Regex.Match(s, @"^1 of your (?<x>\[[^\]]+\](?: or \[[^\]]+\])*) trait Digimon's attack target can't (?:change|be changed|be switched|switch) for the turn$", IC)).Success && !may)
        {
            string x = NamesKo(m.Groups["x"].Value, trait: true);
            return $"{pre}이 턴 동안, 특징으로 {x}{J(x, "을", "를")} 가진 자신의 디지몬 1마리는 어택의 대상이 변경되지 않는다.";
        }
        if ((m = Regex.Match(s, @"^trash the top digivolution card of 1 of your opponent's Digimon for each of that Digimon's colou?rs$", IC)).Success && !may)
            return $"{pre}상대의 디지몬 1마리의 진화원을 그 디지몬의 색 1색마다 위에서부터 1장 파기한다.";
        if (Regex.IsMatch(s, @"^At the end of the battle, if you don't have (?:any )?Digimon(?: in play)?, play this card without paying (?:the|its) (?:memory )?cost$", IC) && !may)
            return $"{pre}배틀 종료 시, 자신의 디지몬이 없다면, 이 카드를 코스트를 지불하지 않고 등장시킨다.";
        if ((m = Regex.Match(s, @"^play (?<a>1 \[[^\]]+\](?: or \[[^\]]+\])*) or (?<b>up to \d+ .+?) from your trash without paying their (?:memory )?costs$", IC)).Success && may
            && PickKo(m.Groups["a"].Value) is { } pa && PickKo(m.Groups["b"].Value) is { } pb)
            return $"{pre}자신의 트래시에서 {pa} 또는 {pb}{J(pb, "을", "를")} 코스트를 지불하지 않고 등장시킬 수 있다.";
        if ((m = Regex.Match(s, @"^(?<a>return up to \d+ .+? from your trash to the top of the deck) ⟦cap58⟧ (?<d>delete 1 of your opponent's Digimon with a play cost of \d+ or less)$", IC)).Success && !may
            && Sentence("You may " + m.Groups["a"].Value + ".", true) is { } ra && Sentence(Cap(m.Groups["d"].Value) + ".", true) is { } da)
            return $"{pre}{ra} 그 후, {da} 이 효과로 되돌린 1장마다 이 효과의 등장 코스트 상한 +1.";
        return null;
    }
}
