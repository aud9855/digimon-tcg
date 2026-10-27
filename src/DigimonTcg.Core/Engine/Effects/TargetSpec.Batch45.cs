using System;
using System.Linq;
using System.Text.RegularExpressions;
using DigimonTcg.Core.Models;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>v0.45.0 원래 규칙이 읽지 못한 대상만 (SentenceParser.Gate45가 열렸을 때)</summary>
public sealed partial class TargetSpec
{
    private static TargetSpec? Parse45(string phrase)
    {
        if (!SentenceParser.Gate45) return null;
        phrase = phrase.Trim().TrimEnd('.').Trim();
        return null;
    }
}
