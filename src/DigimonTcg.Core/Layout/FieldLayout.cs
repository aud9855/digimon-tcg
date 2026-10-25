namespace DigimonTcg.Core.Layout;

/// <summary>배틀 에어리어 위 카드 한 장이 차지하는 칸 (왼쪽 위 X · Y, 너비 · 높이 — 화면 좌표, 화면 전용)</summary>
public readonly record struct FieldRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    /// <summary>두 칸이 gap 만큼의 여백을 두지 않고 겹치는지</summary>
    public bool Overlaps(FieldRect other, double gap = 0) =>
        X < other.Right + gap && other.X < Right + gap &&
        Y < other.Bottom + gap && other.Y < Bottom + gap;

    public FieldRect MoveTo(double x, double y) => this with { X = x, Y = y };
}

/// <summary>
/// 자유 배치 필드의 배치 계산 (화면 전용 — 게임 규칙과 상관없고 저장되지 않는다).
/// 끌어 놓은 자리 근처의 빈 자리 찾기 · 자동 정렬 격자 · 크기가 바뀌어 겹친 카드 밀어내기.
/// </summary>
public static class FieldLayout
{
    /// <summary>카드 사이에 남기는 여백</summary>
    public const double DefaultGap = 8;

    /// <summary>빈 자리를 찾을 때 훑는 간격</summary>
    public const double DefaultStep = 8;

    /// <summary>칸을 필드 안으로 넣는다 (필드보다 큰 칸은 왼쪽 · 위에 붙인다)</summary>
    public static FieldRect Clamp(FieldRect rect, double areaWidth, double areaHeight)
    {
        double x = Math.Max(0, Math.Min(rect.X, areaWidth - rect.Width));
        double y = Math.Max(0, Math.Min(rect.Y, areaHeight - rect.Height));
        return rect.MoveTo(x, y);
    }

    public static bool IsFree(FieldRect rect, IReadOnlyList<FieldRect> occupied, double gap = DefaultGap) =>
        occupied.All(o => !rect.Overlaps(o, gap));

    /// <summary>
    /// desired 자리가 비어 있으면 그대로(필드 안으로만 맞춰서), 다른 카드와 겹치면 가장 가까운 빈 자리로 옮긴다.
    /// 필드 안에 빈 자리가 없으면 가장 아래 카드 밑에 놓는다 (필드가 아래로 늘어나 스크롤된다).
    /// </summary>
    public static FieldRect FindFreeSpot(
        FieldRect desired,
        IReadOnlyList<FieldRect> occupied,
        double areaWidth,
        double areaHeight,
        double gap = DefaultGap,
        double step = DefaultStep)
    {
        var start = Clamp(desired, areaWidth, areaHeight);
        if (IsFree(start, occupied, gap))
            return start;

        double maxX = Math.Max(0, areaWidth - start.Width);
        double maxY = Math.Max(0, areaHeight - start.Height);
        var xs = Lattice(start.X, maxX, step);
        var ys = Lattice(start.Y, maxY, step);

        FieldRect? best = null;
        double bestDist = double.MaxValue;
        foreach (double y in ys)
        {
            foreach (double x in xs)
            {
                double dx = x - desired.X, dy = y - desired.Y;
                double dist = dx * dx + dy * dy;
                if (dist >= bestDist)
                    continue;
                var candidate = start.MoveTo(x, y);
                if (IsFree(candidate, occupied, gap))
                {
                    best = candidate;
                    bestDist = dist;
                }
            }
        }
        if (best is { } found)
            return found;

        double below = occupied.Count == 0 ? 0 : occupied.Max(o => o.Bottom) + gap;
        return start.MoveTo(start.X, below);
    }

    /// <summary>start를 지나는 step 간격 점들 (0과 max 끝점 포함)</summary>
    private static List<double> Lattice(double start, double max, double step)
    {
        var points = new List<double> { 0, max };
        for (double v = start; v >= 0; v -= step)
            points.Add(v);
        for (double v = start + step; v <= max; v += step)
            points.Add(v);
        return points.Distinct().ToList();
    }

    /// <summary>
    /// 자동 정렬 격자: 크기 목록을 한 줄씩 채워 놓는다 (줄이 넘치면 다음 줄).
    /// fromRight면 오른쪽 끝에서 왼쪽으로 채운다 (테이머 자리 · 상대 필드 거울 배치).
    /// </summary>
    public static List<FieldRect> Flow(
        IReadOnlyList<(double Width, double Height)> sizes,
        double areaWidth,
        bool fromRight = false,
        double gap = DefaultGap,
        double startY = 0)
    {
        var result = new List<FieldRect>(sizes.Count);
        double cursor = 0, y = startY, rowHeight = 0;
        foreach (var (w, h) in sizes)
        {
            if (cursor > 0 && cursor + w > areaWidth)
            {
                y += rowHeight + gap;
                cursor = 0;
                rowHeight = 0;
            }
            double x = fromRight ? Math.Max(0, areaWidth - cursor - w) : cursor;
            result.Add(new FieldRect(x, y, w, h));
            cursor += w + gap;
            rowHeight = Math.Max(rowHeight, h);
        }
        return result;
    }

    /// <summary>
    /// 앞에서부터 차례로 자리를 확정하며, 앞 카드와 겹치는 카드는 가장 가까운 빈 자리로 밀어낸다
    /// (레스트로 카드가 넓어지거나 창이 좁아졌을 때). 앞에 있는 카드일수록 제자리를 지킨다.
    /// </summary>
    public static List<FieldRect> ResolveOverlaps(IReadOnlyList<FieldRect> rects, double areaWidth, double areaHeight, double gap = DefaultGap)
    {
        var settled = new List<FieldRect>(rects.Count);
        foreach (var rect in rects)
            settled.Add(FindFreeSpot(rect, settled, areaWidth, areaHeight, gap));
        return settled;
    }
}
