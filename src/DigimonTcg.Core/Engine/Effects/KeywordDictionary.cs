using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>효과 문장 한 절이 대전에서 동작하는지</summary>
public enum ClauseSupport { Supported, Unsupported }

/// <summary>효과 문장 한 절과 지원 상태 (Inherited = 진화원 효과 칸의 절)</summary>
public sealed record EffectClause(bool Inherited, string Text, ClauseSupport Support, string Note);

/// <summary>
/// 키워드 사전 (효과 확장 2단계) — 규칙이 정해진 키워드는 이름만으로 동작을 붙인다.
/// API로 받은 카드(영어 문장)는 두 가지 절을 인식해 카드 스크립트를 자동으로 만든다:
///  ① 키워드만 있는 절: "＜Blocker＞ (설명)" / "＜Rush＞ ＜Piercing＞" — 상시 키워드
///  ② 발동 시점 + 동작 키워드만 있는 절: "[On Play] ＜Draw 1＞ (설명)", "[When Digivolving] ＜De-Digivolve 1＞ 1 of your opponent's Digimon."
/// 조건이 붙은 절("While ~, it gains ＜Piercing＞")은 문장 파서(3단계)가 맡는다.
/// </summary>
public static class KeywordDictionary
{
    // ＜이름 ±N (괄호)＞ — 영어 ＜＞ / 한글판 ≪≫ 모두
    private const string KeywordToken = @"[＜<≪]\s*(?<name>[A-Za-z가-힣][A-Za-z가-힣 .'\-]*?)\s*(?<num>[+\-−]?\s*\d+)?\s*(?:\((?<paren>[^)]*)\))?\s*[＞>≫]";

    // 키워드 뒤 설명문 "(This Digimon can block ...)" — 한 단계 중첩 괄호까지
    private const string Reminder = @"(?:\s*\((?:[^()]|\([^()]*\))*\))?";

    private static readonly Regex StandaloneLine = new(
        $@"^(?:\s*{KeywordToken}{Reminder}\s*[,.]?)+\s*$", RegexOptions.Compiled);

    // 키워드 + 설명문 한 묶음씩 (설명문 안의 키워드 — 예: ≪연계≫ 설명의 "＜Security A. +1＞" — 는 건너뛴다)
    private static readonly Regex KeywordRegex = new($"{KeywordToken}{Reminder}", RegexOptions.Compiled);

    // 동작 키워드의 대상: "1 of your opponent's Digimon" · "2 of ~" · "all of ~" (한글판: "상대의 디지몬 1마리")
    private const string Target = @"(?<target>\s*(?:(?<tcount>\d+|all) of your opponent's Digimon|상대의 디지몬 (?<tcount>\d+)마리))?";

    // 발동 시점 + 동작 키워드 하나로 끝나는 절 ("Then, ~"처럼 뒤에 문장이 이어지면 문장 파서 대상)
    private static readonly Regex TriggeredLine = new(
        $@"^(?<triggers>(?:\s*[\[【]\s*[^\]】]+\s*[\]】])+)\s*{KeywordToken}{Target}\s*[.。]?{Reminder}\s*[.。]*\s*$", RegexOptions.Compiled);

    private static readonly Regex TriggerRegex = new(@"[\[【]\s*(?<t>[^\]】]+?)\s*[\]】]", RegexOptions.Compiled);

    // ---- 상시 키워드 ----

