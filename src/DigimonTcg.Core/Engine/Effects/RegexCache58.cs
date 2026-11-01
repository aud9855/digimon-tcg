using System.Text.RegularExpressions;

namespace DigimonTcg.Core.Engine.Effects;

/// <summary>
/// v0.58.0 문장 분석 · 한글 생성 속도: 효과 문장 규칙은 정적 Regex.Match / Replace / IsMatch(문자열 패턴)를 4천 곳 넘게 쓴다.
/// .NET의 정적 Regex 캐시는 기본 15개라 패턴이 바뀔 때마다 정규식을 다시 만들었다 (읽지 못하는 절 하나를 옮기는 데 수십 초).
/// 캐시를 늘려 같은 패턴은 한 번만 만든다 — 결과는 같다 (캐시는 만든 정규식 객체를 다시 쓸 뿐). SentenceParser · KoreanEffectRenderer를 처음 쓸 때 늘린다.
/// </summary>
internal static class RegexCache58
{
    internal const int Size = 4096;

    internal static void Ensure()
    {
        if (Regex.CacheSize < Size) Regex.CacheSize = Size;
    }
}

internal static partial class SentenceParser
{
    static SentenceParser() => RegexCache58.Ensure();
}

public static partial class KoreanEffectRenderer
{
    static KoreanEffectRenderer() => RegexCache58.Ensure();
}
