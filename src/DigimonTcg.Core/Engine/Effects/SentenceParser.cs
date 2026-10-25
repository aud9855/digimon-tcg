using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// 효과 문장 파서 (효과 확장 3단계) — API 카드의 영어 효과 절을 카드 스크립트로 바꾼다.
/// 절 = [발동 시점 · 조건 표시]* + 문장들. 문장마다 "Then," · "If ~," · "You may" · 동작을 읽고,
/// 하나라도 읽지 못하면 그 절 전체를 미지원으로 둔다 (조용히 일부만 처리하지 않는다).
/// </summary>
internal static class SentenceParser
{
    private enum Qualifier { None, YourTurn, OpponentsTurn, AllTurns }

    private static readonly Dictionary<string, EffectTrigger> EventTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["On Play"] = EffectTrigger.OnPlay,
        ["When Digivolving"] = EffectTrigger.WhenDigivolving,
        ["When Attacking"] = EffectTrigger.WhenAttacking,
        ["On Deletion"] = EffectTrigger.OnDeletion,
        ["Main"] = EffectTrigger.Main,
        ["Security"] = EffectTrigger.Security,
        ["Start of Your Turn"] = EffectTrigger.StartOfYourTurn,
        ["End of Attack"] = EffectTrigger.EndOfAttack,
        ["Start of Your Main Phase"] = EffectTrigger.StartOfYourMainPhase,
        ["End of Your Turn"] = EffectTrigger.EndOfYourTurn,
        ["End of Opponent's Turn"] = EffectTrigger.EndOfOpponentsTurn,
        ["When Moving"] = EffectTrigger.WhenMoving,
    };

    private static readonly Dictionary<EffectTrigger, string> Labels = new()
    {
        [EffectTrigger.OnPlay] = "등장 시", [EffectTrigger.WhenDigivolving] = "진화 시", [EffectTrigger.WhenAttacking] = "어택 시",
        [EffectTrigger.OnDeletion] = "소멸 시", [EffectTrigger.Main] = "메인", [EffectTrigger.Security] = "시큐리티",
        [EffectTrigger.StartOfYourTurn] = "자신의 턴 개시 시", [EffectTrigger.EndOfAttack] = "어택 종료 시",
        [EffectTrigger.StartOfYourMainPhase] = "자신의 메인 페이즈 개시 시", [EffectTrigger.EndOfYourTurn] = "자신의 턴 종료 시",
        [EffectTrigger.DeletedOpponentInBattle] = "배틀에서 소멸시켰을 때", [EffectTrigger.AttackBlocked] = "블록당했을 때",
        [EffectTrigger.EndOfOpponentsTurn] = "상대의 턴 종료 시",
        // 7단계 2차 — "시큐리티"라는 말은 넣지 않는다 (Emit이 [시큐리티] 결과 메시지로 보낸다)
        [EffectTrigger.OnLinked] = "링크 시", [EffectTrigger.ThisDigimonLinked] = "링크되었을 때", [EffectTrigger.AllyDigimonLinked] = "자신의 디지몬이 링크되었을 때",
        [EffectTrigger.AllyHatched] = "부화했을 때", [EffectTrigger.AllyDigimonMovedFromBreeding] = "육성 에어리어에서 이동했을 때",
        [EffectTrigger.OpponentDigimonMovedFromBreeding] = "상대 디지몬이 육성 에어리어에서 이동했을 때",
        [EffectTrigger.AllyFaceUpSecurityPlaced] = "앞면으로 놓였을 때", [EffectTrigger.OpponentFaceUpSecurityPlaced] = "상대 카드가 앞면으로 놓였을 때",
        [EffectTrigger.AllyCheckedFaceUpSecurity] = "앞면 카드를 체크했을 때", [EffectTrigger.ThisDigimonWouldDnaDigivolve] = "DNA 진화하려 할 때",
    };

    private static readonly Dictionary<string, Keyword> GrantableKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blocker"] = Keyword.Blocker, ["Jamming"] = Keyword.Jamming, ["Piercing"] = Keyword.Piercing, ["Retaliation"] = Keyword.Retaliation,
        ["Reboot"] = Keyword.Reboot, ["Rush"] = Keyword.Rush, ["Raid"] = Keyword.Raid, ["Alliance"] = Keyword.Alliance,
        ["Barrier"] = Keyword.Barrier, ["Evade"] = Keyword.Evade,
        ["Armor Purge"] = Keyword.ArmorPurge, ["Fortitude"] = Keyword.Fortitude, ["Collision"] = Keyword.Collision, ["Scapegoat"] = Keyword.Scapegoat,
        ["Iceclad"] = Keyword.Iceclad, ["Progress"] = Keyword.Progress,
        ["Link +1"] = Keyword.LinkPlusOne, ["Vortex"] = Keyword.Vortex, ["Execute"] = Keyword.Execute,
    };

    /// <summary>파싱한 절을 스크립트에 등록하는 일 (전부 읽은 뒤에만 등록한다)</summary>
    private sealed class Pending
    {
        public readonly List<Action<CardScriptDefinition>> Registrations = new();
    }

    // =====================================================================
    // 진입점
    // =====================================================================

    /// <summary>절 하나를 읽어 def에 등록한다. 성공하면 null, 실패하면 미지원 이유. def가 null이면 읽기만 한다 (지원 상태 판단)</summary>
    public static string? TryParse(string rawClause, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string clause = Normalize(rawClause);
        if (clause.Length == 0)
            return null;

        // ACE ≪오버플로우 -N≫: 이 카드가 배틀 에어리어 · 진화원에서 다른 곳으로 가면 메모리 -N
        var overflow = Regex.Match(clause, @"^(?:Ace )?Overflow ＜-(?<n>\d+)＞$", RegexOptions.IgnoreCase);
        if (overflow.Success)
        {
            int n = int.Parse(overflow.Groups["n"].Value);
            def?.Overflow(n);
            return null;
        }
        // "Your [Shoutmon] can digivolve into this card in your hand for a memory cost of 4, ignoring this card's digivolution requirements"
        var special = Regex.Match(clause, @"^Your (?:(?<names>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*)|Digimon with (?<inname>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*) in (?:its|their) names?) can digivolve into this card in (?:your|the) hand for a (?:memory |digivolution )?cost of (?<n>\d+), ignoring (?:this card's )?digivolution requirements\.?$", RegexOptions.IgnoreCase);
        if (special.Success && card.Type == CardType.Digimon)
        {
            int cost = int.Parse(special.Groups["n"].Value);
            bool exact = special.Groups["names"].Success;
            var names = Regex.Matches(special.Groups[exact ? "names" : "inname"].Value, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value).ToList();
            // "Your [Agumon]"은 카드명이 정확히 같은 디지몬만 ("SnowAgumon"은 아님), "with [Agumon] in its name"은 이름에 포함하면 된다
            def?.SpecialEvolution(d => names.Any(n => exact ? CardNames.Is(d.TopCard, n) : TargetSpec.NameHas(d.TopCard, n)), cost);
            return null;
        }
        // 디지크로스 (엔진의 등장 처리 — GoldfishEngine.DigiXros.cs)
        // "This card is also treated as [Shoutmon] (or [ZeigGreymon]) for (a) DigiXros" · "When you would DigiXros, this card/Digimon is also treated as [Shoutmon]"
        var xrosAlias = Regex.Match(clause, $@"^(?:This card is also treated as (?<x>{TargetSpec.NameList}) for (?:a )?DigiXros|When you would DigiXros, this card/Digimon is also treated as (?<x>{TargetSpec.NameList}))\.?$", RegexOptions.IgnoreCase);
        if (xrosAlias.Success)
        {
            foreach (var x in TargetSpec.Names(xrosAlias.Groups["x"].Value)) def?.XrosAlias(x);
            return null;
        }
        // "(While you have a black Tamer,) cards from your trash may also be placed for this card's DigiXros" · "When you would play this card, you can also place cards from your trash in digivolution cards for a DigiXros"
        var xrosTrash = Regex.Match(clause, @"^(?:While (?<cond>[^,]+), )?cards from your trash may also be placed for this card's DigiXros\.?$|^When you would play this card, you may also place cards from your trash (?:in|as) digivolution cards for a DigiXros\.?$", RegexOptions.IgnoreCase);
        if (xrosTrash.Success && card.Type == CardType.Digimon)
        {
            Func<PlayerZones, bool> cond = _ => true;
            if (xrosTrash.Groups["cond"].Success)
            {
                if (ParseZoneCondition(xrosTrash.Groups["cond"].Value) is { } zc) cond = zc;
                else if (ParseSideCondition(xrosTrash.Groups["cond"].Value) is { } sc) cond = z => sc(z, z);   // 자신의 필드만 보는 조건
                else return $"미지원 조건 (디지크로스 트래시: {xrosTrash.Groups["cond"].Value})";
            }
            def?.XrosFromTrash(cond);
            return null;
        }
        // 테이머: "[Your Turn] When you would play 1 Digimon with DigiXros requirements, by suspending this Tamer, you may place cards from under your Tamers as digivolution cards for a DigiXros"
        // · "[All Turns] When any of your [Xros Heart] trait Digimon cards with DigiXros requirements would be played, by suspending this Tamer, ~"
        var xrosTamer = Regex.Match(clause, $@"^(?:\[(?:Your Turn|All Turns)\]\s*)?When (?:you would play (?:1|a) (?:Digimon(?: card)?|(?<spec1>[^,]*?) Digimon card) with (?:a )?DigiXros requirements?|any of your (?<spec2>[^,]*?) Digimon cards with DigiXros requirements would be played), by suspending this Tamer, you may place cards from under your Tamers as digivolution cards for (?:a|their) DigiXros\.?$", RegexOptions.IgnoreCase);
        if (xrosTamer.Success && card.Type == CardType.Tamer)
        {
            string spec = xrosTamer.Groups["spec1"].Success ? xrosTamer.Groups["spec1"].Value : xrosTamer.Groups["spec2"].Value;
            Func<CardData, bool> who = _ => true;
            if (spec.Length > 0)
            {
                if (ParseCardPick($"1 {spec} Digimon card") is not { } sp) return $"미지원 디지크로스 조건: {spec}";
                who = sp.Match;
            }
            def?.XrosUnderTamers(who);   // [Your Turn]은 자신이 패에서 등장시킬 때만 일어나므로 그대로
            return null;
        }
        // ≪머티리얼 세이브 N≫: 이 디지몬이 소멸할 때 진화원 N장을 자신의 테이머 아래에 놓을 수 있다 (엔진의 소멸 처리)
        var materialSave = Regex.Match(clause, @"^＜Material Save (?<n>\d+)＞\.?$", RegexOptions.IgnoreCase);
        if (materialSave.Success && card.Type == CardType.Digimon)
        {
            def?.MaterialSave(int.Parse(materialSave.Groups["n"].Value));
            return null;
        }
        // 링크 조건: "Link Requirements [Link] [Appmon] trait: Cost 2" · "Link Requirements [Link] Lv.3 or higher: Cost 2"
        var linkReq = Regex.Match(clause, $@"^Link Requirements \[Link\]\s*(?<req>.+?)\s*:\s*Cost (?<n>\d+)\.?$", RegexOptions.IgnoreCase);
        if (linkReq.Success)
        {
            Func<CardInstance, bool>? req = null;
            Match rm;
            if ((rm = Regex.Match(linkReq.Groups["req"].Value, $@"^(?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
            { var xs = TargetSpec.Names(rm.Groups["x"].Value); req = d => xs.Any(x => TargetSpec.HasTrait(d.TopCard, x)); }
            else if ((rm = Regex.Match(linkReq.Groups["req"].Value, @"^Lv\.\s*(?<lv>\d+)(?: or (?<dir>higher|lower))?$", RegexOptions.IgnoreCase)).Success)
            {
                int lv = int.Parse(rm.Groups["lv"].Value); string dir = rm.Groups["dir"].Value.ToLowerInvariant();
                req = d => dir == "higher" ? d.TopCard.Level >= lv : dir == "lower" ? d.TopCard.Level <= lv : d.TopCard.Level == lv;
            }
            else if ((rm = Regex.Match(linkReq.Groups["req"].Value, $@"^(?<x>{TargetSpec.NameList})(?: in (?:its|their) names?)?$", RegexOptions.IgnoreCase)).Success)
            { var xs = TargetSpec.Names(rm.Groups["x"].Value); req = d => xs.Any(x => TargetSpec.NameHas(d.TopCard, x)); }
            if (req == null) return $"미지원 링크 조건: {linkReq.Groups["req"].Value}";
            def?.Link(req, int.Parse(linkReq.Groups["n"].Value));
            return null;
        }
        // ≪링크 +1≫: 최대 링크 수 +1
        if (Regex.IsMatch(clause, @"^＜Link \+\d+＞\.?$", RegexOptions.IgnoreCase))
        {
            if (inherited) def?.InheritedKeyword(Keyword.LinkPlusOne); else def?.Keyword(Keyword.LinkPlusOne);
            return null;
        }
        // 떠나려 할 때 · 소멸하려 할 때 키워드: ≪디코드≫ · ≪파티션≫ · ≪프래그먼트≫ · ≪디코이≫ · ≪가드≫, [메인] ≪트레이닝≫
        if (Regex.IsMatch(clause, @"^＜(?:Decode|Partition|Fragment|Decoy|Guard|Training)\b[^＞]*＞\.?$", RegexOptions.IgnoreCase) && card.Type == CardType.Digimon)
            return ParseLeaveKeyword(clause.TrimEnd('.'), inherited, def);
        // [패] [카운터] ≪블래스트 진화≫: 상대 디지몬이 어택했을 때 패의 이 카드로 자신의 디지몬을 코스트 없이 진화 (엔진의 카운터 타이밍)
        if (Regex.IsMatch(clause, @"^\[Hand\]\s*\[Counter\]\s*＜Blast Digivolve＞\.?$", RegexOptions.IgnoreCase) && card.Type == CardType.Digimon)
        {
            def?.BlastDigivolve();
            return null;
        }
        // ≪디지섭션 -N≫: 패에서 이 카드로 진화할 때 자신의 디지몬 1마리를 레스트시켜 진화 코스트 -N
        var digisorption = Regex.Match(clause, @"^＜Digisorption -(?<n>\d+)＞\.?$", RegexOptions.IgnoreCase);
        if (digisorption.Success)
        {
            def?.Digisorption(int.Parse(digisorption.Groups["n"].Value));
            return null;
        }
        // 진화 코스트 감소: "[Your Turn] [Once Per Turn] When this Digimon would digivolve into a Digimon card with the [CS] trait, reduce the digivolution cost by 2."
        //  · "When one of your Digimon would digivolve into ~, by trashing ~, reduce the cost by 1"
        //  · "When one of your Digimon with ~ would digivolve into this card in your hand, reduce the memory cost of the digivolution by 1" (패의 이 카드)
        string evoText = Normalize(clause);
        evoText = Regex.Replace(evoText, @"\bWhen you would digivolve this Digimon into\b", "When this Digimon would digivolve into", RegexOptions.IgnoreCase);
        evoText = Regex.Replace(evoText, @"\bWhen this Digimon or any of your Tamers would digivolve\b", "When this Digimon would digivolve", RegexOptions.IgnoreCase);
        var evoMod = Regex.Match(evoText, @"^(?<labels>(?:\[[^\]]+\]\s*)*)When (?:this (?<sus>suspended )?Digimon|(?:one|any) of your (?<spec>[^,]*?Digimon(?: with [^,]+?)?)) would digivolve(?: into (?<into>[^,]+?))?, (?:if (?<cond>[^,]+), )?(?:by (?<cost>[^,]+?), )?reduce (?:the|its) (?:digivolution |memory )?cost(?: of the digivolution)? by (?<n>\d+)\.?$", RegexOptions.IgnoreCase);
        if (evoMod.Success)
        {
            string evoLabels = evoMod.Groups["labels"].Value;
            if (Regex.IsMatch(evoLabels, @"\[(?!Your Turn\]|Once Per Turn\])[^\]]+\]", RegexOptions.IgnoreCase)) return $"미지원 진화 코스트 감소 시점: {evoLabels.Trim()}";
            var subject = evoMod.Groups["spec"].Success ? EvoCostSubject.AnyOwnDigimon : EvoCostSubject.ThisDigimon;
            Func<EffectContext, CardInstance, bool>? filter = evoMod.Groups["sus"].Success ? (_, d) => d.IsSuspended : null;
            if (evoMod.Groups["spec"].Success && (filter = ParseAllySubject(evoMod.Groups["spec"].Value)) == null)
                return $"미지원 진화 코스트 감소 대상: {evoMod.Groups["spec"].Value}";
            Func<CardData, bool>? into = null;
            if (evoMod.Groups["into"].Success)
            {
                string it = Regex.Replace(evoMod.Groups["into"].Value.Trim(), @" in (?:your|the) hand$", "", RegexOptions.IgnoreCase);
                if (it.Equals("this card", StringComparison.OrdinalIgnoreCase)) subject = EvoCostSubject.IntoThisCard;
                else if ((ParseCardPick(it) ?? ParseNamePick(it) ?? ParseCardPick("1 " + it) ?? ParseNamePick("1 " + it)) is { } pick) into = pick.Match;
                else return $"미지원 진화 코스트 감소 카드: {it}";
            }
            Func<PlayerZones, PlayerZones, bool>? evoCond = null;
            if (evoMod.Groups["cond"].Success)
            {
                string ct = evoMod.Groups["cond"].Value;
                if (ParseZoneCondition(ct) is { } zc) evoCond = (z, _) => zc(z);
                else if ((evoCond = ParseSideCondition(ct)) == null) return $"미지원 조건 (진화 코스트 감소: {ct})";
            }
            int reduceBy = int.Parse(evoMod.Groups["n"].Value);
            PlayCostOption? evoPay = null;
            if (evoMod.Groups["cost"].Success)
            {
                if (ParseCost(evoMod.Groups["cost"].Value, card) is not { } c) return $"미지원 비용 (진화 코스트 감소: by {evoMod.Groups["cost"].Value})";
                evoPay = new PlayCostOption(reduceBy, c.CanPay, c.Pay, c.Recommend, c.Describe, evoMod.Groups["cost"].Value);
            }
            def?.EvoCostModifier(new EvoCostModifier(inherited ? EffectSource.Inherited : EffectSource.Main, subject, filter, into, evoCond, evoPay, reduceBy,
                OncePerTurn: evoLabels.Contains("[Once Per Turn]", StringComparison.OrdinalIgnoreCase),
                YourTurnOnly: evoLabels.Contains("[Your Turn]", StringComparison.OrdinalIgnoreCase)));
            return null;
        }
        // 비용을 내는 코스트 감소: "When this card would be played, by deleting 1 of your Digimon, reduce the play cost by 4."
        //  · "When you would play this card from your hand, you may delete 1 of your [Diaboromon] to reduce this card's play cost by 12."
        var paid = Regex.Match(Normalize(clause), @"^(?:\[[^\]]+\]\s*)*When (?:this card would be played(?: from (?:the|your) hand)?|you would play this (?:card|Digimon)(?: from (?:the|your) hand)?), (?:by (?<cost>[^,]+?), reduce|you may (?<verb>delete|trash|suspend|return|place)(?<obj> .+?) to reduce) (?:the|its|this card's) (?:play |memory )?cost by (?<n>\d+)\.?$", RegexOptions.IgnoreCase);
        if (paid.Success)
        {
            string costText = paid.Groups["cost"].Success ? paid.Groups["cost"].Value : $"{Gerund(paid.Groups["verb"].Value)}{paid.Groups["obj"].Value}";
            if (ParseCost(costText, card) is not { } payCost) return $"미지원 비용 (코스트 감소: by {costText})";
            def?.OptionalPlayCostReduction(new PlayCostOption(int.Parse(paid.Groups["n"].Value), payCost.CanPay, payCost.Pay, payCost.Recommend, payCost.Describe, costText));
            return null;
        }
        // 코스트 감소: "When this card would be played, if you have ~, reduce the play cost by 4."
        //  · "When you would play this card from your hand, reduce its play cost by 1 if you have a blue Digimon in play."
        //  · "~, reduce the play cost by 2 for each of your opponent's Digimon" · "~ by 4 for every 10 cards in your trash"
        var reduce = Regex.Match(Normalize(clause), @"^(?:\[[^\]]+\]\s*)*When (?:this card would be played(?: from (?:the|your) hand)?|you would play this (?:card|Digimon)(?: from (?:the|your) hand)?|you would use this card), (?:if (?<cond>[^,]+), )?reduce (?:the|its|this card's) (?:play |use |memory )?cost by (?<n>\d+)(?: for (?:each|every) (?<counter>.+?)| if (?<cond2>.+?))?\.?$", RegexOptions.IgnoreCase);
        if (reduce.Success)
        {
            int n = int.Parse(reduce.Groups["n"].Value);
            Func<PlayerZones, PlayerZones, bool> cond = (_, _) => true;
            string condText = reduce.Groups["cond"].Success ? reduce.Groups["cond"].Value : reduce.Groups["cond2"].Value;
            if (condText.Length > 0)
            {
                if (ParseZoneCondition(condText) is { } zc) cond = (z, _) => zc(z);
                else if (ParseSideCondition(condText) is { } sc) cond = sc;   // "if your opponent has ~" — 상대 필드도 본다
                else return $"미지원 조건 (코스트 감소: {condText})";
            }
            Func<PlayerZones, PlayerZones, int> amount = (_, _) => n;
            if (reduce.Groups["counter"].Success)
            {
                if (ParseCounter(reduce.Groups["counter"].Value) is not { } counter) return $"미지원 코스트 감소 셈: for each {reduce.Groups["counter"].Value}";
                amount = (z, e) => n * Math.Min(counter(z, e, null), 20);
            }
            def?.ReducePlayCost((z, e) => cond(z, e) ? amount(z, e) : 0);
            return null;
        }
        // 옵션 사용 조건: "While you have a Tamer, you can ignore this card's color requirements."
        var ignore = Regex.Match(clause, @"^While (?<cond>.+?), you can ignore this card's color requirements\.?$", RegexOptions.IgnoreCase);
        if (ignore.Success)
        {
            var zoneCond = ParseZoneCondition(ignore.Groups["cond"].Value);
            if (zoneCond == null) return $"미지원 조건 (색 조건 무시: {ignore.Groups["cond"].Value})";
            def?.IgnoreColorRequirementWhen(zoneCond);
            return null;
        }

        var prefix = Regex.Match(clause, @"^((?:\s*\[[^\]]+\])*)\s*(.*)$");
        var labels = Regex.Matches(prefix.Groups[1].Value, @"\[([^\]]+)\]").Select(m => m.Groups[1].Value.Trim()).ToList();
        string body = prefix.Groups[2].Value.Trim();

        var triggers = new List<EffectTrigger>();
        var qualifier = Qualifier.None;
        bool oncePerTurn = false, twicePerTurn = false;
        string? zoneLabel = null;   // [Hand] · [Trash] — 패 · 트래시에서 쓰는 효과
        foreach (var label in labels)
        {
            if (EventTriggers.TryGetValue(label, out var t)) triggers.Add(t);
            else if (label.Equals("Hand", StringComparison.OrdinalIgnoreCase) || label.Equals("Trash", StringComparison.OrdinalIgnoreCase)) zoneLabel ??= label;
            else if (label.Equals("End of All Turns", StringComparison.OrdinalIgnoreCase)) { triggers.Add(EffectTrigger.EndOfYourTurn); triggers.Add(EffectTrigger.EndOfOpponentsTurn); }
            else if (label.Equals("Your Turn", StringComparison.OrdinalIgnoreCase)) qualifier = Qualifier.YourTurn;
            else if (label.Equals("Opponent's Turn", StringComparison.OrdinalIgnoreCase)) qualifier = Qualifier.OpponentsTurn;
            else if (label.Equals("All Turns", StringComparison.OrdinalIgnoreCase)) qualifier = Qualifier.AllTurns;
            else if (label.Equals("Once Per Turn", StringComparison.OrdinalIgnoreCase)) oncePerTurn = true;
            else if (label.Equals("Twice Per Turn", StringComparison.OrdinalIgnoreCase)) twicePerTurn = true;
            else return $"미지원 시점 [{label}]";
        }
        // "[Hand] [Main] ~" · "[Trash] [Main] ~" — 패 · 트래시의 이 카드로 쓰는 [메인] 기동 효과 (그 밖의 패 · 트래시 시점은 아직 미지원)
        if (zoneLabel != null)
        {
            if (inherited || triggers.Count != 1 || triggers[0] != EffectTrigger.Main || qualifier != Qualifier.None)
                return $"미지원 시점 [{zoneLabel}]";
            triggers[0] = zoneLabel.Equals("Hand", StringComparison.OrdinalIgnoreCase) ? EffectTrigger.HandMain : EffectTrigger.TrashMain;
        }

        // "When this Digimon ~, ~" — 문장 안의 발동 시점
        Func<EffectContext, bool>? eventCondition = null;
        var when = Regex.Match(body, @"^When (?<ev>[^,]+), (?<rest>.+)$", RegexOptions.IgnoreCase);
        if (triggers.Count == 0 && when.Success)
        {
            var ev = ParseEventPhrase(when.Groups["ev"].Value);
            if (ev == null)
            {
                // "[Your Turn] When this Digimon has [Omnimon] in its name, it gets +2000 DP" — 사건이 아니라 상태: While로 읽는다
                if (ParseStaticCondition(when.Groups["ev"].Value) != null)
                {
                    var staticPending = new Pending();
                    if (ParseStatic($"While {when.Groups["ev"].Value}, {when.Groups["rest"].Value}", inherited, card, qualifier, staticPending) == null)
                    {
                        if (def != null) foreach (var r in staticPending.Registrations) r(def);
                        return null;
                    }
                }
                return "미지원 발동 조건 (When ~)";
            }
            triggers.AddRange(ev.Value.Triggers);
            eventCondition = ev.Value.Condition;
            body = when.Groups["rest"].Value;
            // 막기만 하는 "떠나려 할 때" 효과는 막을 수 없는 소멸(DP 0 · 코스트)에서는 발휘하지 않는다 (비용만 내게 되므로)
            if (triggers.Any(t => t is EffectTrigger.ThisDigimonWouldLeave or EffectTrigger.ThisDigimonWouldBeDeleted or EffectTrigger.AllyDigimonWouldLeave or EffectTrigger.AllyDigimonWouldBeDeleted)
                && Regex.IsMatch(body, @"\b(?:prevent|doesn't leave|does not leave|don't leave|isn't deleted|is not deleted|aren't deleted)\b", RegexOptions.IgnoreCase))
            {
                var baseCond = eventCondition;
                eventCondition = c => (baseCond == null || baseCond(c)) && c.Engine.CurrentLeave?.CanPrevent != false;
            }
        }
        else if (when.Success && ParseEventPhrase(when.Groups["ev"].Value) is { } same && same.Triggers.All(triggers.Contains))
        {
            // "[When Attacking] When this Digimon attacks a player, ~" — 같은 시점에 조건만 더한다
            eventCondition = same.Condition;
            body = when.Groups["rest"].Value;
        }

        // 링크 카드: 진화원 칸의 라벨 없는 문장 = 【링크 시】 효과 (링크된 디지몬 기준으로 이 카드의 효과로 처리)
        if (inherited && triggers.Count == 0 && labels.Count == 0 && card.InheritedEffectText.Contains("Link Requirements", StringComparison.OrdinalIgnoreCase))
        {
            triggers.Add(EffectTrigger.OnLinked);
            inherited = false;
        }

        var pending = new Pending();
        string? error = triggers.Count == 0
            ? ParseStatic(body, inherited, card, qualifier, pending)
            : ParseTriggered(body, inherited, card, triggers, qualifier, oncePerTurn, pending, eventCondition, twicePerTurn);
        if (error != null)
            return error;
        if (def != null)
            foreach (var r in pending.Registrations) r(def);
        return null;
    }

    /// <summary>
    /// 고르는 효과: "[Main] Activate 1 of the effects below:" + "・~" 줄들. 선택지마다 문장을 읽고,
    /// 처리할 때 플레이어에게 앞에서부터 물어 고르게 한다 (AI · 테스트는 첫 번째).
    /// </summary>
    public static string? TryParseChoice(string rawHeader, IReadOnlyList<string> rawOptions, bool inherited, CardData card, CardScriptDefinition? def)
    {
        string header = Normalize(rawHeader);
        var prefix = Regex.Match(header, @"^((?:\s*\[[^\]]+\])*)\s*(.*)$");
        var labels = Regex.Matches(prefix.Groups[1].Value, @"\[([^\]]+)\]").Select(m => m.Groups[1].Value.Trim()).ToList();
        var triggers = new List<EffectTrigger>();
        bool oncePerTurn = false, twicePerTurn = false;
        var qualifier = Qualifier.None;
        foreach (var label in labels)
        {
            if (EventTriggers.TryGetValue(label, out var t)) triggers.Add(t);
            else if (label.Equals("Once Per Turn", StringComparison.OrdinalIgnoreCase)) oncePerTurn = true;
            else if (label.Equals("Twice Per Turn", StringComparison.OrdinalIgnoreCase)) twicePerTurn = true;
            else if (label.Equals("Your Turn", StringComparison.OrdinalIgnoreCase)) qualifier = Qualifier.YourTurn;
            else if (label.Equals("Opponent's Turn", StringComparison.OrdinalIgnoreCase)) qualifier = Qualifier.OpponentsTurn;
            else if (label.Equals("All Turns", StringComparison.OrdinalIgnoreCase)) qualifier = Qualifier.AllTurns;
            else return $"미지원 시점 [{label}]";
        }
        // ≪딜레이≫ (옵션): 배틀 에어리어에 놓인 이 카드를 파기하는 것으로 아래 효과(・)를 모두 발휘 — 놓은 턴에는 쓸 수 없다.
        //   "[Main] ＜Delay＞" = [메인] 기동 효과 · "[All Turns] When ~, ＜Delay＞" = 그 사건에 발휘할 수 있다
        var delay = Regex.Match(prefix.Groups[2].Value, @"^(?:When (?<ev>[^,]+), )?(?:If (?<cond>[^,]+), )?＜Delay＞\.?$", RegexOptions.IgnoreCase);
        Func<EffectContext, bool>? delayEvent = null;
        if (delay.Success)
        {
            if (card.Type != CardType.Option || inherited) return "미지원: 옵션이 아닌 카드의 ≪딜레이≫";
            if (delay.Groups["ev"].Success)
            {
                if (ParseEventPhrase(delay.Groups["ev"].Value) is not { } ev) return $"미지원 발동 조건 (When {delay.Groups["ev"].Value})";
                triggers.AddRange(ev.Triggers.Where(t => !triggers.Contains(t)));
                delayEvent = ev.Condition;
            }
            for (int i = 0; i < triggers.Count; i++)
                if (triggers[i] == EffectTrigger.Main) triggers[i] = EffectTrigger.ActivateMain;
        }
        if (triggers.Count == 0) return "미지원: 시점 없는 고르는 효과";
        if (!delay.Success && !MapMainTrigger(triggers, card, inherited)) return "미지원: 진화원의 [메인] 효과";

        // ≪디지버스트 N≫: 이 디지몬의 진화원 N장을 파기하는 것으로 아래 효과(・)를 모두 발휘
        string head = prefix.Groups[2].Value;
        var burst = Regex.Match(head, @"^(?:If (?<cond>[^,]+), )?＜Digi-Burst (?<n>\d+)＞\.?$", RegexOptions.IgnoreCase);
        bool allOptions = burst.Success || delay.Success;
        // 모든 효과를 여러 번: "Activate the following effect twice." · "For each Digimon you have in play, activate the effect below."
        Func<EffectContext, int>? repeat = null;
        Func<EffectContext, bool>? allInstead = null;
        Match body;
        if ((body = Regex.Match(head, @"^activate the (?:following effect|effect below) (?<k>twice|\d+ times)[:.]?$", RegexOptions.IgnoreCase)).Success)
        {
            int k = body.Groups["k"].Value.Equals("twice", StringComparison.OrdinalIgnoreCase) ? 2 : int.Parse(Regex.Match(body.Groups["k"].Value, @"\d+").Value);
            allOptions = true; repeat = _ => k;
        }
        else if ((body = Regex.Match(head, @"^For (?:each|every) (?<counter>[^,]+), activate the (?:following effect|effect below)[:.]?$", RegexOptions.IgnoreCase)).Success)
        {
            if (ParseCounter(body.Groups["counter"].Value) is not { } counter) return $"미지원 셈 (For each {body.Groups["counter"].Value})";
            allOptions = true; repeat = c => Math.Min(counter(c.Owner, c.Enemy, c.Self), 20);
        }
        else if ((body = Regex.Match(head, @"^activate (?:1|one) of the effects below\. If (?<all>[^,]+), activate all of the effects below instead[:.]?$", RegexOptions.IgnoreCase)).Success)
        {
            // "Activate 1 of the effects below. If you have no other Digimon in play, activate all of the effects below instead."
            if ((allInstead = ParseEventCondition(body.Groups["all"].Value)) == null) return $"미지원 조건 (If {body.Groups["all"].Value})";
        }
        else body = burst.Success ? burst
            : delay.Success ? delay
            : Regex.Match(head, @"^(?:If (?<cond>[^,]+), )?(?:you may )?(?:By (?<cost>[^,]+), )?(?:you may )?activate (?:1|one) of the (?:effects below|following effects)[:.]?$", RegexOptions.IgnoreCase);
        if (!body.Success) return "문장 파서(3단계) 대상: 고르는 효과";
        Func<EffectContext, bool>? cond = null;
        if (body.Groups["cond"].Success && (cond = ParseEventCondition(body.Groups["cond"].Value)) == null)
            return $"미지원 조건 (If {body.Groups["cond"].Value})";
        if (delay.Success)
        {
            // 배틀 에어리어에 놓은 다음 턴 이후의 이 카드를 파기하는 것이 비용 — [메인]은 쓰는 것 자체가 선택, 사건은 묻는다
            bool activate = triggers.Contains(EffectTrigger.ActivateMain);
            var ev = delayEvent;
            var ifCond = cond;   // "If ~, ＜Delay＞" — 파기하기 전에 본다
            cond = c =>
            {
                if (ev != null && !ev(c)) return false;
                if (ifCond != null && !ifCond(c)) return false;
                if (c.Engine.ReadyPlacedOptions(c.Owner, c.Card) == 0) return false;
                if (!activate && !c.Engine.AskYesNo(c.Owner, null, $"[{c.Card.Name}] ≪딜레이≫ — 배틀 에어리어의 이 카드를 파기하고 효과를 발휘할까요?", recommended: true)) return false;
                return c.Engine.TrashPlacedOption(c.Owner, c.Card);
            };
        }
        string? costText = burst.Success ? $"trashing {burst.Groups["n"].Value} of this Digimon's digivolution cards" : body.Groups["cost"].Success ? body.Groups["cost"].Value : null;
        if (costText != null)
        {
            var cost = ParseCost(costText, card);
            if (cost == null) return $"미지원 비용 (By {costText})";
            var (canPay, pay, recommend, describe) = cost.Value;
            var baseCond = cond;
            cond = c =>
            {
                if (baseCond != null && !baseCond(c)) return false;
                if (!PayOrSkip(c, canPay, recommend, describe)) return false;
                pay(c);
                return true;
            };
        }

        var options = new List<Step[]>();
        foreach (var raw in rawOptions)
        {
            var steps = new List<Step>();
            foreach (var sentence in MergeRevealSentences(SplitSentences(Normalize(raw))))
            {
                var step = ParseStep(sentence, card, triggers);
                if (step.Error != null) return step.Error;
                if (step.Step != null) steps.Add(step.Step);
            }
            options.Add(steps.ToArray());
        }

        var optionTexts = rawOptions.Select(o => Normalize(o).TrimStart('・').Trim()).ToArray();
        // AI 추천: 문장 점수(AiEvaluator)의 합이 가장 큰 선택지 (같으면 앞의 것)
        var values = optionTexts.Select(t => SplitSentences(t).Sum(AiEvaluator.EstimateSentenceValue)).ToArray();
        int recommended = Array.IndexOf(values, values.Max());
        Action<EffectContext> resolve = c =>
        {
            if (cond != null && !cond(c)) return;
            if (allOptions || allInstead?.Invoke(c) == true)
            {
                int times = repeat?.Invoke(c) ?? 1;
                for (int i = 0; i < times; i++)
                    foreach (var option in options)
                        foreach (var s in option) RunStep(s, c);
                return;
            }
            int pick = c.Engine.ChooseOption(c.Owner, $"[{c.Card.Name}] 발휘할 효과를 1개 고르세요.", optionTexts, recommended);
            foreach (var s in options[pick])
                RunStep(s, c);
        };
        Func<EffectContext, bool>? when = qualifier switch
        {
            Qualifier.YourTurn => c => c.Engine.IsTurnOf(c.Owner),
            Qualifier.OpponentsTurn => c => !c.Engine.IsTurnOf(c.Owner),
            _ => null,
        };
        foreach (var t in triggers)
        {
            var tr = t;
            if (def == null) continue;
            if (tr == EffectTrigger.Main) def.Main(TargetKind.None, resolve);
            else if (inherited) def.Inherited(tr, resolve, when, oncePerTurn, twicePerTurn);
            else def.On(tr, resolve, when, oncePerTurn, twicePerTurn);
        }
        return null;
    }

    // 옵션 [메인] 효과의 대상 구절 후보 ("Delete 1 of your opponent's Digimon with 4000 DP or less" · "1 of your Digimon gets +2000 DP")
    private static readonly Regex OptionTargetPhrase = new(
        @"(?<t>(?:up to )?1 of your (?:opponent's )?(?:[^,.]*? )?Digimon(?: with [^,.]+?)?)(?= gets?\b| gains?\b| to\b| can't\b| cannot\b| doesn't\b| isn't\b| and\b| for\b| without\b| into\b| as\b| at\b| on\b|,|\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 옵션 [메인]의 대상: 처음 나오는 "1 of your (opponent's) ~ Digimon"의 쪽 · 조건 (레스트 · 레벨 · DP · 특징 …).
    /// 대상이 1마리가 아니거나(전부 · 여러 마리 · 양쪽) 읽지 못하면 대상 없음 — 엔진이 그대로 고른다
    /// </summary>
    private static (TargetKind Kind, Func<CardInstance, bool>? Filter, string? Rule) OptionMainTarget(string body)
    {
        foreach (Match m in OptionTargetPhrase.Matches(Normalize(body)))
        {
            string phrase = m.Groups["t"].Value;
            if (TargetSpec.Parse(phrase) is not { } spec || spec.IsThis || spec.IsThat || spec.AnySide || spec.Count != 1)
                continue;
            var kind = spec.Opponent ? TargetKind.OpponentDigimon : TargetKind.OwnDigimon;
            if (spec.Filters.Count == 0)
                return (kind, null, null);
            var filters = spec.Filters.ToList();
            string rule = Regex.Replace(KoreanEffectRenderer.TargetPhraseKo(phrase) ?? phrase, @"\s*1마리$", "");
            return (kind, d => filters.All(f => f(d)), rule);
        }
        return (TargetKind.None, null, null);
    }

    /// <summary>한글 생성(KoreanEffectRenderer)이 같은 정리 · 문장 나누기를 쓰도록</summary>
    internal static string NormalizeForRender(string clause) => Normalize(clause);
    internal static List<string> SentencesForRender(string body) => MergeRevealSentences(SplitSentences(body));

    /// <summary>설명문 괄호 · 라벨("Security Effect", "Inherited Effect", "・") · 특수 공백 정리 (키워드 ＜＞ 안의 괄호는 보존)</summary>
    private static string Normalize(string clause)
    {
        string s = clause.Replace(" ", " ").Replace("’", "'").Replace("‘", "'").Trim();
        s = Regex.Replace(s, @"^(?:Security Effect|Inherited Effect|・)\s*", "", RegexOptions.IgnoreCase);
        // 초기 세트(BT1 ~)의 옛 표기 · 오타를 지금 표기로
        s = Regex.Replace(s, @"\brecycle bin\b", "trash", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bhas has\b", "has", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bor below\b", "or lower", RegexOptions.IgnoreCase);
        // "If you don't have an [Arata Sanada] card in play, you may play one from your hand" — "one" = 앞의 그 카드
        s = Regex.Replace(s, @"(?<head>If you don't have an? \[(?<x>[^\]]+)\](?: card)? in play, you may play) one from", "${head} 1 [${x}] from", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bor above\b", "or higher", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bopponents Digimon\b", "opponent's Digimon", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bwithout paying (the|their|its) costs\b", "without paying $1 cost", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(?<=[.＞]\s*)When you do, ", "Then, ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(?<=\]\s*)When DNA digivolving, ", "If DNA digivolving, ", RegexOptions.IgnoreCase);   // "[When Digivolving] When DNA digivolving, ~"
        s = Regex.Replace(s, @"\bAny \[On Play\] effects on the Digimon played\b", "Any [On Play] effects on Digimon played", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(?<=\.\s*)\[On Play\] effects of (?:Tamers|Digimon|cards) played by this effect don't activate", "Any [On Play] effects on cards played by this effect don't activate", RegexOptions.IgnoreCase);
        // "During your opponent's next unsuspend phase, none of your opponent's Digimon can unsuspend" → "~ don't unsuspend during ~"
        s = Regex.Replace(s, @"During (?<who>your opponent's|their) next unsuspend phase, none of (?<t>[^.]+?) can unsuspend", "All of ${t} don't unsuspend during ${who} next unsuspend phase", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\byou can (?!ignore)", "you may ", RegexOptions.IgnoreCase);
        // 시큐리티 장수 조건의 다른 표기 → "you have N or fewer security cards"
        //   "3 of fewer"(원문 오타) · "N or fewer cards in your security stack" · "N security cards or fewer"
        s = Regex.Replace(s, @"\b(\d+) of (fewer|less|more)\b", "$1 or $2", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bthere're\b", "there are", RegexOptions.IgnoreCase);
        // "To 1 of your opponent's Digimon, give -3000 DP until their turn ends" → "1 of your opponent's Digimon gets -3000 DP until their turn ends"
        s = Regex.Replace(s, @"\bTo (?<t>(?:\d+|all) of (?:your|your opponent's|their) [^,]*?Digimon), give (?<dp>[+-]\d+ DP)\b", "${t} gets ${dp}", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bbatttle\b", "battle", RegexOptions.IgnoreCase);   // 원문 오타
        s = Regex.Replace(s, @"\b(rest|cards) the the (bottom|top)\b", "$1 to the $2", RegexOptions.IgnoreCase);   // 원문 오타 "Return the rest the the bottom"
        s = Regex.Replace(s, @"\bBy Reveal\b", "Reveal");   // 원문 오타 "By Reveal the top 4 cards"
        s = Regex.Replace(s, @"\bthe Digimon (?:this|this card|it) was placed under\b", "that Digimon", RegexOptions.IgnoreCase);   // "attack ~ with the Digimon this was placed under"
        // 어택: "you may attack with this Digimon without suspending it" → "this Digimon may attack without suspending"
        s = Regex.Replace(s, @"\byou may attack with (?<t>this Digimon|it|that Digimon)(?<nosus> without suspending(?: it)?)?(?=[.,]|$)", "${t} may attack${nosus}", RegexOptions.IgnoreCase);
        // 진화: "you may digivolve this Digimon into ~" → "this Digimon may digivolve into ~" (주어를 앞으로)
        s = Regex.Replace(s, @"\b(?:you may )?digivolve (?<t>this Digimon|\d+ of your (?:other )?[^,.]*?Digimon(?: with [^,.]+?)?) into\b", "${t} may digivolve into", RegexOptions.IgnoreCase);
        // "~ for its digivolution cost. When it would digivolve by this effect, reduce the cost by 2." → "~ for its digivolution cost with the digivolution cost reduced by 2."
        s = Regex.Replace(s, @"(?<head>for its digivolution cost)\.\s*When (?:digivolving|(?:it|this Digimon|that Digimon) would digivolve) (?:by|with|using) this effect, (?:reduce|yellowuce) the (?:digivolution )?cost by (?<n>\d+)\.?", "${head} with the digivolution cost reduced by ${n}.", RegexOptions.IgnoreCase);   // "yellowuce" = 원문 오타
        s = Regex.Replace(s, @"\[(?<a>[^\]]+)\] trait or \[(?<b>[^\]]+)\] trait\b", "[${a}] or [${b}] trait");   // "the [A] trait or [B] trait" = "the [A] or [B] trait"
        s = Regex.Replace(s, @"\btrait trait\b", "trait", RegexOptions.IgnoreCase);   // 원문 오타
        // "a multicolored Digimon that is purple or yellow" · "multicolor Blue or Red Digimon cards" → "a multicolored purple or yellow Digimon card"
        s = Regex.Replace(s, $@"\b(?<n>an?|1) multicolou?r(?:ed)? Digimon(?: card)? that is (?<c>(?:{Colors})(?: or (?:{Colors}))*)\b", "${n} multicolored ${c} Digimon card", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, $@"\bmulticolou?r(?:ed)? (?<c>(?:{Colors})(?: or (?:{Colors}))*) Digimon cards\b", "a multicolored ${c} Digimon card", RegexOptions.IgnoreCase);
        // "Add 1 Digimon card and 1 Tamer card―both with the [Xros Heart] trait―among them" → 조건을 양쪽에 붙인다
        s = Regex.Replace(s, @"\b(?<a>\d+ (?:Digimon|Tamer|Option) cards?) and (?<b>\d+ (?:Digimon|Tamer|Option) cards?)\s*[―—-]\s*both (?<w>with [^―—]+?)\s*[―—-]\s*", "${a} ${w} and ${b} ${w} ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\byou have (\d+) or (fewer|less|more) cards in your security stack\b", "you have $1 or $2 security cards", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\byou have (\d+) security cards or (fewer|less|more)\b", "you have $1 or $2 security cards", RegexOptions.IgnoreCase);
        // ＜Recovery +1 (Deck)＞ 같은 키워드 안 괄호를 잠시 바꿔 두고, 설명문 괄호를 지운다
        s = Regex.Replace(s, @"[＜<]([^＞>]*)[＞>]", m => "＜" + m.Groups[1].Value.Replace("(", "⟮").Replace(")", "⟯") + "＞");
        s = Regex.Replace(s, @"\s*\((?:[^()]|\([^()]*\))*\)", "");
        s = s.Replace("⟮", "(").Replace("⟯", ")");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Regex.Replace(s, @"\s+([.,])", "$1");
        return s;
    }

    /// <summary>"When ~," 문장 안의 발동 시점 → 엔진 시점 + 추가 조건 (예: "your other Digimon" = 자신이 아닌 디지몬)</summary>
    private static (List<EffectTrigger> Triggers, Func<EffectContext, bool>? Condition)? ParseEventPhrase(string ev)
    {
        ev = ev.Trim();
        Match m;
        (List<EffectTrigger>, Func<EffectContext, bool>?) One(EffectTrigger t, Func<EffectContext, bool>? cond = null) => (new List<EffectTrigger> { t }, cond);

        if (Regex.IsMatch(ev, @"^this Digimon deletes (?:an|1 of your|your) opponent's Digimon in battle(?: and survives)?$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.DeletedOpponentInBattle);
        if (Regex.IsMatch(ev, @"^this Digimon (?:is|becomes|gets) blocked$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AttackBlocked);
        if (Regex.IsMatch(ev, @"^this Digimon attacks$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.WhenAttacking);
        if (Regex.IsMatch(ev, @"^this Digimon attacks (?:a player|your opponent|the opponent)$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.WhenAttacking, c => c.Other == null);
        if (Regex.IsMatch(ev, @"^this Digimon attacks (?:a|an opponent's|your opponent's) Digimon$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.WhenAttacking, c => c.Other != null);
        if (Regex.IsMatch(ev, @"^this Digimon wins a battle$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.WonBattle);
        if (Regex.IsMatch(ev, @"^this Digimon (?:becomes unsuspended|unsuspends) during your main phase$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.ThisDigimonUnsuspendedInMain);
        if (Regex.IsMatch(ev, @"^this Digimon (?:becomes unsuspended|unsuspends) during your unsuspend phase$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.ThisDigimonUnsuspendedInUnsuspendPhase);
        // "When this Digimon (with [X] in its name) would leave the battle area other than by your effects" · "When one of your [Diaboromon] would be deleted in battle"
        if ((m = Regex.Match(ev, $@"^(?:(?<self>this Digimon)(?<selfwith> with [^,]+?)?|(?:one of |any of )?your (?<subj>(?:other )?[^,]*?(?:Digimon|\]))(?<with> with [^,]+?)?) would (?<what>leave the battle area|be deleted)(?<q>.*)$", RegexOptions.IgnoreCase)).Success
            && ParseLeaveQualifier(m.Groups["q"].Value) is { } leaveOk)
        {
            bool deletion = m.Groups["what"].Value.StartsWith("be deleted", StringComparison.OrdinalIgnoreCase);
            if (m.Groups["self"].Success)
            {
                // "this Digimon with [Gallantmon] in its name" · "this Digimon with the [Hybrid] or [Ten Warriors] trait"
                TargetSpec? selfSpec = m.Groups["selfwith"].Success ? TargetSpec.Parse("all of your Digimon" + m.Groups["selfwith"].Value) : null;
                if (m.Groups["selfwith"].Success && selfSpec == null) return null;
                return One(deletion ? EffectTrigger.ThisDigimonWouldBeDeleted : EffectTrigger.ThisDigimonWouldLeave,
                    c => c.Engine.CurrentLeave is { } l && ReferenceEquals(l.Digimon, c.Self) && leaveOk(l)
                         && (selfSpec == null || selfSpec.Filters.All(f => f(c.Self!))));
            }
            string subj = m.Groups["subj"].Value + m.Groups["with"].Value;
            Func<EffectContext, CardInstance, bool>? who = null;
            var named = Regex.Match(subj, $@"^(?<other>other )?(?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase);
            if (named.Success)
            {
                var xs = TargetSpec.Names(named.Groups["x"].Value); bool other = named.Groups["other"].Success;
                who = (c, d) => (!other || !ReferenceEquals(d, c.Self)) && xs.Any(x => TargetSpec.NameHas(d.TopCard, x));
            }
            else who = ParseAllySubject(subj);
            if (who != null)
                return (new List<EffectTrigger> { deletion ? EffectTrigger.AllyDigimonWouldBeDeleted : EffectTrigger.AllyDigimonWouldLeave,
                                                  deletion ? EffectTrigger.ThisDigimonWouldBeDeleted : EffectTrigger.ThisDigimonWouldLeave },
                    c => c.Engine.CurrentLeave is { } l && leaveOk(l)
                         && (ReferenceEquals(l.Digimon, c.Other) || ReferenceEquals(l.Digimon, c.Self)) && who(c, l.Digimon));
        }
        if (Regex.IsMatch(ev, @"^this Digimon (?:gets|is|becomes) linked$|^a card (?:is linked|links) to this Digimon$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.ThisDigimonLinked);
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?!opponent)(?<subj>(?:other )?[^,]*?Digimon(?: with [^,]+?)?) (?:get|gets|are|is|become|becomes) linked$", RegexOptions.IgnoreCase)).Success
            && ParseAllySubject(m.Groups["subj"].Value) is { } linkedWho)
            return One(EffectTrigger.AllyDigimonLinked, c => c.Other != null && linkedWho(c, c.Other));
        // 육성: "When one of your Digimon with [Agumon] in its name moves from the breeding area to the battle area" · "When you hatch in the breeding area"
        const string movedFromBreeding = @"(?:is |are )?(?:moves?|moved) from (?:your|their|the) breeding area(?: to (?:your|their|the) battle area)?";
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?!opponent)(?<subj>(?:other )?[^,]*?Digimon(?: with [^,]+?)?) {movedFromBreeding}$", RegexOptions.IgnoreCase)).Success
            && ParseAllySubject(Regex.Replace(m.Groups["subj"].Value, @"\bin their names\b", "in its name", RegexOptions.IgnoreCase)) is { } movedWho)
            return One(EffectTrigger.AllyDigimonMovedFromBreeding, c => c.Other != null && movedWho(c, c.Other));
        if ((m = Regex.Match(ev, $@"^(?:an|one of your|any of your) opponent's (?<spec>[^,]*?)Digimon {movedFromBreeding}$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your opponent's " + m.Groups["spec"].Value + "Digimon") is { } oppMoved)
            return One(EffectTrigger.OpponentDigimonMovedFromBreeding, c => c.Other != null && oppMoved.Filters.All(f => f(c.Other)));
        if (Regex.IsMatch(ev, @"^your opponent moves a Digimon from their breeding area(?: to their battle area)?$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentDigimonMovedFromBreeding);
        if (Regex.IsMatch(ev, @"^you hatch (?:a Digi-Egg )?in (?:the|your) breeding area$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyHatched);
        // DNA 진화: "When this Digimon would DNA digivolve (into a green Digimon card)"
        if ((m = Regex.Match(ev, @"^this Digimon would DNA digivolve(?: into (?<pick>.+))?$", RegexOptions.IgnoreCase)).Success)
        {
            if (!m.Groups["pick"].Success) return One(EffectTrigger.ThisDigimonWouldDnaDigivolve);
            if ((ParseCardPick(Regex.Replace(m.Groups["pick"].Value, @"^an? ", "1 ")) ?? ParseNamePick("1 " + m.Groups["pick"].Value)) is not { } dnaPick) return null;
            return One(EffectTrigger.ThisDigimonWouldDnaDigivolve, c => c.TriggerCard != null && dnaPick.Match(c.TriggerCard));
        }
        // 시큐리티에서 카드가 빠졌을 때 / 놓였을 때
        //   "When your security stack is removed from" · "When a card is removed from your security stack" · "When cards are trashed from your security stack"
        if (Regex.IsMatch(ev, @"^(?:your security stack is removed from|(?:a card is|cards are|one or more cards are) (?:removed|trashed) from your security stack|your security cards? (?:is|are) (?:removed|trashed))$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllySecurityRemoved);
        if (Regex.IsMatch(ev, @"^(?:your opponent's security stack is removed from|(?:a card is|cards are|one or more cards are) (?:removed|trashed) from your opponent's security stack|your opponent's security cards? (?:is|are) (?:removed|trashed))$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentSecurityRemoved);
        if (Regex.IsMatch(ev, @"^(?:security stacks are removed from|any security stack is removed from|(?:a card is|cards are) removed from (?:a|any) (?:player's )?security stack)$", RegexOptions.IgnoreCase))
            return (new List<EffectTrigger> { EffectTrigger.AllySecurityRemoved, EffectTrigger.OpponentSecurityRemoved }, null);
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) (?:added to|placed in|placed on top of|placed at the top of) your security stack$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllySecurityAdded);

        // 앞면 시큐리티: "When this Digimon checks a face-up security card" · "When any of your Digimon check face-up security cards"
        if (Regex.IsMatch(ev, @"^this Digimon checks (?:a )?face[- ]up security cards?$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyCheckedFaceUpSecurity, c => ReferenceEquals(c.Other, c.Self));
        if (Regex.IsMatch(ev, @"^(?:any of |one of )?your Digimon checks? (?:a )?face[- ]up security cards?$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyCheckedFaceUpSecurity);
        // "When cards are placed face up in your security stack" · "When face-up [Royal Base] trait cards are placed in your security stack"
        if ((m = Regex.Match(ev, $@"^(?:face[- ]up (?:(?<x>{TargetSpec.NameList}) (?:trait )?)?cards? (?:are|is) (?:placed|added) (?:in|to)|cards? (?:are|is) placed face[- ]up in) your (?<opp>opponent's )?security stack$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = m.Groups["x"].Success ? TargetSpec.Names(m.Groups["x"].Value) : null;
            return One(m.Groups["opp"].Success ? EffectTrigger.OpponentFaceUpSecurityPlaced : EffectTrigger.AllyFaceUpSecurityPlaced,
                xs == null ? null : c => c.TriggerCard != null && xs.Any(x => TargetSpec.HasTrait(c.TriggerCard, x) || TargetSpec.NameHas(c.TriggerCard, x)));
        }
        if (Regex.IsMatch(ev, @"^face[- ]up cards? (?:are|is) (?:placed|added) (?:in|to) your opponent's security stack$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentFaceUpSecurityPlaced);
        if (Regex.IsMatch(ev, @"^this card is trashed (?:due to|by) activating this Digimon's ＜Digi-Burst＞$|^activating this Digimon's ＜Digi-Burst＞ trashes this card$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.DigiBurstTrashedThis);
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your Digimon activates ＜Digi-Burst＞$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyDigiBurst);
        // "When you attack one of your opponent's Digimon (with the highest DP)" · "When this Digimon attacks one of your opponent's Digimon with 12000 DP or more"
        if ((m = Regex.Match(ev, @"^(?:you attack|this Digimon attacks) (?:one of |1 of |an? )?(?:your )?opponent's (?<spec>[^,]*?Digimon(?: with [^,]+)?)$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your opponent's " + Regex.Replace(m.Groups["spec"].Value, @"^opponent's ", "")) is { } atkSpec)
            return One(EffectTrigger.WhenAttacking, c => c.Other != null && atkSpec.Filters.All(f => f(c.Other)) && atkSpec.ContextFilters.All(f => f(c, c.Other))
                                                         && (atkSpec.Selector == null || atkSpec.Selector(c.Enemy.BattleArea).Contains(c.Other)));
        // "When this Digimon deletes one of your opponent's level 6 or higher Digimon in battle (and survives)"
        if ((m = Regex.Match(ev, @"^this Digimon deletes (?:one of |1 of |an? )?(?:your )?opponent's (?<spec>[^,]*?Digimon(?: with [^,]+?)?) in battle(?: and survives)?$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your opponent's " + m.Groups["spec"].Value) is { } delSpec)
            return One(EffectTrigger.DeletedOpponentInBattle, c => c.Other == null || delSpec.Filters.All(f => f(c.Other)));
        if (Regex.IsMatch(ev, @"^you play another Digimon with the same name as this Digimon$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllySameNamePlayed, c => c.Other != null && c.Self != null && CardNames.Same(c.Self.TopCard, c.Other.TopCard));
        if ((m = Regex.Match(ev, @"^(?:an|one of your) opponent's (?<spec>[^,]*?)Digimon (?:is|are) played$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your opponent's " + m.Groups["spec"].Value + "Digimon") is { } oppPlayed)
            return One(EffectTrigger.OpponentDigimonPlayed, c => c.Other != null && oppPlayed.Filters.All(f => f(c.Other)));
        if ((m = Regex.Match(ev, @"^(?:another|a|an) (?<spec>(?![^,]*opponent)[^,]*?)Digimon (?:is|are) (?<what>played|deleted)$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your " + m.Groups["spec"].Value + "Digimon") is { } anySpec)
        {
            // "When a level 4 or lower Digimon is played" · "When another Digimon is deleted" — 양쪽 디지몬
            bool other = ev.StartsWith("another", StringComparison.OrdinalIgnoreCase);
            bool isPlayed = m.Groups["what"].Value.Equals("played", StringComparison.OrdinalIgnoreCase);
            Func<EffectContext, bool> cond = c => c.Other != null && (!other || !ReferenceEquals(c.Other, c.Self)) && anySpec.Filters.All(f => f(c.Other));
            return (isPlayed
                ? new List<EffectTrigger> { EffectTrigger.AllyDigimonPlayed, EffectTrigger.OpponentDigimonPlayed }
                : new List<EffectTrigger> { EffectTrigger.AllyDigimonDeleted, EffectTrigger.OpponentDigimonDeleted }, cond);
        }
        if (Regex.IsMatch(ev, @"^(?:an|your) opponent plays a Tamer$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentTamerPlayed);
        if (Regex.IsMatch(ev, @"^(?:an|one of your) opponent's Tamers? (?:becomes suspended|suspends|is suspended)$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentTamerSuspended);
        // 어택 대상이 바뀌었을 때: "When an attack target is switched" · "When attack targets change" · "When this Digimon's attack target is switched"
        if (Regex.IsMatch(ev, @"^(?:an|the) attack targets? (?:is|are) (?:switched|changed)$|^attack targets? (?:change|changes|switch|switches)$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AttackTargetSwitched);
        if (Regex.IsMatch(ev, @"^this Digimon's attack target (?:is switched|is changed|changes|switches)$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyAttackTargetSwitched, c => c.Self != null && ReferenceEquals(c.Other, c.Self));
        if (Regex.IsMatch(ev, @"^(?:one|any) of your Digimon's attack targets? (?:is|are) (?:switched|changed)$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyAttackTargetSwitched);
        if (Regex.IsMatch(ev, @"^you use an Option card$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyOptionUsed);
        // "When you use an Option card with a cost of 3 or more" · "When you use [TS] trait Option cards" — 사용한 옵션의 조건
        if ((m = Regex.Match(ev, @"^you use (?:an? |1 )?(?<pre>[^,]*?)Option cards?(?<with> with .+)?$", RegexOptions.IgnoreCase)).Success)
        {
            string with = Regex.Replace(m.Groups["with"].Value, @"\ba (?:use |memory )?cost (?:of )?(?=\d)", "a cost of ", RegexOptions.IgnoreCase);
            if (ParseCardPick($"1 {m.Groups["pre"].Value}Option card{with}") is { } optPick)
            {
                var match = optPick.Match;
                return One(EffectTrigger.AllyOptionUsed, c => c.TriggerCard != null && match(c.TriggerCard));
            }
        }
        // 패 · 덱에서 버려졌을 때: "When your hand is trashed from" · "When one of your effects trashes a card in your hand" · "When a card is trashed from your deck"
        if (Regex.IsMatch(ev, @"^your hand is trashed from$|^one of your effects trashes (?:a card|cards) in your hand$|^you trash (?:a card|cards) in your hand using one of your effects$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyHandTrashed);
        if (Regex.IsMatch(ev, @"^your opponent's hand is trashed from$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentHandTrashed);
        if (Regex.IsMatch(ev, @"^(?:a card is|cards are) trashed from your deck$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyDeckTrashed);
        if (Regex.IsMatch(ev, @"^an effect adds (?:a card|cards) to your opponent's hand$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentHandAddedByEffect);
        // 효과로 등장 · 레스트: "When an effect plays one of your Digimon" · "When an effect suspends one of your Digimon" · "When any Digimon suspend"
        if (Regex.IsMatch(ev, @"^an effect plays (?:one of your|a) Digimon$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyDigimonPlayed, c => c.Engine.PlayingByEffect);
        if (Regex.IsMatch(ev, @"^an effect plays (?:an|one of your) opponent's Digimon$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentDigimonPlayed, c => c.Engine.PlayingByEffect);
        if (Regex.IsMatch(ev, @"^an effect suspends one of your Digimon$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyDigimonSuspended, c => !c.Engine.SuspendingForAttack && !c.Engine.SuspendingForBlock);
        if (Regex.IsMatch(ev, @"^an effect suspends this Digimon$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.ThisDigimonSuspends, c => !c.Engine.SuspendingForAttack && !c.Engine.SuspendingForBlock);
        if (Regex.IsMatch(ev, @"^any Digimon (?:suspend|suspends|becomes? suspended)$", RegexOptions.IgnoreCase))
            return (new List<EffectTrigger> { EffectTrigger.AllyDigimonSuspended, EffectTrigger.OpponentDigimonSuspended }, null);
        if (Regex.IsMatch(ev, @"^your opponent uses an Option card$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentOptionUsed);
        if (Regex.IsMatch(ev, @"^(?:a|any) player uses an Option card$", RegexOptions.IgnoreCase))
            return (new List<EffectTrigger> { EffectTrigger.AllyOptionUsed, EffectTrigger.OpponentOptionUsed }, null);
        if (Regex.IsMatch(ev, @"^(?:an|one of your|any of your) opponent's Digimon attacks (?:a player|you)$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentDigimonAttacks, c => c.TriggerCard == null);
        // "When one of your opponent's level 5 or lower Digimon is deleted" · "When one of your opponent's Digimon attacks" — 상대 디지몬이 주어.
        // 아래 "your ~ Digimon"(자신의 디지몬) 규칙보다 먼저 본다 ("one of your opponent's"도 "your"로 시작해서 자신의 디지몬 시점으로 잘못 붙었다 — BT8-094 · BT22-091)
        if ((m = Regex.Match(ev, @"^(?:an|one of your|any of your) opponent's (?<spec>[^,]*?Digimon(?: with [^,]+?)?) (?<what>attacks|is deleted|are deleted)$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your opponent's " + m.Groups["spec"].Value) is { } enemySubject)
        {
            bool attacks = m.Groups["what"].Value.Equals("attacks", StringComparison.OrdinalIgnoreCase);
            return One(attacks ? EffectTrigger.OpponentDigimonAttacks : EffectTrigger.OpponentDigimonDeleted,
                c => c.Other != null && enemySubject.Filters.All(f => f(c.Other)) && enemySubject.ContextFilters.All(f => f(c, c.Other)));
        }
        // "When one of your blue Digimon attacks (a player)" · "When you play a level 3 yellow Digimon" · "When you play another Digimon"
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<subj>(?:other )?[^,]*?Digimon(?: with [^,]+?)?) attacks(?<p> a player| your opponent)?$", RegexOptions.IgnoreCase)).Success
            && ParseAllySubject(m.Groups["subj"].Value) is { } attacker)
        {
            bool player = m.Groups["p"].Success;
            return One(EffectTrigger.AllyDigimonAttacks, c => c.Other != null && attacker(c, c.Other) && (!player || c.TriggerCard == null));
        }
        if ((m = Regex.Match(ev, @"^you play (?<subj>(?:an?|another|1) [^,]*?Digimon(?: with [^,]+?)?)$", RegexOptions.IgnoreCase)).Success
            && ParseAllySubject(m.Groups["subj"].Value) is { } played)
            return One(EffectTrigger.AllyDigimonPlayed, c => c.Other != null && played(c, c.Other));
        if ((m = Regex.Match(ev, @"^your opponent plays (?<subj>(?:an?|1) [^,]*?Digimon(?: with [^,]+?)?)$", RegexOptions.IgnoreCase)).Success
            && ParseAllySubject(m.Groups["subj"].Value) is { } enemyPlayed)
            return One(EffectTrigger.OpponentDigimonPlayed, c => c.Other != null && enemyPlayed(c, c.Other));
        if ((m = Regex.Match(ev, @"^(?:one of |any of )?your (?<subj>(?:other )?[^,]*?Digimon(?: with [^,]+?)?) (?:is|are) deleted$", RegexOptions.IgnoreCase)).Success
            && ParseAllySubject(m.Groups["subj"].Value) is { } deleted)
            return One(EffectTrigger.AllyDigimonDeleted, c => c.Other == null || deleted(c, c.Other));
        // "When one of your [Myotismon] is deleted" · "When one of your [Galacticmon] attacks" — 이름만 있는 주어
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<other>other )?(?<x>{TargetSpec.NameList}) (?<what>is deleted|are deleted|attacks?)$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            bool other = m.Groups["other"].Success;
            Func<EffectContext, bool> named = c => c.Other != null && (!other || !ReferenceEquals(c.Other, c.Self)) && xs.Any(x => TargetSpec.NameHas(c.Other.TopCard, x));
            return One(m.Groups["what"].Value.StartsWith("attack", StringComparison.OrdinalIgnoreCase) ? EffectTrigger.AllyDigimonAttacks : EffectTrigger.AllyDigimonDeleted, named);
        }
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?:(?<color>(?:{Colors})(?: or (?:{Colors}))*) )?Tamers? (?:becomes suspended|suspends|is suspended|become suspended)$", RegexOptions.IgnoreCase)).Success)
        {
            var colors = Regex.Matches(m.Groups["color"].Value, Colors, RegexOptions.IgnoreCase).Select(x => Enum.Parse<CardColor>(x.Value, ignoreCase: true)).ToList();
            return One(EffectTrigger.AllyTamerSuspended, c => colors.Count == 0 || (c.TriggerCard?.AllColors.Any(colors.Contains) ?? false));
        }
        if (Regex.IsMatch(ev, @"^(?:an|one of your|any of your) opponent's Digimon (?:becomes suspended|suspends|is suspended)$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentDigimonSuspended);
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<other>other )?(?:(?<names>{TargetSpec.NameList}) trait )?Digimon(?: with (?<inname>{TargetSpec.NameList}) in (?:its|their) names?)? (?<what>attacks?|suspends?|becomes suspended|become suspended)$", RegexOptions.IgnoreCase)).Success)
        {
            bool other = m.Groups["other"].Success;
            var traits = m.Groups["names"].Success ? TargetSpec.Names(m.Groups["names"].Value) : null;
            var names = m.Groups["inname"].Success ? TargetSpec.Names(m.Groups["inname"].Value) : null;
            var trig = m.Groups["what"].Value.StartsWith("attack", StringComparison.OrdinalIgnoreCase) ? EffectTrigger.AllyDigimonAttacks : EffectTrigger.AllyDigimonSuspended;
            return One(trig, c => c.Other != null
                && (!other || !ReferenceEquals(c.Other, c.Self))
                && (traits == null || traits.Any(x => TargetSpec.HasTrait(c.Other.TopCard, x)))
                && (names == null || names.Any(x => TargetSpec.NameHas(c.Other.TopCard, x))));
        }
        if (Regex.IsMatch(ev, @"^this Digimon (?:suspends|becomes suspended|is suspended)$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.ThisDigimonSuspends);
        if (Regex.IsMatch(ev, @"^(?:an|one of your|any of your) opponent's Digimon attacks?$|^your opponent's Digimon attacks?$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentDigimonAttacks);
        if (Regex.IsMatch(ev, @"^(?:an|one of your|any of your) opponent's Digimon (?:is|are) deleted$|^your opponent's Digimon (?:is|are) deleted$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentDigimonDeleted);
        if (Regex.IsMatch(ev, @"^(?:an|one of your|any of your) opponent's Digimon (?:is|are) deleted by (?:dropping|being reduced) to 0 DP$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.OpponentDigimonDeleted, c => c.Engine.CurrentDeletionCause == GoldfishEngine.DeletionCause.ZeroDp);
        if (Regex.IsMatch(ev, @"^(?:one of |any of )?your other Digimon (?:is|are) deleted$", RegexOptions.IgnoreCase))
            return One(EffectTrigger.AllyDigimonDeleted);

        // "your (other) ([X] trait) Digimon are played (or digivolve)"
        if ((m = Regex.Match(ev, $@"^(?:one of |any of )?your (?<other>other )?(?:(?<names>{TargetSpec.NameList}) trait )?Digimon(?: with (?<inname>{TargetSpec.NameList}) in (?:its|their) names?)? (?:is |are )?(?<what>played or digivolves?|played|digivolves?|digivolved)$", RegexOptions.IgnoreCase)).Success)
        {
            bool other = m.Groups["other"].Success;
            var traits = m.Groups["names"].Success ? TargetSpec.Names(m.Groups["names"].Value) : null;
            var names = m.Groups["inname"].Success ? TargetSpec.Names(m.Groups["inname"].Value) : null;
            string what = m.Groups["what"].Value.ToLowerInvariant();
            var triggers = new List<EffectTrigger>();
            if (what.Contains("played")) triggers.Add(EffectTrigger.AllyDigimonPlayed);
            if (what.Contains("digivolv")) triggers.Add(EffectTrigger.AllyDigimonDigivolved);
            Func<EffectContext, bool> cond = c => c.Other != null
                && (!other || !ReferenceEquals(c.Other, c.Self))
                && (traits == null || traits.Any(x => TargetSpec.HasTrait(c.Other.TopCard, x)))
                && (names == null || names.Any(x => TargetSpec.NameHas(c.Other.TopCard, x)));
            return (triggers, cond);
        }
        return null;
    }

    /// <summary>
    /// 떠나려 할 때 · 소멸하려 할 때 키워드 (설명문은 Normalize가 지웠다) — 공식 설명문 기준:
    /// ≪디코드 (조건)≫ 배틀 이외로 떠날 때 진화원의 조건 카드 1장 등장 · ≪파티션 (A &amp; B)≫ 자신의 효과 · 배틀 이외로 떠날 때 A · B 1장씩 등장 ·
    /// ≪프래그먼트 (N)≫ 소멸할 때 진화원 N장 파기로 소멸하지 않음 · ≪디코이 (조건)≫ 다른 조건 디지몬이 상대 효과로 소멸할 때 이 디지몬을 소멸시켜 막음 ·
    /// ≪가드≫ 다른 디지몬이 상대 효과로 떠날 때 이 디지몬을 소멸시켜 막음 · ≪트레이닝≫ [메인] 레스트 → 덱 위 1장을 진화원 맨 아래 (육성 에어리어에서도)
    /// </summary>
    private static string? ParseLeaveKeyword(string clause, bool inherited, CardScriptDefinition? def)
    {
        var m = Regex.Match(clause, @"^＜(?<k>Decode|Partition|Fragment|Decoy|Guard|Training)\s*(?:\((?<arg>.*)\))?\s*＞$", RegexOptions.IgnoreCase);
        if (!m.Success) return $"미지원 키워드 형식: {clause}";
        string k = m.Groups["k"].Value.ToLowerInvariant(), arg = m.Groups["arg"].Value.Trim();
        var regs = new List<(EffectTrigger Trigger, Action<EffectContext> Act, Func<EffectContext, bool>? When)>();

        switch (k)
        {
            case "decode":
            {
                if (ParseSpecifiedCard(arg) is not { } spec) return $"미지원 ≪디코드≫ 조건: {arg}";
                regs.Add((EffectTrigger.ThisDigimonWouldLeave, c => PlayFromOwnSources(c, new[] { spec }, "≪디코드≫"),
                    c => c.Engine.CurrentLeave is { } l && ReferenceEquals(l.Digimon, c.Self) && !l.InBattle));
                break;
            }
            case "partition":
            {
                var specs = arg.Split('&').Select(p => ParseSpecifiedCard(p.Trim())).ToList();
                if (specs.Count < 2 || specs.Any(s => s == null)) return $"미지원 ≪파티션≫ 조건: {arg}";
                var list = specs.Select(s => s!).ToArray();
                regs.Add((EffectTrigger.ThisDigimonWouldLeave, c => PlayFromOwnSources(c, list, "≪파티션≫"),
                    c => c.Engine.CurrentLeave is { } l && ReferenceEquals(l.Digimon, c.Self) && !l.InBattle && !l.ByOwnEffect
                         && list.All(s => c.Self!.EvolutionSources.Any(s))));
                break;
            }
            case "fragment":
            {
                if (!int.TryParse(arg, out int n)) return $"미지원 ≪프래그먼트≫ 수: {arg}";
                regs.Add((EffectTrigger.ThisDigimonWouldBeDeleted, c =>
                {
                    if (c.Self == null || c.Self.EvolutionSources.Count < n
                        || !c.Engine.AskYesNo(c.Owner, c.Self, $"≪프래그먼트 ({n})≫ 진화원 {n}장을 파기하고 소멸을 막을까요?", recommended: true)) return;
                    foreach (var x in c.Engine.ChooseCards(c.Owner, CardChoiceZone.DigivolutionCards, c.Self.EvolutionSources.Reverse().ToList(), n, upTo: false, $"≪프래그먼트 ({n})≫ 파기할 진화원 {n}장을 고르세요."))
                        if (c.Self.TakeSource(y => ReferenceEquals(y, x)) != null) c.Owner.Trash.Add(x);
                    c.PreventLeave = true;
                    c.Log($"[≪프래그먼트 ({n})≫] [{c.Self.TopCard.Name}]: 진화원 {n}장을 파기하고 소멸하지 않습니다.");
                }, c => c.Engine.CurrentLeave is { } l && ReferenceEquals(l.Digimon, c.Self) && l.CanPrevent));
                break;
            }
            case "decoy":
            case "guard":
            {
                bool decoy = k == "decoy";
                Func<CardData, bool>? spec = null;
                if (decoy && (spec = ParseSpecifiedCard(arg)) == null) return $"미지원 ≪디코이≫ 조건: {arg}";
                regs.Add((decoy ? EffectTrigger.AllyDigimonWouldBeDeleted : EffectTrigger.AllyDigimonWouldLeave, c =>
                {
                    if (c.Self == null || !c.Owner.BattleArea.Contains(c.Self)
                        || !c.Engine.AskYesNo(c.Owner, c.Self, $"{(decoy ? "≪디코이≫" : "≪가드≫")} [{c.Self.TopCard.Name}]을(를) 소멸시켜 [{c.Other!.TopCard.Name}]을(를) 지킬까요?",
                            recommended: c.Other!.TopCard.Level >= c.Self.TopCard.Level)) return;
                    c.Log($"[{(decoy ? "≪디코이≫" : "≪가드≫")}] [{c.Self.TopCard.Name}]을(를) 소멸시켜 [{c.Other.TopCard.Name}]을(를) 지킵니다.");
                    c.DeleteOwn(c.Self);
                    c.PreventLeave = true;
                }, c => c.Engine.CurrentLeave is { } l && !l.InBattle && !l.ByOwnEffect && l.CanPrevent && ReferenceEquals(l.Digimon, c.Other)
                        && !ReferenceEquals(c.Other, c.Self) && (spec == null || spec(c.Other!.TopCard))));
                break;
            }
            case "training":
                def?.AllowBreedingActivation();
                regs.Add((EffectTrigger.ActivateMain, c =>
                {
                    if (c.Self == null || c.Self.IsSuspended || c.Owner.MainDeck.Count == 0) { c.StepsRan = true; return; }
                    c.Engine.Suspend(c.Self);
                    c.Self.AddBottomSource(c.Owner.MainDeck[0]);
                    c.Owner.MainDeck.RemoveAt(0);
                    c.RecalculateFieldBuffs();
                    c.Log($"[≪트레이닝≫] [{c.Self.TopCard.Name}]: 레스트 → 덱 위 1장을 진화원 맨 아래에");
                }, null));
                break;
        }
        if (def != null)
            foreach (var (t, act, when) in regs)
            {
                if (inherited) def.Inherited(t, act, when);
                else def.On(t, act, when);
            }
        return null;
    }

    /// <summary>키워드의 "지정 카드" 조건: "[Aegiomon]" · "Blue Lv.4" · "Lv.4 or lower w/[Agumon]/[Greymon] in name or w/[ME]/[VB] trait" · "[Bagra Army] trait" · "Black"</summary>
    private static Func<CardData, bool>? ParseSpecifiedCard(string spec)
    {
        spec = spec.Trim();
        Match m;
        if (Regex.IsMatch(spec, @"^\[[^\]]+\](?:\s*/\s*\[[^\]]+\])*$"))
        {
            var names = Regex.Matches(spec, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value).ToList();
            return c => names.Any(n => TargetSpec.NameHas(c, n));
        }
        if ((m = Regex.Match(spec, $@"^(?<x>\[[^\]]+\](?:\s*/\s*\[[^\]]+\])*) trait$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = Regex.Matches(m.Groups["x"].Value, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value).ToList();
            return c => xs.Any(x => TargetSpec.HasTrait(c, x));
        }
        if ((m = Regex.Match(spec, $@"^(?<colors>(?:{Colors})(?:/(?:{Colors}))*)(?: Lv\.(?<lv>\d+))?$", RegexOptions.IgnoreCase)).Success)
        {
            var colors = Regex.Matches(m.Groups["colors"].Value, Colors, RegexOptions.IgnoreCase).Select(x => Enum.Parse<CardColor>(x.Value, ignoreCase: true)).ToList();
            int lv = m.Groups["lv"].Success ? int.Parse(m.Groups["lv"].Value) : 0;
            return c => c.Type == CardType.Digimon && c.AllColors.Any(colors.Contains) && (lv == 0 || c.Level == lv);
        }
        if ((m = Regex.Match(spec, @"^Lv\.(?<lv>\d+)(?<low> or lower)?\s*(?<rest>.*)$", RegexOptions.IgnoreCase)).Success)
        {
            int lv = int.Parse(m.Groups["lv"].Value); bool low = m.Groups["low"].Success;
            var ors = new List<Func<CardData, bool>>();
            foreach (Match w in Regex.Matches(m.Groups["rest"].Value, @"w/(?<names>(?:\[[^\]]+\]/?)+)\s*(?<kind>in name|in any trait|in any of its traits|trait)", RegexOptions.IgnoreCase))
            {
                var xs = Regex.Matches(w.Groups["names"].Value, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value).ToList();
                ors.Add(w.Groups["kind"].Value.Equals("in name", StringComparison.OrdinalIgnoreCase)
                    ? c => xs.Any(x => TargetSpec.NameHas(c, x)) : c => xs.Any(x => TargetSpec.HasTrait(c, x)));
            }
            if (m.Groups["rest"].Value.Trim().Length > 0 && ors.Count == 0) return null;
            return c => c.Type == CardType.Digimon && (low ? c.Level <= lv : c.Level == lv) && (ors.Count == 0 || ors.Any(f => f(c)));
        }
        return null;
    }

    /// <summary>진화원에서 지정 카드를 1장씩 코스트 없이 등장 ("~할 수 있다" — 한 번 묻는다)</summary>
    private static void PlayFromOwnSources(EffectContext c, IReadOnlyList<Func<CardData, bool>> specs, string label)
    {
        if (c.Self == null || !specs.Any(s => c.Self.EvolutionSources.Any(x => x.Type == CardType.Digimon && s(x)))) return;
        if (!c.Engine.AskYesNo(c.Owner, c.Self, $"{label} [{c.Self.TopCard.Name}]의 진화원에서 지정 카드를 코스트 없이 등장시킬까요?", recommended: true)) return;
        var played = new List<string>();
        foreach (var spec in specs)
        {
            var pick = c.Engine.ChooseCards(c.Owner, CardChoiceZone.DigivolutionCards,
                c.Self.EvolutionSources.Where(x => x.Type == CardType.Digimon && spec(x)).OrderByDescending(x => x.Level).ToList(), 1, upTo: false,
                $"{label} 등장시킬 진화원을 고르세요.").FirstOrDefault();
            if (pick == null || c.Owner.BattleArea.Count >= 12 || c.Self.TakeSource(y => ReferenceEquals(y, pick)) == null) continue;
            c.Engine.PlayWithoutCost(c.Owner, pick);
            played.Add(pick.Name);
        }
        if (played.Count > 0) c.Log($"[{label}] 진화원의 [{string.Join(", ", played)}] 코스트 없이 등장");
    }

    /// <summary>"would leave the battle area" 뒤의 단서 → 조건 (읽지 못하면 null)</summary>
    private static Func<GoldfishEngine.LeaveInfo, bool>? ParseLeaveQualifier(string q)
    {
        q = q.Trim().TrimEnd('.');
        if (q.Length == 0 || Regex.IsMatch(q, @"^other than by DigiXros$", RegexOptions.IgnoreCase)) return _ => true;
        var parts = Regex.Split(q, @"\s*(?:,|\bor\b|\band\b)\s*(?=other than|outside|by|in\b)", RegexOptions.IgnoreCase).Where(p => p.Length > 0).ToList();
        var conds = new List<Func<GoldfishEngine.LeaveInfo, bool>>();
        foreach (var raw in parts)
        {
            string p = Regex.Replace(raw.Trim(), @"^other than ", "other than ", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(p, @"^other than (?:by )?(?:your|one of your) (?:own )?effects?$", RegexOptions.IgnoreCase)) conds.Add(l => !l.ByOwnEffect);
            else if (Regex.IsMatch(p, @"^(?:other than (?:in|by) battles?|outside of (?:a )?battle|other than battle)$", RegexOptions.IgnoreCase)) conds.Add(l => !l.InBattle);
            else if (Regex.IsMatch(p, @"^(?:in|by) battle$", RegexOptions.IgnoreCase)) conds.Add(l => l.InBattle);
            else if (Regex.IsMatch(p, @"^by (?:an|your opponent's|one of your opponent's|opponent's) effects?$", RegexOptions.IgnoreCase))
                conds.Add(p.Contains("opponent", StringComparison.OrdinalIgnoreCase) ? l => !l.InBattle && !l.ByOwnEffect : l => !l.InBattle);
            else if (Regex.IsMatch(p, @"^other than by DigiXros$", RegexOptions.IgnoreCase)) continue;
            else return null;
        }
        return l => conds.All(f => f(l));
    }

    /// <summary>토큰 카드 (부르는 때마다 새 카드 — 아이디 "TOKEN-" 접두사는 필드를 떠나면 사라진다). 모르는 토큰이면 null</summary>
    private static CardData? MakeToken(string name) => name.Trim().ToLowerInvariant() switch
    {
        // BT2~ 디아블로몬 토큰: Digimon/Cost 14/Lv.6/White/Mega/Unknown/Unidentified/3000 DP
        "diaboromon" => new CardData
        {
            CardId = "TOKEN-Diaboromon", Name = "Diaboromon Token", Type = CardType.Digimon, Color = CardColor.White,
            Level = 6, PlayCost = 14, DP = 3000, Traits = new List<string> { "Mega", "Unknown", "Unidentified" },
        },
        _ => null,
    };

    /// <summary>"(an|another|other) level 3 yellow Digimon (with [X] in its name)" → 사건 디지몬 조건 (other = 이 디지몬이 아닌)</summary>
    private static Func<EffectContext, CardInstance, bool>? ParseAllySubject(string subj)
    {
        bool other = Regex.IsMatch(subj, @"^(?:another|other)\b", RegexOptions.IgnoreCase);
        string p = Regex.Replace(subj, @"^(?:another|other|an?|1)\s+", "", RegexOptions.IgnoreCase);
        p = Regex.Replace(p, @"^other\s+", "", RegexOptions.IgnoreCase);
        var spec = TargetSpec.Parse("all of your " + p);
        if (spec == null || spec.Opponent || spec.AnySide) return null;   // "opponent's Digimon"은 자신의 디지몬이 아니다
        return (c, d) => (!other || !ReferenceEquals(d, c.Self)) && spec.Filters.All(f => f(d));
    }

    private static List<string> SplitSentences(string body) =>
        Regex.Split(Regex.Replace(body, @"＞\s+Then\b", "＞. Then"), @"(?<=[.!])\s+(?=[A-Z＜<\[])")
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

    // =====================================================================
    // 지속 효과: "[Your Turn] This Digimon gets +1000 DP." · "While ~, this Digimon gains ＜Blocker＞."
    // =====================================================================

    private static string? ParseStatic(string body, bool inherited, CardData card, Qualifier qualifier, Pending pending, Func<StaticContext, bool>? outer = null)
    {
        var sentences = SplitSentences(body);
        if (sentences.Count != 1)
            return "문장 파서(3단계) 대상: 여러 문장의 지속 효과";
        string s = sentences[0].TrimEnd('.');

        Func<StaticContext, bool> turn = qualifier switch
        {
            Qualifier.YourTurn => c => c.OwnersTurn,
            Qualifier.OpponentsTurn => c => !c.OwnersTurn,
            _ => outer ?? (_ => true),
        };
        Func<StaticContext, bool> cond = turn;
        var w = Regex.Match(s, @"^(?:While|If) (?<cond>.+?), (?<rest>(?:this Digimon|it|all of your|your|this card) .+)$", RegexOptions.IgnoreCase);
        if (w.Success)
        {
            var parsed = ParseStaticCondition(w.Groups["cond"].Value);
            if (parsed == null) return $"미지원 조건 (While {w.Groups["cond"].Value})";
            cond = c => turn(c) && parsed(c);
            s = w.Groups["rest"].Value;
        }

        var source = inherited ? EffectSource.Inherited : EffectSource.Main;
        Match m;
        // "For each other Digimon you have in play, this Digimon gets +1000 DP" = "this Digimon gets +1000 DP for each ~"
        if ((m = Regex.Match(s, @"^For (?:each|every) (?<cnt>[^,]+), (?<rest>(?:this Digimon|it) .+)$", RegexOptions.IgnoreCase)).Success)
            s = $"{m.Groups["rest"].Value} for each {m.Groups["cnt"].Value}";
        // "This Digimon gains ＜Security A. +1＞ for each of your other suspended Digimon"
        if ((m = Regex.Match(s, @"^(?:this Digimon|it) gains? ＜Security A(?:ttack)?\.? (?<sign>[+-])(?<n>\d+)\s*＞ for (?:every|each) (?<counter>.+)$", RegexOptions.IgnoreCase)).Success
            && ParseCounter(m.Groups["counter"].Value) is { } saCounter)
        {
            int sa = int.Parse(m.Groups["n"].Value) * (m.Groups["sign"].Value == "-" ? -1 : 1);
            var c2 = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.SecurityAttack, source, c => c2(c) ? sa * saCounter(c.Owner, c.Enemy, c.Digimon) : 0));
            return null;
        }
        // "all of your Security Digimon get +1000 DP" (디지몬 · 테이머 지속 효과)
        if ((m = Regex.Match(s, @"^(?:all of )?your Security Digimon gets? (?<sign>[+-])(?<n>\d+) DP$", RegexOptions.IgnoreCase)).Success)
        {
            int dp = int.Parse(m.Groups["n"].Value) * (m.Groups["sign"].Value == "-" ? -1 : 1);
            var c2 = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.SecurityDigimonDp, card.Type == CardType.Tamer ? EffectSource.Main : source, c => c2(c) ? dp : 0));
            return null;
        }
        // "This Digimon gets +1000 DP for every suspended Digimon your opponent has"
        if ((m = Regex.Match(s, @"^(?:this Digimon|it|this card) gets? (?<sign>[+-])(?<n>\d+) DP for (?:every|each) (?<counter>.+)$", RegexOptions.IgnoreCase)).Success
            && ParseCounter(m.Groups["counter"].Value) is { } counter)
        {
            int dp = int.Parse(m.Groups["n"].Value) * (m.Groups["sign"].Value == "-" ? -1 : 1);
            var c2 = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.Dp, source, c => c2(c) ? dp * counter(c.Owner, c.Enemy, c.Digimon) : 0));
            return null;
        }
        if ((m = Regex.Match(s, @"^(?:this Digimon|it|this card) gets? (?<sign>[+-])(?<n>\d+) DP$", RegexOptions.IgnoreCase)).Success)
        {
            int dp = int.Parse(m.Groups["n"].Value) * (m.Groups["sign"].Value == "-" ? -1 : 1);
            var c2 = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.Dp, source, c => c2(c) ? dp : 0));
            return null;
        }
        // "All of your (black / [X] trait) Digimon get +1000 DP" — 자신의 디지몬 전부(조건에 맞는 것)에 주는 효과
        if ((m = Regex.Match(s, @"^(?<who>all of your (?:[^,]*? )?Digimon|your (?:[^,]*? )?Digimon) gets? (?<sign>[+-])(?<n>\d+) DP$", RegexOptions.IgnoreCase)).Success
            && !m.Groups["who"].Value.Contains("opponent", StringComparison.OrdinalIgnoreCase))
        {
            string who = Regex.Replace(m.Groups["who"].Value, @"^your ", "all of your ", RegexOptions.IgnoreCase);
            var spec = TargetSpec.Parse(who);
            if (spec == null) return $"미지원 대상 (지속 DP): {who}";
            int dp = int.Parse(m.Groups["n"].Value) * (m.Groups["sign"].Value == "-" ? -1 : 1);
            var c2 = cond;
            Func<CardInstance, bool> subject = d => spec.Filters.All(f => f(d));
            pending.Registrations.Add(d => d.Static(StaticStat.AllyDigimonDp, card.Type == CardType.Tamer ? EffectSource.Main : source, c => c2(c) ? dp : 0, subject: subject));
            return null;
        }
        if ((m = Regex.Match(s, @"^all of your opponent's Security Digimon gets? (?<sign>[+-])(?<n>\d+) DP$", RegexOptions.IgnoreCase)).Success)
        {
            int dp = int.Parse(m.Groups["n"].Value) * (m.Groups["sign"].Value == "-" ? -1 : 1);
            var c2 = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.EnemySecurityDigimonDp, card.Type == CardType.Tamer ? EffectSource.Main : source, c => c2(c) ? dp : 0));
            return null;
        }
        // "All of your (level 4 or lower) Digimon gain ＜Jamming＞" · "All of your opponent's Digimon with no digivolution cards gain ＜Security A. -1＞"
        if ((m = Regex.Match(s, @"^(?<who>all of your (?:[^,]*? )?Digimon(?: with [^,]+?)?|your (?:[^,]*? )?Digimon(?: with [^,]+?)?) gains? (?<kws>＜.+)$", RegexOptions.IgnoreCase)).Success)
        {
            string who = Regex.Replace(m.Groups["who"].Value, @"^your ", "all of your ", RegexOptions.IgnoreCase);
            bool enemy = who.Contains("opponent", StringComparison.OrdinalIgnoreCase);
            var spec = TargetSpec.Parse(who);
            if (spec == null) return $"미지원 대상 (키워드 부여): {who}";
            var grants = ParseKeywordList(m.Groups["kws"].Value);
            if (grants == null) return "미지원 키워드 부여";
            var c2 = cond;
            Func<CardInstance, bool> subject = d => spec.Filters.All(f => f(d));
            var auraSource = card.Type == CardType.Tamer ? EffectSource.Main : source;
            foreach (var (k, sa) in grants)
            {
                if (k is { } kw)
                {
                    if (enemy) return "미지원: 상대 디지몬에게 키워드 부여";
                    pending.Registrations.Add(d => d.Static(StaticStat.AllyKeyword, auraSource, c => c2(c) ? 1 : 0, kw, subject));
                }
                else
                    pending.Registrations.Add(d => d.Static(enemy ? StaticStat.EnemySecurityAttack : StaticStat.AllySecurityAttack, auraSource, c => c2(c) ? sa : 0, subject: subject));
            }
            return null;
        }
        // "This Digimon gets +1000 DP and ＜Piercing＞" · "gets +2000 DP and gains ＜Blocker＞"
        if ((m = Regex.Match(s, @"^(?<subj>this Digimon|it|this card) gets? (?<dp>[+-]\d+ DP) and (?:gains? )?(?<kws>＜.+)$", RegexOptions.IgnoreCase)).Success)
        {
            string? e1 = ParseStatic($"{m.Groups["subj"].Value} gets {m.Groups["dp"].Value}", inherited, card, Qualifier.None, pending, cond);
            if (e1 != null) return e1;
            return ParseStatic($"{m.Groups["subj"].Value} gains {m.Groups["kws"].Value}", inherited, card, Qualifier.None, pending, cond);
        }
        if (Regex.IsMatch(s, @"^(?:this Digimon|it) (?:can't be blocked(?: by your opponent's Digimon)?|is unblockable)$", RegexOptions.IgnoreCase))
        {
            var c2 = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.Keyword, source, c => c2(c) ? 1 : 0, Keyword.Unblockable));
            return null;
        }
        if (Regex.IsMatch(s, @"^(?:this Digimon|it) can't be attacked$", RegexOptions.IgnoreCase))
        {
            var c2 = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.Keyword, source, c => c2(c) ? 1 : 0, Keyword.CannotBeAttacked));
            return null;
        }
        if (Regex.IsMatch(s, @"^(?:this Digimon's|its) attack target can't (?:change|be changed|be switched|switch)$", RegexOptions.IgnoreCase))
        {
            var c2 = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.Keyword, source, c => c2(c) ? 1 : 0, Keyword.AttackTargetLocked));
            return null;
        }
        if (Regex.IsMatch(s, @"^your opponent can't reduce (?:digivolution|digivolve) costs$", RegexOptions.IgnoreCase))
        {
            var c2 = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.Keyword, source, c => c2(c) ? 1 : 0, Keyword.BlockOpponentEvoCostReduction));
            return null;
        }
        if (Regex.IsMatch(s, @"^(?:this Digimon|it) can't attack$", RegexOptions.IgnoreCase))
        {
            var c2 = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.Keyword, source, c => c2(c) ? 1 : 0, Keyword.CannotAttack));
            return null;
        }
        if (Regex.IsMatch(s, @"^(?:this Digimon|it) can't be blocked by your opponent's Digimon with no digivolution cards$", RegexOptions.IgnoreCase))
        {
            var c2 = cond;
            pending.Registrations.Add(d => d.Static(StaticStat.Keyword, source, c => c2(c) ? 1 : 0, Keyword.UnblockableBySourceless));
            return null;
        }
        if ((m = Regex.Match(s, @"^(?:this Digimon|it) gains? (?<kws>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            var grants = ParseKeywordList(m.Groups["kws"].Value);
            if (grants == null) return "미지원 키워드 부여";
            var c2 = cond;
            foreach (var (k, sa) in grants)
            {
                if (k is { } kw)
                    pending.Registrations.Add(d => d.Static(StaticStat.Keyword, source, c => c2(c) ? 1 : 0, kw));
                else
                    pending.Registrations.Add(d => d.Static(StaticStat.SecurityAttack, source, c => c2(c) ? sa : 0));
            }
            return null;
        }
        return $"문장 파서(3단계) 대상: {s}";
    }

    /// <summary>＜Blocker＞, ＜Rush＞ and ＜Security A. +1＞ → (키워드 또는 null, 시큐리티 어택 수)</summary>
    private static List<(Keyword? Keyword, int SecurityAttack)>? ParseKeywordList(string text)
    {
        text = text.Trim().TrimEnd('.');
        var result = new List<(Keyword?, int)>();
        string rest = text;
        while (rest.Length > 0)
        {
            var m = Regex.Match(rest, @"^(?:,\s*|and\s+|,\s*and\s+)?＜(?<name>[^＞0-9+\-−]+?)\s*(?<num>[+\-−]\s*\d+)?\s*＞\s*", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string name = m.Groups["name"].Value.Trim();
            if (GrantableKeywords.TryGetValue(name, out var k)) result.Add((k, 0));
            else if (name.Equals("Link", StringComparison.OrdinalIgnoreCase) && m.Groups["num"].Value.Trim().StartsWith("+")) result.Add((Keyword.LinkPlusOne, 0));
            else if (name.StartsWith("Security A", StringComparison.OrdinalIgnoreCase))
                result.Add((null, int.Parse(m.Groups["num"].Value.Replace(" ", "").Replace("−", "-"))));
            else return null;
            rest = rest[m.Length..].Trim();
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>필드 상태 조건 (옵션 색 조건 무시용) — 자신의 배틀 에어리어 · 테이머 기준</summary>
    private static Func<PlayerZones, bool>? ParseZoneCondition(string cond)
    {
        cond = cond.Trim();
        Match m;
        if (Regex.IsMatch(cond, @"^you have (?:a|any) Tamers?$", RegexOptions.IgnoreCase)) return z => z.Tamers.Count > 0;
        // 앞면 시큐리티: "you have no (blue) face-up ([X]) security cards" · "you have a face-up ([X]) security card" · "[X] is face-up in your security stack"
        if ((m = Regex.Match(cond, $@"^you have (?<q>no|a|an|any|(?<n>\d+) or more) (?:(?<color>{Colors}) )?face[- ]up (?:(?<x>{TargetSpec.NameList}) )?(?:trait )?security cards?$", RegexOptions.IgnoreCase)).Success)
        {
            bool none = m.Groups["q"].Value.Equals("no", StringComparison.OrdinalIgnoreCase);
            int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
            CardColor? color = m.Groups["color"].Success ? Enum.Parse<CardColor>(m.Groups["color"].Value, ignoreCase: true) : null;
            var xs = m.Groups["x"].Success ? TargetSpec.Names(m.Groups["x"].Value) : null;
            Func<CardData, bool> ok = x => (color == null || x.AllColors.Contains(color.Value)) && (xs == null || xs.Any(v => TargetSpec.NameHas(x, v) || TargetSpec.HasTrait(x, v)));
            return z => none ? !GoldfishEngine.FaceUpCards(z).Any(ok) : GoldfishEngine.FaceUpCards(z).Count(ok) >= n;
        }
        if ((m = Regex.Match(cond, $@"^(?<x>{TargetSpec.NameList}) is face[- ]up in your security stack$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return z => GoldfishEngine.FaceUpCards(z).Any(x => xs.Any(v => TargetSpec.NameHas(x, v)));
        }
        // 육성: "your breeding area is empty" · "you have a Digimon in your breeding area" · "you have [Mother Eater] in the breeding area"
        if (Regex.IsMatch(cond, @"^(?:your breeding area is empty|you have no (?:Digimon|cards) in (?:the|your) breeding area|you don't have (?:a Digimon|any cards) in (?:the|your) breeding area)$", RegexOptions.IgnoreCase))
            return z => z.BreedingArea == null;
        if (Regex.IsMatch(cond, @"^you have (?:a|any) (?:Digimon|cards?) in (?:the|your) breeding area$", RegexOptions.IgnoreCase))
            return z => z.BreedingArea != null;
        if ((m = Regex.Match(cond, $@"^you (?<neg>have|don't have) (?:an? )?(?<x>{TargetSpec.NameList}) in (?:the|your) breeding area$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); bool neg = m.Groups["neg"].Value.StartsWith("don't", StringComparison.OrdinalIgnoreCase);
            return z => (z.BreedingArea is { } b && xs.Any(x => TargetSpec.NameHas(b.TopCard, x))) != neg;
        }
        if ((m = Regex.Match(cond, @"^you have (?:a |an )?(?:\[(?<x>[^\]]+)\] trait (?<kind>Digimon or Tamer|Digimon|Tamer)|(?<kind>Digimon or Tamer|Digimon|Tamer) with the \[(?<x>[^\]]+)\] trait)(?: on the field| in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = m.Groups["x"].Value; string kind = m.Groups["kind"].Value.ToLowerInvariant();
            return z => (kind != "tamer" && z.BattleArea.Any(d => TargetSpec.HasTrait(d.TopCard, x)))
                        || (kind != "digimon" && z.Tamers.Any(t => TargetSpec.HasTrait(t, x)));
        }
        if ((m = Regex.Match(cond, @"^you (?:have )?(?<neg>don't have|do not have|have) \[(?<x>[^\]]+)\] in (?:the|your) battle area$", RegexOptions.IgnoreCase)).Success)
        {
            string x = m.Groups["x"].Value; bool neg = !m.Groups["neg"].Value.Equals("have", StringComparison.OrdinalIgnoreCase);
            return z => z.BattleArea.Any(d => TargetSpec.NameHas(d.TopCard, x)) != neg;
        }
        if ((m = Regex.Match(cond, $@"^(?:you have|there are) (?<n>\d+) or more (?:cards )?(?:with (?<x>{TargetSpec.NameList}) in (?:its|their) (?<where>texts?|names?) |(?:(?<t>{TargetSpec.NameList}) trait )?cards )?in your trash$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            var xs = m.Groups["x"].Success ? TargetSpec.Names(m.Groups["x"].Value) : null;
            var ts = m.Groups["t"].Success ? TargetSpec.Names(m.Groups["t"].Value) : null;
            bool inName = m.Groups["where"].Value.StartsWith("name", StringComparison.OrdinalIgnoreCase);
            return z => z.Trash.Count(c => (xs == null || xs.Any(x => inName ? TargetSpec.NameHas(c, x) : TargetSpec.TextHas(c, x)))
                                           && (ts == null || ts.Any(x => TargetSpec.HasTrait(c, x)))) >= n;
        }
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) or (?<dir>more|fewer|less) security cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value == "more"; return z => more ? z.SecurityStack.Count >= n : z.SecurityStack.Count <= n; }
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) or more Tamers$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return z => z.Tamers.Count >= n; }
        if ((m = Regex.Match(cond, @"^you have (?:a |an )?(?<kind>Digimon|Tamer) with \[(?<x>[^\]]+)\] in its name(?: on the field| in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            string x = m.Groups["x"].Value; bool tamer = m.Groups["kind"].Value.Equals("Tamer", StringComparison.OrdinalIgnoreCase);
            return z => tamer ? z.Tamers.Any(t => TargetSpec.NameHas(t, x)) : z.BattleArea.Any(d => TargetSpec.NameHas(d.TopCard, x));
        }
        return null;
    }

    /// <summary>자신 · 상대 필드 조건 (지속 효과 · 발동 효과 공통): 색 테이머 수, 트래시 매수, 진화원 없는 상대 디지몬 …</summary>
    private static Func<PlayerZones, PlayerZones, bool>? ParseSideCondition(string cond)
    {
        cond = cond.Trim();
        Match m;
        if ((m = Regex.Match(cond, $@"^you have (?:(?:a|an)|(?<n>\d+) or more) (?:(?<color>{Colors}) )?Tamers?(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
            CardColor? color = m.Groups["color"].Success ? Enum.Parse<CardColor>(m.Groups["color"].Value, ignoreCase: true) : null;
            return (o, _) => o.Tamers.Count(t => color == null || t.AllColors.Contains(color.Value)) >= n;
        }
        if ((m = Regex.Match(cond, $@"^you have (?:(?:a|an)|(?<n>\d+) or more) (?:(?<color>{Colors}) )?(?:level (?<lv>\d+) or (?<dir>higher|lower) )?(?<color2>{Colors})? ?Digimon(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
            string colorText = m.Groups["color"].Success ? m.Groups["color"].Value : m.Groups["color2"].Value;
            CardColor? color = colorText.Length > 0 ? Enum.Parse<CardColor>(colorText, ignoreCase: true) : null;
            int lv = m.Groups["lv"].Success ? int.Parse(m.Groups["lv"].Value) : 0; bool higher = m.Groups["dir"].Value == "higher";
            return (o, _) => o.BattleArea.Count(d => (color == null || d.TopCard.AllColors.Contains(color.Value))
                                                   && (lv == 0 || (higher ? d.TopCard.Level >= lv : d.TopCard.Level <= lv))) >= n;
        }
        if ((m = Regex.Match(cond, @"^there are (?<n>\d+) or more cards in your opponent's trash$|^your opponent has (?<n>\d+) or more cards in (?:their|the) trash$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return (_, e) => e.Trash.Count >= n; }
        if (Regex.IsMatch(cond, @"^your opponent has (?:a|any) Digimon with no digivolution cards(?: in play)?$", RegexOptions.IgnoreCase))
            return (_, e) => e.BattleArea.Any(d => d.EvolutionSources.Count == 0);
        if ((m = Regex.Match(cond, @"^your opponent has (?<n>\d+) or more Digimon with no digivolution cards(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return (_, e) => e.BattleArea.Count(d => d.EvolutionSources.Count == 0) >= n; }
        if ((m = Regex.Match(cond, @"^your opponent has (?<n>\d+) or more (?<what>suspended Digimon|Digimon|Tamers)(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); string what = m.Groups["what"].Value.ToLowerInvariant();
            return (_, e) => (what == "tamers" ? e.Tamers.Count : e.BattleArea.Count(d => what != "suspended digimon" || d.IsSuspended)) >= n;
        }
        // "you have a [Diaboromon] in play" · "you don't have an [Arata Sanada] card in play" — 이름이 같은 카드 (디지몬 · 테이머)
        //  · "you have [Angewomon] or [Mirei Mikagura]" · "you have [Guilmon], [Terriermon] or [Impmon] in play" · "you don't have another [ADR-02 Searcher] in play"
        if ((m = Regex.Match(cond, $@"^(?<who>you|your opponent) (?:(?<neg>don't|doesn't|do not|does not) )?ha(?:ve|s) (?:(?:a|an|any|another) )?(?<x>{TargetSpec.NameList})(?: card)?(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); bool neg = m.Groups["neg"].Success; bool opp = m.Groups["who"].Value.Contains("opponent", StringComparison.OrdinalIgnoreCase);
            return (o, e) => { var z = opp ? e : o; return xs.Any(x => z.BattleArea.Any(d => TargetSpec.NameHas(d.TopCard, x)) || z.Tamers.Any(t => TargetSpec.NameHas(t, x))) != neg; };
        }
        // "your opponent has a level 6 or higher Digimon in play" · "you have a Digimon with ＜Blocker＞ in play" · "you have a Digimon in play with [Omnimon] in its name"
        if ((m = Regex.Match(cond, @"^(?<who>you|your opponent) (?:(?<neg>don't|doesn't|do not|does not) )?ha(?:ve|s) (?:a|an|any|1) (?<spec>[^,]*?Digimon)(?: in play)?(?<with> with [^,]+?)?(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            bool neg = m.Groups["neg"].Success; bool opp = m.Groups["who"].Value.Contains("opponent", StringComparison.OrdinalIgnoreCase);
            var spec = TargetSpec.Parse($"all of {(opp ? "your opponent's" : "your")} {m.Groups["spec"].Value}{m.Groups["with"].Value}");
            if (spec != null)
                return (o, e) => (opp ? e : o).BattleArea.Any(d => spec.Filters.All(f => f(d))) != neg;
        }
        // "one of your Digimon has [Greymon] in its name or [Dramon] in its name"
        if ((m = Regex.Match(cond, @"^(?:one of|any of) your Digimon (?:has|have) (?<rest>.+)$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your Digimon with " + Regex.Replace(m.Groups["rest"].Value, @" in its name or \[", " or [", RegexOptions.IgnoreCase)) is { } anyOf)
            return (o, _) => o.BattleArea.Any(d => anyOf.Filters.All(f => f(d)));
        // "a Digimon card with the [Unidentified] trait is in your trash"
        if ((m = Regex.Match(cond, $@"^(?:a|an) (?:(?<kind>Digimon|Tamer|Option) )?card with (?:the )?(?<x>{TargetSpec.NameList}) (?<what>traits?|in its name) is in your trash$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value); bool trait = m.Groups["what"].Value.StartsWith("trait", StringComparison.OrdinalIgnoreCase);
            string kind = m.Groups["kind"].Value;
            return (o, _) => o.Trash.Any(c => (kind.Length == 0 || c.Type.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase))
                                              && xs.Any(x => trait ? TargetSpec.HasTrait(c, x) : TargetSpec.NameHas(c, x)));
        }

        // ---- 장수 · 유무 조건 (상황 정보가 필요 없어 "If ~"와 "While ~" 양쪽에서 같이 쓴다) ----
        static bool Cmp(int value, int n, string dir) => dir.ToLowerInvariant() is "more" or "higher" ? value >= n : value <= n;
        if ((m = Regex.Match(cond, @"^(?<who>you|your opponent) (?:have|has) (?<n>\d+) or (?<dir>fewer|less|more) cards in (?:your|their|your opponent's) hand$", RegexOptions.IgnoreCase)).Success)
        { bool me = m.Groups["who"].Value.Equals("you", StringComparison.OrdinalIgnoreCase); int n = int.Parse(m.Groups["n"].Value); string dir = m.Groups["dir"].Value; return (o, e) => Cmp((me ? o : e).Hand.Count, n, dir); }
        if ((m = Regex.Match(cond, @"^(?<who>you|your opponent) (?:have|has) (?<n>\d+) or (?<dir>fewer|less|more) cards in (?:your|their|your opponent's|the) trash$|^there are (?<n>\d+) or (?<dir>fewer|less) cards in (?<whose>your|your opponent's) trash$", RegexOptions.IgnoreCase)).Success)
        {
            bool me = m.Groups["who"].Success ? m.Groups["who"].Value.Equals("you", StringComparison.OrdinalIgnoreCase) : m.Groups["whose"].Value.Equals("your", StringComparison.OrdinalIgnoreCase);
            int n = int.Parse(m.Groups["n"].Value); string dir = m.Groups["dir"].Value; return (o, e) => Cmp((me ? o : e).Trash.Count, n, dir);
        }
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) or (?<dir>fewer|less|more) Tamers(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); string dir = m.Groups["dir"].Value; return (o, _) => Cmp(o.Tamers.Count, n, dir); }
        if ((m = Regex.Match(cond, @"^(?<who>you|your opponent) (?:have|has) (?<n>\d+) or (?<dir>fewer|less) Digimon(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        { bool me = m.Groups["who"].Value.Equals("you", StringComparison.OrdinalIgnoreCase); int n = int.Parse(m.Groups["n"].Value); return (o, e) => (me ? o : e).BattleArea.Count <= n; }
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) security cards?$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return (o, _) => o.SecurityStack.Count == n; }
        // 트래시의 조건에 맞는 카드 수: "you have 4 or more cards with [Lucemon] or [Witchelny] in its text in your trash" · "~ 8 or more Digimon cards with [Chessmon] in their names in your trash"
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) or more (?<pick>.+?) in your trash$", RegexOptions.IgnoreCase)).Success
            && ParseCardPick($"{m.Groups["n"].Value} {m.Groups["pick"].Value}") is { } trashPick)
        { int n = int.Parse(m.Groups["n"].Value); var match = trashPick.Match; return (o, _) => o.Trash.Count(match) >= n; }
        // 양쪽 필드: "there is a Digimon with 13000 DP or more" · "there are 2 or more suspended Digimon" · "there are 3 or more Digimon"
        if ((m = Regex.Match(cond, @"^there (?:is|are) (?:a|an|(?<n>\d+) or more) (?<sus>suspended )?Digimon(?: with (?<dp>\d+) DP or (?<dir>more|less))?(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1; bool sus = m.Groups["sus"].Success;
            int dp = m.Groups["dp"].Success ? int.Parse(m.Groups["dp"].Value) : -1; bool more = m.Groups["dir"].Value.Equals("more", StringComparison.OrdinalIgnoreCase);
            return (o, e) => o.BattleArea.Concat(e.BattleArea).Count(d => (!sus || d.IsSuspended) && (dp < 0 || (more ? d.GetCurrentDP() >= dp : d.GetCurrentDP() <= dp))) >= n;
        }
        // "there are 12 or more levels' total worth of Digimon" — 양쪽 디지몬 레벨 합계
        if ((m = Regex.Match(cond, @"^there are (?<n>\d+) or more levels' total worth of Digimon(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return (o, e) => o.BattleArea.Concat(e.BattleArea).Sum(d => d.TopCard.Level) >= n; }
        // 상대와 비교: "you have fewer Digimon than your opponent" · "your hand has fewer cards than your opponent's"
        if (Regex.IsMatch(cond, @"^you have fewer Digimon than your opponent(?: does)?$", RegexOptions.IgnoreCase))
            return (o, e) => o.BattleArea.Count < e.BattleArea.Count;
        if (Regex.IsMatch(cond, @"^your hand has fewer cards than your opponent's(?: hand)?$|^you have fewer cards in (?:your )?hand than your opponent(?: does)?$", RegexOptions.IgnoreCase))
            return (o, e) => o.Hand.Count < e.Hand.Count;
        if (Regex.IsMatch(cond, @"^(?<who>you|your opponent) (?:don't|doesn't|do not|does not) have (?:a|any) Digimon(?: in play)?$|^(?:you|your opponent) (?:have|has) no Digimon(?: in play)?$", RegexOptions.IgnoreCase))
        { bool me = cond.StartsWith("you ", StringComparison.OrdinalIgnoreCase); return (o, e) => (me ? o : e).BattleArea.Count == 0; }
        if (Regex.IsMatch(cond, @"^your opponent has no (?:unsuspended|active) Digimon(?: in play)?$|^they have no (?:unsuspended|active) Digimon(?: in play)?$", RegexOptions.IgnoreCase))
            return (_, e) => e.BattleArea.All(d => d.IsSuspended);
        if (Regex.IsMatch(cond, @"^your opponent has no Digimon with digivolution cards(?: in play)?$", RegexOptions.IgnoreCase))
            return (_, e) => e.BattleArea.All(d => d.EvolutionSources.Count == 0);
        // 양쪽 시큐리티 합계: "If there are 4 or fewer total cards in both players' security stacks" · "If the total cards in both players' security stacks is 4 or less"
        if ((m = Regex.Match(cond, @"^there are (?<n>\d+) or (?<dir>fewer|less|more) total cards in both players' security stacks$|^the total (?:number of )?cards in both players' security stacks (?:is|are) (?<n>\d+) or (?<dir>fewer|less|more)$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); string dir = m.Groups["dir"].Value; return (o, e) => Cmp(o.SecurityStack.Count + e.SecurityStack.Count, n, dir); }
        return null;
    }

    /// <summary>"this Digimon has the [A] or [B] trait" · "has [X] in its name / text" · "[A] or [B] is in this Digimon's digivolution cards"</summary>
    private static Func<CardInstance?, bool>? ParseDigimonCondition(string cond)
    {
        cond = cond.Trim();
        Match m;
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) is (?:a )?level (?<n>\d+)(?: or (?<dir>higher|lower|more|less))?(?: Digimon)?$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); string dir = m.Groups["dir"].Value.ToLowerInvariant();
            return d => d != null && (dir is "higher" or "more" ? d.TopCard.Level >= n : dir is "lower" or "less" ? d.TopCard.Level <= n : d.TopCard.Level == n);
        }
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) has ＜(?<k>[A-Za-z .\-]+?)\s*＞ in its texts?$", RegexOptions.IgnoreCase)).Success)
        {
            // "this Digimon has ＜Save＞ in its text" — 맨 위 카드 · 진화원 문장에 그 키워드가 있는지
            string k = m.Groups["k"].Value.Trim();
            return d => d != null && d.GetEvolutionStackBottomUp().Append(d.TopCard)
                .Any(x => (x.EffectText + " " + x.InheritedEffectText).Contains($"＜{k}", StringComparison.OrdinalIgnoreCase));
        }
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) has ＜(?<k>[A-Za-z .\-]+?)\s*＞$", RegexOptions.IgnoreCase)).Success)
        {
            string k = m.Groups["k"].Value.Trim();
            if (GrantableKeywords.TryGetValue(k, out var kw))
                return d => d != null && (kw == Keyword.Blocker ? d.HasBlocker : CardScripts.HasKeyword(d, kw));
            // ≪진격≫처럼 동작 키워드: 이 디지몬(맨 위 · 진화원)의 효과 문장에 그 키워드가 있는지
            return d => d != null && d.GetEvolutionStackBottomUp().Append(d.TopCard).Any(x => x.EffectText.Contains($"＜{k}", StringComparison.OrdinalIgnoreCase));
        }
        if ((m = Regex.Match(cond, $@"^this Digimon's digivolution cards include (?:a |an )?(?:(?<kind>Digimon|Tamer) )?card with the (?<x>{TargetSpec.NameList}) traits?(?: or (?:a |an )?(?<color>{Colors}) Tamer card)?$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            CardColor? color = m.Groups["color"].Success ? Enum.Parse<CardColor>(m.Groups["color"].Value, ignoreCase: true) : null;
            return d => d != null && d.EvolutionSources.Any(s => xs.Any(x => TargetSpec.HasTrait(s, x)) || (color != null && s.Type == CardType.Tamer && s.AllColors.Contains(color.Value)));
        }
        if ((m = Regex.Match(cond, $@"^(?:this Digimon|it) has (?:the )?(?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return d => d != null && xs.Any(x => TargetSpec.HasTrait(d.TopCard, x)); }
        if ((m = Regex.Match(cond, $@"^(?:this Digimon|it) has (?<x>{TargetSpec.NameList}) in its names?$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return d => d != null && xs.Any(x => TargetSpec.NameHas(d.TopCard, x)); }
        if ((m = Regex.Match(cond, $@"^(?:this Digimon|it) has (?<x>{TargetSpec.NameList}) in its texts?$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return d => d != null && xs.Any(x => TargetSpec.TextHas(d.TopCard, x)); }
        if ((m = Regex.Match(cond, $@"^(?:(?:a|an) (?:Digimon |Tamer )?card with )?(?<x>{TargetSpec.NameList})(?: in its name)? (?:is|are) in this Digimon's digivolution cards$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return d => d != null && d.EvolutionSources.Any(s => xs.Any(x => TargetSpec.NameHas(s, x))); }
        if ((m = Regex.Match(cond, $@"^this Digimon's digivolution cards include (?:a card with )?(?<x>{TargetSpec.NameList})(?: in its name)?$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return d => d != null && d.EvolutionSources.Any(s => xs.Any(x => TargetSpec.NameHas(s, x))); }
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) has (?<n>\d+) or (?<dir>more|fewer|less) digivolution cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value == "more"; return d => d != null && (more ? d.EvolutionSources.Count >= n : d.EvolutionSources.Count <= n); }
        if ((m = Regex.Match(cond, $@"^(?:this Digimon|it) has (?<x>{TargetSpec.NameList}) in its digivolution cards$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return d => d != null && d.EvolutionSources.Any(s => xs.Any(x => TargetSpec.NameHas(s, x))); }
        if (Regex.IsMatch(cond, @"^(?:this Digimon|it) has no digivolution cards$", RegexOptions.IgnoreCase)) return d => d != null && d.EvolutionSources.Count == 0;
        if (Regex.IsMatch(cond, @"^(?:this Digimon|it) is suspended$", RegexOptions.IgnoreCase)) return d => d?.IsSuspended == true;
        if (Regex.IsMatch(cond, @"^(?:this Digimon|it) is unsuspended$", RegexOptions.IgnoreCase)) return d => d?.IsSuspended == false;
        // "this Digimon has 10000 DP or more" — "it"은 사건의 다른 디지몬일 수 있어 "this Digimon"만
        if ((m = Regex.Match(cond, @"^this Digimon has (?<n>\d+) DP or (?<dir>more|less|higher|lower)$|^this Digimon's DP is (?<n>\d+) or (?<dir>more|less|higher|lower)$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value.ToLowerInvariant() is "more" or "higher"; return d => d != null && (more ? d.GetCurrentDP() >= n : d.GetCurrentDP() <= n); }
        // "this Digimon has 2 or more colors" — 맨 위 카드의 색 수
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) has (?<n>\d+) or more colors$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return d => d != null && d.TopCard.AllColors.Count >= n; }
        return null;
    }

    private static Func<StaticContext, bool>? ParseStaticCondition(string cond)
    {
        cond = cond.Trim();
        if (ParseDigimonCondition(cond) is { } dc) return c => dc(c.Digimon);
        if (ParseSideCondition(cond) is { } sc) return c => sc(c.Owner, c.Enemy);
        Match m;
        if (Regex.IsMatch(cond, @"^you have another Digimon with the same name as this Digimon(?: in play)?$", RegexOptions.IgnoreCase))
            return c => c.Digimon != null && c.Owner.BattleArea.Any(d => !ReferenceEquals(d, c.Digimon) && CardNames.Same(d.TopCard, c.Digimon.TopCard));
        if (Regex.IsMatch(cond, @"^(?:this Digimon|it) is suspended$", RegexOptions.IgnoreCase)) return c => c.Digimon?.IsSuspended == true;
        if (Regex.IsMatch(cond, @"^(?:this Digimon|it) is unsuspended$", RegexOptions.IgnoreCase)) return c => c.Digimon?.IsSuspended == false;
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) has (?<n>\d+) or more digivolution cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return c => (c.Digimon?.EvolutionSources.Count ?? 0) >= n; }
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) has \[(?<x>[^\]]+)\] in its name$", RegexOptions.IgnoreCase)).Success)
        { string x = m.Groups["x"].Value; return c => c.Digimon != null && TargetSpec.NameHas(c.Digimon.TopCard, x); }
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) has the \[(?<x>[^\]]+)\] trait$", RegexOptions.IgnoreCase)).Success)
        { string x = m.Groups["x"].Value; return c => c.Digimon != null && TargetSpec.HasTrait(c.Digimon.TopCard, x); }
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) or (?<dir>fewer|less|more) security cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value == "more"; return c => more ? c.Owner.SecurityStack.Count >= n : c.Owner.SecurityStack.Count <= n; }
        if ((m = Regex.Match(cond, @"^your opponent has (?<n>\d+) or (?<dir>fewer|less|more) security cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value == "more"; return c => more ? c.Enemy.SecurityStack.Count >= n : c.Enemy.SecurityStack.Count <= n; }
        if (Regex.IsMatch(cond, @"^you have a Tamer$", RegexOptions.IgnoreCase)) return c => c.Owner.Tamers.Count > 0;
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) or more Tamers$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return c => c.Owner.Tamers.Count >= n; }
        if ((m = Regex.Match(cond, @"^(?:there are|you have) (?<n>\d+) or more cards in your trash$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return c => c.Owner.Trash.Count >= n; }
        if ((m = Regex.Match(cond, @"^your opponent has (?<n>\d+) or more cards in (?:their|your opponent's) trash$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return c => c.Enemy.Trash.Count >= n; }
        return null;
    }

    // =====================================================================
    // 발동 효과: [시점] 문장들
    // =====================================================================

    /// <summary>문장 하나: 조건 · 동작 · "~할 수 있다"(Optional — 발휘할지 묻는다) · 원문(물을 때 보여 줌)</summary>
    private sealed record Step(Func<EffectContext, bool>? Condition, Action<EffectContext> Act, bool Optional = false, string Text = "");

    /// <summary>문장을 처리한다: 조건 확인 → "~할 수 있다"면 효과 주인에게 묻기(추천 = AI 평가) → 동작</summary>
    private static void RunStep(Step s, EffectContext c)
    {
        c.StepsRan = true;
        if (s.Condition != null && !s.Condition(c)) { c.LastStepResolved = false; return; }
        if (s.Optional && !c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 이 효과를 발휘할까요?\n{s.Text}", AiEvaluator.EstimateSentenceValue(s.Text) >= 0)) { c.LastStepResolved = false; return; }
        c.LastStepResolved = true;
        s.Act(c);
        if (c.LastStepResolved) c.AnyStepResolved = true;   // 비용 문장은 비용을 내지 않으면 Act 안에서 false가 된다
    }

    /// <summary>
    /// [Main]의 뜻을 카드 종류에 맞춘다: 옵션 = 사용 효과(Main), 테이머 · 디지몬(메인 효과 칸) = 기동 효과(ActivateMain).
    /// 진화원 칸의 [Main]은 아직 지원하지 않는다 (false).
    /// </summary>
    private static bool MapMainTrigger(List<EffectTrigger> triggers, CardData card, bool inherited)
    {
        int i = triggers.IndexOf(EffectTrigger.Main);
        if (i < 0 || card.Type == CardType.Option && !inherited)
            return true;
        if (inherited)
            return false;
        triggers[i] = EffectTrigger.ActivateMain;
        return true;
    }

    private static string? ParseTriggered(string body, bool inherited, CardData card, List<EffectTrigger> triggers, Qualifier qualifier, bool oncePerTurn, Pending pending, Func<EffectContext, bool>? eventCondition = null, bool twicePerTurn = false)
    {
        if (!MapMainTrigger(triggers, card, inherited))
            return "미지원: 진화원의 [메인] 효과";
        foreach (var t in triggers)
        {
            // 엔진이 이 시점을 처리하는 카드 종류인지
            if (t == EffectTrigger.Security && card.Type == CardType.Tamer && !IsTamerPlayRule(body)) return "미지원: 테이머의 [시큐리티] 효과";
            if (t == EffectTrigger.OnPlay && card.Type == CardType.Option) return "미지원: 옵션의 [등장 시]";
        }

        var sentences = MergeRevealSentences(SplitSentences(body));
        var steps = new List<Step>();
        foreach (var raw in sentences)
        {
            // "Delete ~ 5000 DP or less. If you have a red Tamer in play, delete ~ 8000 DP or less instead." — 조건이 맞으면 앞 문장 대신
            var inst = Regex.Match(raw.Trim().TrimEnd('.'), @"^(?:If|When) (?<c>[^,]+), (?:instead,? )?(?<rest>.+?)(?:,? instead)?$", RegexOptions.IgnoreCase);
            if (inst.Success && steps.Count > 0 && Regex.IsMatch(raw, @"\binstead\b", RegexOptions.IgnoreCase))
            {
                var cond = ParseEventCondition(inst.Groups["c"].Value);
                if (cond == null) return $"미지원 조건 ({inst.Groups["c"].Value})";
                string rest = inst.Groups["rest"].Value;
                var alt = ParseStep(char.ToUpperInvariant(rest[0]) + rest[1..] + ".", card, triggers);
                if (alt.Error != null) return alt.Error;
                if (alt.Step == null) continue;
                var (prev, altStep) = (steps[^1], alt.Step);
                steps[^1] = prev with { Condition = null, Act = c => { if (cond(c)) RunStep(altStep, c); else RunStep(prev, c); }, Optional = false };
                continue;
            }
            var step = ParseStep(raw, card, triggers);
            if (step.Error != null) return step.Error;
            if (step.Step != null) steps.Add(step.Step);
        }
        if (steps.Count == 0)
            return null;   // 규칙으로 처리되는 문장뿐 (테이머 [시큐리티] 등장)

        Func<EffectContext, bool> turnOk = qualifier switch
        {
            Qualifier.YourTurn => c => c.Engine.IsTurnOf(c.Owner),
            Qualifier.OpponentsTurn => c => !c.Engine.IsTurnOf(c.Owner),
            _ => _ => true,
        };
        var stepList = steps.ToArray();
        foreach (var trigger in triggers)
        {
            var tr = trigger;
            Action<EffectContext> resolve = c =>
            {
                foreach (var s in stepList)
                    RunStep(s, c);
                if (tr == EffectTrigger.Security && c.Summary.Length == 0)
                    c.Summary = c.Message.Length > 0 ? Shorten(c.Message) : "효과 발휘";
            };
            Func<EffectContext, bool>? when = qualifier == Qualifier.None || qualifier == Qualifier.AllTurns ? null : turnOk;
            if (eventCondition != null)
            {
                var turnWhen = when;
                when = c => (turnWhen == null || turnWhen(c)) && eventCondition(c);
            }
            if (tr == EffectTrigger.Main)
            {
                // 옵션 [메인]: 첫 대상("1 of your opponent's suspended Digimon")의 종류 · 조건 — 화면이 끌어 놓을 곳을 정하고, 조건에 안 맞는 디지몬이면 쓰기 전에 막는다
                var (kind, filter, rule) = card.Type == CardType.Option ? OptionMainTarget(body) : (TargetKind.None, null, null);
                pending.Registrations.Add(d => d.Main(kind, resolve, filter, rule));
            }
            else if (inherited)
                pending.Registrations.Add(d => d.Inherited(tr, resolve, when, oncePerTurn, twicePerTurn));
            else
                pending.Registrations.Add(d => d.On(tr, resolve, when, oncePerTurn, twicePerTurn));
        }
        return null;
    }

    private static string Shorten(string s) => s.Length <= 40 ? s : s[..40] + "…";

    private static bool IsTamerPlayRule(string body) =>
        Regex.IsMatch(body.Trim().TrimEnd('.'), @"^(?:Play this card without paying (?:the|its) (?:memory )?cost|Place this card in (?:the|your) battle area)$", RegexOptions.IgnoreCase);

    /// <summary>"Reveal the top N ~. Add ~ among them to the hand. Return the rest ~." 를 한 문장으로 합친다</summary>
    private static List<string> MergeRevealSentences(List<string> sentences)
    {
        var result = new List<string>();
        // "Any [On Play] effects on Digimon played with this effect don't activate." — 앞 문장(등장)에 붙인다
        for (int k = sentences.Count - 1; k > 0; k--)
        {
            if (Regex.IsMatch(sentences[k], @"^Any \[On Play\] effects on (?:Digimon|cards) played (?:with|by) this effect don't activate\.?$", RegexOptions.IgnoreCase))
            {
                sentences[k - 1] = sentences[k - 1].TrimEnd('.') + " ⟦no-onplay⟧.";
                sentences.RemoveAt(k);
            }
        }
        for (int i = 0; i < sentences.Count; i++)
        {
            // "Reveal the top card of your deck. Add it to your hand if it's a green Digimon card. Otherwise, place it at the bottom of your deck."
            //  · "By suspending this Tamer, reveal 1 card from the top of your deck. If that card has the [X] trait, add it to your hand. Trash the rest."
            if (Regex.IsMatch(sentences[i], @"^(?:[^.]*?\b)?Reveal (?:the top card of your deck|1 card from the top of your deck)\.?$", RegexOptions.IgnoreCase)
                && i + 2 < sentences.Count
                && Regex.IsMatch(sentences[i + 1], @"^(?:Add it to (?:the|your) hand if it's|If (?:that card|it) (?:is|has)) ", RegexOptions.IgnoreCase)
                && Regex.IsMatch(sentences[i + 2], @"^(?:Otherwise|Trash the rest\.?$|Place the rest at the bottom of (?:the|your) deck\.?$)", RegexOptions.IgnoreCase))
            {
                result.Add($"{sentences[i]} {sentences[i + 1]} {sentences[i + 2]}");
                i += 2;
                continue;
            }
            if (Regex.IsMatch(sentences[i], @"^(?:Then, )?(?:You may )?Reveal (?:the top \d+ cards? of your deck|\d+ cards? from the top of your deck)(?: for each [^.]+)?\.?$", RegexOptions.IgnoreCase)
                && i + 2 < sentences.Count
                && Regex.IsMatch(sentences[i + 1], @"^(?:Add .+ among them to (?:the|your) hand|(?:You may )?play .+ among them(?: without paying .+)?)\.?$", RegexOptions.IgnoreCase))
            {
                result.Add($"{sentences[i]} {sentences[i + 1]} {sentences[i + 2]}");
                i += 2;
            }
            // "By suspending this Digimon, reveal the top 3 cards ~. Among them, add ~ and trash 1 such card. Return the rest ~."
            else if (Regex.IsMatch(sentences[i], @"^(?:[^.]*?\b)?Reveal (?:the top \d+ cards? of your deck|\d+ cards? from the top of your deck)(?: for each [^.]+)?\.?$", RegexOptions.IgnoreCase)
                     && i + 2 < sentences.Count
                     && Regex.IsMatch(sentences[i + 1], @"^(?:Among them, )?(?:You may )?(?:Add|Play|Trash|Place) .+$", RegexOptions.IgnoreCase)
                     && Regex.IsMatch(sentences[i + 2], $@"^{RevealRest}\.?$", RegexOptions.IgnoreCase))
            {
                result.Add($"{sentences[i]} {sentences[i + 1]} {sentences[i + 2]}");
                i += 2;
            }
            else result.Add(sentences[i]);
        }
        return result;
    }

    private static (Step? Step, string? Error) ParseStep(string sentence, CardData card, List<EffectTrigger> triggers)
    {
        string s = sentence.Trim();
        s = Regex.Replace(s, @"^Then,?\s+", "", RegexOptions.IgnoreCase);

        Func<EffectContext, bool>? cond = null;
        var ifm = Regex.Match(s, @"^(?:If|While) (?<c>[^,]+), (?<rest>.+)$", RegexOptions.IgnoreCase);
        if (ifm.Success)
        {
            cond = ParseEventCondition(ifm.Groups["c"].Value);
            if (cond == null) return (null, $"미지원 조건 (If {ifm.Groups["c"].Value})");
            s = ifm.Groups["rest"].Value;
        }
        bool optional = Regex.IsMatch(s, @"^you may\s+", RegexOptions.IgnoreCase);   // "~할 수 있다": 발휘할지 효과 주인이 정한다
        s = Regex.Replace(s, @"^you may\s+", "", RegexOptions.IgnoreCase);
        s = s.Trim();

        // 비용의 다른 표기 → "By ~ing, ~"
        //  "suspend this Tamer to gain 1 memory" · "unsuspend this Digimon by decreasing your memory by 3"
        var toForm = Regex.Match(s, @"^(?<verb>suspend|trash|delete|return|pay|place|add)\b(?<obj>[^,]*?) to (?<rest>(?:gain|draw|unsuspend|delete|suspend|give|reveal|return|play|trash|add|reduce|have|prevent|＜)\b?.+)$", RegexOptions.IgnoreCase);
        if (toForm.Success)
            s = $"By {Gerund(toForm.Groups["verb"].Value)}{toForm.Groups["obj"].Value}, {toForm.Groups["rest"].Value}";
        var byForm = Regex.Match(s, @"^(?<act>[^,]+?) by (?<cost>(?:suspending|trashing|deleting|returning|paying|placing|decreasing your memory by \d+)\b[^,]*)$", RegexOptions.IgnoreCase);
        if (byForm.Success)
            s = $"By {byForm.Groups["cost"].Value.TrimEnd('.')}, {byForm.Groups["act"].Value}";

        // 비용: "By trashing 1 card in your hand, ~" — 낼 수 있을 때만, 낼지는 효과 주인이 정한다 (플레이어는 물어봄)
        var by = Regex.Match(s, @"^By (?<cost>[^,]+), (?<rest>.+)$", RegexOptions.IgnoreCase);
        if (by.Success)
        {
            var cost = ParseCost(by.Groups["cost"].Value, card);
            if (cost == null) return (null, $"미지원 비용 (By {by.Groups["cost"].Value})");
            var inner = ParseStep(by.Groups["rest"].Value, card, triggers);
            if (inner.Error != null || inner.Step == null) return (null, inner.Error ?? "미지원 비용 뒤 문장");
            var (canPay, pay, recommend, describe) = cost.Value;
            var innerStep = inner.Step;
            Action<EffectContext> paidAct = c =>
            {
                if (!PayOrSkip(c, canPay, recommend, describe)) { c.LastStepResolved = false; return; }
                pay(c);
                RunStep(innerStep, c);
            };
            return (new Step(cond, paidAct), null);
        }
        if (s.Length > 0) s = char.ToUpperInvariant(s[0]) + s[1..];

        // "＜Draw 1＞ for every 2 security cards you have" · "For each of your Tamers, ~" — 셈만큼 반복
        var mult = Regex.Match(s.TrimEnd('.'), @"^(?<core>.+?) for (?:every|each) (?<counter>.+)$", RegexOptions.IgnoreCase);
        if (!mult.Success)
            mult = Regex.Match(s.TrimEnd('.'), @"^For (?:every|each) (?<counter>[^,]+), (?<core>.+)$", RegexOptions.IgnoreCase);
        // 셈 뒤의 기간은 동작에 붙인다: "gets -1000 DP for each card in your hand for the turn"
        string multCounter = mult.Success ? mult.Groups["counter"].Value.TrimEnd('.') : "";
        string multDur = "";
        if (mult.Success && Regex.Match(multCounter, @"^(?<cnt>.+?)(?<dur> for the turn| until the end of .+)$", RegexOptions.IgnoreCase) is { Success: true } md)
        {
            multCounter = md.Groups["cnt"].Value;
            multDur = md.Groups["dur"].Value;
        }
        if (mult.Success && ParseCounter(multCounter) is { } counter)
        {
            string core = mult.Groups["core"].Value + multDur;
            core = char.ToUpperInvariant(core[0]) + core[1..];
            // DP 변화는 대상을 한 번만 고르고 값에 곱한다 (k번 반복하면 대상이 바뀔 수 있다)
            var dpm = Regex.Match(core, @"^(?<t>.+?) gets? (?<sign>[+-])(?<n>\d+) DP(?<rest>.*)$", RegexOptions.IgnoreCase);
            if (dpm.Success)
            {
                int unit = int.Parse(dpm.Groups["n"].Value);
                if (ParseAction(core, card, triggers).Error is { } dpErr) return (null, dpErr);
                var byK = new Dictionary<int, Action<EffectContext>?>();
                Action<EffectContext> scaled = c =>
                {
                    int k = Math.Min(counter(c.Owner, c.Enemy, c.Self), 20);
                    if (k <= 0) return;
                    if (!byK.TryGetValue(k, out var a))
                        byK[k] = a = ParseAction($"{dpm.Groups["t"].Value} gets {dpm.Groups["sign"].Value}{unit * k} DP{dpm.Groups["rest"].Value}", card, triggers).Act;
                    a?.Invoke(c);
                };
                return (new Step(cond, scaled, optional, sentence.Trim()), null);
            }
            var inner = ParseAction(core, card, triggers);
            if (inner.Error != null) return (null, inner.Error);
            var innerAct = inner.Act!;
            Action<EffectContext> repeated = c =>
            {
                int k = Math.Min(counter(c.Owner, c.Enemy, c.Self), 20);
                for (int i = 0; i < k; i++) innerAct(c);
            };
            return (new Step(cond, repeated, optional, sentence.Trim()), null);
        }

        bool security = triggers.Contains(EffectTrigger.Security);
        if (security && card.Type == CardType.Tamer && IsTamerPlayRule(s))
            return (null, null);   // 테이머 [시큐리티] 등장 = 엔진 규칙

        var act = ParseAction(s.TrimEnd('.'), card, triggers);
        if (act.Error != null) return (null, act.Error);
        return (new Step(cond, act.Act!, optional, sentence.Trim()), null);
    }

    /// <summary>
    /// "for every / for each ~"의 셈 → (자신 필드, 상대 필드, 이 디지몬) → 배수.
    /// 예: "2 security cards you have", "suspended Digimon your opponent has", "of your Tamers", "3 cards in your trash"
    /// </summary>
    private static Func<PlayerZones, PlayerZones, CardInstance?, int>? ParseCounter(string counter)
    {
        counter = counter.Trim().TrimEnd('.').Trim();
        Match m;
        int Per(Match x) => x.Groups["n"].Success && int.TryParse(x.Groups["n"].Value, out int n) && n > 0 ? n : 1;
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) )?(?:security cards? you have|cards? in your security stack|of your security cards)$", RegexOptions.IgnoreCase)).Success)
        { int per = Per(m); return (o, _, _) => o.SecurityStack.Count / per; }
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) )?(?:security cards? your opponent has|cards? in your opponent's security stack)$", RegexOptions.IgnoreCase)).Success)
        { int per = Per(m); return (_, e, _) => e.SecurityStack.Count / per; }
        if (Regex.IsMatch(counter, @"^(?:of your face[- ]up security cards|face[- ]up security cards? you have)$", RegexOptions.IgnoreCase))
            return (o, _, _) => GoldfishEngine.FaceUpCards(o).Count;   // 앞면 시큐리티
        if (Regex.IsMatch(counter, @"^(?:of your opponent's suspended Digimon|suspended Digimon your opponent has)$", RegexOptions.IgnoreCase))
            return (_, e, _) => e.BattleArea.Count(d => d.IsSuspended);
        if (Regex.IsMatch(counter, @"^(?:of your opponent's Digimon|Digimon your opponent has)(?: in play)?$", RegexOptions.IgnoreCase))
            return (_, e, _) => e.BattleArea.Count;
        if (Regex.IsMatch(counter, @"^(?:of your opponent's Digimon with no digivolution cards|Digimon with no digivolution cards your opponent has)$", RegexOptions.IgnoreCase))
            return (_, e, _) => e.BattleArea.Count(d => d.EvolutionSources.Count == 0);
        if ((m = Regex.Match(counter, $@"^(?:of your (?:(?<color>{Colors}) )?Tamers|(?:(?<color>{Colors}) )?Tamer you have(?: in play)?)$", RegexOptions.IgnoreCase)).Success)
        {
            CardColor? color = m.Groups["color"].Success ? Enum.Parse<CardColor>(m.Groups["color"].Value, ignoreCase: true) : null;
            return (o, _, _) => o.Tamers.Count(t => color == null || t.AllColors.Contains(color.Value));
        }
        if ((m = Regex.Match(counter, @"^(?:of your (?<other>other )?(?<sus>suspended )?Digimon|(?<other>other )?(?<sus>suspended )?Digimon you have)(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            bool other = m.Groups["other"].Success, sus = m.Groups["sus"].Success;
            return (o, _, self) => o.BattleArea.Count(d => (!other || !ReferenceEquals(d, self)) && (!sus || d.IsSuspended));
        }
        // "Digimon card in this Digimon's digivolution cards with [Greymon] in its name or [Hybrid] trait"
        if ((m = Regex.Match(counter, @"^(?<kind>Digimon |Tamer )?cards? in this Digimon's digivolution cards (?<filter>with .+)$", RegexOptions.IgnoreCase)).Success
            && ParseCardPick($"1 {m.Groups["kind"].Value}card {m.Groups["filter"].Value}") is { } srcPick)
            return (_, _, self) => self?.EvolutionSources.Count(srcPick.Match) ?? 0;
        // "gain 1 memory for each Digimon deleted" (소멸했을 때 효과 — 사건 1번에 1마리)
        if (Regex.IsMatch(counter, @"^Digimon deleted$", RegexOptions.IgnoreCase))
            return (_, _, _) => 1;
        // "[OmniShoutmon] or [ZeigGreymon] in this Digimon's digivolution cards"
        if ((m = Regex.Match(counter, $@"^(?<x>{TargetSpec.NameList}) in this Digimon's digivolution cards$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return (_, _, self) => self?.EvolutionSources.Count(s => xs.Any(x => TargetSpec.NameHas(s, x))) ?? 0; }
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) )?cards? in your trash$", RegexOptions.IgnoreCase)).Success)
        { int per = Per(m); return (o, _, _) => o.Trash.Count / per; }
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) )?cards? in your opponent's trash$", RegexOptions.IgnoreCase)).Success)
        { int per = Per(m); return (_, e, _) => e.Trash.Count / per; }
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) )?cards? in your (?<opp>opponent's )?hand$", RegexOptions.IgnoreCase)).Success)
        { int per = Per(m); bool opp = m.Groups["opp"].Success; return (o, e, _) => (opp ? e : o).Hand.Count / per; }
        if ((m = Regex.Match(counter, @"^(?:(?<n>\d+) )?(?:cards? in this Digimon's digivolution cards|of this Digimon's digivolution cards|digivolution cards? (?:this Digimon has|under this Digimon))$", RegexOptions.IgnoreCase)).Success)
        { int per = Per(m); return (_, _, self) => (self?.EvolutionSources.Count ?? 0) / per; }
        return null;
    }

    private static string Gerund(string verb) => verb.ToLowerInvariant() switch
    {
        "delete" => "deleting",
        "place" => "placing",
        var v => v + "ing",
    };

    /// <summary>비용 구절 → (낼 수 있는지, 내기, AI 추천, 설명)</summary>
    private static (Func<EffectContext, bool> CanPay, Action<EffectContext> Pay, Func<EffectContext, bool> Recommend, string Describe)? ParseCost(string cost, CardData card)
    {
        cost = cost.Trim();
        Match m;
        // ≪디지버스트≫: 이 디지몬의 진화원 N장 파기 (아래쪽부터 — 오래된 카드)
        if ((m = Regex.Match(cost, @"^trashing (?<n>\d+) of this Digimon's digivolution cards$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => c.Self != null && c.Self.EvolutionSources.Count >= n,
                    c => c.Engine.PayDigiBurst(c.Owner, c.Self!, n),
                    c => c.Self != null && c.Self.EvolutionSources.Count > n,
                    $"진화원 {n}장을 파기하여 (≪디지버스트 {n}≫)");
        }
        if (Regex.IsMatch(cost, @"^suspending this Digimon$", RegexOptions.IgnoreCase))
            return (c => c.Self != null && !c.Self.IsSuspended && c.Owner.BattleArea.Contains(c.Self), c => c.Engine.Suspend(c.Self!), _ => true, "이 디지몬을 레스트시켜");
        if (Regex.IsMatch(cost, @"^suspending this Tamer$", RegexOptions.IgnoreCase) && card.Type == CardType.Tamer)
            return (c => c.Owner.HasActiveTamer(c.Card), c => c.Engine.SuspendTamer(c.Owner, c.Card), _ => true, "이 테이머를 레스트시켜");
        if ((m = Regex.Match(cost, @"^suspending (?<n>\d+) of your other Digimon$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            IEnumerable<CardInstance> Others(EffectContext c) => c.Owner.BattleArea.Where(d => !d.IsSuspended && !ReferenceEquals(d, c.Self)).OrderBy(d => d.HasBlocker ? 1 : 0).ThenBy(d => d.GetCurrentDP());
            return (c => Others(c).Count() >= n, c => { foreach (var d in Others(c).Take(n).ToList()) c.Engine.Suspend(d); }, _ => true, $"다른 디지몬 {n}마리를 레스트시켜");
        }
        // "suspending 2 Digimon" · "suspending 1 of your [WG] trait Digimon" — 자신의 디지몬 (이 디지몬도 될 수 있다)
        if ((m = Regex.Match(cost, @"^suspending (?<n>\d+) (?:of your )?(?<spec>(?:[^,]*? )?Digimon(?: with [^,]+?)?)$", RegexOptions.IgnoreCase)).Success
            && !m.Groups["spec"].Value.StartsWith("other", StringComparison.OrdinalIgnoreCase)
            && TargetSpec.Parse($"all of your {m.Groups["spec"].Value}") is { } susSpec)
        {
            int n = int.Parse(m.Groups["n"].Value);
            IEnumerable<CardInstance> Able(EffectContext c) => c.Owner.BattleArea.Where(d => !d.IsSuspended && susSpec.Filters.All(f => f(d)) && !EffectRules.IsSuspendLocked(c.Engine.State, d))
                .OrderBy(d => d.HasBlocker ? 1 : 0).ThenBy(d => d.GetCurrentDP());
            return (c => Able(c).Count() >= n,
                    c => { foreach (var d in c.Engine.ChooseTargets(c.Owner, Able(c).ToList(), n, $"[{c.Card.Name}] 비용으로 레스트시킬 디지몬 {n}마리를 고르세요.", optional: false, preselected: null)) c.Engine.Suspend(d); },
                    _ => true, $"디지몬 {n}마리를 레스트시켜");
        }
        // "suspending 1 of your yellow Tamers"
        if ((m = Regex.Match(cost, $@"^suspending (?<n>\d+) of your (?:(?<color>{Colors}) )?Tamers?$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            CardColor? color = m.Groups["color"].Success ? Enum.Parse<CardColor>(m.Groups["color"].Value, ignoreCase: true) : null;
            IEnumerable<CardData> Able(EffectContext c) => c.Owner.Tamers.Distinct().Where(t => c.Owner.HasActiveTamer(t) && (color == null || t.AllColors.Contains(color.Value)));
            return (c => Able(c).Count() >= n, c => { foreach (var t in Able(c).Take(n).ToList()) c.Engine.SuspendTamer(c.Owner, t); }, _ => true, $"테이머 {n}명을 레스트시켜");
        }
        if (Regex.IsMatch(cost, @"^unsuspending this Digimon$", RegexOptions.IgnoreCase))
            return (c => c.Self != null && c.Self.IsSuspended && c.Owner.BattleArea.Contains(c.Self) && !EffectRules.IsUnsuspendLocked(c.Engine.State, c.Self),
                    c => c.Engine.Unsuspend(c.Self!), _ => true, "이 디지몬을 액티브로 하여");
        // "trashing the top security card of 1 player with the most security cards" — 시큐리티가 더 많은 쪽 (같으면 고른다 — 추천: 상대)
        if (Regex.IsMatch(cost, @"^trashing the top security card of (?:1|one|a) player with the most security cards$", RegexOptions.IgnoreCase))
            return (c => c.Owner.SecurityStack.Count + c.Enemy.SecurityStack.Count > 0,
                    c =>
                    {
                        int mine = c.Owner.SecurityStack.Count, theirs = c.Enemy.SecurityStack.Count;
                        var zones = mine > theirs ? c.Owner : theirs > mine ? c.Enemy
                            : c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 시큐리티 수가 같습니다. 상대의 시큐리티 위 1장을 파기할까요? (아니오 = 자신)", true) ? c.Enemy : c.Owner;
                        var x = zones.SecurityStack[0];
                        zones.SecurityStack.RemoveAt(0);
                        zones.Trash.Add(x);
                        c.Engine.NotifySecurityRemoved(zones, x);
                    },
                    c => c.Enemy.SecurityStack.Count >= c.Owner.SecurityStack.Count, "시큐리티가 가장 많은 플레이어의 시큐리티 위 1장을 파기하여");
        // "trashing the top or bottom card of your security stack" · "trashing your top or bottom security card" — 위 · 아래 중 고른다 (추천: 위)
        if (Regex.IsMatch(cost, @"^trashing (?:the top or bottom card of your security stack|your top or bottom security card)$", RegexOptions.IgnoreCase))
            return (c => c.Owner.SecurityStack.Count > 0,
                    c =>
                    {
                        var sec = c.Owner.SecurityStack;
                        int index = sec.Count > 1 && !c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] 시큐리티 맨 위 카드를 파기할까요? (아니오 = 맨 아래)", true) ? sec.Count - 1 : 0;
                        var x = sec[index];
                        sec.RemoveAt(index);
                        c.Owner.Trash.Add(x);
                        c.Engine.NotifySecurityRemoved(c.Owner, x);
                    },
                    c => c.Owner.SecurityStack.Count >= 3, "시큐리티 위 또는 아래 1장을 파기하여");
        // 진화원 아래에 놓기: "placing 1 card in your hand face down as this Digimon's bottom digivolution card" · "~ from your trash ~" · "placing your deck's top card ~"
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) (?:in|from) (?<from>your hand|your trash) (?:face down )?as (?:this Digimon's|its) bottom digivolution cards?$", RegexOptions.IgnoreCase)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } srcPick)
        {
            var (n, match) = srcPick;
            bool hand = m.Groups["from"].Value.EndsWith("hand", StringComparison.OrdinalIgnoreCase);
            List<CardData> Zone(EffectContext c) => hand ? c.Owner.Hand : c.Owner.Trash;
            return (c => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && Zone(c).Count(match) >= n,
                    c =>
                    {
                        var zone = Zone(c);
                        foreach (var x in c.Engine.ChooseCards(c.Owner, hand ? CardChoiceZone.Hand : CardChoiceZone.Trash, zone.Where(match).OrderBy(x => x.Level).ToList(), n, upTo: false, $"[{c.Card.Name}] 진화원 아래에 놓을 카드 {n}장을 고르세요."))
                        {
                            zone.Remove(x);
                            c.Self!.AddBottomSource(x);
                        }
                        c.RecalculateFieldBuffs();
                    },
                    c => !hand || c.Owner.Hand.Count >= n + 2, $"{(hand ? "패" : "트래시")}의 카드 {n}장을 진화원 아래에 놓아");
        }
        if (Regex.IsMatch(cost, @"^placing (?:your deck's top card|the top card of your deck) (?:face down )?as (?:this Digimon's|its) bottom digivolution card$", RegexOptions.IgnoreCase))
            return (c => c.Self != null && c.Owner.BattleArea.Contains(c.Self) && c.Owner.MainDeck.Count > 0,
                    c => { var x = c.Owner.MainDeck[0]; c.Owner.MainDeck.RemoveAt(0); c.Self!.AddBottomSource(x); c.RecalculateFieldBuffs(); },
                    c => c.Owner.MainDeck.Count >= 5, "덱 위 1장을 진화원 아래에 놓아");
        // "trashing the bottom face-down card from under any of your Tamers" — 테이머 아래 카드 (마지막에 놓인 것이 맨 아래)
        if ((m = Regex.Match(cost, @"^trashing (?:the|(?<n>\d+)) bottom (?:face[- ]down )?cards? from under (?:any of |one of |1 of )?your Tamers$", RegexOptions.IgnoreCase)).Success)
        {
            int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
            return (c => c.Owner.UnderTamers.Count >= n,
                    c => { for (int i = 0; i < n; i++) { var x = c.Owner.UnderTamers[^1]; c.Owner.UnderTamers.RemoveAt(c.Owner.UnderTamers.Count - 1); c.Owner.Trash.Add(x); } },
                    _ => true, $"테이머 아래 카드 {n}장을 파기하여");
        }
        if (Regex.IsMatch(cost, @"^trashing (?:the top card of your security stack|your top security card)$", RegexOptions.IgnoreCase))
            return (c => c.Owner.SecurityStack.Count > 0,
                    c => { c.Owner.Trash.Add(c.Owner.SecurityStack[0]); c.Owner.SecurityStack.RemoveAt(0); c.Engine.NotifySecurityRemoved(c.Owner); },
                    c => c.Owner.SecurityStack.Count >= 3, "시큐리티 위 1장을 파기하여");
        if ((m = Regex.Match(cost, @"^trashing (?<n>\d+) cards? (?:in|from) your hand$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => c.Owner.Hand.Count >= n, c => c.Engine.DiscardFromHand(c.Owner, n), c => c.Owner.Hand.Count >= n + 2, $"패 {n}장을 버려");
        }
        // "trashing 1 card with the [X] trait in your hand" · "trashing 1 [X] trait card from your hand"
        if ((m = Regex.Match(cost, @"^trashing (?<pick>.+?) (?:in|from) your hand$", RegexOptions.IgnoreCase)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } pick)
        {
            var (n, match) = pick;
            return (c => c.Owner.Hand.Count(match) >= n,
                    c =>
                    {
                        var trashed = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, c.Owner.Hand.Where(match).OrderBy(x => x.Level).ToList(), n, upTo: false, $"[{c.Card.Name}] 비용으로 버릴 패 {n}장을 고르세요.");
                        foreach (var x in trashed)
                        {
                            c.Owner.Hand.Remove(x);
                            c.Owner.Trash.Add(x);
                        }
                        c.Engine.NotifyHandTrashed(c.Owner, trashed);
                    },
                    c => c.Owner.Hand.Count >= n + 2, $"패에서 조건에 맞는 카드 {n}장을 버려");
        }
        // 앞면 시큐리티: "flipping your top face-up security card face down"
        if (Regex.IsMatch(cost, @"^flipping your top face[- ]up security card face[- ]down$", RegexOptions.IgnoreCase))
            return (c => GoldfishEngine.FaceUpCards(c.Owner).Count > 0, c => GoldfishEngine.FlipTopFaceUpDown(c.Owner), _ => true, "앞면 시큐리티 1장을 뒷면으로 뒤집어");
        // "placing 1 Digimon card with the [Royal Base] trait from your hand face up as the bottom security card" · "~ in your hand ~" · "~ from your hand or trash ~"
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) (?:from|in) (?<from>your hand or (?:your )?trash|your trash|your hand) face[- ]up as (?:the|your) (?<pos>top|bottom) security card$", RegexOptions.IgnoreCase)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } fuCost)
        {
            var (n, match) = fuCost;
            bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase);
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash");
            IEnumerable<PoolCard> Pool(EffectContext c) => (fromHand ? PoolFrom(c.Owner.Hand, match) : Enumerable.Empty<PoolCard>()).Concat(fromTrash ? PoolFrom(c.Owner.Trash, match) : Enumerable.Empty<PoolCard>());
            return (c => Pool(c).Count() >= n,
                    c =>
                    {
                        foreach (var p in ChooseFromPool(c, fromHand && fromTrash ? CardChoiceZone.HandOrTrash : fromHand ? CardChoiceZone.Hand : CardChoiceZone.Trash, Pool(c).OrderBy(x => x.Card.PlayCost).ToList(), n,
                                     $"[{c.Card.Name}] 비용으로 시큐리티에 앞면으로 놓을 카드 {n}장을 고르세요."))
                            if (TakeFromPool(c, p)) c.Engine.PlaceSecurityFaceUp(c.Owner, p.Card, top);
                    },
                    _ => true, $"카드 {n}장을 시큐리티에 앞면으로 놓아");
        }
        // "playing 1 [Lucemon: Larva] from your trash to your empty breeding area without paying the cost" — 육성 에어리어에 등장 (【등장 시】 없음)
        if ((m = Regex.Match(cost, @"^playing (?<pick>.+?) from (?<from>your hand or (?:your )?trash|your trash|your hand) to (?:your empty|an empty space in your|the empty space in your) breeding area without paying (?:the|its) cost$", RegexOptions.IgnoreCase)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } bpick)
        {
            var match = bpick.Match;
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash");
            Func<CardData, bool> ok = x => x.Type == CardType.Digimon && match(x);
            IEnumerable<PoolCard> Pool(EffectContext c) => (fromHand ? PoolFrom(c.Owner.Hand, ok) : Enumerable.Empty<PoolCard>()).Concat(fromTrash ? PoolFrom(c.Owner.Trash, ok) : Enumerable.Empty<PoolCard>());
            return (c => c.Owner.BreedingArea == null && Pool(c).Any(),
                    c =>
                    {
                        var p = ChooseFromPool(c, fromHand && fromTrash ? CardChoiceZone.HandOrTrash : fromHand ? CardChoiceZone.Hand : CardChoiceZone.Trash, Pool(c).ToList(), 1,
                            $"[{c.Card.Name}] 비용으로 육성 에어리어에 등장시킬 카드를 고르세요.").FirstOrDefault();
                        if (p.Card != null && TakeFromPool(c, p)) c.Engine.PlayToBreeding(c.Owner, p.Card);
                    },
                    _ => true, "육성 에어리어에 카드를 등장시켜");
        }
        // "returning 2 non-Digi-Egg cards from your trash to the bottom of the deck" · "returning 1 Digi-Egg card from your trash to the bottom of the Digi-Egg deck"
        if ((m = Regex.Match(cost, @"^returning (?<pick>.+?) from your trash to the (?<pos>top|bottom) of (?:the|your) (?<egg>Digi-Egg )?deck(?: in any order)?$", RegexOptions.IgnoreCase)).Success
            && (Regex.Match(m.Groups["pick"].Value, @"^(?<n>\d+) Digi-Egg cards?$", RegexOptions.IgnoreCase) is { Success: true } eggPick
                    ? ((int, Func<CardData, bool>)?)(int.Parse(eggPick.Groups["n"].Value), x => x.Type == CardType.Digitama)
                    : ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } rpick)
        {
            var (n, match) = rpick;
            bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase), toEggDeck = m.Groups["egg"].Success;
            if (toEggDeck && top) return null;
            Func<CardData, bool> ok = x => match(x) && (toEggDeck ? x.Type == CardType.Digitama : x.Type != CardType.Digitama);
            return (c => c.Owner.Trash.Count(ok) >= n,
                    c =>
                    {
                        var picked = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Trash, c.Owner.Trash.Where(ok).OrderBy(x => x.Level).ToList(), n, upTo: false,
                            $"[{c.Card.Name}] 비용으로 트래시에서 {(toEggDeck ? "디지타마 덱" : "덱")} {(top ? "위" : "아래")}로 되돌릴 카드 {n}장을 고르세요.").ToList();
                        foreach (var x in picked) c.Owner.Trash.Remove(x);
                        if (toEggDeck) GoldfishEngine.AddToDigitamaDeckBottom(c.Owner, picked);
                        else if (top) c.Owner.MainDeck.InsertRange(0, picked);
                        else c.Owner.MainDeck.AddRange(picked);
                    },
                    c => c.Owner.Trash.Count(ok) >= n + 3, $"트래시의 카드 {n}장을 {(toEggDeck ? "디지타마 덱" : "덱")} {(top ? "위" : "아래")}로 되돌려");
        }
        if ((m = Regex.Match(cost, @"^deleting (?<n>\d+) of your (?<other>other )?Digimon$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); bool other = m.Groups["other"].Success;
            IEnumerable<CardInstance> Victims(EffectContext c) => c.Owner.BattleArea.Where(d => !other || !ReferenceEquals(d, c.Self)).OrderBy(d => d.TopCard.Level).ThenBy(d => d.GetCurrentDP());
            return (c => Victims(c).Count() >= n, c => { foreach (var d in Victims(c).Take(n).ToList()) c.DeleteOwn(d); },
                    c => Victims(c).Take(n).All(d => d.TopCard.Level <= 3), $"자신의 디지몬 {n}마리를 소멸시켜");
        }
        if (Regex.IsMatch(cost, @"^deleting this Digimon$", RegexOptions.IgnoreCase))
            return (c => c.Self != null && c.Owner.BattleArea.Contains(c.Self), c => c.DeleteOwn(c.Self!), _ => false, "이 디지몬을 소멸시켜");
        // "deleting one of your other Digimon" · "deleting 1 of your [Cerberusmon]" · "returning 1 of your other Digimon to its owner's hand"
        if ((m = Regex.Match(cost, @"^(?<v>deleting|returning) (?:one|1) (?:of your )?(?<other>other )?(?<what>[^,]*?)(?: to (?:its owner's|your|the) hand)?$", RegexOptions.IgnoreCase)).Success
            && !Regex.IsMatch(m.Groups["what"].Value, @"\b(?:card|cards)\b", RegexOptions.IgnoreCase))
        {
            bool delete = m.Groups["v"].Value.Equals("deleting", StringComparison.OrdinalIgnoreCase), other = m.Groups["other"].Success;
            string what = m.Groups["what"].Value.Trim();
            if (!delete && !cost.Contains(" hand", StringComparison.OrdinalIgnoreCase)) return null;
            Func<CardInstance, bool>? filter = null;
            if (Regex.IsMatch(what, @"^\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*$"))
            {
                var names = Regex.Matches(what, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value).ToList();
                filter = d => names.Any(n => TargetSpec.NameHas(d.TopCard, n));
            }
            else if (TargetSpec.Parse("all of your " + what) is { } spec) filter = d => spec.Filters.All(f => f(d));
            if (filter == null) return null;
            IEnumerable<CardInstance> Pool(EffectContext c) => c.Owner.BattleArea.Where(d => (!other || !ReferenceEquals(d, c.Self)) && filter(d))
                .OrderBy(d => d.TopCard.Level).ThenBy(d => d.GetCurrentDP());
            return (c => Pool(c).Any(),
                    c =>
                    {
                        var d = c.Engine.AskDigimon(c.Owner, delete ? "비용으로 소멸시킬 자신의 디지몬을 고르세요." : "비용으로 패로 되돌릴 자신의 디지몬을 고르세요.", Pool(c).ToList(), Pool(c).First(), optional: false) ?? Pool(c).First();
                        if (delete) c.DeleteOwn(d); else c.Engine.ReturnToHand(c.Owner, d);
                    },
                    c => Pool(c).First().TopCard.Level <= 3 || !delete, delete ? "자신의 디지몬 1마리를 소멸시켜" : "자신의 디지몬 1마리를 패로 되돌려");
        }
        // 링크 카드 비용: "trashing 1 of this Digimon's link cards" · "placing 1 of its link cards as its bottom digivolution card"
        if ((m = Regex.Match(cost, @"^(?<v>trashing|placing) (?<n>\d+) of (?:this Digimon's|its) link cards(?<bottom> as (?:its|this Digimon's) bottom digivolution cards?)?$", RegexOptions.IgnoreCase)).Success
            && (m.Groups["v"].Value.Equals("trashing", StringComparison.OrdinalIgnoreCase) || m.Groups["bottom"].Success))
        {
            int n = int.Parse(m.Groups["n"].Value);
            bool trash = m.Groups["v"].Value.Equals("trashing", StringComparison.OrdinalIgnoreCase);
            return (c => c.Self != null && c.Self.LinkedCards.Count >= n,
                    c =>
                    {
                        foreach (var x in c.Engine.ChooseCards(c.Owner, CardChoiceZone.Other, c.Self!.LinkedCards.OrderBy(x => GoldfishEngine.LinkDp(x)).ToList(), n, upTo: false, $"[{c.Card.Name}] 비용으로 쓸 링크 카드 {n}장을 고르세요."))
                        {
                            if (!c.Self.LinkedCards.Remove(x)) continue;
                            if (trash) c.Owner.Trash.Add(x); else c.Self.AddBottomSource(x);
                        }
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, trash ? $"링크 카드 {n}장을 파기하여" : $"링크 카드 {n}장을 진화원 맨 아래에 놓아");
        }
        // "placing 1 Digimon card with ＜Save＞ in its text from your hand (face down) under this Tamer"
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) from your hand (?:face down )?under (?:this Tamer|(?:1|one) of your Tamers|your Tamers)$", RegexOptions.IgnoreCase)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } underPick)
        {
            var (n, match) = underPick;
            return (c => c.Owner.Tamers.Count > 0 && c.Owner.Hand.Count(match) >= n,
                    c =>
                    {
                        foreach (var x in c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, c.Owner.Hand.Where(match).OrderBy(x => x.Level).ToList(), n, upTo: false, $"[{c.Card.Name}] 테이머 아래에 놓을 패 {n}장을 고르세요."))
                        {
                            c.Owner.Hand.Remove(x);
                            c.Owner.UnderTamers.Add(x);
                        }
                    },
                    c => c.Owner.Hand.Count >= n + 2, $"패 {n}장을 테이머 아래에 놓아");
        }
        // "adding the top card of your security stack to your hand"
        if (Regex.IsMatch(cost, @"^adding (?:the top card of your security stack|your top security card) to (?:the|your) hand$", RegexOptions.IgnoreCase))
            return (c => c.Owner.SecurityStack.Count > 0,
                    c => { c.Owner.Hand.Add(c.Owner.SecurityStack[0]); c.Owner.SecurityStack.RemoveAt(0); c.Engine.NotifySecurityRemoved(c.Owner); },
                    c => c.Owner.SecurityStack.Count >= 3, "시큐리티 위 1장을 패에 넣어");
        // "placing 1 [Durandamon] or [BryweLudramon] from your hand on top of this card's digivolution cards"
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) from your hand (?:on top of|at the top of) this (?:card|Digimon)'s digivolution cards$", RegexOptions.IgnoreCase)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } placePick)
        {
            var (n, match) = placePick;
            return (c => c.Self != null && c.Owner.Hand.Count(match) >= n,
                    c =>
                    {
                        foreach (var x in c.Engine.ChooseCards(c.Owner, CardChoiceZone.Hand, c.Owner.Hand.Where(match).OrderBy(x => x.Level).ToList(), n, upTo: false, $"[{c.Card.Name}] 진화원 위에 놓을 패 {n}장을 고르세요."))
                        {
                            c.Owner.Hand.Remove(x);
                            c.Self!.EvolutionSources.Push(x);   // 진화원 맨 위 (맨 위 카드 바로 아래)
                        }
                    },
                    _ => true, $"패의 카드 {n}장을 이 디지몬의 진화원 위에 놓아");
        }
        if (Regex.IsMatch(cost, @"^returning this Tamer to the bottom of the deck$", RegexOptions.IgnoreCase) && card.Type == CardType.Tamer)
            return (c => c.Owner.Tamers.Contains(c.Card),
                    c => { c.Owner.Tamers.Remove(c.Card); c.Owner.SuspendedTamers.Remove(c.Card); c.Owner.MainDeck.Add(c.Card); c.Engine.ClearUnderTamersIfNoTamer(c.Owner); },
                    _ => true, "이 테이머를 덱 아래로 되돌려");
        if ((m = Regex.Match(cost, @"^paying (?<n>\d+) (?:memory|cost)$|^decreasing your memory by (?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => true, c => c.AddMemory(-n), c => c.OwnerMemory - n >= 0, $"메모리 {n}을(를) 지불하여");
        }
        // "placing 1 [ShoeShoemon] from your trash as any of your [Shoemon]'s bottom digivolution card" · "~ from your hand under one of your [Jellymon] as its bottom digivolution card"
        //  — 고른 자신의 디지몬 아래에 놓는다 (그 디지몬이 다음 문장의 "it" · "that Digimon")
        if ((m = Regex.Match(cost, @"^placing (?<pick>.+?) (?:in|from) (?<from>your hand|your trash) (?:face down )?(?:as (?<t>(?:any|1|one) of your .+?)'s bottom digivolution cards?|under (?<t>(?:any|1|one) of your .+?) as (?:its|their) bottom digivolution cards?)$", RegexOptions.IgnoreCase)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } underPickOther
            && OwnDigimonFilter(Regex.Replace(m.Groups["t"].Value, @"\]s\b", "]")) is { } otherFilter)
        {
            var (n, match) = underPickOther;
            bool hand = m.Groups["from"].Value.EndsWith("hand", StringComparison.OrdinalIgnoreCase);
            List<CardData> Zone(EffectContext c) => hand ? c.Owner.Hand : c.Owner.Trash;
            List<CardInstance> Targets(EffectContext c) => c.Owner.BattleArea.Where(otherFilter).ToList();
            return (c => Targets(c).Count > 0 && Zone(c).Count(x => match(x) && !ReferenceEquals(x, c.Card)) >= n,
                    c =>
                    {
                        var d = c.Engine.ChooseTargets(c.Owner, Targets(c), 1, $"[{c.Card.Name}] 진화원 맨 아래에 카드를 놓을 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        if (d == null) return;
                        var zone = Zone(c);
                        foreach (var x in c.Engine.ChooseCards(c.Owner, hand ? CardChoiceZone.Hand : CardChoiceZone.Trash, zone.Where(x => match(x) && !ReferenceEquals(x, c.Card)).ToList(), n, upTo: false, $"[{c.Card.Name}] [{d.TopCard.Name}]의 진화원 맨 아래에 놓을 카드를 고르세요."))
                        {
                            zone.Remove(x);
                            d.AddBottomSource(x);
                        }
                        c.LastTargets = new List<CardInstance> { d };
                        c.RecalculateFieldBuffs();
                    },
                    _ => true, $"{(hand ? "패" : "트래시")}의 카드를 자신의 디지몬 진화원 맨 아래에 놓아");
        }
        // [Hand] [Main]: "paying 1 cost and placing this card as the bottom digivolution card of 1 of your Digimon that's level 3 or has the [Legend-Arms] trait"
        //  · "~ as 1 of your level 5 or higher Digimon's bottom digivolution card" — 놓은 디지몬이 다음 문장의 "that Digimon"
        if ((m = Regex.Match(cost, @"^paying (?<n>\d+) (?:memory|cost) and placing this card (?:as the bottom digivolution card of (?<t>.+)|as (?<t>.+?)'s bottom digivolution card|under (?<t>.+?) as its bottom digivolution card)$", RegexOptions.IgnoreCase)).Success
            && OwnDigimonFilter(m.Groups["t"].Value) is { } underFilter)
        {
            int n = int.Parse(m.Groups["n"].Value);
            List<CardInstance> Targets(EffectContext c) => c.Owner.BattleArea.Where(underFilter).OrderByDescending(d => d.GetCurrentDP()).ToList();
            return (c => (c.Owner.Hand.Contains(c.Card) || c.Owner.Trash.Contains(c.Card)) && Targets(c).Count > 0,
                    c =>
                    {
                        var d = c.Engine.ChooseTargets(c.Owner, Targets(c), 1, $"[{c.Card.Name}] 이 카드를 진화원 맨 아래에 놓을 디지몬을 고르세요.", optional: false, preselected: null).FirstOrDefault();
                        if (d == null) return;
                        c.AddMemory(-n);
                        if (!c.Owner.Hand.Remove(c.Card)) c.Owner.Trash.Remove(c.Card);
                        d.AddBottomSource(c.Card);
                        c.LastTargets = new List<CardInstance> { d };
                        c.RecalculateFieldBuffs();
                    },
                    c => c.OwnerMemory - n >= 0, $"메모리 {n}을(를) 지불하고 이 카드를 진화원 맨 아래에 놓아");
        }
        return null;
    }

    /// <summary>"1 of your Digimon with ~" · "any of your level 5 or higher Digimon" · "1 of your Digimon that's level 3 or has the [X] trait" → 자신의 디지몬 조건</summary>
    private static Func<CardInstance, bool>? OwnDigimonFilter(string phrase)
    {
        phrase = Regex.Replace(phrase.Trim(), @"^(?:any|one) of your ", "1 of your ", RegexOptions.IgnoreCase);
        var m = Regex.Match(phrase, $@"^1 of your Digimon that's level (?<lv>\d+) or has the (?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            int lv = int.Parse(m.Groups["lv"].Value);
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            return d => d.TopCard.Level == lv || xs.Any(x => TargetSpec.HasTrait(d.TopCard, x));
        }
        if (TargetSpec.Parse(phrase) is { Opponent: false, AnySide: false, IsThis: false, IsThat: false } spec)
            return d => spec.Filters.All(f => f(d));
        // 이름만: "1 of your [Shoemon]" · "1 of your [Jellymon] or [TeslaJellymon]"
        if ((m = Regex.Match(phrase, $@"^1 of your (?<x>{TargetSpec.NameList})$", RegexOptions.IgnoreCase)).Success)
        {
            var names = TargetSpec.Names(m.Groups["x"].Value);
            return d => names.Any(x => TargetSpec.NameHas(d.TopCard, x));
        }
        return null;
    }

    private static Func<EffectContext, bool>? ParseEventCondition(string cond)
    {
        cond = cond.Trim();
        if (ParseDigimonCondition(cond) is { } dc) return c => dc(c.Self);
        if (ParseSideCondition(cond) is { } sc) return c => sc(c.Owner, c.Enemy);
        if (Regex.IsMatch(cond, @"^(?:it is )?during an attack$", RegexOptions.IgnoreCase)) return c => c.Engine.CurrentAttacker != null;   // 어택 선언 ~ 어택 종료
        if (Regex.IsMatch(cond, @"^this card was face[- ]up$", RegexOptions.IgnoreCase)) return c => c.Engine.LastCheckedWasFaceUp;   // [시큐리티] 체크한 이 카드가 앞면이었는지
        if (Regex.Match(cond, @"^DigiXrosing(?: with (?<n>\d+) (?:or more )?cards)?$", RegexOptions.IgnoreCase) is { Success: true } xr)
        { int need = xr.Groups["n"].Success ? int.Parse(xr.Groups["n"].Value) : 1; return c => c.Engine.XrosCountOf(c.Self) >= need; }   // 디지크로스로 등장하는 중 (놓은 장수)
        if (Regex.IsMatch(cond, @"^attacking by ＜Execute＞$", RegexOptions.IgnoreCase)) return c => c.Engine.IsExecuteAttack(c.Other ?? c.Self);   // ≪에그제큐트≫ 어택 중 (어택 사건이면 Other = 어택한 디지몬)
        if (Regex.IsMatch(cond, @"^DNA digivolving$", RegexOptions.IgnoreCase)) return c => c.Engine.IsDnaDigivolving(c.Self);   // DNA 진화로 [진화 시] 중
        if (Regex.IsMatch(cond, @"^this effect DNA digivolved$", RegexOptions.IgnoreCase)) return c => c.LastStepResolved;   // 앞 문장의 DNA 진화가 일어났는지
        if (Regex.Match(cond, $@"^any of them (?:have|has) the (?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase) is { Success: true } anyOf)
        { var xs = TargetSpec.Names(anyOf.Groups["x"].Value); return c => c.TriggerCard != null && xs.Any(x => TargetSpec.HasTrait(c.TriggerCard, x)); }   // "When cards are placed face up ~, if any of them have the [X] trait"
        Match m;
        if ((m = Regex.Match(cond, @"^your hand has (?<n>\d+) or (?<dir>fewer|less|more) cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value == "more"; return c => more ? c.Owner.Hand.Count >= n : c.Owner.Hand.Count <= n; }
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) or (?<dir>less|fewer|more) memory$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value == "more"; return c => more ? c.OwnerMemory >= n : c.OwnerMemory <= n; }
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) or (?<dir>fewer|less|more) security cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value == "more"; return c => more ? c.Owner.SecurityStack.Count >= n : c.Owner.SecurityStack.Count <= n; }
        if ((m = Regex.Match(cond, @"^your opponent has (?<n>\d+) or (?<dir>fewer|less|more) security cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value == "more"; return c => more ? c.Enemy.SecurityStack.Count >= n : c.Enemy.SecurityStack.Count <= n; }
        if (Regex.IsMatch(cond, @"^your opponent has (?:a|any) Digimon$", RegexOptions.IgnoreCase)) return c => c.Enemy.BattleArea.Count > 0;
        if ((m = Regex.Match(cond, @"^your opponent has (?<n>\d+) or more Digimon$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return c => c.Enemy.BattleArea.Count >= n; }
        if (Regex.IsMatch(cond, @"^you have a Tamer$", RegexOptions.IgnoreCase)) return c => c.Owner.Tamers.Count > 0;
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) or more Tamers$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return c => c.Owner.Tamers.Count >= n; }
        if ((m = Regex.Match(cond, @"^you have (?:a|an) (?<kind>Digimon|Tamer) with \[(?<x>[^\]]+)\] in its name$", RegexOptions.IgnoreCase)).Success)
        {
            string x = m.Groups["x"].Value; bool tamer = m.Groups["kind"].Value.Equals("Tamer", StringComparison.OrdinalIgnoreCase);
            return c => tamer ? c.Owner.Tamers.Any(t => TargetSpec.NameHas(t, x)) : c.Owner.BattleArea.Any(d => TargetSpec.NameHas(d.TopCard, x));
        }
        if ((m = Regex.Match(cond, @"^you have (?:a|an) (?:\[(?<x>[^\]]+)\] trait (?<kind>Digimon|Tamer)|(?<kind>Digimon|Tamer) with the \[(?<x>[^\]]+)\] trait)$", RegexOptions.IgnoreCase)).Success)
        {
            string x = m.Groups["x"].Value; bool tamer = m.Groups["kind"].Value.Equals("Tamer", StringComparison.OrdinalIgnoreCase);
            return c => tamer ? c.Owner.Tamers.Any(t => TargetSpec.HasTrait(t, x)) : c.Owner.BattleArea.Any(d => TargetSpec.HasTrait(d.TopCard, x));
        }
        if ((m = Regex.Match(cond, @"^(?:this Digimon|it) has (?<n>\d+) or more digivolution cards$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return c => (c.Self?.EvolutionSources.Count ?? 0) >= n; }
        if ((m = Regex.Match(cond, @"^(?:there are|you have) (?<n>\d+) or more cards in your trash$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return c => c.Owner.Trash.Count >= n; }
        if (Regex.IsMatch(cond, @"^it's your turn$", RegexOptions.IgnoreCase)) return c => c.Engine.IsTurnOf(c.Owner);
        if (Regex.IsMatch(cond, @"^it's (?:your opponent's|their) turn$", RegexOptions.IgnoreCase)) return c => !c.Engine.IsTurnOf(c.Owner);
        if (Regex.IsMatch(cond, @"^this (?:card|Digimon) was deleted by (?:an|your opponent's) effects?$", RegexOptions.IgnoreCase))
            return c => c.Engine.CurrentDeletionCause is GoldfishEngine.DeletionCause.Effect or GoldfishEngine.DeletionCause.Cost;
        if (Regex.IsMatch(cond, @"^this (?:card|Digimon) was deleted in battle$", RegexOptions.IgnoreCase))
            return c => c.Engine.CurrentDeletionCause == GoldfishEngine.DeletionCause.Battle;
        if (Regex.IsMatch(cond, @"^you do$", RegexOptions.IgnoreCase))
            return c => c.LastStepResolved;   // "If you do, ~" — 앞 문장을 실제로 처리했을 때
        // 공개 효과 뒤: "If you added cards" · "If this effect added to a hand" · "If a card is placed by this effect" — 공개한 카드를 1장이라도 가져갔을 때
        if (Regex.IsMatch(cond, @"^(?:you added (?:a card|cards?)(?: to (?:the|your) hand)?|this effect added (?:a card |cards )?to (?:a|the|your) hand|a card (?:is|was) placed by this effect|this effect placed(?: a card| cards)?)$", RegexOptions.IgnoreCase))
            return c => c.LastStepResolved;
        // "1 Digimon"(양쪽)을 레스트한 뒤: "If this effect suspended your Digimon" · "If this effect unsuspended"
        if (Regex.IsMatch(cond, @"^this effect (?:suspended|suspends) (?:1 of )?your Digimon$", RegexOptions.IgnoreCase))
            return c => c.LastStepResolved && c.LastTargets.Any(d => d.IsSuspended && c.Owner.BattleArea.Contains(d));
        if (Regex.IsMatch(cond, @"^this effect (?:suspended|suspends) (?:1 of )?your opponent's Digimon$", RegexOptions.IgnoreCase))
            return c => c.LastStepResolved && c.LastTargets.Any(d => d.IsSuspended && c.Enemy.BattleArea.Contains(d));
        if (Regex.IsMatch(cond, @"^this effect (?:unsuspended|unsuspends)(?: (?:a|that|your) Digimon)?$", RegexOptions.IgnoreCase))
            return c => c.LastStepResolved && c.LastTargets.Any(d => !d.IsSuspended);
        if (Regex.IsMatch(cond, @"^no Digimon (?:was|were|is) deleted by this effect$", RegexOptions.IgnoreCase))
            return c => !c.LastTargets.Any(d => !c.Owner.BattleArea.Contains(d) && !c.Enemy.BattleArea.Contains(d));
        if (Regex.IsMatch(cond, @"^you have no other Digimon(?: in play)?$", RegexOptions.IgnoreCase))
            return c => c.Owner.BattleArea.All(d => ReferenceEquals(d, c.Self));
        if ((m = Regex.Match(cond, @"^(?:that Digimon's|its) play cost is (?<n>\d+) or (?<dir>less|more|lower|higher)$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); bool less = m.Groups["dir"].Value is "less" or "lower";
            return c => c.LastTargets.FirstOrDefault() is { } d && (less ? d.TopCard.PlayCost <= n : d.TopCard.PlayCost >= n);
        }
        if ((m = Regex.Match(cond, @"^(?:that Digimon|it) has (?<n>\d+) DP or (?<dir>more|less)$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value.Equals("more", StringComparison.OrdinalIgnoreCase);
            return c => c.LastTargets.FirstOrDefault() is { } d && (more ? d.GetCurrentDP() >= n : d.GetCurrentDP() <= n);
        }
        if (Regex.IsMatch(cond, @"^you have a Tamer in play$", RegexOptions.IgnoreCase)) return c => c.Owner.Tamers.Count > 0;
        // DNA 진화 · 디지크로스는 아직 엔진에 없으므로 "~했다면"은 언제나 거짓 (특수 메커닉 — 7단계)
        if (Regex.IsMatch(cond, @"^(?:DNA digivolving|DigiXrosing|it DNA digivolved|this Digimon DNA digivolved|DigiXrosing with \d+ or more cards)$", RegexOptions.IgnoreCase)) return _ => false;
        if ((m = Regex.Match(cond, $@"^you have (?:a|an) (?<color>{Colors}) (?<kind>Tamer|Digimon)(?: in play)?$", RegexOptions.IgnoreCase)).Success)
        {
            var color = Enum.Parse<CardColor>(m.Groups["color"].Value, ignoreCase: true); bool tamer = m.Groups["kind"].Value.Equals("Tamer", StringComparison.OrdinalIgnoreCase);
            return c => tamer ? c.Owner.Tamers.Any(t => t.AllColors.Contains(color)) : c.Owner.BattleArea.Any(d => d.TopCard.AllColors.Contains(color));
        }
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) security cards?$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); return c => c.Owner.SecurityStack.Count == n; }
        if (Regex.IsMatch(cond, @"^you (?:don't|do not) have (?:a|any) Digimon$", RegexOptions.IgnoreCase)) return c => c.Owner.BattleArea.Count == 0;
        if (Regex.IsMatch(cond, @"^your opponent (?:doesn't|does not) have (?:a|any) Digimon$", RegexOptions.IgnoreCase)) return c => c.Enemy.BattleArea.Count == 0;
        if ((m = Regex.Match(cond, $@"^(?:any of them|it|that Digimon) (?:have|has) (?:the )?(?<x>{TargetSpec.NameList}) traits?$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return c => c.Other != null && xs.Any(x => TargetSpec.HasTrait(c.Other.TopCard, x)); }
        if ((m = Regex.Match(cond, $@"^(?:any of them|it|that Digimon) (?:have|has) (?<x>{TargetSpec.NameList}) in (?:its|their) names?$", RegexOptions.IgnoreCase)).Success)
        { var xs = TargetSpec.Names(m.Groups["x"].Value); return c => c.Other != null && xs.Any(x => TargetSpec.NameHas(c.Other.TopCard, x)); }
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) or (?<dir>fewer|less|more) Tamers$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value == "more"; return c => more ? c.Owner.Tamers.Count >= n : c.Owner.Tamers.Count <= n; }
        if ((m = Regex.Match(cond, @"^you have (?<n>\d+) or (?<dir>fewer|less|more) cards in your hand$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value == "more"; return c => more ? c.Owner.Hand.Count >= n : c.Owner.Hand.Count <= n; }
        if ((m = Regex.Match(cond, @"^your opponent has (?<n>\d+) or (?<dir>fewer|less|more) cards in (?:their|your opponent's) hand$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value == "more"; return c => more ? c.Enemy.Hand.Count >= n : c.Enemy.Hand.Count <= n; }
        if ((m = Regex.Match(cond, @"^your opponent has (?<n>\d+) or (?<dir>fewer|less|more) memory$", RegexOptions.IgnoreCase)).Success)
        { int n = int.Parse(m.Groups["n"].Value); bool more = m.Groups["dir"].Value == "more"; return c => more ? -c.OwnerMemory >= n : -c.OwnerMemory <= n; }
        return ParseZoneCondition(cond) is { } zc ? c => zc(c.Owner) : null;   // 육성 에어리어 · 트래시 매수 등 자신의 영역 조건
    }

    // =====================================================================
    // 동작
    // =====================================================================

    private enum Duration { None, ThisTurn, OpponentsTurnEnd, OpponentsNextTurnEnd, YourTurnEnd, YourNextTurnEnd, ThisAttack }

    private static Duration ParseDuration(string text, out string rest)
    {
        rest = text;
        var pairs = new (string Pattern, Duration D)[]
        {
            (@"\s*for the turn$", Duration.ThisTurn),
            (@"\s*for the attack$", Duration.ThisAttack),
            (@"\s*until (?:their|your opponent's|the opponent's) turn ends$", Duration.OpponentsTurnEnd),
            (@"\s*until your turn ends$", Duration.YourTurnEnd),
            (@"\s*until the end of (?:your opponent's|their|the opponent's) next turn$", Duration.OpponentsNextTurnEnd),
            (@"\s*until the end of (?:your opponent's|their|the opponent's) turn$", Duration.OpponentsTurnEnd),
            (@"\s*until the end of your turn$", Duration.YourTurnEnd),
            (@"\s*until the end of (?:the|this) turn$", Duration.ThisTurn),
            (@"\s*until the end of your next turn$", Duration.YourNextTurnEnd),
        };
        foreach (var (p, d) in pairs)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success) { rest = text[..m.Index]; return d; }
        }
        // "For the turn, ~" / "Until the end of your opponent's turn, ~" 앞에 붙는 형태
        //   "Until your opponent's turn ends, ~" · "Until their turn ends, ~" · "Until your turn ends, ~" 도 같은 뜻
        var lead = Regex.Match(text, @"^(?<d>for the turn|until the end of (?:your opponent's|their) (?:next )?turn|until the end of your turn|until (?:your opponent's|their) turn ends|until your turn ends), (?<rest>.+)$", RegexOptions.IgnoreCase);
        if (lead.Success)
        {
            rest = lead.Groups["rest"].Value;
            string d = lead.Groups["d"].Value.ToLowerInvariant();
            if (d.EndsWith("turn ends")) d = d.Contains("opponent") || d.Contains("their") ? "until the end of your opponent's turn" : "until the end of your turn";
            return d == "for the turn" ? Duration.ThisTurn : d.Contains("next") ? Duration.OpponentsNextTurnEnd : d.EndsWith("your turn") && !d.Contains("opponent") && !d.Contains("their") ? Duration.YourTurnEnd : Duration.OpponentsTurnEnd;
        }
        return Duration.None;
    }

    /// <summary>기간 → TurnSequence 기준 끝나는 턴 (효과 주인의 턴인지에 따라)</summary>
    private static int Until(EffectContext c, Duration d)
    {
        bool mine = c.Engine.IsTurnOf(c.Owner);
        int seq = c.TurnSequence;
        return d switch
        {
            Duration.ThisTurn => seq,
            Duration.OpponentsTurnEnd => mine ? seq + 1 : seq,
            Duration.OpponentsNextTurnEnd => mine ? seq + 1 : seq + 2,
            Duration.YourTurnEnd => mine ? seq : seq + 1,
            Duration.YourNextTurnEnd => mine ? seq + 2 : seq + 1,
            _ => seq,
        };
    }

    private static (Action<EffectContext>? Act, string? Error) ParseAction(string s, CardData card, List<EffectTrigger> triggers)
    {
        bool security = triggers.Contains(EffectTrigger.Security);
        string label = string.Join(" · ", triggers.Select(t => Labels.GetValueOrDefault(t, t.ToString())));
        Match m;

        // "Unsuspend 1 of your Digimon and suspend 1 of your opponent's Digimon" · "Unsuspend all of your Digimon with ＜Blocker＞ and they get +5000 DP"
        // — 대상 구절이 and 뒤까지 삼키기 전에 두 동작으로 나눈다
        if ((m = Regex.Match(s, @"^(?<l>(?:Suspend|Unsuspend|Delete|Return|Trash)\b.+?) and (?<r>(?:suspend|unsuspend|delete|return|trash|gain|draw|have|they|it|that Digimon|those Digimon)\b.+)$", RegexOptions.IgnoreCase)).Success)
        {
            string r = m.Groups["r"].Value;
            var a = ParseAction(m.Groups["l"].Value, card, triggers);
            var b = ParseAction(char.ToUpperInvariant(r[0]) + r[1..], card, triggers);
            if (a.Error == null && b.Error == null)
            {
                var (actA, actB) = (a.Act!, b.Act!);
                return (c => { actA(c); actB(c); }, null);
            }
        }
        // "Delete 1 of your opponent's unsuspended level 4 Digimon and 1 of your opponent's unsuspended level 5 Digimon" — 같은 동사, 두 대상
        if ((m = Regex.Match(s, @"^(?<v>Delete|Suspend|Unsuspend) (?<a>(?:up to )?\d+ of .+?) and (?<b>(?:up to )?\d+ of .+)$", RegexOptions.IgnoreCase)).Success)
        {
            var a = ParseAction($"{m.Groups["v"].Value} {m.Groups["a"].Value}", card, triggers);
            var b = ParseAction($"{m.Groups["v"].Value} {m.Groups["b"].Value}", card, triggers);
            if (a.Error == null && b.Error == null)
            {
                var (actA, actB) = (a.Act!, b.Act!);
                return (c => { actA(c); actB(c); }, null);
            }
        }

        // ---- 이 카드 자체 ([시큐리티]) ----
        if (Regex.IsMatch(s, @"^Add this card to (?:the|your|its owner's) hand$", RegexOptions.IgnoreCase) && security && card.Type == CardType.Option)
            return (c => { c.ReturnToHand = true; Emit(c, label, "이 카드를 패에 넣는다"); }, null);
        // "Play 1 [Diaboromon] Token without paying the cost" — 토큰 (알려진 것만)
        if ((m = Regex.Match(s, @"^Play (?<n>\d+|a|an) \[(?<name>[^\]]+)\] Tokens? without paying (?:the|its) (?:memory )?costs?$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.TryParse(m.Groups["n"].Value, out int k) ? k : 1;
            if (MakeToken(m.Groups["name"].Value) is not { } proto) return (null, $"미지원 토큰: {m.Groups["name"].Value}");
            return (c =>
            {
                int played = 0;
                for (int i = 0; i < n && c.Owner.BattleArea.Count < 12; i++, played++)
                    c.Engine.PlayWithoutCost(c.Owner, MakeToken(proto.Name.Replace(" Token", ""))!);
                Emit(c, label, played > 0 ? $"[{proto.Name}] {played}장 등장" : "등장시킬 수 없습니다");
            }, null);
        }
        // ---- 육성 · 디지타마 ----
        // "Hatch in your breeding area" · "hatch 1 Digi-Egg card to an empty space in your breeding area" · "hatch a Digi-Egg in your breeding area"
        if (Regex.IsMatch(s, @"^Hatch (?:(?:1|a|one) Digi-Egg(?: card)? )?(?:in|into|to) (?:an empty space in )?your breeding area$", RegexOptions.IgnoreCase))
            return (c =>
            {
                var egg = c.Engine.Hatch(c.Owner);
                Emit(c, label, egg != null ? $"[{egg.Name}] 육성 에어리어에 부화" : "부화할 수 없습니다 (육성 에어리어가 비어 있지 않거나 디지타마 덱이 없음)");
            }, null);
        // "Move 1 of your Digimon in your breeding area to your battle area" · "move 1 level 3 or higher Digimon from your breeding area to your battle area"
        if ((m = Regex.Match(s, @"^Move (?:1 of )?(?:your )?(?<spec>[^,]*?)Digimon (?:in|from) (?:your|the) breeding area to (?:your|the) battle area$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse("all of your " + m.Groups["spec"].Value + "Digimon") is { } moveSpec)
            return (c =>
            {
                var b = c.Owner.BreedingArea;
                bool ok = b != null && b.TopCard.Type == CardType.Digimon && moveSpec.Filters.All(f => f(b)) && moveSpec.ContextFilters.All(f => f(c, b));
                var moved = ok ? c.Engine.MoveBreedingToBattle(c.Owner) : null;
                Emit(c, label, moved != null ? $"[{moved.TopCard.Name}] 육성 에어리어 → 배틀 에어리어" : "이동할 디지몬이 없습니다");
                if (moved != null) c.LastTargets = new List<CardInstance> { moved };
                else c.LastStepResolved = false;
            }, null);
        // "This Digimon may move to the empty space in your breeding area"
        if ((m = Regex.Match(s, @"^This Digimon (?<may>may )?moves? to (?:the|an) empty space in your breeding area$", RegexOptions.IgnoreCase)).Success)
        {
            bool may = m.Groups["may"].Success;
            return (c =>
            {
                bool ok = c.Self != null && c.Owner.BreedingArea == null && c.Owner.BattleArea.Contains(c.Self)
                          && (!may || c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Self.TopCard.Name}]을(를) 육성 에어리어로 옮길까요?", recommended: c.Self.IsSuspended))
                          && c.Engine.MoveBattleToBreeding(c.Owner, c.Self);
                if (!ok) c.LastStepResolved = false;
                Emit(c, label, ok ? $"[{c.Self!.TopCard.Name}] 배틀 에어리어 → 육성 에어리어" : "육성 에어리어로 옮기지 않았습니다");
            }, null);
        }
        // ---- 앞면 시큐리티 ----
        // "Add your bottom security card to the hand and place this card face up as the bottom security card" — 두 동작
        if ((m = Regex.Match(s, @"^(?<l>Add your (?:top |bottom )?(?:face[- ]down )?security card to (?:the|your) hand) and (?<r>place this card face[- ]up .+)$", RegexOptions.IgnoreCase)).Success)
        {
            var a = ParseAction(m.Groups["l"].Value, card, triggers);
            var b = ParseAction("P" + m.Groups["r"].Value[1..], card, triggers);
            if (a.Error == null && b.Error == null) { var (actA, actB) = (a.Act!, b.Act!); return (c => { actA(c); actB(c); }, null); }
        }
        // "Place this card face up as the bottom security card" (옵션 [메인] · [소멸 시]) · "place this Digimon face up as the bottom security card"
        if ((m = Regex.Match(s, @"^Place this (?:card|Digimon) face[- ]up as (?:the|your) (?<pos>top|bottom) security card$", RegexOptions.IgnoreCase)).Success)
        {
            bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase);
            bool optionMain = card.Type == CardType.Option && triggers.Contains(EffectTrigger.Main);
            return (c =>
            {
                if (optionMain) { c.ThisCardToSecurityTop = top; Emit(c, label, $"이 카드를 시큐리티 맨 {(top ? "위" : "아래")}에 앞면으로"); return; }
                // [소멸 시] 등: 트래시(또는 패)에 있는 이 카드
                if (!(c.Owner.Trash.Remove(c.Card) || c.Owner.Hand.Remove(c.Card))) { c.LastStepResolved = false; Emit(c, label, "이 카드를 놓을 수 없습니다"); return; }
                c.Engine.PlaceSecurityFaceUp(c.Owner, c.Card, top);
                Emit(c, label, $"이 카드를 시큐리티 맨 {(top ? "위" : "아래")}에 앞면으로");
            }, null);
        }
        // "Place 1 [Royal Base] trait Digimon card from your hand face up as the bottom security card" · "~ as the top or bottom security card"
        if ((m = Regex.Match(s, @"^Place (?<pick>.+?) from (?<from>your hand or (?:from )?(?:your )?trash|your trash|your hand) face[- ]up as (?:the|your) (?<pos>top or bottom|top|bottom) security cards?$", RegexOptions.IgnoreCase)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } fuPick)
        {
            var (count, match) = fuPick;
            string pos = m.Groups["pos"].Value.ToLowerInvariant();
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash");
            return (c =>
            {
                var pool = new List<PoolCard>();
                if (fromHand) pool.AddRange(PoolFrom(c.Owner.Hand, match));
                if (fromTrash) pool.AddRange(PoolFrom(c.Owner.Trash, match));
                var zoneKind = fromHand && fromTrash ? CardChoiceZone.HandOrTrash : fromHand ? CardChoiceZone.Hand : CardChoiceZone.Trash;
                var placed = new List<string>();
                foreach (var p in ChooseFromPool(c, zoneKind, pool.OrderBy(x => x.Card.PlayCost).ToList(), count, $"[{c.Card.Name}] 시큐리티에 앞면으로 놓을 카드 {count}장을 고르세요."))
                {
                    if (!TakeFromPool(c, p)) continue;
                    // "top or bottom": 위에 두면 다음 체크로 바로 열린다 — 추천은 아래
                    bool top = pos == "top" || (pos == "top or bottom" && c.Engine.AskYesNo(c.Owner, null, $"[{p.Card.Name}]을(를) 시큐리티 맨 위에 놓을까요? (아니요 = 맨 아래)", recommended: false));
                    c.Engine.PlaceSecurityFaceUp(c.Owner, p.Card, top);
                    placed.Add(p.Card.Name);
                }
                if (placed.Count == 0) c.LastStepResolved = false;
                Emit(c, label, placed.Count > 0 ? $"[{string.Join(", ", placed)}] 시큐리티에 앞면으로" : "놓을 카드가 없습니다");
            }, null);
        }
        // "Flip your opponent's top face-down security card face up"
        if (Regex.IsMatch(s, @"^Flip your opponent's top face[- ]down security card face[- ]up$", RegexOptions.IgnoreCase))
            return (c =>
            {
                var flipped = c.Engine.FlipTopFaceDownUp(c.Enemy);
                if (flipped == null) c.LastStepResolved = false;
                Emit(c, label, flipped != null ? $"상대 시큐리티 [{flipped.Name}]을(를) 앞면으로" : "뒤집을 시큐리티가 없습니다");
            }, null);
        // "Add your top face-down security card to the hand"
        if (Regex.IsMatch(s, @"^Add your top face[- ]down security card to (?:the|your) hand$", RegexOptions.IgnoreCase))
            return (c =>
            {
                var x = c.Owner.SecurityStack.FirstOrDefault(sc => !GoldfishEngine.IsFaceUp(c.Owner, sc));
                if (x == null) { c.LastStepResolved = false; Emit(c, label, "뒷면 시큐리티가 없습니다"); return; }
                c.Owner.SecurityStack.Remove(x);
                c.Engine.NotifySecurityRemoved(c.Owner, x);
                c.Owner.Hand.Add(x);
                Emit(c, label, "뒷면 시큐리티 맨 위 1장을 패에 넣는다");
            }, null);
        // [Hand] · [Trash] [Main]: "play this card (from your trash) with the play cost reduced by 4" — 패 · 트래시의 이 카드를 줄인 코스트로 등장
        if ((m = Regex.Match(s, @"^Play this card(?: from (?:your|the) (?:hand|trash))? with the (?:play )?cost reduced by (?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                if (!(c.Owner.Hand.Remove(c.Card) || c.Owner.Trash.Remove(c.Card))) { c.LastStepResolved = false; Emit(c, label, "이 카드를 등장시킬 수 없습니다"); return; }
                c.Engine.PlayPayingCost(c.Owner, c.Card, Math.Max(0, CardScripts.EffectivePlayCost(c.Owner, c.Card, c.Enemy) - n));
            }, null);
        }
        // [Hand] [Main]: "it digivolves into this card for a digivolution cost of 3, ignoring digivolution requirements" · "1 of your [Agumon] digivolves into this card ~"
        if ((m = Regex.Match(s, @"^(?<t>it|that Digimon|1 of your .+?) digivolves? into this card for (?:a )?digivolution cost of (?<n>\d+),? ignoring (?:its |this card's |the )?digivolution requirements$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse(m.Groups["t"].Value) is { Opponent: false, AnySide: false } evoBase)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                var d = Pick(evoBase, c, security).FirstOrDefault(x => c.Owner.BattleArea.Contains(x));
                if (d == null || !c.Owner.Hand.Contains(c.Card)) { c.LastStepResolved = false; Emit(c, label, "진화할 디지몬이 없습니다"); return; }
                int cost = Math.Max(0, n);
                c.Engine.EvolveByEffect(c.Owner, d, c.Card, cost, c.Owner.Hand);   // 진화 조건을 무시한다
                c.LastTargets = new List<CardInstance> { d };
                Emit(c, label, $"[{d.TopCard.Name}]이(가) 이 카드로 진화 (코스트 {cost}, 진화 조건 무시)");
            }, null);
        }
        // 효과로 옵션 사용: "You may use 1 Option card with a cost of 5 or less in your hand without paying the cost"
        //  · "~ from your hand or under your Tamers ~" · "use 1 use cost 3 or lower Option card with [X] in its text from your hand or trash ~"
        //  사용할 수 있는 옵션만 (색 조건은 그대로 — 코스트만 내지 않는다)
        if ((m = Regex.Match(s, @"^Use (?<pick>.+?) (?:in|from) (?<from>your hand or (?:from )?(?:your )?trash|your hand or (?:from )?under (?:one of )?your Tamers|under (?:this Tamer|(?:one of )?your Tamers)|your hand|your trash) without paying (?:the|its) (?:memory |use )?cost$", RegexOptions.IgnoreCase)).Success)
        {
            string pt = Regex.Replace(m.Groups["pick"].Value.Trim(), @"^(?:an?|one) ", "1 ", RegexOptions.IgnoreCase);
            pt = Regex.Replace(pt, @"^(?<n>\d+) (?:use |memory |play )?cost (?<c>\d+) or (?:lower|less) (?<rest>.+?Option cards?)", "${n} ${rest} with a cost of ${c} or less", RegexOptions.IgnoreCase);
            if (ParseCardPick(pt) is not { } optPick) return (null, $"미지원 옵션 사용 조건: {m.Groups["pick"].Value}");
            var (count, pickMatch) = optPick;
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash"), fromUnder = from.Contains("under");
            return (c =>
            {
                bool Usable(CardData x) => x.Type == CardType.Option && pickMatch(x) && ActionValidator.MissingOptionColors(c.Owner, x).Count == 0;
                var pool = new List<PoolCard>();
                if (fromHand) pool.AddRange(PoolFrom(c.Owner.Hand, Usable));
                if (fromTrash) pool.AddRange(PoolFrom(c.Owner.Trash, Usable));
                if (fromUnder) pool.AddRange(PoolFrom(c.Owner.UnderTamers, Usable));
                var zoneKind = fromUnder ? CardChoiceZone.Other : fromHand && fromTrash ? CardChoiceZone.HandOrTrash : fromHand ? CardChoiceZone.Hand : CardChoiceZone.Trash;
                var picks = ChooseFromPool(c, zoneKind, pool.OrderByDescending(x => x.Card.PlayCost).ToList(), count, $"[{c.Card.Name}] 코스트 없이 사용할 옵션 카드를 고르세요.");
                int used = 0;
                foreach (var p in picks)
                {
                    if (!TakeFromPool(c, p)) continue;
                    c.Engine.UseOptionByEffect(c.Owner, p.Card);
                    used++;
                }
                if (used == 0) { c.LastStepResolved = false; Emit(c, label, "사용할 수 있는 옵션 카드가 없습니다"); }
            }, null);
        }
        // "Play 1 blue or yellow [TS] trait Digimon card from your hand with the play cost reduced by 3" · "~ for a play cost of 3" — 코스트를 낸다
        // (메모리가 상대 쪽으로 넘어가지 않는 카드만 후보 — 넘기면서까지 내는 선택은 하지 않는다)
        if ((m = Regex.Match(s, @"^Play (?<pick>.+?) from (?<from>your hand or (?:from )?(?:your )?trash|your trash|your hand) (?:with the (?:play |memory )?cost reduced by (?<red>\d+)|for a (?:play |memory )?cost of (?<fix>\d+))$", RegexOptions.IgnoreCase)).Success
            && (ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value)) is { } paidPick)
        {
            var (count, match) = paidPick;
            int? red = m.Groups["red"].Success ? int.Parse(m.Groups["red"].Value) : null, fix = m.Groups["fix"].Success ? int.Parse(m.Groups["fix"].Value) : null;
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash");
            int CostOf(CardData x) => fix ?? Math.Max(0, x.PlayCost - red!.Value);
            return (c =>
            {
                Func<CardData, bool> ok = x => x.Type is CardType.Digimon or CardType.Tamer && match(x) && CostOf(x) <= Math.Max(0, c.OwnerMemory);
                var pool = new List<PoolCard>();
                if (fromHand) pool.AddRange(PoolFrom(c.Owner.Hand, ok));
                if (fromTrash) pool.AddRange(PoolFrom(c.Owner.Trash, ok));
                var zoneKind = fromHand && fromTrash ? CardChoiceZone.HandOrTrash : fromHand ? CardChoiceZone.Hand : CardChoiceZone.Trash;
                var played = new List<string>();
                foreach (var p in ChooseFromPool(c, zoneKind, pool.OrderByDescending(x => x.Card.Level).ThenByDescending(x => x.Card.PlayCost).ToList(), count, $"[{c.Card.Name}] 등장시킬 카드 {count}장을 고르세요 (코스트 {(fix != null ? $"{fix}" : $"-{red}")})."))
                {
                    if (c.Owner.BattleArea.Count >= 12 || !TakeFromPool(c, p)) continue;
                    c.Engine.PlayPayingCost(c.Owner, p.Card, CostOf(p.Card));
                    played.Add($"{p.Card.Name}(코스트 {CostOf(p.Card)})");
                }
                if (played.Count == 0) c.LastStepResolved = false;
                Emit(c, label, played.Count > 0 ? $"[{string.Join(", ", played)}] 등장" : "등장시킬 카드가 없습니다");
            }, null);
        }
        // "you may place the top card of this Digimon face-up at the bottom of the security stack" · "place this Digimon's top stacked card face up as the bottom security card"
        if (Regex.IsMatch(s, @"^Place (?:the top card of this Digimon|this Digimon's top stacked card) face[- ]up (?:at the bottom of (?:the|your) security stack|as (?:the|your) bottom security card)$", RegexOptions.IgnoreCase))
            return (c =>
            {
                var top = c.Self != null && c.Owner.BattleArea.Contains(c.Self) ? c.Self.TrashTopCard() : null;   // 진화원이 없으면 놓을 수 없다
                if (top == null) { c.LastStepResolved = false; Emit(c, label, "놓을 카드가 없습니다"); return; }
                c.RecalculateFieldBuffs();
                c.Engine.PlaceSecurityFaceUp(c.Owner, top, top: false);
                Emit(c, label, $"맨 위 카드 [{top.Name}]을(를) 시큐리티 맨 아래에 앞면으로");
            }, null);
        // "Play 1 face-up [Dark Masters] trait Digimon card from your security stack without paying the cost" · "~ from your face-up security cards ~"
        if ((m = Regex.Match(s, @"^Play (?<pick>.+?) from your (?<fu>face[- ]up )?security (?:stack|cards) without paying (?:the|its) cost$", RegexOptions.IgnoreCase)).Success
            && (m.Groups["fu"].Success || Regex.IsMatch(m.Groups["pick"].Value, @"\bface[- ]up\b", RegexOptions.IgnoreCase))
            && (ParseCardPick(Regex.Replace(m.Groups["pick"].Value, @"\bface[- ]up\s+", "", RegexOptions.IgnoreCase)) is { } secPick))
        {
            var (count, match) = secPick;
            return (c =>
            {
                var pool = GoldfishEngine.FaceUpCards(c.Owner).Where(x => x.Type is CardType.Digimon or CardType.Tamer && match(x)).Select(x => new PoolCard(x, c.Owner.SecurityStack)).ToList();
                var played = new List<string>();
                foreach (var p in ChooseFromPool(c, CardChoiceZone.Other, pool.OrderByDescending(x => x.Card.Level).ToList(), count, $"[{c.Card.Name}] 앞면 시큐리티에서 등장시킬 카드 {count}장을 고르세요."))
                {
                    if (c.Owner.BattleArea.Count >= 12 || !TakeFromPool(c, p)) continue;
                    c.Owner.FaceUpSecurity.Remove(p.Card);
                    c.Engine.PlayWithoutCost(c.Owner, p.Card);
                    played.Add(p.Card.Name);
                }
                if (played.Count == 0) c.LastStepResolved = false;
                Emit(c, label, played.Count > 0 ? $"앞면 시큐리티에서 [{string.Join(", ", played)}] 코스트 없이 등장" : "등장시킬 카드가 없습니다");
            }, null);
        }
        // "Return 1 Digi-Egg card from your trash to the bottom of your Digi-Egg deck"
        if ((m = Regex.Match(s, @"^Return (?<upto>up to )?(?<n>\d+) Digi-Egg cards? from your trash to the bottom of (?:your|the) Digi-Egg deck$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            bool upTo = m.Groups["upto"].Success;
            return (c =>
            {
                var eggs = c.Owner.Trash.Where(x => x.Type == CardType.Digitama).ToList();
                var picked = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Trash, eggs, n, upTo, $"[{c.Card.Name}] 디지타마 덱 아래로 되돌릴 디지타마 {n}장을 고르세요.")
                    .Select(i => eggs[i]).ToList();
                foreach (var x in picked) c.Owner.Trash.Remove(x);
                GoldfishEngine.AddToDigitamaDeckBottom(c.Owner, picked);
                Emit(c, label, picked.Count > 0 ? $"[{string.Join(", ", picked.Select(x => x.Name))}] 디지타마 덱 아래로" : "되돌릴 디지타마가 없습니다");
            }, null);
        }
        // 트래시로 간 이 카드를 패로 ([소멸 시] · ≪디지버스트≫로 트래시되었을 때)
        if (Regex.IsMatch(s, @"^(?:Return|Add) this card (?:from (?:the|your) trash )?to (?:the|your|its owner's) hand$", RegexOptions.IgnoreCase) && !security
            && (triggers.Contains(EffectTrigger.DigiBurstTrashedThis) || triggers.Contains(EffectTrigger.OnDeletion)))
            return (c =>
            {
                if (!c.Owner.Trash.Remove(c.Card)) return;
                c.Owner.Hand.Add(c.Card);
                Emit(c, label, "트래시의 이 카드를 패에 넣는다");
            }, null);
        // 링크: "link 1 level 4 or lower Digimon card from your hand or this Digimon's digivolution cards to this Digimon without paying the cost"
        if ((m = Regex.Match(s, @"^Link (?:up to )?(?<pick>.+?) from (?<from>your hand or (?:from )?(?:your )?trash|your hand or this Digimon's digivolution cards|your trash or this Digimon's digivolution cards|your hand|your trash|this Digimon's digivolution cards) to (?<t>this Digimon|(?:1|one) of your Digimon(?: with [^,]+?)?) (?:without paying the cost|with the cost reduced by (?<r>\d+))$", RegexOptions.IgnoreCase)).Success)
        {
            var pick = ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value);
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (pick == null || target == null) return (null, $"미지원 링크: {s}");
            var (count, match) = pick.Value;
            int reduce = m.Groups["r"].Success ? int.Parse(m.Groups["r"].Value) : -1;   // -1 = 코스트 없이
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash"), fromSources = from.Contains("digivolution");
            return (c =>
            {
                var d = Pick(target, c, security).FirstOrDefault();
                if (d == null) { c.LastStepResolved = false; Emit(c, label, "링크할 디지몬이 없습니다"); return; }
                var pool = new List<CardData>();
                if (fromHand) pool.AddRange(c.Owner.Hand.Where(match));
                if (fromTrash) pool.AddRange(c.Owner.Trash.Where(match));
                if (fromSources && c.Self != null) pool.AddRange(c.Self.EvolutionSources.Where(match));
                // 링크 조건을 아는 카드는 조건에 맞아야 하고, 코스트를 줄여 링크하면 그 코스트를 낸다 (모르는 카드는 조건 없음 · 코스트 0)
                pool = pool.Where(x => GoldfishEngine.MeetsLinkRequirement(x, d) != false).ToList();
                var zone = fromSources ? CardChoiceZone.DigivolutionCards : fromHand && fromTrash ? CardChoiceZone.HandOrTrash : fromHand ? CardChoiceZone.Hand : CardChoiceZone.Trash;
                var picks = c.Engine.ChooseCards(c.Owner, zone, pool.OrderByDescending(x => GoldfishEngine.LinkDp(x)).ThenByDescending(x => x.Level).ToList(), count, upTo: true, $"[{c.Card.Name}] [{d.TopCard.Name}]에 링크할 카드를 고르세요.");
                if (picks.Count == 0) { c.LastStepResolved = false; Emit(c, label, "링크할 카드가 없습니다"); return; }
                foreach (var x in picks)
                {
                    bool taken = (fromHand && c.Owner.Hand.Remove(x)) || (fromTrash && c.Owner.Trash.Remove(x))
                                 || (fromSources && c.Self?.TakeSource(y => ReferenceEquals(y, x)) != null);
                    if (!taken) continue;
                    if (reduce >= 0) c.AddMemory(-Math.Max(0, (GoldfishEngine.LinkCostOf(x) ?? 0) - reduce));
                    c.Engine.LinkCard(c.Owner, d, x);
                }
                Emit(c, label, $"[{string.Join(", ", picks.Select(x => x.Name))}]을(를) [{d.TopCard.Name}]에 링크");
            }, null);
        }
        // ≪마인드 링크≫ (테이머): 이 테이머를 진화원에 테이머 카드가 없는 디지몬의 진화원 맨 아래에 놓는다
        if ((m = Regex.Match(s, @"^＜Mind Link＞ (?:to|with) (?<t>(?:1|one) of your Digimon(?: with [^,]+?)?)$", RegexOptions.IgnoreCase)).Success && card.Type == CardType.Tamer
            && TargetSpec.Parse(m.Groups["t"].Value) is { } mindTarget)
            return (c =>
            {
                if (!c.Owner.Tamers.Contains(c.Card)) { c.LastStepResolved = false; return; }
                var d = Pick(mindTarget, c, security).FirstOrDefault(x => !x.EvolutionSources.Any(s => s.Type == CardType.Tamer));
                if (d == null) { c.LastStepResolved = false; Emit(c, label, "≪마인드 링크≫할 디지몬이 없습니다"); return; }
                c.Owner.Tamers.Remove(c.Card);
                c.Owner.SuspendedTamers.Remove(c.Card);
                d.AddBottomSource(c.Card);
                c.Engine.ClearUnderTamersIfNoTamer(c.Owner);
                c.RecalculateFieldBuffs();
                Emit(c, label, $"≪마인드 링크≫ 이 테이머를 [{d.TopCard.Name}]의 진화원 맨 아래에");
            }, null);
        // 옵션 · 이 카드: "link this card to 1 of your Digimon (on the field) without paying the cost"
        if ((m = Regex.Match(s, @"^Link this card to (?<t>this Digimon|(?:1|one) of your Digimon(?: on the field)?(?: with [^,]+?)?) without paying the cost$", RegexOptions.IgnoreCase)).Success
            && TargetSpec.Parse(Regex.Replace(m.Groups["t"].Value, @" on the field", "", RegexOptions.IgnoreCase)) is { } linkTarget)
            return (c =>
            {
                var d = Pick(linkTarget, c, security).Where(x => GoldfishEngine.MeetsLinkRequirement(c.Card, x) != false).FirstOrDefault();
                if (d == null) { c.LastStepResolved = false; return; }
                // 옵션을 쓰는 경로(PlayCard · AI · 효과로 패의 옵션 사용)는 모두 이미 패에서 뺐다 — 여기서 또 빼면 같은 카드의 다른 사본이 사라진다.
                // 트래시 처리는 엔진이 건너뛴다 (시큐리티에서 쓴 것은 엔진이 트래시에서 뺀다)
                c.LinkedThisCard = true;
                c.Engine.LinkCard(c.Owner, d, c.Card);
                Emit(c, label, $"이 카드를 [{d.TopCard.Name}]에 링크");
            }, null);
        // "Return 1 yellow Digimon card from this Digimon's digivolution cards to the hand"
        if ((m = Regex.Match(s, @"^(?:Return|Add) (?<pick>.+?) from (?:this Digimon's|its) digivolution cards to (?:the|your|its owner's) hand$", RegexOptions.IgnoreCase)).Success)
        {
            var pick = ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value);
            if (pick == null) return (null, $"미지원 회수 조건: {m.Groups["pick"].Value}");
            var (count, match) = pick.Value;
            return (c =>
            {
                if (c.Self == null) return;
                var cards = c.Engine.ChooseCards(c.Owner, CardChoiceZone.DigivolutionCards, c.Self.EvolutionSources.Where(match).OrderByDescending(x => x.Level).ToList(),
                    count, upTo: false, $"[{c.Card.Name}] 진화원에서 패로 되돌릴 카드 {count}장을 고르세요.");
                foreach (var x in cards)
                    if (c.Self.TakeSource(y => ReferenceEquals(y, x)) != null) c.Owner.Hand.Add(x);
                c.RecalculateFieldBuffs();
                Emit(c, label, cards.Count > 0 ? $"진화원 [{string.Join(", ", cards.Select(x => x.Name))}] 패로" : "되돌릴 카드가 없습니다");
            }, null);
        }
        // "~ would leave the battle area / be deleted" 효과의 막기: "prevent it from leaving" · "it doesn't leave" · "it isn't deleted"
        if (Regex.IsMatch(s, @"^(?:prevent (?:it|that Digimon|this Digimon|them|those Digimon|that card) from (?:leaving(?: the battle area)?|being deleted)|(?:it|they|this Digimon|that Digimon) (?:doesn't|does not|don't|do not) leave(?: the battle area)?|(?:it|they|this Digimon|that Digimon) (?:isn't|is not|aren't|are not) deleted|prevent (?:that|the|this) (?:deletion|Digimon from leaving))$", RegexOptions.IgnoreCase))
            return (c => { c.PreventLeave = true; Emit(c, label, "배틀 에어리어를 떠나지 않는다"); }, null);
        // ≪세이브≫ ([소멸 시]): 트래시로 간 이 카드를 자신의 테이머 아래에 놓을 수 있다
        if (Regex.IsMatch(s, @"^＜Save＞$", RegexOptions.IgnoreCase))
            return (c =>
            {
                if (c.Owner.Tamers.Count == 0 || !c.Owner.Trash.Contains(c.Card)) { c.LastStepResolved = false; return; }
                if (!c.Engine.AskYesNo(c.Owner, null, $"≪세이브≫ [{c.Card.Name}]을(를) 자신의 테이머 아래에 놓을까요?", recommended: true)) { c.LastStepResolved = false; return; }
                c.Owner.Trash.Remove(c.Card);
                c.Owner.UnderTamers.Add(c.Card);
                Emit(c, label, "≪세이브≫ 이 카드를 테이머 아래에 놓는다");
            }, null);
        // "Place 1 Digimon card with the [Xros Heart] trait from your hand or trash under 1 of your Tamers" · "Place the top card of your deck face down under this Tamer"
        if (Regex.IsMatch(s, @"^Place the top card of your deck(?: face down)? under (?:this Tamer|(?:1|one) of your Tamers|your Tamers)$", RegexOptions.IgnoreCase))
            return (c =>
            {
                if (c.Owner.Tamers.Count == 0 || c.Owner.MainDeck.Count == 0) { c.LastStepResolved = false; return; }
                c.Owner.UnderTamers.Add(c.Owner.MainDeck[0]);
                c.Owner.MainDeck.RemoveAt(0);
                Emit(c, label, "덱 위 1장을 테이머 아래에 놓는다");
            }, null);
        if ((m = Regex.Match(s, @"^Place (?<pick>.+?) from (?<from>your hand or (?:from )?(?:your )?trash|your trash or hand|your hand|your trash|this Digimon's digivolution cards|its digivolution cards) (?:face down )?under (?:this Tamer|(?:1|one) of your Tamers|your Tamers)$", RegexOptions.IgnoreCase)).Success)
        {
            var pick = ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value);
            if (pick == null) return (null, $"미지원 회수 조건: {m.Groups["pick"].Value}");
            var (count, match) = pick.Value;
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash"), fromSources = from.Contains("digivolution");
            return (c =>
            {
                if (c.Owner.Tamers.Count == 0) { c.LastStepResolved = false; Emit(c, label, "테이머가 없습니다"); return; }
                var pool = new List<PoolCard>();
                if (fromHand) pool.AddRange(PoolFrom(c.Owner.Hand, match));
                if (fromTrash) pool.AddRange(PoolFrom(c.Owner.Trash, match));
                if (fromSources && c.Self != null) pool.AddRange(c.Self.EvolutionSources.Where(match).Select(x => new PoolCard(x, null)));
                var zone = fromSources ? CardChoiceZone.DigivolutionCards : fromHand && fromTrash ? CardChoiceZone.HandOrTrash : fromHand ? CardChoiceZone.Hand : CardChoiceZone.Trash;
                var picks = new List<CardData>();
                foreach (var p in ChooseFromPool(c, zone, pool.OrderBy(x => x.Card.Level).ToList(), count, $"[{c.Card.Name}] 테이머 아래에 놓을 카드 {count}장을 고르세요."))
                {
                    if (!TakeFromPool(c, p)) continue;
                    c.Owner.UnderTamers.Add(p.Card);
                    picks.Add(p.Card);
                }
                if (picks.Count == 0) c.LastStepResolved = false;
                Emit(c, label, picks.Count > 0 ? $"[{string.Join(", ", picks.Select(x => x.Name))}]을(를) 테이머 아래에" : "놓을 카드가 없습니다");
            }, null);
        }
        // ≪딜레이≫ 옵션: "Then, place this card in your battle area" — 트래시 대신 배틀 에어리어에 놓는다 (엔진이 사용 뒤 처리)
        if (Regex.IsMatch(s, @"^Place this card in (?:the|your) battle area$", RegexOptions.IgnoreCase) && card.Type == CardType.Option)
            return (c => { c.PlaceInBattleArea = true; Emit(c, label, "이 카드를 배틀 에어리어에 놓는다"); }, null);
        if (Regex.IsMatch(s, @"^Activate this card'?s \[Main\] effects?$", RegexOptions.IgnoreCase) && security && card.Type == CardType.Option)
            return (c => ActivateMainEffects(c, label), null);

        // ---- 동작 키워드 ----
        if ((m = Regex.Match(s, @"^＜Draw (?<n>\d+)＞(?: and trash (?<t>\d+) cards? (?:in|from) your hand)?$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value); int trash = m.Groups["t"].Success ? int.Parse(m.Groups["t"].Value) : 0;
            return (c =>
            {
                int drawn = 0;
                for (int i = 0; i < n; i++) if (c.Draw() != null) drawn++;
                string extra = trash > 0 ? $", 패 {c.Engine.DiscardFromHand(c.Owner, trash)}장 파기" : "";
                Emit(c, label, $"≪{n} 드로우≫ ({drawn}장){extra}");
            }, null);
        }
        if ((m = Regex.Match(s, @"^＜Recovery \+(?<n>\d+)\s*(?:\(Deck\))?\s*＞$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => Emit(c, label, $"≪리커버리 +{n}≫ 시큐리티 {c.Recovery(n)}장 추가"), null);
        }
        // "＜Recovery +1 (Deck)＞ until there are 3 cards in your security stack" — 시큐리티가 K장이 될 때까지 반복
        if ((m = Regex.Match(s, @"^＜Recovery \+(?<n>\d+)\s*(?:\(Deck\))?\s*＞ until (?:there are|you have) (?<k>\d+) (?:cards in your security stack|security cards)$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value), k = int.Parse(m.Groups["k"].Value);
            return (c =>
            {
                int added = 0;
                while (c.Owner.SecurityStack.Count < k)
                {
                    int got = c.Recovery(Math.Min(n, k - c.Owner.SecurityStack.Count));
                    if (got == 0) break;   // 덱이 비었다
                    added += got;
                }
                Emit(c, label, $"≪리커버리 +{n}≫ 시큐리티 {k}장이 될 때까지 — {added}장 추가");
            }, null);
        }
        if ((m = Regex.Match(s, @"^＜De-Digivolve (?<n>\d+)＞ (?<t>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (target == null || !target.Opponent) return (null, $"미지원 대상 (퇴화): {m.Groups["t"].Value}");
            return (c =>
            {
                var parts = new List<string>();
                foreach (var t in Pick(target, c, security).Where(d => d.EvolutionSources.Count > 0 && d.TopCard.Level > 3))
                {
                    string before = t.TopCard.Name;
                    c.DeDigivolveEnemy(t, n);
                    parts.Add($"[{before}] → [{t.TopCard.Name}]");
                }
                Emit(c, label, parts.Count > 0 ? $"≪퇴화 {n}≫ {string.Join(", ", parts)}" : "≪퇴화≫할 대상이 없습니다");
            }, null);
        }

        // ---- 메모리 ----
        if ((m = Regex.Match(s, @"^(?<v>Gain|Lose) (?<n>\d+) memory$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value) * (m.Groups["v"].Value.Equals("Lose", StringComparison.OrdinalIgnoreCase) ? -1 : 1);
            return (c => { c.AddMemory(n); Emit(c, label, $"메모리 {(n > 0 ? "+" : "")}{n} (현재 {c.OwnerMemory})"); }, null);
        }
        if ((m = Regex.Match(s, @"^Set (?:it|your memory) to (?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => { c.AddMemory(n - c.OwnerMemory); Emit(c, label, $"메모리를 {n}로 한다"); }, null);
        }

        // ---- 시큐리티 · 덱 · 패 ----
        if ((m = Regex.Match(s, @"^Trash (?:the top (?:card|(?<n>\d+) cards) of your opponent's security stack|your opponent's top (?:security card|(?<n>\d+) security cards)|their top (?:security card|(?<n>\d+) security cards)|the top (?:card|(?<n>\d+) cards) of their security stack)$", RegexOptions.IgnoreCase)).Success)
        {
            int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
            return (c =>
            {
                int k = 0;
                while (k < n && c.Enemy.SecurityStack.Count > 0) { c.Enemy.Trash.Add(c.Enemy.SecurityStack[0]); c.Enemy.SecurityStack.RemoveAt(0); c.Engine.NotifySecurityRemoved(c.Enemy); k++; }
                Emit(c, label, $"상대 시큐리티 위에서 {k}장 파기");
            }, null);
        }
        if ((m = Regex.Match(s, @"^Trash the top (?:card|(?<n>\d+) cards) of your deck$", RegexOptions.IgnoreCase)).Success)
        {
            int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
            return (c => c.Mill(n, $"{c.Card.Name} [{label}]"), null);
        }
        if ((m = Regex.Match(s, @"^Trash (?<n>\d+) cards? (?:in|from) your hand$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => Emit(c, label, $"패 {c.Engine.DiscardFromHand(c.Owner, n)}장 파기"), null);
        }
        if ((m = Regex.Match(s, $@"^Reveal (?:the top (?<n>\d+) cards? of your deck|(?<n>\d+) cards? from the top of your deck)(?: for each (?<counter>[^.]+?))?\.? (?<mid>(?!Add it\b)(?:Among them, )?(?:You may )?(?:Add|Play|Trash|Place) .+?)\.? (?<rest>{RevealRest})\.?$", RegexOptions.IgnoreCase)).Success)
            return ParseReveal(m, label, card, triggers);

        if ((m = Regex.Match(s, @"^At (?:the )?end of (?:the |this )?turn, lose (?<n>\d+) memory$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => { c.LoseMemoryAtEndOfTurn(n); Emit(c, label, $"이 턴 종료 시 메모리 -{n}"); }, null);
        }
        if (Regex.IsMatch(s, @"^Trash (?:the top card of your security stack|your top security card)$", RegexOptions.IgnoreCase))
            return (c =>
            {
                if (c.Owner.SecurityStack.Count == 0) return;
                c.Owner.Trash.Add(c.Owner.SecurityStack[0]);
                c.Owner.SecurityStack.RemoveAt(0);
                c.Engine.NotifySecurityRemoved(c.Owner);
                Emit(c, label, "자신의 시큐리티 위에서 1장 파기");
            }, null);
        if ((m = Regex.Match(s, @"^Trash (?:up to )?(?<n>\d+) digivolution cards? from the (?<pos>top|bottom) of (?<t>.+)$", RegexOptions.IgnoreCase)).Success)
            return ParseAction($"Trash the {m.Groups["pos"].Value} {m.Groups["n"].Value} digivolution cards of {m.Groups["t"].Value}", card, triggers);
        if ((m = Regex.Match(s, @"^Add your (?<pos>top|bottom) security card to (?:the|your) hand$", RegexOptions.IgnoreCase)).Success)
        {
            bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase);
            return (c =>
            {
                var sec = c.Owner.SecurityStack;
                if (sec.Count == 0) { Emit(c, label, "시큐리티가 없습니다"); return; }
                int i = top ? 0 : sec.Count - 1;
                var card = sec[i];
                sec.RemoveAt(i);
                c.Owner.Hand.Add(card);
                Emit(c, label, $"시큐리티 {(top ? "맨 위" : "맨 아래")} 1장을 패에 넣는다");
            }, null);
        }
        if ((m = Regex.Match(s, @"^(?:Return|Add) (?<pick>.+?) from your trash to (?:the|your) hand$", RegexOptions.IgnoreCase)).Success)
        {
            var pick = ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value);
            if (pick == null) return (null, $"미지원 회수 조건: {m.Groups["pick"].Value}");
            var (count, match) = pick.Value;
            bool upTo = IsUpTo(m.Groups["pick"].Value);
            return (c =>
            {
                var ranked = c.Owner.Trash.Where(match).OrderByDescending(x => x.Level).ThenByDescending(x => x.PlayCost).ToList();
                var cards = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Trash, ranked, count, upTo, $"[{c.Card.Name}] 트래시에서 패로 되돌릴 카드를 {(upTo ? $"{count}장까지" : $"{count}장")} 고르세요.");
                foreach (var card in cards) { c.Owner.Trash.Remove(card); c.Owner.Hand.Add(card); }
                Emit(c, label, cards.Count > 0 ? $"트래시의 [{string.Join(", ", cards.Select(x => x.Name))}] 패로 회수" : "회수할 카드가 없습니다");
            }, null);
        }
        // "Play 1 level 4 or lower digivolution card under this card as another Digimon without paying its memory cost"
        if ((m = Regex.Match(s, @"^Play (?<n>\d+) (?<adj>.*?)digivolution cards? (?:under this (?:card|Digimon)|of this Digimon) as (?:another|other) Digimon (?<rest>without paying .+)$", RegexOptions.IgnoreCase)).Success)
            return ParseAction($"Play {m.Groups["n"].Value} {m.Groups["adj"].Value}Digimon card from this Digimon's digivolution cards {m.Groups["rest"].Value}", card, triggers);
        if ((m = Regex.Match(s, @"^Play (?<pick>.+?)(?<sus> suspended)? from (?<from>your hand or (?:from your )?trash|your hand or from your trash|your trash or hand|your hand,? or (?:from )?under (?:one of |1 of )?your Tamers|(?:from )?under (?:one of |1 of )?your Tamers|your hand|your trash|this Digimon's digivolution cards|its digivolution cards|the digivolution cards of (?:your |the )?(?:Digimon|\[(?<bsrc>[^\]]+)\]) in (?:the|your) breeding area)(?<sus> suspended)?(?<breed> (?:in|to|into) (?:an empty space in your|your empty|the empty space in your|your) breeding area)? without paying (?:the|its) (?:memory |play )?cost(?<noplay> ⟦no-onplay⟧)?$", RegexOptions.IgnoreCase)).Success)
        {
            bool noOnPlay = m.Groups["noplay"].Success, suspended = m.Groups["sus"].Success, toBreeding = m.Groups["breed"].Success;
            string pickText = m.Groups["pick"].Value;
            // "1 [Deva] trait Digimon card without the same name as the cards in your battle area or trash" — 필드 · 트래시와 같은 명칭 제외
            var sameName = Regex.Match(pickText, @"\s*without the same name as (?:the |any )?(?:cards?|Digimon)(?: cards?)? in your (?<z>battle area or trash|battle area|trash)", RegexOptions.IgnoreCase);
            if (sameName.Success) pickText = pickText.Remove(sameName.Index, sameName.Length);
            string sameZones = sameName.Success ? sameName.Groups["z"].Value.ToLowerInvariant() : "";
            var pick = ParseCardPick(pickText) ?? ParseNamePick(pickText);
            if (pick == null) return (null, $"미지원 등장 조건: {m.Groups["pick"].Value}");
            var (count, baseMatch) = pick.Value;
            if (toBreeding) count = Math.Min(count, 1);   // 육성 에어리어는 1장만
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash"), fromBreedSrc = from.Contains("breeding area"), fromSources = from.Contains("digivolution") && !fromBreedSrc, fromUnder = from.Contains("under");
            string? bsrc = m.Groups["bsrc"].Success ? m.Groups["bsrc"].Value : null;   // "the digivolution cards of your [Gate of Deadly Sins] in the breeding area"
            return (c =>
            {
                var played = new List<string>();
                if (toBreeding && c.Owner.BreedingArea != null)
                {
                    Emit(c, label, "육성 에어리어가 비어 있지 않아 등장시킬 수 없습니다");
                    c.LastStepResolved = false;
                    return;
                }
                var blockedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (sameZones.Contains("battle area")) blockedNames.UnionWith(c.Owner.BattleArea.Select(d => CardNames.MatchName(d.TopCard)).Concat(c.Owner.Tamers.Select(CardNames.MatchName)));
                if (sameZones.Contains("trash")) blockedNames.UnionWith(c.Owner.Trash.Select(CardNames.MatchName));
                Func<CardData, bool> match = x => baseMatch(x) && !blockedNames.Contains(CardNames.MatchName(x));
                Func<CardData, bool> playable = x => (toBreeding ? x.Type == CardType.Digimon : x.Type is CardType.Digimon or CardType.Tamer) && match(x);
                var pool = new List<PoolCard>();
                if (fromUnder) pool.AddRange(PoolFrom(c.Owner.UnderTamers, playable));
                if (fromHand) pool.AddRange(PoolFrom(c.Owner.Hand, playable));
                if (fromTrash) pool.AddRange(PoolFrom(c.Owner.Trash, playable));
                if (fromSources && c.Self != null) pool.AddRange(c.Self.EvolutionSources.Where(x => x.Type == CardType.Digimon && match(x)).Select(x => new PoolCard(x, null)));
                if (fromBreedSrc && c.Owner.BreedingArea is { } breedSrc && (bsrc == null || TargetSpec.NameHas(breedSrc.TopCard, bsrc)))
                    pool.AddRange(breedSrc.EvolutionSources.Where(x => x.Type == CardType.Digimon && match(x)).Select(x => new PoolCard(x, null)));
                var zone = fromSources || fromBreedSrc ? CardChoiceZone.DigivolutionCards : fromUnder ? CardChoiceZone.Other : fromHand && fromTrash ? CardChoiceZone.HandOrTrash : fromHand ? CardChoiceZone.Hand : CardChoiceZone.Trash;
                var ranked = pool.OrderByDescending(x => x.Card.Level).ThenByDescending(x => x.Card.PlayCost).ToList();
                foreach (var pick in ChooseFromPool(c, zone, ranked, count, $"[{c.Card.Name}] 코스트 없이 등장시킬 카드 {count}장을 고르세요."))
                {
                    if (!toBreeding && c.Owner.BattleArea.Count >= 12) break;
                    if (fromBreedSrc ? c.Owner.BreedingArea?.TakeSource(x => ReferenceEquals(x, pick.Card)) == null : !TakeFromPool(c, pick)) continue;
                    if (toBreeding) c.Engine.PlayToBreeding(c.Owner, pick.Card);   // 【등장 시】 없음 (공식 Q&A BT23-084)
                    else c.Engine.PlayWithoutCost(c.Owner, pick.Card, triggerOnPlay: !noOnPlay, suspended: suspended);
                    played.Add(pick.Card.Name);
                }
                Emit(c, label, played.Count > 0 ? $"[{string.Join(", ", played)}] 코스트 없이 {(toBreeding ? "육성 에어리어에 " : "")}등장" : "등장시킬 카드가 없습니다");
            }, null);
        }
        if ((m = Regex.Match(s, @"^Trash the (?<pos>top|bottom) (?:(?<n>\d+) )?digivolution cards? of (?<t>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            bool top = m.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase);
            int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (target == null) return (null, $"미지원 대상 (진화원 파기): {m.Groups["t"].Value}");
            return (c =>
            {
                var parts = new List<string>();
                foreach (var d in Pick(target, c, security).Where(d => d.EvolutionSources.Count > 0))
                {
                    var zone = c.Engine.OwnerOf(d);
                    var removed = top ? c.Engine.TrashTopSources(zone, d, n) : d.RemoveBottomSources(n);
                    if (!top) zone.Trash.AddRange(removed);
                    parts.Add($"[{d.TopCard.Name}] {removed.Count}장");
                }
                Emit(c, label, parts.Count > 0 ? $"진화원 파기: {string.Join(", ", parts)}" : "진화원을 파기할 대상이 없습니다");
            }, null);
        }
        if (security && card.Type == CardType.Digimon && Regex.IsMatch(s, @"^At the end of the battle, play this card without paying (?:the|its) (?:memory )?cost$", RegexOptions.IgnoreCase))
            return (c => { c.PlayAfterBattle = true; Emit(c, label, "배틀이 끝나면 이 카드를 코스트 없이 등장"); }, null);

        // 효과로 어택: "This Digimon may attack" · "1 of your Digimon may attack your opponent's Digimon" · "It may attack a player" · "~ may attack without suspending"
        if ((m = Regex.Match(s, @"^(?<t>.+?) may attack(?: (?<what>a player|your opponent|(?:your|an) opponent's Digimon))?(?<nosus> without suspending(?: it)?)?$", RegexOptions.IgnoreCase)).Success)
        {
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (target == null || target.Opponent || target.AnySide) return (null, $"미지원 대상 (어택): {m.Groups["t"].Value}");
            string what = m.Groups["what"].Value.ToLowerInvariant();
            var allowed = what.EndsWith("digimon") ? EffectAttackTarget.DigimonOnly : what.Length > 0 ? EffectAttackTarget.PlayerOnly : EffectAttackTarget.Any;
            bool noSuspend = m.Groups["nosus"].Success;
            target.PreferOwnSide = true;
            target.ContextFilters.Add((c, d) => c.Engine.CanEffectAttack(c.Owner, d, noSuspend));   // 어택할 수 있는 디지몬 중에서 고른다
            return (c =>
            {
                var attackers = Pick(target, c, security);
                int attacked = 0;
                foreach (var d in attackers)
                    if (c.Engine.EffectAttack(c.Owner, d, allowed, noSuspend, $"{c.Card.Name} {label}")) attacked++;
                if (attacked == 0) { c.LastStepResolved = false; Emit(c, label, "어택하지 않았습니다"); }
            }, null);
        }

        // ---- 대상을 가진 동작 ----
        if ((m = Regex.Match(s, @"^Delete (?<t>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (target == null) return (null, $"미지원 대상 (소멸): {m.Groups["t"].Value}");
            return (c =>
            {
                var victims = Pick(target, c, security);
                var names = new List<string>();
                foreach (var v in victims)
                {
                    bool gone = c.Enemy.BattleArea.Contains(v) ? c.DeleteEnemy(v) : c.Engine.DeleteFrom(c.Owner, v);
                    if (gone) names.Add(v.TopCard.Name);
                }
                Emit(c, label, names.Count > 0 ? $"[{string.Join(", ", names)}] 소멸" : "소멸시킬 대상이 없습니다", attackerRemoved: victims.Contains(c.Other!) && names.Count > 0);
            }, null);
        }
        if ((m = Regex.Match(s, @"^(?<v>Suspend|Unsuspend) (?<t>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            bool suspend = m.Groups["v"].Value.Equals("Suspend", StringComparison.OrdinalIgnoreCase);
            string tt = Regex.Replace(m.Groups["t"].Value, @"Digimon or Tamers", "Digimon", RegexOptions.IgnoreCase);
            var target = TargetSpec.Parse(tt);
            if (target == null) return (null, $"미지원 대상 ({(suspend ? "레스트" : "액티브")})");
            target.PreferOwnSide = !suspend;
            return (c =>
            {
                var list = Pick(target, c, security).Where(d => d.IsSuspended != suspend).ToList();
                if (target.Count > 0) list = list.Take(target.Count).ToList();
                foreach (var d in list)
                {
                    if (suspend) c.Engine.Suspend(d);
                    else c.Engine.Unsuspend(d);
                }
                c.LastTargets = list;   // "If this effect suspended your Digimon" — 실제로 바뀐 디지몬
                if (list.Count == 0) c.LastStepResolved = false;
                Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}] {(suspend ? "레스트" : "액티브")}" : "대상이 없습니다");
            }, null);
        }
        if ((m = Regex.Match(s, @"^Return (?<t>.+?) to (?:the|its owner's|their owners'?|their|the owner's|your) hands?$", RegexOptions.IgnoreCase)).Success)
        {
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (target == null) return (null, $"미지원 대상 (패로): {m.Groups["t"].Value}");
            return (c =>
            {
                var list = Pick(target, c, security);
                foreach (var d in list) c.Engine.ReturnToHand(c.Engine.OwnerOf(d), d);
                Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}] 패로" : "대상이 없습니다", attackerRemoved: list.Contains(c.Other!));
            }, null);
        }
        if ((m = Regex.Match(s, @"^(?:Place|Return) (?<t>.+?) (?:at|on|to) the bottom of (?:its owner's|their owners'?|their|the owner's|the|your opponent's) decks?(?: in any order)?$", RegexOptions.IgnoreCase)).Success)
        {
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (target == null) return (null, $"미지원 대상 (덱 아래로): {m.Groups["t"].Value}");
            return (c =>
            {
                var list = Pick(target, c, security);
                foreach (var d in list) c.Engine.BottomDeck(c.Engine.OwnerOf(d), d);
                Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}] 덱 아래로" : "대상이 없습니다", attackerRemoved: list.Contains(c.Other!));
            }, null);
        }

        // 덱 위 1장 공개 → 조건에 맞으면 패, 아니면 덱 아래
        if ((m = Regex.Match(s, @"^(?:Then, )?Reveal (?:the top card of your deck|1 card from the top of your deck)\.? (?:Add it to (?:the|your) hand if it's (?<pick>an? [^.]+?)|If (?:that card|it) is (?<pick>an? [^,]+?), add it to (?:the|your) hand|If (?:that card|it) has (?<has>[^,]+?), add it to (?:the|your) hand)\.? (?:Otherwise,? (?:place|put|return) it (?:at|on|to) the bottom of (?:the|your) deck|(?<trash>Trash the rest)|Place the rest at the bottom of (?:the|your) deck)$", RegexOptions.IgnoreCase)).Success)
        {
            string pickText = m.Groups["has"].Success ? $"1 card with {m.Groups["has"].Value}" : Regex.Replace(m.Groups["pick"].Value, @"^an? ", "1 ");
            var pick = ParseCardPick(pickText);
            if (pick == null) return (null, $"미지원 공개 조건 ({pickText})");
            var match = pick.Value.Match;
            bool trashMiss = m.Groups["trash"].Success;
            return (c =>
            {
                if (c.Owner.MainDeck.Count == 0) { Emit(c, label, "덱이 없습니다"); return; }
                var top = c.Owner.MainDeck[0];
                c.Owner.MainDeck.RemoveAt(0);
                if (match(top)) { c.Owner.Hand.Add(top); Emit(c, label, $"덱 위 [{top.Name}] 공개 → 패에 추가"); }
                else if (trashMiss) { c.Owner.Trash.Add(top); Emit(c, label, $"덱 위 [{top.Name}] 공개 → 파기"); }
                else { c.Owner.MainDeck.Add(top); Emit(c, label, $"덱 위 [{top.Name}] 공개 → 덱 아래로"); }
            }, null);
        }
        // 육성: "Place 1 Digimon card with the [Royal Knight] trait from your hand under a [King Drasil_7D6] in the breeding area as its bottom digivolution card"
        // · "place 1 [Eater] from this Digimon's digivolution cards as the bottom digivolution card of your [Mother Eater] in the breeding area" · "place this card as ~"
        if ((m = Regex.Match(s, $@"^Place (?:(?<self>this card)|(?<pick>.+?) from (?<from>your hand or (?:from )?(?:your )?trash|your trash|your hand|this Digimon's digivolution cards)) (?:under (?:a |an |1 of |one of )?(?:your |the )?(?<x>{TargetSpec.NameList}) in (?:the|your) breeding area as its bottom digivolution card|as the bottom digivolution card of (?:one of |1 of )?(?:your |the )?(?<x>{TargetSpec.NameList}) in (?:the|your) breeding area)$", RegexOptions.IgnoreCase)).Success)
        {
            var xs = TargetSpec.Names(m.Groups["x"].Value);
            bool self = m.Groups["self"].Success;
            var pick = self ? null : ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value);
            if (!self && pick == null) return (null, $"미지원 진화원 넣기: {s}");
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash"), fromSources = from.Contains("digivolution");
            return (c =>
            {
                var b = c.Owner.BreedingArea;
                if (b == null || !xs.Any(x => TargetSpec.NameHas(b.TopCard, x))) { c.LastStepResolved = false; Emit(c, label, "육성 에어리어에 대상이 없습니다"); return; }
                var placed = new List<CardData>();
                if (self)
                {
                    if (c.Owner.Trash.Remove(c.Card) || c.Owner.Hand.Remove(c.Card)) placed.Add(c.Card);
                }
                else
                {
                    var (count, match) = pick!.Value;
                    var pool = new List<PoolCard>();
                    if (fromHand) pool.AddRange(PoolFrom(c.Owner.Hand, match));
                    if (fromTrash) pool.AddRange(PoolFrom(c.Owner.Trash, match));
                    if (fromSources && c.Self != null) pool.AddRange(c.Self.EvolutionSources.Where(match).Select(x => new PoolCard(x, null)));
                    var zoneKind = fromSources ? CardChoiceZone.DigivolutionCards : fromHand && fromTrash ? CardChoiceZone.HandOrTrash : fromHand ? CardChoiceZone.Hand : CardChoiceZone.Trash;
                    foreach (var p in ChooseFromPool(c, zoneKind, pool.OrderBy(x => x.Card.Level).ToList(), count, $"[{c.Card.Name}] 육성 에어리어 [{b.TopCard.Name}]의 진화원 아래에 놓을 카드 {count}장을 고르세요."))
                        if (TakeFromPool(c, p)) placed.Add(p.Card);
                }
                foreach (var x in placed) b.AddBottomSource(x);
                if (placed.Count == 0) c.LastStepResolved = false;
                c.RecalculateFieldBuffs();
                Emit(c, label, placed.Count > 0 ? $"[{string.Join(", ", placed.Select(x => x.Name))}]을(를) 육성 에어리어 [{b.TopCard.Name}]의 진화원 맨 아래에" : "놓을 카드가 없습니다");
            }, null);
        }
        // "Place 1 [Palmon] from your trash under 1 of your green Digimon at the bottom of its digivolution cards" · "~ from your hand under 1 of your Digimon as its bottom digivolution card"
        if ((m = Regex.Match(s, @"^Place (?<pick>.+?) from (?<from>your hand,? or (?:from )?under (?:one of |1 of )?your Tamers,?|your hand or (?:from )?(?:your )?trash|your trash|your hand) under (?<t>.+?) (?:at the bottom of its digivolution cards|as its bottom digivolution card)$", RegexOptions.IgnoreCase)).Success)
        {
            var pick = ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick(m.Groups["pick"].Value);
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (pick == null || target == null) return (null, $"미지원 진화원 넣기: {s}");
            var (count, match) = pick.Value;
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash"), fromUnder = from.Contains("under");
            return (c =>
            {
                var d = Pick(target, c, security).FirstOrDefault();
                var pool = new List<PoolCard>();
                if (fromHand) pool.AddRange(PoolFrom(c.Owner.Hand, match));
                if (fromTrash) pool.AddRange(PoolFrom(c.Owner.Trash, match));
                if (fromUnder) pool.AddRange(PoolFrom(c.Owner.UnderTamers, match));
                var zoneKind = fromUnder ? CardChoiceZone.Other : fromHand && fromTrash ? CardChoiceZone.HandOrTrash : fromHand ? CardChoiceZone.Hand : CardChoiceZone.Trash;
                var picks = ChooseFromPool(c, zoneKind, pool.OrderBy(x => x.Card.Level).ToList(), count, $"[{c.Card.Name}] 진화원 아래에 놓을 카드 {count}장을 고르세요.");
                var cards = picks.Select(p => p.Card).ToList();
                if (d == null || cards.Count == 0) { c.LastStepResolved = false; Emit(c, label, "놓을 카드나 대상이 없습니다"); return; }
                foreach (var p in picks)
                {
                    if (!TakeFromPool(c, p)) continue;
                    d.AddBottomSource(p.Card);
                }
                c.RecalculateFieldBuffs();
                Emit(c, label, $"[{string.Join(", ", cards.Select(x => x.Name))}]을(를) [{d.TopCard.Name}]의 진화원 맨 아래에");
            }, null);
        }
        // "Delete 1 of your opponent's Tamers" · "Suspend all of your opponent's Tamers"
        if ((m = Regex.Match(s, @"^(?<v>Delete|Suspend) (?<n>\d+|all) of your opponent's Tamers$", RegexOptions.IgnoreCase)).Success)
        {
            bool delete = m.Groups["v"].Value.Equals("Delete", StringComparison.OrdinalIgnoreCase);
            int n = int.TryParse(m.Groups["n"].Value, out int k) ? k : 99;
            return (c =>
            {
                var tamers = c.Enemy.Tamers.Where(t => delete || c.Enemy.HasActiveTamer(t)).OrderByDescending(t => t.PlayCost).Take(n).ToList();
                foreach (var t in tamers)
                {
                    if (delete) { c.Enemy.Tamers.Remove(t); c.Enemy.SuspendedTamers.Remove(t); c.Enemy.Trash.Add(t); c.Engine.ClearUnderTamersIfNoTamer(c.Enemy); }
                    else c.Engine.SuspendTamer(c.Enemy, t);
                }
                Emit(c, label, tamers.Count > 0 ? $"상대 테이머 [{string.Join(", ", tamers.Select(t => t.Name))}] {(delete ? "소멸" : "레스트")}" : "대상이 없습니다");
            }, null);
        }
        // "have 1 of your opponent's Digimon get -2000 DP for the turn" (비용 뒤) = "~ gets ~"
        if ((m = Regex.Match(s, @"^Have (?<t>.+?) (?<v>get|gain) (?<rest>.+)$", RegexOptions.IgnoreCase)).Success)
            return ParseAction($"{m.Groups["t"].Value} {m.Groups["v"].Value}s {m.Groups["rest"].Value}", card, triggers);
        // "Trash up to 2 bottom digivolution cards of ~" · "Choose 1 digivolution card of ~ and trash it"
        if ((m = Regex.Match(s, @"^Trash (?:up to )?(?<n>\d+) (?<pos>bottom|top) digivolution cards? of (?<t>.+)$", RegexOptions.IgnoreCase)).Success)
            return ParseAction($"Trash the {m.Groups["pos"].Value} {m.Groups["n"].Value} digivolution cards of {m.Groups["t"].Value}", card, triggers);
        if ((m = Regex.Match(s, @"^Choose (?<n>\d+) digivolution cards? of (?<t>.+?) and trash (?:it|them)$", RegexOptions.IgnoreCase)).Success)
            return ParseAction($"Trash the bottom {m.Groups["n"].Value} digivolution cards of {m.Groups["t"].Value}", card, triggers);
        // 정보만 보는 동작 — 엔진은 덱 순서를 바꾸지 않는다
        if (Regex.IsMatch(s, @"^Look at the top card of your deck$", RegexOptions.IgnoreCase))
            return (c => Emit(c, label, c.Owner.MainDeck.Count > 0 ? $"덱 위 카드 확인: [{c.Owner.MainDeck[0].Name}]" : "덱이 없습니다"), null);
        if ((m = Regex.Match(s, @"^Both players trash the top (?<n>\d+) cards? of their decks?$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                foreach (var z in new[] { c.Owner, c.Enemy })
                {
                    var top = z.MainDeck.Take(n).ToList();
                    z.MainDeck.RemoveRange(0, top.Count);
                    z.Trash.AddRange(top);
                }
                Emit(c, label, $"양쪽 덱 위 {n}장 파기");
            }, null);
        }
        if ((m = Regex.Match(s, @"^Trash (?<n>\d+) cards? from the top of both players' security stacks$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c =>
            {
                foreach (var z in new[] { c.Owner, c.Enemy })
                    for (int i = 0; i < n && z.SecurityStack.Count > 0; i++) { z.Trash.Add(z.SecurityStack[0]); z.SecurityStack.RemoveAt(0); c.Engine.NotifySecurityRemoved(z); }
                Emit(c, label, $"양쪽 시큐리티 위 {n}장 파기");
            }, null);
        }
        if ((m = Regex.Match(s, @"^Your opponent trashes (?<n>\d+) cards? in their hand$", RegexOptions.IgnoreCase)).Success)
        {
            int n = int.Parse(m.Groups["n"].Value);
            return (c => Emit(c, label, $"상대 패 {c.Engine.DiscardFromHand(c.Enemy, n)}장 파기"), null);
        }
        // 시큐리티 맨 위에 뒷면으로: "Place this card on top of your security stack face down" · "Place 1 of your Digimon ~" · "place 1 yellow Digimon from your trash ~"
        if (Regex.IsMatch(s, @"^Place this card (?:on|at the) top of your security stack(?: face down)?$", RegexOptions.IgnoreCase) && triggers.Contains(EffectTrigger.OnDeletion))
            return (c =>
            {
                if (!c.Owner.Trash.Remove(c.Card)) return;
                c.Owner.SecurityStack.Insert(0, c.Card);
                c.Engine.NotifySecurityAdded(c.Owner, c.Card);
                Emit(c, label, "이 카드를 시큐리티 맨 위에 놓는다");
            }, null);
        if ((m = Regex.Match(s, @"^Place (?<pick>.+?) from your trash (?:on|at the) top of your security stack(?: face down)?$", RegexOptions.IgnoreCase)).Success)
        {
            var pick = ParseCardPick(m.Groups["pick"].Value + (Regex.IsMatch(m.Groups["pick"].Value, @"\bcards?$") ? "" : " card")) ?? ParseNamePick(m.Groups["pick"].Value);
            if (pick == null) return (null, $"미지원 회수 조건: {m.Groups["pick"].Value}");
            var (count, match) = pick.Value;
            bool upTo = IsUpTo(m.Groups["pick"].Value);
            return (c =>
            {
                var cards = c.Engine.ChooseCards(c.Owner, CardChoiceZone.Trash, c.Owner.Trash.Where(match).OrderByDescending(x => x.DP).ToList(), count, upTo, $"[{c.Card.Name}] 시큐리티 위에 놓을 카드를 {(upTo ? $"{count}장까지" : $"{count}장")} 고르세요.");
                foreach (var x in cards) { c.Owner.Trash.Remove(x); c.Owner.SecurityStack.Insert(0, x); c.Engine.NotifySecurityAdded(c.Owner, x); }
                Emit(c, label, cards.Count > 0 ? $"트래시의 [{string.Join(", ", cards.Select(x => x.Name))}] 시큐리티 맨 위에" : "놓을 카드가 없습니다");
            }, null);
        }
        if ((m = Regex.Match(s, @"^Place (?<t>(?:\d+|all) of (?:your|your opponent's) .+?) (?:on|at the) top of (?:your|its owner's|their owners'?) security stacks?(?: face down)?$", RegexOptions.IgnoreCase)).Success)
        {
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (target == null) return (null, $"미지원 대상 (시큐리티로): {m.Groups["t"].Value}");
            return (c =>
            {
                var list = Pick(target, c, security);
                foreach (var d in list) c.Engine.PlaceOnSecurity(c.Engine.OwnerOf(d), d);
                Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}] 시큐리티 맨 위로" : "대상이 없습니다", attackerRemoved: list.Contains(c.Other!));
            }, null);
        }
        // "Change the original DP of 1 of your opponent's Digimon to 3000 until ~" — 원래 DP와의 차이만큼 DP 변화로 처리
        if ((m = Regex.Match(s, @"^Change the original DP of (?<t>.+?) to (?<n>\d+)(?<dur> .+)?$", RegexOptions.IgnoreCase)).Success)
        {
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (target == null) return (null, $"미지원 대상 (원래 DP): {m.Groups["t"].Value}");
            int n = int.Parse(m.Groups["n"].Value);
            string leftover = "";
            var origDur = m.Groups["dur"].Success ? ParseDuration(m.Groups["dur"].Value.Trim(), out leftover) : Duration.ThisTurn;
            if (m.Groups["dur"].Success && (origDur == Duration.None || leftover.Trim().Length > 0)) return (null, $"미지원 기간: {m.Groups["dur"].Value}");
            return (c =>
            {
                var list = Pick(target, c, security);
                bool removed = false;
                foreach (var d in list)
                    if (c.Engine.ApplyDpChange(d, n - d.TopCard.DP, Until(c, origDur), $"{c.Card.Name} [{label}]") && ReferenceEquals(d, c.Other)) removed = true;
                Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}] 원래 DP {n:N0}" : "대상이 없습니다", attackerRemoved: removed);
            }, null);
        }

        // "Give 1 of your opponent's Digimon ＜Security A. -1＞ until their turn ends" = "~ gains ＜…＞"
        var give = Regex.Match(s, @"^Give (?<t>.+?) (?<kws>＜.+)$", RegexOptions.IgnoreCase);
        if (give.Success)
            s = $"{give.Groups["t"].Value} gains {give.Groups["kws"].Value}";
        var giveDp = Regex.Match(s, @"^Give (?<t>.+?) (?<dp>[+-]\d+ DP.*)$", RegexOptions.IgnoreCase);
        if (giveDp.Success)
            s = $"{giveDp.Groups["t"].Value} gets {giveDp.Groups["dp"].Value}";
        // 진화원 파기의 다른 표기: "Trash 4 digivolution cards at the bottom of ~" · "Trash all digivolution cards under ~"
        var trashAt = Regex.Match(s, @"^Trash (?:up to )?(?<n>\d+) digivolution cards? (?:at|from) the (?<pos>bottom|top) of (?<t>.+)$", RegexOptions.IgnoreCase);
        if (trashAt.Success)
            return ParseAction($"Trash the {trashAt.Groups["pos"].Value} {trashAt.Groups["n"].Value} digivolution cards of {trashAt.Groups["t"].Value}", card, triggers);
        var trashAll = Regex.Match(s, @"^Trash all (?:of the )?digivolution cards (?:under|of) (?<t>.+)$", RegexOptions.IgnoreCase);
        if (trashAll.Success)
            return ParseAction($"Trash the bottom 99 digivolution cards of {trashAll.Groups["t"].Value}", card, triggers);
        // "This Digimon unsuspends" = "Unsuspend this Digimon"
        var unsus = Regex.Match(s, @"^(?<t>.+?) unsuspends?$", RegexOptions.IgnoreCase);
        if (unsus.Success && !s.StartsWith("Unsuspend", StringComparison.OrdinalIgnoreCase))
            return ParseAction($"Unsuspend {unsus.Groups["t"].Value}", card, triggers);
        // "This Digimon's attack target can't change" · "~ can't be switched" — 이 어택 동안 (어택 중이 아니면 효과 없음)
        if (Regex.IsMatch(s, @"^(?:This Digimon's|Its) attack target can't (?:change|be changed|be switched|switch)(?: for the turn)?$", RegexOptions.IgnoreCase))
            return (c =>
            {
                if (c.Self != null && ReferenceEquals(c.Engine.CurrentAttacker, c.Self)) { c.Self.AttackTargetLocked = true; Emit(c, label, "이 어택의 대상이 바뀌지 않습니다"); }
                else c.LastStepResolved = false;
            }, null);
        // "Change the attack target to this Digimon" · "switch the attack target to 1 of your [Eosmon]" · "~ to 1 of your suspended Digimon with the [WG] trait"
        if ((m = Regex.Match(s, @"^(?:Change|Switch) the attack target to (?<t>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (target == null || target.Opponent || target.AnySide || target.IsThat) return (null, $"미지원 대상 (어택 대상 변경): {m.Groups["t"].Value}");
            target.PreferOwnSide = true;
            return (c =>
            {
                var d = Pick(target, c, security).FirstOrDefault();
                if (d != null && c.Engine.RedirectAttack(c.Owner, d))
                    Emit(c, label, $"어택 대상을 [{d.TopCard.Name}]으로 바꿨습니다");
                else c.LastStepResolved = false;
            }, null);
        }

        var dur = ParseDuration(s, out string core);

        // "~ gains ＜Blocker＞ and gets +2000 DP" · "~ get -5000 DP and gain ＜Security A. -1＞"
        var combo = Regex.Match(core, @"^(?<t>.+?) (?:gains? (?<kws>＜[^＞]+＞(?:\s*(?:,|and)\s*＜[^＞]+＞)*)\s*,?\s*and gets? (?<sign>[+-])(?<n>\d+) DP|gets? (?<sign>[+-])(?<n>\d+) DP\s*,?\s*and (?:gains? )?(?<kws>＜[^＞]+＞(?:\s*(?:,|and)\s*＜[^＞]+＞)*))$", RegexOptions.IgnoreCase);
        if (combo.Success)
        {
            var target = TargetSpec.Parse(combo.Groups["t"].Value);
            var grants = ParseKeywordList(combo.Groups["kws"].Value);
            if (target == null || grants == null) return (null, "미지원 키워드 부여 + DP");
            int dp = int.Parse(combo.Groups["n"].Value) * (combo.Groups["sign"].Value == "-" ? -1 : 1);
            target.PreferOwnSide = dp > 0;
            var d2 = dur == Duration.None ? Duration.ThisTurn : dur;
            return (c =>
            {
                int until = Until(c, d2);
                var list = Pick(target, c, security);
                foreach (var d in list)
                {
                    foreach (var (k, sa) in grants)
                    {
                        if (k is { } kw) d.TimedKeywords.Add((kw, until));
                        else d.TimedSecurityAttackBuffs.Add((sa, until));
                    }
                    c.Engine.ApplyDpChange(d, dp, until, $"{c.Card.Name} [{label}]");
                }
                c.RecalculateFieldBuffs();
                Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}] DP {(dp > 0 ? "+" : "")}{dp:N0} · 키워드 획득" : "대상이 없습니다");
            }, null);
        }
        if ((m = Regex.Match(core, @"^(?<t>.+?) gets? (?<sign>[+-])(?<n>\d+) DP$", RegexOptions.IgnoreCase)).Success)
        {
            if (dur == Duration.None) dur = Duration.ThisTurn;   // 발동 효과의 기간 없는 DP 변화 = 이 턴 동안
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (target == null) return (null, $"미지원 대상 (DP): {m.Groups["t"].Value}");
            int dp = int.Parse(m.Groups["n"].Value) * (m.Groups["sign"].Value == "-" ? -1 : 1);
            target.PreferOwnSide = dp > 0;
            return (c =>
            {
                var list = Pick(target, c, security);
                bool removed = false;
                foreach (var d in list)
                {
                    if (dur == Duration.ThisAttack) { d.AttackDpBonus += dp; continue; }
                    if (c.Engine.ApplyDpChange(d, dp, Until(c, dur), $"{c.Card.Name} [{label}]") && ReferenceEquals(d, c.Other)) removed = true;
                }
                Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}] DP {(dp > 0 ? "+" : "")}{dp:N0}" : "대상이 없습니다", attackerRemoved: removed);
            }, null);
        }
        if ((m = Regex.Match(core, @"^(?<t>.+?) gains? (?<kws>＜.+)$", RegexOptions.IgnoreCase)).Success)
        {
            if (dur == Duration.None) dur = Duration.ThisTurn;
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            var grants = ParseKeywordList(m.Groups["kws"].Value);
            if (target == null || grants == null) return (null, "미지원 키워드 부여");
            target.PreferOwnSide = true;
            return (c =>
            {
                int until = Until(c, dur);
                var list = Pick(target, c, security);
                foreach (var d in list)
                    foreach (var (k, sa) in grants)
                    {
                        if (k is { } kw) d.TimedKeywords.Add((kw, until));
                        else if (dur == Duration.ThisAttack) d.AttackSecurityAttackBonus += sa;
                        else d.TimedSecurityAttackBuffs.Add((sa, until));
                    }
                c.RecalculateFieldBuffs();
                Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}] 키워드 획득" : "대상이 없습니다");
            }, null);
        }
        if ((m = Regex.Match(core, @"^(?<t>.+?) can't (?<what>attack or block|attack and block|attack|block)$", RegexOptions.IgnoreCase)).Success)
        {
            if (dur == Duration.None) return (null, "미지원: 기간 없는 어택 · 블록 불가");
            var target = TargetSpec.Parse(m.Groups["t"].Value);
            if (target == null) return (null, $"미지원 대상 (어택 · 블록 불가): {m.Groups["t"].Value}");
            string what = m.Groups["what"].Value.ToLowerInvariant();
            return (c =>
            {
                int until = Until(c, dur);
                var list = Pick(target, c, security);
                foreach (var d in list)
                {
                    if (what.Contains("attack")) d.CannotAttackUntil = Math.Max(d.CannotAttackUntil, until);
                    if (what.Contains("block")) d.CannotBlockUntil = Math.Max(d.CannotBlockUntil, until);
                }
                Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}] {(what.Contains("block") && what.Contains("attack") ? "어택 · 블록" : what.Contains("block") ? "블록" : "어택")} 불가" : "대상이 없습니다");
            }, null);
        }

        // "1 of your opponent's Digimon can't unsuspend until the end of their turn" · "1 of their Digimon or Tamers can't suspend until their turn ends"
        //  — 기간 동안 액티브(액티브 페이즈 · 효과) / 레스트(어택 · 블록 · 효과)가 될 수 없다. 테이머는 대상에서 뺀다
        if (dur != Duration.None && dur != Duration.ThisAttack
            && (m = Regex.Match(core, @"^(?<t>.+?) (?:can't|cannot|doesn't|don't|does not|do not) (?<v>unsuspend|suspend|become unsuspended|become suspended)$", RegexOptions.IgnoreCase)).Success)
        {
            bool un = m.Groups["v"].Value.Contains("unsuspend", StringComparison.OrdinalIgnoreCase);
            var target = TargetSpec.Parse(Regex.Replace(m.Groups["t"].Value, @"\bDigimon or Tamers\b", "Digimon", RegexOptions.IgnoreCase));
            if (target == null) return (null, $"미지원 대상 ({(un ? "액티브" : "레스트")} 불가): {m.Groups["t"].Value}");
            return (c =>
            {
                int until = Until(c, dur);
                var list = Pick(target, c, security);
                foreach (var d in list)
                {
                    if (un) d.CannotUnsuspendUntil = Math.Max(d.CannotUnsuspendUntil, until);
                    else d.CannotSuspendUntil = Math.Max(d.CannotSuspendUntil, until);
                }
                Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}] {(un ? "액티브" : "레스트")}가 될 수 없음" : "대상이 없습니다");
            }, null);
        }

        // "It doesn't unsuspend during your opponent's next unsuspend phase" — 대상 주인의 다음 액티브 페이즈에 액티브가 되지 않는다
        if ((m = Regex.Match(core, @"^(?<t>.+?) (?:doesn't|don't|does not|do not|can't) (?:unsuspend|become unsuspended) (?:during|in) (?:its owner's|their owner's|your opponent's|their|your|its) next unsuspend phase$", RegexOptions.IgnoreCase)).Success)
        {
            string tp = Regex.Replace(m.Groups["t"].Value, @"^none of ", "all of ", RegexOptions.IgnoreCase);
            var target = TargetSpec.Parse(tp);
            if (target == null) return (null, $"미지원 대상 (액티브 불가): {m.Groups["t"].Value}");
            return (c =>
            {
                var list = Pick(target, c, security);
                foreach (var d in list)
                {
                    // 대상 주인의 턴이면 그다음 주인 턴, 아니면 바로 다음 턴의 액티브 페이즈
                    bool ownersTurn = c.Engine.IsTurnOf(c.Owner.BattleArea.Contains(d) ? c.Owner : c.Enemy);
                    d.SkipUnsuspendUntil = Math.Max(d.SkipUnsuspendUntil, c.TurnSequence + (ownersTurn ? 2 : 1));
                }
                Emit(c, label, list.Count > 0 ? $"[{string.Join(", ", list.Select(d => d.TopCard.Name))}] 다음 액티브 페이즈에 액티브가 되지 않음" : "대상이 없습니다");
            }, null);
        }

        // "＜Draw 1＞ and gain 1 memory" — 두 동작이 and로 이어진 문장
        foreach (Match and in Regex.Matches(s, @"\s+and\s+", RegexOptions.IgnoreCase))
        {
            string left = s[..and.Index].Trim(), right = s[(and.Index + and.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;
            right = char.ToUpperInvariant(right[0]) + right[1..];
            var a = ParseAction(left, card, triggers);
            if (a.Error != null) continue;
            var b = ParseAction(right, card, triggers);
            if (b.Error != null) continue;
            var (actA, actB) = (a.Act!, b.Act!);
            return (c => { actA(c); actB(c); }, null);
        }

        // DNA 진화: "2 of your Digimon may DNA digivolve into [Silphymon] in the hand" · "This Digimon and any of your other Digimon may DNA digivolve into a Digimon card in the hand"
        // (조그레스 조건 · 코스트는 그 카드의 것 — 공식 한글 문구, GoldfishEngine.Dna.cs)
        if ((m = Regex.Match(s, @"^(?<who>2 of your Digimon|this Digimon and (?:any of )?(?:your other|another of your) (?<spec>[^,]*?)Digimon(?<with> with [^,]+?)?) (?:may )?DNA digivolve into (?<pick>.+?) in (?:the|your) hand(?<free> without paying (?:the|its) cost)?$", RegexOptions.IgnoreCase)).Success)
        {
            bool withSelf = m.Groups["who"].Value.StartsWith("this", StringComparison.OrdinalIgnoreCase);
            var other = withSelf ? TargetSpec.Parse("all of your " + m.Groups["spec"].Value + "Digimon" + m.Groups["with"].Value) : null;
            var pick = ParseCardPick("1 " + m.Groups["pick"].Value) ?? ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick("1 " + m.Groups["pick"].Value);
            if (pick == null || (withSelf && other == null)) return (null, $"미지원 DNA 진화 대상: {s}");
            var match = pick.Value.Match;
            bool free = m.Groups["free"].Success;
            return (c =>
            {
                Func<CardInstance, CardInstance, bool> pairOk = (l, r) => !withSelf || ReferenceEquals(l, c.Self) && other!.Filters.All(f => f(r)) || ReferenceEquals(r, c.Self) && other!.Filters.All(f => f(l));
                var options = c.Owner.Hand.Where(x => x.Type == CardType.Digimon && match(x))
                    .Select(x => (Card: x, Pairs: c.Engine.DnaPairs(c.Owner, x).Where(p => pairOk(p.Left, p.Right)).ToList()))
                    .Where(o => o.Pairs.Count > 0 && (free || (GoldfishEngine.GetDnaCost(o.Card) ?? 99) <= Math.Max(0, c.OwnerMemory) || !c.OwnerIsPlayer))
                    .OrderByDescending(o => o.Card.Level).ThenByDescending(o => o.Card.DP).ToList();
                if (options.Count == 0 || !c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] DNA 진화할까요? (후보: {string.Join(", ", options.Select(o => o.Card.Name).Distinct())})", recommended: true))
                {
                    c.LastStepResolved = false;
                    Emit(c, label, options.Count == 0 ? "DNA 진화할 수 있는 카드 · 디지몬이 없습니다" : "DNA 진화하지 않았습니다");
                    return;
                }
                var cardsList = options.Select(o => o.Card).ToList();
                int ci = c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Hand, cardsList, 1, upTo: false, $"[{c.Card.Name}] DNA 진화할 카드를 고르세요.").FirstOrDefault();
                var chosen = options[Math.Clamp(ci, 0, options.Count - 1)];
                // 두 마리: 첫 번째 → 그와 짝이 되는 두 번째 (추천 = 진화원이 많은 쪽부터)
                var firsts = chosen.Pairs.SelectMany(p => new[] { p.Left, p.Right }).Distinct().OrderByDescending(d => d.EvolutionSources.Count).ToList();
                var a = c.Engine.ChooseTargets(c.Owner, firsts, 1, $"[{chosen.Card.Name}]으로 DNA 진화할 디지몬 1마리를 고르세요.", optional: false, preselected: firsts[0]).FirstOrDefault() ?? firsts[0];
                var partners = chosen.Pairs.Where(p => ReferenceEquals(p.Left, a) || ReferenceEquals(p.Right, a)).Select(p => ReferenceEquals(p.Left, a) ? p.Right : p.Left).Distinct().ToList();
                if (partners.Count == 0) { c.LastStepResolved = false; Emit(c, label, "짝이 되는 디지몬이 없습니다"); return; }
                var b = partners.Count == 1 ? partners[0]
                    : c.Engine.ChooseTargets(c.Owner, partners, 1, $"[{a.TopCard.Name}]과(와) 함께 DNA 진화할 디지몬을 고르세요.", optional: false, preselected: partners[0]).FirstOrDefault() ?? partners[0];
                int cost = free ? 0 : GoldfishEngine.GetDnaCost(chosen.Card) ?? 0;
                var dnaMsg = c.Engine.DnaDigivolveCore(c.Owner, chosen.Card, a, b, cost, c.Owner.Hand);
                var dnaDigimon = c.Owner.BattleArea.FirstOrDefault(d => ReferenceEquals(d.TopCard, chosen.Card));
                c.LastTargets = dnaDigimon != null ? new List<CardInstance> { dnaDigimon } : new List<CardInstance>();
                c.LastStepResolved = dnaDigimon != null;
                Emit(c, label, dnaMsg);
            }, null);
        }
        // "1 of your Digimon may digivolve into [Greymon] in the hand without paying the cost" · "~ in the hand or trash" · "~ from your trash"
        // 육성: "1 of your Digimon in the breeding area may digivolve into ~" (육성 에어리어에서의 진화 — [진화 시]는 발휘하지 않는다)
        if ((m = Regex.Match(s, @"^(?<t>(?<breed>(?:\d+ of )?your (?<bspec>[^,]*?)Digimon(?<bwith> with [^,]+?)? in (?:the|your) breeding area)|this Digimon|\d+ of your (?:other )?(?:[^,]*? )?Digimon(?: with [^,]+?)?) (?:may )?digivolves? into (?<pick>.+?) (?:in|from) (?:the |your )?(?<from>hand or (?:the |your |from your )?trash|hand|trash)(?: for (?:a digivolution cost of (?<cost>\d+)|its digivolution cost))? ?(?<free>without paying (?:the|its) (?:memory |digivolution )?cost|with the digivolution cost reduced by (?<red>\d+))?$", RegexOptions.IgnoreCase)).Success)
        {
            bool breed = m.Groups["breed"].Success;
            var target = breed ? TargetSpec.Parse("all of your " + m.Groups["bspec"].Value + "Digimon" + m.Groups["bwith"].Value) : TargetSpec.Parse(m.Groups["t"].Value);
            var pick = ParseCardPick("1 " + m.Groups["pick"].Value) ?? ParseCardPick(m.Groups["pick"].Value) ?? ParseNamePick("1 " + m.Groups["pick"].Value);
            if (target == null || pick == null) return (null, $"미지원 진화 대상: {s}");
            var match = pick.Value.Match;
            bool free = m.Groups["free"].Value.StartsWith("without", StringComparison.OrdinalIgnoreCase);
            int reduce = m.Groups["red"].Success ? int.Parse(m.Groups["red"].Value) : 0;
            int? fixedCost = m.Groups["cost"].Success ? int.Parse(m.Groups["cost"].Value) : null;
            string from = m.Groups["from"].Value.ToLowerInvariant();
            bool fromHand = from.Contains("hand"), fromTrash = from.Contains("trash");
            return (c =>
            {
                IEnumerable<CardInstance> bases = breed
                    ? (c.Owner.BreedingArea is { } b && b.TopCard.Type == CardType.Digimon && target.Filters.All(f => f(b)) ? new[] { b } : Array.Empty<CardInstance>())
                    : Pick(target, c, security).Where(x => c.Owner.BattleArea.Contains(x));
                foreach (var d in bases.ToList())
                {
                    var pool = new List<PoolCard>();
                    if (fromHand) pool.AddRange(PoolFrom(c.Owner.Hand, x => match(x) && ActionValidator.ValidateEvolution(d, x).CanExecute));
                    if (fromTrash) pool.AddRange(PoolFrom(c.Owner.Trash, x => match(x) && ActionValidator.ValidateEvolution(d, x).CanExecute));
                    var evo = pool.OrderByDescending(x => x.Card.Level).ThenByDescending(x => x.Card.DP).FirstOrDefault();
                    if (evo.Card == null) continue;
                    int cost = free ? 0 : fixedCost ?? Math.Max(0, evo.Card.EvoCost - (c.Engine.CanReduceEvoCost(c.Owner) ? reduce : 0));
                    c.Engine.EvolveByEffect(c.Owner, d, evo.Card, cost, evo.Zone);
                    if (breed) c.LastTargets = new List<CardInstance> { d };
                    Emit(c, label, $"{(breed ? "육성 에어리어의 " : "")}[{d.TopCard.Name}]으로 진화 (코스트 {cost})");
                    return;
                }
                Emit(c, label, "진화할 수 있는 카드가 없습니다");
                c.LastStepResolved = false;
            }, null);
        }

        return (null, $"문장 파서(3단계) 대상: {s}");
    }

    /// <summary>대상을 고르고 앞 문장 대상("that Digimon")으로 기억한다</summary>
    private static List<CardInstance> Pick(TargetSpec target, EffectContext c, bool security)
    {
        var list = target.Resolve(c, Preferred(c, security));
        c.LastTargets = list;
        return list;
    }

    /// <summary>대상 고르기에서 먼저 볼 디지몬: [시큐리티]면 어택 중인 디지몬, 그 밖엔 UI가 고른 대상 → 어택 대상</summary>
    private static CardInstance? Preferred(EffectContext c, bool security) => security ? c.Other : c.ChosenTarget ?? c.Other;

    /// <summary>여러 곳에서 모은 후보 1장 — Zone = 있던 곳 (null이면 이 디지몬의 진화원)</summary>
    private readonly record struct PoolCard(CardData Card, List<CardData>? Zone);

    private static IEnumerable<PoolCard> PoolFrom(List<CardData> zone, Func<CardData, bool> match) => zone.Where(match).Select(x => new PoolCard(x, zone));

    /// <summary>
    /// 후보(추천 순서) 중 count장을 고른다. 같은 카드 여러 장은 같은 객체라 패와 트래시에 함께 있을 수 있으므로
    /// 번호로 골라 그 후보가 있던 곳을 함께 돌려준다 (빼기는 TakeFromPool).
    /// </summary>
    private static List<PoolCard> ChooseFromPool(EffectContext c, CardChoiceZone zone, IReadOnlyList<PoolCard> ranked, int count, string question) =>
        c.Engine.ChooseCardIndexes(c.Owner, zone, ranked.Select(p => p.Card).ToList(), count, upTo: false, question).Select(i => ranked[i]).ToList();

    /// <summary>고른 후보를 그 후보가 있던 곳에서 뺀다 (이미 없으면 false)</summary>
    private static bool TakeFromPool(EffectContext c, PoolCard p) =>
        p.Zone != null ? p.Zone.Remove(p.Card) : c.Self?.TakeSource(x => ReferenceEquals(x, p.Card)) != null;

    /// <summary>효과 결과 기록: [메인] · [시큐리티]는 결과 메시지로, 그 밖엔 로그로</summary>
    /// <summary>
    /// 비용("By ~ing, ~")을 낼지: 낼 수 없거나 내지 않기로 하면 로그를 남긴다 (조용히 넘어가면 효과가 안 나온 것처럼 보인다)
    /// </summary>
    private static bool PayOrSkip(EffectContext c, Func<EffectContext, bool> canPay, Func<EffectContext, bool> recommend, string describe)
    {
        if (!canPay(c))
        {
            c.Log($"[{c.Card.Name}] 비용을 낼 수 없어 효과를 발휘하지 않습니다 ({describe} 발휘하는 효과)");
            return false;
        }
        if (!c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] {describe} 효과를 발휘할까요?", recommend(c)))
        {
            c.Log($"[{c.Card.Name}] 효과를 발휘하지 않았습니다 ({describe} 발휘하는 효과)");
            return false;
        }
        return true;
    }

    private static void Emit(EffectContext c, string label, string text, bool attackerRemoved = false)
    {
        if (attackerRemoved) c.AttackerRemoved = true;
        if (label.Contains("시큐리티") || label == "메인")
            c.Message = c.Message.Length == 0 ? text : $"{c.Message} / {text}";
        else
            c.Log($"[{label}] {c.Card.Name}: {text}");
    }

    private static void ActivateMainEffects(EffectContext c, string label)
    {
        var script = CardScripts.For(c.Card);
        foreach (var e in script?.Effects.Where(e => e.Trigger == EffectTrigger.Main) ?? Enumerable.Empty<CardEffect>())
            e.Resolve(c);
        if (c.Summary.Length == 0) c.Summary = "[메인] 효과 발휘";
    }

    // ---- 덱 위 공개 ----

    private enum RevealVerb { Add, Play, Trash, Place, AddOrPlace }

    /// <summary>공개한 카드에 하는 일 하나: "add 1 X to the hand" · "play 1 X without paying the cost" · "trash 1 such card" · "place 1 X under this Digimon"</summary>
    private sealed record RevealAction(RevealVerb Verb, List<(int Count, Func<CardData, bool> Match, bool UpTo)> Picks, Func<EffectContext, CardData, bool>? Place, bool NoOnPlay);

    /// <summary>공개 효과의 마지막 문장: "Return the rest to the bottom of the deck" · "Trash the remaining cards" · "Return the revealed cards to the top or bottom of the deck"</summary>
    internal const string RevealRest = @"(?:Return|Place|Put|Trash) (?:the |those )?(?:rest|remaining cards?|revealed cards?|cards)\b[^.]*";

    private static (Action<EffectContext>? Act, string? Error) ParseReveal(Match m, string label, CardData card, List<EffectTrigger> triggers)
    {
        int n = int.Parse(m.Groups["n"].Value);
        Func<PlayerZones, PlayerZones, CardInstance?, int>? perCount = null;
        if (m.Groups["counter"].Success && (perCount = ParseCounter(m.Groups["counter"].Value)) == null)
            return (null, $"미지원 공개 매수: for each {m.Groups["counter"].Value}");
        bool security = triggers.Contains(EffectTrigger.Security);

        // "Among them, add ~ and trash 1 such card" · "You may add ~, and play ~"
        string mid = Regex.Replace(m.Groups["mid"].Value.Trim().TrimEnd('.'), @"^Among them,\s*", "", RegexOptions.IgnoreCase);
        bool mayAll = Regex.IsMatch(mid, @"^You may ", RegexOptions.IgnoreCase);
        if (mayAll) mid = mid[8..];
        var actions = new List<RevealAction>();
        string? prevPart = null;
        foreach (var rawAction in Regex.Split(mid, @",? and (?=(?:you may )?(?:add|play|place|trash) )", RegexOptions.IgnoreCase))
        {
            string a = rawAction.Trim();
            bool may = mayAll || Regex.IsMatch(a, @"^you may ", RegexOptions.IgnoreCase);
            a = Regex.Replace(a, @"^you may ", "", RegexOptions.IgnoreCase);
            bool noOnPlay = a.Contains("⟦no-onplay⟧");
            a = a.Replace("⟦no-onplay⟧", "").Trim();
            Match am;
            RevealVerb verb;
            string picksText;
            Func<EffectContext, CardData, bool>? place = null;
            // "add 1 X to the hand or place it under this Digimon as its bottom digivolution card" — 둘 중 하나를 고른다
            if (((am = Regex.Match(a, @"^add (?<p>.+?)(?: (?:from )?among them)? to (?:the|your) hand,? or place (?:it|1 such card) (?<w>(?:face down )?(?:under|as) .+)$", RegexOptions.IgnoreCase)).Success
                 || (am = Regex.Match(a, @"^place (?<p>.+?)(?: among them)? (?<w>(?:face down )?(?:under|as) .+?),? or add it to (?:the|your) hand$", RegexOptions.IgnoreCase)).Success)
                && (place = ParseRevealPlace(am.Groups["w"].Value, security)) != null)
                (verb, picksText) = (RevealVerb.AddOrPlace, am.Groups["p"].Value);
            else if ((am = Regex.Match(a, @"^add (?<p>.+?)(?: (?:from )?among them)? to (?:the|your) hand$", RegexOptions.IgnoreCase)).Success)
                (verb, picksText) = (RevealVerb.Add, am.Groups["p"].Value);
            else if ((am = Regex.Match(a, @"^play (?<p>.+?)(?: (?:from )?among(?: them)?)?(?: without paying (?:its|the|their) (?:memory |play )?costs?)?$", RegexOptions.IgnoreCase)).Success)
                (verb, picksText) = (RevealVerb.Play, am.Groups["p"].Value);
            else if ((am = Regex.Match(a, @"^place (?<p>.+?)(?: among them)? (?<w>(?:face down )?(?:under|as) .+)$", RegexOptions.IgnoreCase)).Success
                     && (place = ParseRevealPlace(am.Groups["w"].Value, security)) != null)
                (verb, picksText) = (RevealVerb.Place, am.Groups["p"].Value);
            else if ((am = Regex.Match(a, @"^trash (?<p>.+?)(?: among them)?$", RegexOptions.IgnoreCase)).Success)
                (verb, picksText) = (RevealVerb.Trash, am.Groups["p"].Value);
            else return (null, $"미지원 공개 후 처리: {a}");

            var picks = new List<(int Count, Func<CardData, bool> Match, bool UpTo)>();
            var parts = Regex.Split(picksText, @"\s+and\s+(?=(?:up to )?\d+ )", RegexOptions.IgnoreCase);
            // "1 blue and 1 green Digimon card" — 앞 조건에 뒤의 종류를 붙인다
            string kindTail = Regex.Match(parts[^1], @"(?:Digimon|Tamer|Option) cards?$", RegexOptions.IgnoreCase).Value;
            foreach (var raw in parts)
            {
                string part = kindTail.Length > 0 && !Regex.IsMatch(raw, @"\b(?:cards?|Digimon|Tamers?|Options?)\b|\[", RegexOptions.IgnoreCase) ? $"{raw} {kindTail}" : raw;
                bool such = Regex.IsMatch(part, @"^(?:up to )?\d+ such ", RegexOptions.IgnoreCase);
                var pick = such ? ParseSuchPick(part, prevPart) : ParseCardPick(part) ?? ParseNamePick(part);
                if (pick == null) return (null, $"미지원 공개 조건 ({part})");
                picks.Add((pick.Value.Count, pick.Value.Match, may || IsUpTo(part)));
                if (!such) prevPart = part;
            }
            actions.Add(new RevealAction(verb, picks, place, noOnPlay));
        }

        string rest = m.Groups["rest"].Value.TrimEnd('.');
        int restMode;   // 0 덱 아래 · 1 덱 위 · 2 파기
        if (Regex.IsMatch(rest, @"^Trash the (?:rest|remaining cards?|revealed cards?)$", RegexOptions.IgnoreCase)) restMode = 2;
        else if (Regex.Match(rest, @"^(?:Return|Place|Put) (?:the |those )?(?:rest|remaining cards?|revealed cards?|cards) (?:to|at|on|in) (?:the )?(?<or>top or (?:the )?|either the top or (?:the )?)?(?<pos>bottom|top) of (?:the |your )?deck(?: in any order)?$", RegexOptions.IgnoreCase) is { Success: true } rm)
            restMode = rm.Groups["pos"].Value.Equals("top", StringComparison.OrdinalIgnoreCase) && !rm.Groups["or"].Success ? 1 : 0;
        else return (null, $"미지원 공개 후 처리: {rest}");

        return (c =>
        {
            int total = perCount != null ? n * Math.Min(perCount(c.Owner, c.Enemy, c.Self), 20) : n;
            var deck = c.Owner.MainDeck;
            var revealed = deck.Take(total).ToList();
            deck.RemoveRange(0, revealed.Count);
            // 같은 카드 여러 장은 같은 CardData 객체이므로, 고른 카드는 공개한 순서의 번호로 기억한다
            var takenIndex = new HashSet<int>();
            var chosen = new List<(RevealAction Action, List<CardData> Cards)>();
            foreach (var action in actions)
            {
                var cards = new List<CardData>();
                bool play = action.Verb == RevealVerb.Play;
                string what = action.Verb switch { RevealVerb.Play => "등장시킬", RevealVerb.Trash => "파기할", RevealVerb.Place => "진화원 아래에 놓을", RevealVerb.AddOrPlace => "패에 넣거나 진화원 아래에 놓을", _ => "패에 넣을" };
                foreach (var (count, match, upTo) in action.Picks)
                {
                    var pool = Enumerable.Range(0, revealed.Count)
                        .Where(i => !takenIndex.Contains(i) && match(revealed[i]) && (!play || revealed[i].Type is CardType.Digimon or CardType.Tamer))
                        .OrderByDescending(i => revealed[i].Level).ThenByDescending(i => revealed[i].DP).ToList();
                    foreach (int k in c.Engine.ChooseCardIndexes(c.Owner, CardChoiceZone.Other, pool.Select(i => revealed[i]).ToList(), count, upTo,
                                 $"[{c.Card.Name}] 공개한 카드 중 {what} 카드를 고르세요."))
                    { takenIndex.Add(pool[k]); cards.Add(revealed[pool[k]]); }
                }
                chosen.Add((action, cards));
            }
            int takenCount = takenIndex.Count;
            revealed = revealed.Where((_, i) => !takenIndex.Contains(i)).ToList();
            if (restMode == 2) c.Owner.Trash.AddRange(revealed);
            else if (restMode == 1) deck.InsertRange(0, revealed);
            else deck.AddRange(revealed);

            var log = new List<string>();
            var toPlay = new List<(CardData Card, bool NoOnPlay)>();
            foreach (var (action, cards) in chosen)
            {
                if (cards.Count == 0) continue;
                string names = string.Join(", ", cards.Select(x => x.Name));
                switch (action.Verb)
                {
                    case RevealVerb.Add: c.Owner.Hand.AddRange(cards); log.Add($"[{names}] 패에 추가"); break;
                    case RevealVerb.Trash: c.Owner.Trash.AddRange(cards); log.Add($"[{names}] 파기"); break;
                    case RevealVerb.Place:
                        foreach (var x in cards)
                            if (!action.Place!(c, x)) c.Owner.Trash.Add(x);   // 놓을 곳이 없으면 파기 (공개한 카드는 덱으로 돌아가지 않는다)
                        c.RecalculateFieldBuffs();
                        log.Add($"[{names}] 진화원 아래에");
                        break;
                    case RevealVerb.AddOrPlace:
                        foreach (var x in cards)
                        {
                            // 놓을지 묻는다 (아니오 = 패에 추가). AI는 패에 넣는다
                            if (c.Engine.AskYesNo(c.Owner, c.Self, $"[{c.Card.Name}] [{x.Name}]을(를) 진화원 아래에 놓을까요? (아니오 = 패에 추가)", false) && action.Place!(c, x))
                            { c.RecalculateFieldBuffs(); log.Add($"[{x.Name}] 진화원 아래에"); }
                            else { c.Owner.Hand.Add(x); log.Add($"[{x.Name}] 패에 추가"); }
                        }
                        break;
                    default: toPlay.AddRange(cards.Select(x => (x, action.NoOnPlay))); break;
                }
            }
            // 공개한 카드 중에서 코스트 없이 등장 (남은 카드를 덱으로 보낸 뒤 — [등장 시] 효과가 덱을 볼 수 있게)
            var played = new List<string>();
            foreach (var (t, noOnPlay) in toPlay)
            {
                if (c.Owner.BattleArea.Count >= 12) { deck.Add(t); continue; }
                c.Engine.PlayWithoutCost(c.Owner, t, triggerOnPlay: !noOnPlay);
                played.Add(t.Name);
            }
            if (played.Count > 0) log.Add($"[{string.Join(", ", played)}] 코스트 없이 등장");
            if (takenCount == 0) c.LastStepResolved = false;   // "If you added cards, ~"
            bool onlyPlay = actions.All(x => x.Verb == RevealVerb.Play);
            Emit(c, label, log.Count > 0 ? $"덱 위 {total}장 공개 → {string.Join(" · ", log)}"
                : onlyPlay ? $"덱 위 {total}장 공개 → 등장시킬 카드 없음" : $"덱 위 {total}장 공개 → 조건에 맞는 카드 없음");
        }, null);
    }

    /// <summary>"1 such Tamer card" — 앞에서 고른 카드의 조건(종류 제외)에 새 종류를 더한다</summary>
    private static (int Count, Func<CardData, bool> Match)? ParseSuchPick(string part, string? prevPart)
    {
        var m = Regex.Match(part.Trim(), @"^(?:up to )?(?<n>\d+) such (?<k>.+)$", RegexOptions.IgnoreCase);
        if (!m.Success || prevPart == null) return null;
        var kind = ParseCardPick($"{m.Groups["n"].Value} {m.Groups["k"].Value}");
        string baseText = Regex.Replace(prevPart, @"\b(?:Digimon|Tamer|Option) (?=cards?\b)", "", RegexOptions.IgnoreCase);
        var basePick = ParseCardPick(baseText) ?? ParseNamePick(prevPart);
        if (kind == null || basePick == null) return null;
        var (km, bm) = (kind.Value.Match, basePick.Value.Match);
        return (kind.Value.Count, x => km(x) && bm(x));
    }

    /// <summary>공개한 카드를 놓을 곳: "under this Digimon (as its bottom digivolution card)" · "under this Tamer" · "face down under any of your [X] trait Tamers" · "as the bottom digivolution card of 1 of your Digimon"</summary>
    private static Func<EffectContext, CardData, bool>? ParseRevealPlace(string where, bool security)
    {
        where = where.Trim();
        if (Regex.IsMatch(where, @"^(?:under this (?:Digimon|card)(?: as its bottom digivolution card)?|as (?:this Digimon's|its) bottom digivolution card)$", RegexOptions.IgnoreCase))
            return (c, x) => { if (c.Self == null) return false; c.Self.AddBottomSource(x); return true; };
        if (Regex.IsMatch(where, @"^(?:face down )?under (?:this Tamer|(?:any of |1 of |one of )?your (?:.+? )?Tamers?)$", RegexOptions.IgnoreCase))
            return (c, x) => { if (c.Owner.Tamers.Count == 0) return false; c.Owner.UnderTamers.Add(x); return true; };
        var m = Regex.Match(where, @"^(?:face down )?(?:as the bottom digivolution card of (?<t>.+)|under (?<t>.+?) as its bottom digivolution card)$", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(where, @"^(?:face down )?as (?<t>.+?)'s bottom digivolution card$", RegexOptions.IgnoreCase);   // "as any of your blue Digimon's bottom digivolution card"
        if (!m.Success) return null;
        // "any of your Digimon with the [DM] trait" · "1 of your [Mother D-Reaper]s" — 한 마리를 고른다
        string t = Regex.Replace(m.Groups["t"].Value, @"^any of your ", "1 of your ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\]s\b", "]");
        if (TargetSpec.Parse(t) is not { } target) return null;
        return (c, x) =>
        {
            var d = Pick(target, c, security).FirstOrDefault();
            if (d == null) return false;
            d.AddBottomSource(x);
            return true;
        };
    }

    /// <summary>"up to 2 ~" — 0장도 고를 수 있다 (ParseCardPick · ParseNamePick은 장수만 돌려준다)</summary>
    private static bool IsUpTo(string part) => Regex.IsMatch(part.Trim(), @"^up to\b", RegexOptions.IgnoreCase);

    /// <summary>"1 [Agumon]" · "1 [Agumon] or [Gabumon]" · "up to 2 [Greymon]" — 카드 이름</summary>
    private static (int Count, Func<CardData, bool> Match)? ParseNamePick(string part)
    {
        var m = Regex.Match(part.Trim(), @"^(?:up to )?(?<n>\d+|a|an) (?<names>\[[^\]]+\](?:,? (?:or )?\[[^\]]+\])*)$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        int count = int.TryParse(m.Groups["n"].Value, out int k) ? k : 1;
        var names = Regex.Matches(m.Groups["names"].Value, @"\[([^\]]+)\]").Select(x => x.Groups[1].Value).ToList();
        return (count, c => names.Any(n => TargetSpec.NameHas(c, n)));
    }

    private const string Colors = "red|blue|yellow|green|black|purple|white";

    /// <summary>
    /// 카드 고르기 조건: "1 card with [Greymon] in its name" · "up to 2 Tamer cards" · "1 [Royal Knight] trait Digimon card" ·
    /// "1 green or blue level 4 or lower Digimon card" · "1 X card or 1 Y card"(둘 중 하나) …
    /// </summary>
    private static (int Count, Func<CardData, bool> Match)? ParseCardPick(string part)
    {
        // "1 A card or 1 B card" — 어느 한쪽 조건
        var alternatives = Regex.Split(part.Trim(), @"(?<!level \d+)\s+or\s+(?=(?:up to )?(?:\d+|a|an) )", RegexOptions.IgnoreCase);   // "level 4 or 5"는 나누지 않는다
        // "1 purple Digimon card or Option card" — 뒤쪽에 수가 없으면 앞의 수를 붙인다
        var lead = Regex.Match(part.Trim(), @"^(?:up to )?(?:\d+|a|an) ");
        if (alternatives.Length == 1 && lead.Success)
        {
            var tail = Regex.Split(part.Trim(), @"\s+or\s+(?=(?:Digimon|Tamer|Option) cards?\b)", RegexOptions.IgnoreCase);
            if (tail.Length > 1) alternatives = tail.Select((t, i) => i == 0 ? t : lead.Value + t).ToArray();
        }
        if (alternatives.Length > 1)
        {
            var parsed = alternatives.Select(ParseSingleCardPick).ToList();
            if (parsed.Any(p => p == null)) return null;
            return (parsed.Max(p => p!.Value.Count), c => parsed.Any(p => p!.Value.Match(c)));
        }
        return ParseSingleCardPick(part);
    }

    private static (int Count, Func<CardData, bool> Match)? ParseSingleCardPick(string part)
    {
        // "3 non-Digi-Egg cards" · "1 non-Digi-Egg card with [X] in its text" — 디지타마가 아닌 카드
        var nonEgg = Regex.Match(part, @"\bnon[- ]Digi-Egg\s+", RegexOptions.IgnoreCase);
        if (nonEgg.Success)
            return ParseSingleCardPick(part.Remove(nonEgg.Index, nonEgg.Length)) is { } inner
                ? (inner.Count, c => c.Type != CardType.Digitama && inner.Match(c)) : null;
        var m = Regex.Match(part.Trim(),
            $@"^(?:up to )?(?<n>\d+|a|an|all) (?<adj>(?:(?:\d+-colou?r|multicolou?r(?:ed)?|single-colou?r|{TargetSpec.NameList}(?: traits?)?|level \d+(?: or (?:lower|higher|(?:level )?\d+))?|play costs? \d+ or (?:lower|higher|less|more)|(?:{Colors})(?:(?:,| or| and/or|/) (?:{Colors}))*)\s+)*)(?<kind>Digimon|Tamer|Option)?\s*(?:cards?)?(?<filter>.*)$",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        int count = int.TryParse(m.Groups["n"].Value, out int k) ? k : m.Groups["n"].Value.Equals("all", StringComparison.OrdinalIgnoreCase) ? 99 : 1;
        var filters = new List<Func<CardData, bool>>();
        string kind = m.Groups["kind"].Value.ToLowerInvariant();
        if (kind == "digimon") filters.Add(c => c.Type == CardType.Digimon);
        else if (kind == "tamer") filters.Add(c => c.Type == CardType.Tamer);
        else if (kind == "option") filters.Add(c => c.Type == CardType.Option);

        string adj = m.Groups["adj"].Value;
        foreach (Match a in Regex.Matches(adj, $@"(?<multi>\d+)-colou?r|(?<multicolor>multicolou?r(?:ed)?)|(?<single>single-colou?r)|(?<names>{TargetSpec.NameList})(?<trait> traits?)?|level (?<lv>\d+)(?: or (?:(?<dir>lower|higher)|(?:level )?(?<lv2>\d+)))?|play costs? (?<pc>\d+) or (?<pdir>lower|higher|less|more)|(?<colors>(?:{Colors})(?:(?:,| or| and/or|/) (?:{Colors}))*)", RegexOptions.IgnoreCase))
        {
            if (a.Groups["multi"].Success)
            {
                int colors = int.Parse(a.Groups["multi"].Value);   // "2-color black card" — 색이 2개
                filters.Add(c => c.AllColors.Count == colors);
            }
            else if (a.Groups["multicolor"].Success)
                filters.Add(c => c.AllColors.Count >= 2);   // "multicolored" — 색이 2개 이상
            else if (a.Groups["single"].Success)
                filters.Add(c => c.AllColors.Count == 1);   // "single-color" — 색이 1개
            else if (a.Groups["names"].Success)
            {
                var xs = TargetSpec.Names(a.Groups["names"].Value);
                filters.Add(a.Groups["trait"].Success ? c => xs.Any(x => TargetSpec.HasTrait(c, x)) : c => xs.Any(x => TargetSpec.NameHas(c, x) || TargetSpec.HasTrait(c, x)));
            }
            else if (a.Groups["pc"].Success)
            {
                int pc = int.Parse(a.Groups["pc"].Value); bool low = a.Groups["pdir"].Value is "lower" or "less";
                filters.Add(c => low ? c.PlayCost <= pc : c.PlayCost >= pc);
            }
            else if (a.Groups["pc"].Success)
            {
                int pc = int.Parse(a.Groups["pc"].Value); bool low = a.Groups["pdir"].Value is "lower" or "less";
                filters.Add(c => low ? c.PlayCost <= pc : c.PlayCost >= pc);
            }
            else if (a.Groups["lv"].Success)
            {
                int lv = int.Parse(a.Groups["lv"].Value); string dir = a.Groups["dir"].Value.ToLowerInvariant();
                if (a.Groups["lv2"].Success) { int lv2 = int.Parse(a.Groups["lv2"].Value); filters.Add(c => c.Level == lv || c.Level == lv2); }   // "level 4 or 5"
                else filters.Add(dir == "lower" ? c => c.Level <= lv : dir == "higher" ? c => c.Level >= lv : c => c.Level == lv);
            }
            else if (a.Groups["colors"].Success)
            {
                var colors = Regex.Matches(a.Groups["colors"].Value, Colors, RegexOptions.IgnoreCase).Select(x => Enum.Parse<CardColor>(x.Value, ignoreCase: true)).ToList();
                filters.Add(c => c.AllColors.Any(colors.Contains));
            }
        }

        string rest = m.Groups["filter"].Value.Trim();
        while (rest.Length > 0)
        {
            Match f;
            if ((f = Regex.Match(rest, $@"^(?:with |and |or |,\s*)?(?<x>{TargetSpec.NameList}) in (?:its|their) names?", RegexOptions.IgnoreCase)).Success)
            { var xs = TargetSpec.Names(f.Groups["x"].Value); filters.Add(c => xs.Any(x => TargetSpec.NameHas(c, x))); }
            else if ((f = Regex.Match(rest, $@"^(?:with |and |or |,\s*)?(?:the )?(?<x>{TargetSpec.NameList}) traits?|^(?:with |,\s*)?(?<x>{TargetSpec.NameList}) in any of its traits", RegexOptions.IgnoreCase)).Success)
            { var xs = TargetSpec.Names(f.Groups["x"].Value); filters.Add(c => xs.Any(x => TargetSpec.HasTrait(c, x))); }
            else if ((f = Regex.Match(rest, @"^(?:with |and )?(?:a )?DigiXros requirements?", RegexOptions.IgnoreCase)).Success)
                filters.Add(GoldfishEngine.HasDigiXros);   // 디지크로스 조건 (공식 한글 문구)
            else if ((f = Regex.Match(rest, $@"^(?:with |and |or |,\s*)?(?<x>{TargetSpec.NameList}) in (?:its|their) texts?", RegexOptions.IgnoreCase)).Success)
            { var xs = TargetSpec.Names(f.Groups["x"].Value); filters.Add(c => xs.Any(x => TargetSpec.TextHas(c, x))); }
            else if ((f = Regex.Match(rest, @"^(?:with |and |,\s*)?(?:a )?play costs? of (?<n>\d+) or (?<dir>less|more)", RegexOptions.IgnoreCase)).Success)
            { int v = int.Parse(f.Groups["n"].Value); bool less = f.Groups["dir"].Value == "less"; filters.Add(c => less ? c.PlayCost <= v : c.PlayCost >= v); }
            else if ((f = Regex.Match(rest, @"^(?:with |and |,\s*)?(?<n>\d+) DP or (?<dir>less|more)", RegexOptions.IgnoreCase)).Success)
            { int v = int.Parse(f.Groups["n"].Value); bool less = f.Groups["dir"].Value == "less"; filters.Add(c => less ? c.DP <= v : c.DP >= v); }
            else if ((f = Regex.Match(rest, @"^(?:with |and |,\s*)?(?:a )?level (?:of )?(?<n>\d+)(?: or (?<dir>lower|higher|less|more))?", RegexOptions.IgnoreCase)).Success)
            {
                int v = int.Parse(f.Groups["n"].Value); string dir = f.Groups["dir"].Value.ToLowerInvariant();
                filters.Add(dir is "lower" or "less" ? c => c.Level <= v : dir is "higher" or "more" ? c => c.Level >= v : c => c.Level == v);
            }
            else if ((f = Regex.Match(rest, $@"^(?:,\s*)?other than (?<x>{TargetSpec.NameList})", RegexOptions.IgnoreCase)).Success)
            { var xs = TargetSpec.Names(f.Groups["x"].Value); filters.Add(c => !xs.Any(x => CardNames.Is(c, x))); }
            else if ((f = Regex.Match(rest, $@"^(?<x>{TargetSpec.NameList})", RegexOptions.IgnoreCase)).Success)
            { var xs = TargetSpec.Names(f.Groups["x"].Value); filters.Add(c => xs.Any(x => TargetSpec.NameHas(c, x) || TargetSpec.HasTrait(c, x))); }
            else if ((f = Regex.Match(rest, @"^(?:with |and )?(?<n>\d+) or more colou?rs", RegexOptions.IgnoreCase)).Success)
            { int v = int.Parse(f.Groups["n"].Value); filters.Add(c => c.AllColors.Count >= v); }
            else if ((f = Regex.Match(rest, @"^with (?:an? )?inherited effects?", RegexOptions.IgnoreCase)).Success)
            { filters.Add(c => c.InheritedEffectText.Trim().Length > 0); }
            else if ((f = Regex.Match(rest, @"^with (?:a )?(?:memory |play |use )?cost of (?<n>\d+)$", RegexOptions.IgnoreCase)).Success)
            { int v = int.Parse(f.Groups["n"].Value); filters.Add(c => c.PlayCost == v); }
            else if ((f = Regex.Match(rest, @"^(?:with |and |or )?(?:a |an )?(?:memory |play |use )?cost of (?<n>\d+) or (?<dir>less|more|lower|higher)", RegexOptions.IgnoreCase)).Success)
            { int v = int.Parse(f.Groups["n"].Value); bool less = f.Groups["dir"].Value is "less" or "lower"; filters.Add(c => less ? c.PlayCost <= v : c.PlayCost >= v); }
            else if ((f = Regex.Match(rest, @"^(?:with |and |or )?＜(?<k>[^＞]+?)\s*＞(?: in (?:its|their) texts?)?", RegexOptions.IgnoreCase)).Success)
            {
                // "with ＜Digi-Burst＞" · "with ＜Blitz＞" · "with ＜Save＞ in its text" — 카드 문장에 그 키워드가 있는지 (블로커는 카드 수치도)
                string kw = f.Groups["k"].Value.Trim();
                filters.Add(c => c.EffectText.Contains($"＜{kw}", StringComparison.OrdinalIgnoreCase) || (kw.Equals("Blocker", StringComparison.OrdinalIgnoreCase) && c.IsBlocker));
            }
            else if ((f = Regex.Match(rest, @"^with an? \[(?<t>[^\]]+)\] effect", RegexOptions.IgnoreCase)).Success)
            { string t = f.Groups["t"].Value; filters.Add(c => c.EffectText.Contains($"[{t}]", StringComparison.OrdinalIgnoreCase)); }
            else if ((f = Regex.Match(rest, @"^suspended", RegexOptions.IgnoreCase)).Success)
            { /* "suspended" 등장 — 레스트 상태로 등장 (지금은 액티브로 등장) */ }
            else return null;
            rest = rest[f.Length..].Trim().TrimStart(',').Trim();
        }
        return (count, c => filters.All(f => f(c)));
    }
}