    private static readonly Dictionary<string, Keyword> StaticKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blocker"] = Keyword.Blocker, ["블로커"] = Keyword.Blocker,
        ["Jamming"] = Keyword.Jamming, ["재밍"] = Keyword.Jamming,
        ["Piercing"] = Keyword.Piercing, ["관통"] = Keyword.Piercing,
        ["Retaliation"] = Keyword.Retaliation, ["길동무"] = Keyword.Retaliation,
        ["Reboot"] = Keyword.Reboot, ["재기동"] = Keyword.Reboot,
        ["Rush"] = Keyword.Rush, ["속공"] = Keyword.Rush,
        ["Raid"] = Keyword.Raid, ["돌진"] = Keyword.Raid,
        ["Alliance"] = Keyword.Alliance, ["연계"] = Keyword.Alliance,
        ["Barrier"] = Keyword.Barrier, ["방벽"] = Keyword.Barrier,
        ["Evade"] = Keyword.Evade, ["회피"] = Keyword.Evade,
        ["Armor Purge"] = Keyword.ArmorPurge, ["아머 퍼지"] = Keyword.ArmorPurge,
        ["Fortitude"] = Keyword.Fortitude, ["불굴"] = Keyword.Fortitude,
        ["Collision"] = Keyword.Collision, ["충돌"] = Keyword.Collision,
        ["Vortex"] = Keyword.Vortex, ["볼텍스"] = Keyword.Vortex, ["Execute"] = Keyword.Execute, ["에그제큐트"] = Keyword.Execute,
        ["Scapegoat"] = Keyword.Scapegoat, ["스케이프고트"] = Keyword.Scapegoat,
        ["Iceclad"] = Keyword.Iceclad, ["빙장"] = Keyword.Iceclad,
        ["Progress"] = Keyword.Progress, ["프로그레스"] = Keyword.Progress,
        ["Engage"] = Keyword.Engage, ["급습"] = Keyword.Engage,
        ["Ascension"] = Keyword.Ascension, ["천승"] = Keyword.Ascension,
    };

    private static readonly Dictionary<string, EffectTrigger> Triggers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["On Play"] = EffectTrigger.OnPlay, ["등장 시"] = EffectTrigger.OnPlay,
        ["When Digivolving"] = EffectTrigger.WhenDigivolving, ["진화 시"] = EffectTrigger.WhenDigivolving,
        ["When Attacking"] = EffectTrigger.WhenAttacking, ["어택 시"] = EffectTrigger.WhenAttacking,
        ["On Deletion"] = EffectTrigger.OnDeletion, ["소멸 시"] = EffectTrigger.OnDeletion,
        ["Main"] = EffectTrigger.Main, ["메인"] = EffectTrigger.Main,
        ["Security"] = EffectTrigger.Security, ["시큐리티"] = EffectTrigger.Security,
        ["Start of Your Turn"] = EffectTrigger.StartOfYourTurn, ["자신의 턴 개시 시"] = EffectTrigger.StartOfYourTurn,
        ["End of Attack"] = EffectTrigger.EndOfAttack, ["어택 종료 시"] = EffectTrigger.EndOfAttack,
    };

    private static readonly Dictionary<EffectTrigger, string> TriggerLabels = new()
    {
        [EffectTrigger.OnPlay] = "등장 시", [EffectTrigger.WhenDigivolving] = "진화 시", [EffectTrigger.WhenAttacking] = "어택 시",
        [EffectTrigger.OnDeletion] = "소멸 시", [EffectTrigger.Main] = "메인", [EffectTrigger.Security] = "시큐리티",
        [EffectTrigger.StartOfYourTurn] = "자신의 턴 개시 시", [EffectTrigger.EndOfAttack] = "어택 종료 시",
    };

    /// <summary>동작 키워드 절 하나: 발동 시점 · 동작 · 수치 · 대상 수(퇴화: 0 = 전부) · [턴에 1회]</summary>
    private sealed record KeywordAction(EffectTrigger Trigger, string Action, int Amount, int TargetCount, bool OncePerTurn);

    /// <summary>한 절에서 읽어 낸 내용</summary>
    private sealed record ParsedClause(List<Keyword> Keywords, int SecurityAttack, List<KeywordAction> Actions, string? Unsupported);

    // =====================================================================
    // 절 나누기 · 인식
    // =====================================================================

    /// <summary>효과 문장을 절(줄) 단위로 나눈다 (API 문장의 "br>" 같은 깨진 줄바꿈도 처리)</summary>
    public static IEnumerable<string> SplitClauses(string? text) =>
        (text ?? string.Empty)
            .Replace(" ", " ").Replace("<br>", "\n").Replace("br>", "\n").Replace("\r", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("|"));   // "|applinkdp =" 같은 API 데이터 잡음 제외

    private static ParsedClause Parse(string clause)
    {
        var keywords = new List<Keyword>();
        int sa = 0;
        var actions = new List<KeywordAction>();

        if (StandaloneLine.IsMatch(clause))
        {
            foreach (Match m in KeywordRegex.Matches(clause))
            {
                string name = m.Groups["name"].Value.Trim();
                if (StaticKeywords.TryGetValue(name, out var k))
                    keywords.Add(k);
                else if (IsSecurityAttack(name))
                    sa += ParseNumber(m.Groups["num"].Value);
                else
                    return new ParsedClause(keywords, sa, actions, $"미지원 키워드 ≪{name}≫");
            }
            return new ParsedClause(keywords, sa, actions, null);
        }

        var t = TriggeredLine.Match(clause);
        if (t.Success)
        {
            var triggers = new List<EffectTrigger>();
            bool oncePerTurn = false;
            foreach (Match tm in TriggerRegex.Matches(t.Groups["triggers"].Value))
            {
                string label = tm.Groups["t"].Value.Trim();
                if (label.Equals("Once Per Turn", StringComparison.OrdinalIgnoreCase) || label == "턴에 1회")
                    oncePerTurn = true;
                else if (Triggers.TryGetValue(label, out var trig))
                    triggers.Add(trig);
                else
                    return new ParsedClause(keywords, sa, actions, $"미지원 시점 [{label}]");
            }
            if (triggers.Count == 0)
                return new ParsedClause(keywords, sa, actions, "발동 시점 없음");

            string name = t.Groups["name"].Value.Trim();
            bool hasTarget = t.Groups["target"].Success && t.Groups["target"].Value.Trim().Length > 0;
            string tcount = t.Groups["tcount"].Value;
            int targetCount = tcount.Equals("all", StringComparison.OrdinalIgnoreCase) ? 0 : int.TryParse(tcount, out int tc) ? tc : 1;
            int n = ParseNumber(t.Groups["num"].Value);
            string? action = name.ToLowerInvariant() switch
            {
                "draw" or "드로우" when !hasTarget => "Draw",
                "recovery" or "리커버리" when !hasTarget => "Recovery",
                "blitz" or "진격" when !hasTarget => "Blitz",
                "de-digivolve" or "퇴화" when hasTarget => "DeDigivolve",
                _ => null,
            };
            if (action == null)
                return new ParsedClause(keywords, sa, actions, $"미지원 동작 ≪{name}≫");
            foreach (var trig in triggers)
                actions.Add(new KeywordAction(trig, action, n, targetCount, oncePerTurn));
            return new ParsedClause(keywords, sa, actions, null);
        }

        return new ParsedClause(keywords, sa, actions, "문장 파서(3단계) 대상");
    }

    private static bool IsSecurityAttack(string name) =>
        name.StartsWith("Security A", StringComparison.OrdinalIgnoreCase) || name.StartsWith("시큐리티 어택", StringComparison.Ordinal);

    private static int ParseNumber(string raw)
    {
        string s = raw.Replace(" ", "").Replace("−", "-");
        return int.TryParse(s, out int n) ? n : 0;
    }

    // =====================================================================
    // API 카드: 카드 데이터 수치 · 자동 스크립트 · 지원 상태
    // =====================================================================

    /// <summary>
    /// API 카드의 키워드 수치(≪블로커≫ · ≪시큐리티 어택 ±N≫)를 효과 문장에서 다시 읽어 카드 데이터에 넣는다.
    /// (키워드만 있는 절이면 문장 맨 앞이 아니어도 인식한다. 내장 카드는 코드 정의를 쓰므로 호출하지 않는다)
    /// </summary>
    public static void ApplyDataKeywords(CardData card)
    {
        var main = SplitClauses(card.EffectText).Select(Parse).ToList();
        var inherited = SplitClauses(card.SourceEffectText).Select(Parse).ToList();   // 듀얼 카드의 옵션 쪽은 진화원 효과가 아니다
        card.IsBlocker = main.Any(p => p.Keywords.Contains(Keyword.Blocker));
        card.SecurityAttackBonus = main.Sum(p => p.SecurityAttack);
        card.InheritedSecurityAttackBonus = inherited.Sum(p => p.SecurityAttack);
    }

    /// <summary>API 카드의 효과 문장에서 키워드 · 키워드 동작을 읽어 카드 스크립트를 만든다 (인식한 것이 없으면 null)</summary>
    public static CardScript? GenerateScript(CardData card)
    {
        var script = new CardScript(card.CardId);
        var def = new CardScriptDefinition(script);
        foreach (var (inherited, clause, options) in ClausesOf(card))
        {
            if (card.IsDual && inherited)
                continue;   // 듀얼 카드의 옵션 쪽 → OptionSideScript
            if (options != null)
            {
                SentenceParser.TryParseChoice(clause, options, inherited, card, def);   // "Activate 1 of the effects below:" + "・~"
                continue;
            }
            var parsed = Parse(StripLabel(clause));
            if (parsed.Unsupported == null)
                AddParsed(script, new[] { parsed }, inherited);
            else
                SentenceParser.TryParse(clause, inherited, card, def);   // 키워드 절이 아니면 문장 파서 (읽지 못하면 등록하지 않음)
        }
        bool any = script.Keywords.Count + script.InheritedKeywords.Count + script.Effects.Count + script.Statics.Count > 0
                   || script.OverflowMemory > 0 || script.DigisorptionReduction > 0 || script.BlastDigivolve || script.MaterialSave > 0 || script.LinkRequirement != null || script.SpecialEvolutions.Count > 0 || script.TamerEvolutions.Count > 0 || script.IgnoreColorRequirement != null || script.PlayCostReduction != null || script.PlayCostOptions.Count > 0 || script.EvoCostModifiers.Count > 0 || script.FieldPlayCostModifiers.Count > 0 || script.GainedSourceEffects.Count > 0
                   || script.XrosAliases.Count > 0 || script.XrosFromTrash != null || script.XrosUnderTamersFor != null || script.OverclockSpecified != null;
        return any ? script : null;
    }

    /// <summary>
    /// 듀얼 카드의 옵션 쪽 (종합 룰 4-6-6: 옵션 쪽의 효과는 옵션 카드의 효과) — 옵션 카드로 보고 읽는 대리 카드.
    /// 이름 · 색 · 특징 · 사용 코스트(PlayCost)는 같고, 효과 칸 = 옵션 쪽 문장
    /// </summary>
    public static CardData OptionSideCard(CardData dual) => new()
    {
        CardId = dual.CardId, Name = dual.Name, EnglishName = dual.EnglishName, Type = CardType.Option,
        Color = dual.Color, SecondaryColor = dual.SecondaryColor, PlayCost = dual.PlayCost,
        EffectText = dual.InheritedEffectText, Traits = dual.Traits, ImageUrl = dual.ImageUrl, Rarity = dual.Rarity,
    };

    /// <summary>듀얼 카드의 옵션 쪽 스크립트 ([메인] · 사용 조건 · 사용 코스트 증감). 듀얼 카드가 아니거나 읽은 것이 없으면 null</summary>
    public static CardScript? GenerateOptionSideScript(CardData card) =>
        card.IsDual && !string.IsNullOrWhiteSpace(card.InheritedEffectText) ? GenerateScript(OptionSideCard(card)) : null;

    /// <summary>"~ Activate 1 of the effects below:" 다음에 오는 "・~" 줄들을 선택지로 묶는다 (그 밖의 절은 options = null)</summary>
    private static IEnumerable<(string Clause, List<string>? Options)> GroupChoices(IEnumerable<string> clauses)
    {
        var list = clauses.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            bool header = Regex.IsMatch(list[i], @"(?:effects? below|following effects?)(?: twice| \d+ times| instead)?\s*[:.]?\s*$", RegexOptions.IgnoreCase)
                          || list[i].Contains("＜Digi-Burst", StringComparison.OrdinalIgnoreCase) || list[i].Contains("＜Delay", StringComparison.OrdinalIgnoreCase);   // ≪디지버스트 N≫ (설명문 뒤에 ・효과)
            if (header && i + 1 < list.Count && list[i + 1].StartsWith("・"))
            {
                var options = new List<string>();
                int j = i + 1;
                while (j < list.Count && list[j].StartsWith("・")) options.Add(list[j++]);
                yield return (list[i], options);
                i = j - 1;
            }
            else yield return (list[i], null);
        }
    }

    private static void AddParsed(CardScript script, IEnumerable<ParsedClause> clauses, bool inherited)
    {
        var def = new CardScriptDefinition(script);
        foreach (var clause in clauses.Where(c => c.Unsupported == null))
        {
            foreach (var k in clause.Keywords)
            {
                if (inherited) def.InheritedKeyword(k);
                else def.Keyword(k);
            }
            foreach (var a in clause.Actions)
            {
                var resolve = BuildAction(a);
                if (a.Trigger == EffectTrigger.Main)
                    def.Main(a.Action == "DeDigivolve" && a.TargetCount == 1 ? TargetKind.OpponentDigimon : TargetKind.None, resolve);
                else if (inherited)
                    def.Inherited(a.Trigger, resolve, oncePerTurn: a.OncePerTurn);
                else
                    def.On(a.Trigger, resolve, oncePerTurn: a.OncePerTurn);
            }
        }
    }

    private static Action<EffectContext> BuildAction(KeywordAction a)
    {
        string label = TriggerLabels[a.Trigger];
        bool security = a.Trigger == EffectTrigger.Security;
        int amount = a.Amount;
        return a.Action switch
        {
            "Draw" => c =>
            {
                int drawn = 0;
                for (int i = 0; i < amount; i++)
                    if (c.Draw() != null) drawn++;
                Report(c, security, label, $"≪{amount} 드로우≫ ({drawn}장)", $"{drawn}장 드로우");
            },
            "Recovery" => c =>
            {
                int n = c.Recovery(amount);
                Report(c, security, label, $"≪리커버리 +{amount}≫ 시큐리티 {n}장 추가", $"리커버리 +{n}");
            },
            "DeDigivolve" => c =>
            {
                // 대상: 퇴화할 수 있는(진화원이 있고 Lv.4 이상) 상대 디지몬 — 고른 대상 / 어택 중인 디지몬 먼저, 그다음 레벨이 높은 순
                var preferred = security ? c.Other : c.ChosenTarget;
                var targets = c.Enemy.BattleArea.Where(d => d.EvolutionSources.Count > 0 && d.TopCard.Level > 3)
                    .OrderByDescending(d => ReferenceEquals(d, preferred)).ThenByDescending(d => d.TopCard.Level).ThenByDescending(d => d.GetCurrentDP())
                    .Take(a.TargetCount == 0 ? int.MaxValue : a.TargetCount).ToList();
                if (targets.Count == 0)
                {
                    Report(c, security, label, "≪퇴화≫할 상대 디지몬이 없습니다", "불발");
                    return;
                }
                var parts = new List<string>();
                foreach (var target in targets)
                {
                    string before = target.TopCard.Name;
                    int n = c.DeDigivolveEnemy(target, amount);
                    parts.Add($"[{before}] → [{target.TopCard.Name}] ({n}장 파기)");
                }
                Report(c, security, label, $"상대 디지몬 ≪퇴화 {amount}≫: {string.Join(", ", parts)}", $"{targets.Count}마리 퇴화");
            },
            "Blitz" => c =>
            {
                if (c.Self != null && c.Engine.TryGrantBlitz(c.Owner, c.Self))
                    c.Log($"[{label}] {c.Card.Name}: ≪진격≫ — 상대 쪽 메모리가 1 이상이어서 이 디지몬으로 어택할 수 있습니다");
            },
            _ => _ => { },
        };
    }

    private static void Report(EffectContext c, bool security, string label, string detail, string summary)
    {
        if (security)
            c.SecurityResult(detail, summary);
        else if (label == "메인")
            c.Message = $"{c.Card.Name}: {detail}";
        else
            c.Log($"[{label}] {c.Card.Name}: {detail}");
    }

    /// <summary>
    /// 카드 효과의 절 단위 지원 상태 (API 카드 기준 — 키워드 사전으로 동작하는 절만 Supported).
    /// 테이머 [시큐리티] "코스트 없이 등장"처럼 엔진이 규칙으로 처리하는 절도 Supported.
    /// </summary>
    public static List<EffectClause> Analyze(CardData card)
    {
        var result = new List<EffectClause>();
        var optionSide = card.IsDual ? OptionSideCard(card) : null;
        foreach (var (field, clause, options) in ClausesOf(card))
        {
            // 듀얼 카드의 옵션 쪽(진화원 효과 칸에 들어 있다)은 옵션 카드의 효과로 읽는다 — 목록에서는 그 칸 그대로 (Inherited = true)
            bool inherited = field;
            var parseCard = card;
            if (optionSide != null && field) { inherited = false; parseCard = optionSide; }
            EffectClause Clause(string text, ClauseSupport support, string note) => new(field, text, support, note);
            if (options != null)
            {
                string? choiceError = SentenceParser.TryParseChoice(clause, options, inherited, parseCard, def: null);
                foreach (var line in options.Prepend(clause))
                    result.Add(Clause(line, choiceError == null ? ClauseSupport.Supported : ClauseSupport.Unsupported, choiceError ?? "고르는 효과"));
                continue;
            }
            if (IsRuleHandledClause(parseCard, StripLabel(clause)))
            {
                result.Add(Clause(clause, ClauseSupport.Supported, "규칙으로 처리"));
                continue;
            }
            var parsed = Parse(StripLabel(clause));
            if (parsed.Unsupported == null)
            {
                result.Add(Clause(clause, ClauseSupport.Supported, "키워드"));
                continue;
            }
            string? sentenceError = SentenceParser.TryParse(clause, inherited, parseCard, def: null);
            result.Add(sentenceError == null
                ? Clause(clause, ClauseSupport.Supported, "문장")
                : Clause(clause, ClauseSupport.Unsupported, IsKeywordShapedClause(StripLabel(clause)) ? parsed.Unsupported : sentenceError));
        }
        return result;
    }

    /// <summary>
    /// 카드의 절 목록 (메인 효과 칸 → 진화원 효과 칸). API 데이터는 칸과 실제 종류가 어긋날 때가 있어 머리말로 고친다:
    /// 메인 칸의 "Inherited Effect ~"는 진화원 효과, 진화원 칸의 "Security Effect ~"(옵션 · 테이머)는 이 카드 자신의 [시큐리티] 효과.
    /// </summary>
    private static IEnumerable<(bool Inherited, string Clause, List<string>? Options)> ClausesOf(CardData card)
    {
        foreach (var (field, text) in new[] { (false, card.EffectText), (true, card.InheritedEffectText) })
        {
            foreach (var (clause, options) in GroupChoices(SplitClauses(text)))
            {
                bool inherited = field;
                if (Regex.IsMatch(clause, @"^Inherited Effect\b", RegexOptions.IgnoreCase)) inherited = true;
                else if (Regex.IsMatch(clause, @"^Security Effect\b", RegexOptions.IgnoreCase)) inherited = false;
                yield return (inherited, clause, options);
            }
        }
    }

    /// <summary>절 머리말("Inherited Effect" · "Security Effect") 제거</summary>
    private static string StripLabel(string clause) =>
        Regex.Replace(clause, @"^(?:Inherited|Security) Effect\s*", "", RegexOptions.IgnoreCase);

    /// <summary>
    /// 키워드가 들어 있는 절인지 (2단계 완료 기준 "키워드 절"의 범위 판단용):
    /// 키워드만 있는 절이거나, 발동 시점 + 키워드 하나로 된 절
    /// </summary>
    public static bool IsKeywordShapedClause(string clause) =>
        StandaloneLine.IsMatch(clause) || TriggeredLine.IsMatch(clause);

    private static bool IsRuleHandledClause(CardData card, string clause) =>
        card.Type == CardType.Tamer
        && Regex.IsMatch(clause, @"^\[Security\]\s*Play this card without paying (?:the|its) (?:memory )?cost\.?$", RegexOptions.IgnoreCase);
}
