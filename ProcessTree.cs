using System.Collections.ObjectModel;

namespace ProcWatch;

/// <summary>
/// Ardışık anlık görüntüleri karşılaştırarak canlı bir süreç ağacı tutar.
/// Sonlanan süreçler ağaçta soluk halde kalır: akış anlaşılsın diye.
/// </summary>
public sealed class ProcessTree
{
    readonly Dictionary<int, ProcNode> _live = new();
    readonly List<ProcNode> _all = new();
    bool _firstPass = true;

    public ObservableCollection<ProcNode> Roots { get; } = new();
    public int LiveCount => _live.Count;
    public int TotalCount => _all.Count;

    public IReadOnlyList<ProcEvent> Update(Dictionary<int, RawProc> snapshot)
    {
        var events = new List<ProcEvent>();
        var now = DateTime.Now;

        // Sonlananlar
        foreach (var pid in _live.Keys.ToList())
        {
            if (snapshot.ContainsKey(pid) && SameProcess(_live[pid], snapshot[pid])) continue;

            var dead = _live[pid];
            dead.ExitedAt = now;
            _live.Remove(pid);
            if (!_firstPass) events.Add(new ProcEvent(now, false, dead));
        }

        // Yeni doğanlar
        foreach (var raw in snapshot.Values.OrderBy(r => r.StartTime ?? DateTime.MinValue))
        {
            if (_live.ContainsKey(raw.Pid)) continue;

            var signature = Signing.Inspect(raw.Path);
            var node = new ProcNode(raw, signature, startedThisSession: !_firstPass);
            _live[raw.Pid] = node;
            _all.Add(node);
            Attach(node);
            if (!_firstPass) events.Add(new ProcEvent(now, true, node));
        }

        _firstPass = false;
        return events;
    }

    static bool SameProcess(ProcNode node, RawProc raw)
    {
        // PID geri dönüşümü: aynı numara, farklı başlangıç zamanı = farklı süreç.
        if (node.StartTime is null || raw.StartTime is null) return node.Name == raw.Name;
        return Math.Abs((node.StartTime.Value - raw.StartTime.Value).TotalSeconds) < 1;
    }

    void Attach(ProcNode node)
    {
        var parent = FindParent(node);
        node.IsTargetSubtree = TargetPids.Contains(node.Pid) || (parent?.IsTargetSubtree ?? false);

        if (parent is null) Roots.Add(node);
        else parent.Children.Add(node);
    }

    ProcNode? FindParent(ProcNode node)
    {
        if (node.ParentPid == 0 || node.ParentPid == node.Pid) return null;

        if (_live.TryGetValue(node.ParentPid, out var live) && StartsBefore(live, node))
            return live;

        // Ebeveyn ölmüşse ağaçtaki en son örneğine bağla; yoksa kök olur.
        return _all.Where(n => n.Pid == node.ParentPid && StartsBefore(n, node))
                   .OrderByDescending(n => n.StartTime ?? DateTime.MinValue)
                   .FirstOrDefault();
    }

    static bool StartsBefore(ProcNode parent, ProcNode child)
    {
        if (parent.StartTime is null || child.StartTime is null) return true;
        return parent.StartTime <= child.StartTime.Value.AddSeconds(1);
    }

    /// <summary>Analiz edilen hedefin PID'leri; alt ağacı işaretlemek için kullanılır.</summary>
    public HashSet<int> TargetPids { get; } = new();

    /// <summary>Filtreyi uygular. Eşleşen bir düğümün üst zinciri bağlam olarak görünür kalır.</summary>
    public void ApplyFilter(string filter, bool onlyImportant, bool showExited, bool onlyTarget = false)
    {
        foreach (var root in Roots) Evaluate(root, filter, onlyImportant, showExited, onlyTarget);
    }

    static bool Evaluate(ProcNode node, string filter, bool onlyImportant, bool showExited, bool onlyTarget)
    {
        var anyChildVisible = false;
        foreach (var child in node.Children)
            anyChildVisible |= Evaluate(child, filter, onlyImportant, showExited, onlyTarget);

        var self = node.Matches(filter)
                   && (!onlyImportant || node.IsImportant)
                   && (!onlyTarget || node.IsTargetSubtree)
                   && (showExited || node.IsAlive);

        node.IsVisible = self || anyChildVisible;
        return node.IsVisible;
    }
}
