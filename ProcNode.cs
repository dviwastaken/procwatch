using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProcWatch;

public sealed class ProcNode : INotifyPropertyChanged
{
    public ProcNode(RawProc raw, SignatureInfo signature, bool startedThisSession)
    {
        Raw = raw;
        Signature = signature.Kind;
        Publisher = signature.Publisher;
        SignatureStatus = signature.Status;
        StartedThisSession = startedThisSession;
        IsImportant = Importance.IsImportant(raw, signature, startedThisSession);
    }

    public RawProc Raw { get; }
    public int Pid => Raw.Pid;
    public int ParentPid => Raw.ParentPid;
    public string Name => Raw.Name;
    public string? Path => Raw.Path;
    public string? CommandLine => Raw.CommandLine;
    public DateTime? StartTime => Raw.StartTime;
    public SignatureKind Signature { get; }
    public string Publisher { get; }
    public string SignatureStatus { get; }
    public bool StartedThisSession { get; }
    public bool IsImportant { get; }

    /// <summary>Analiz edilen hedefin kendisi ya da onun soyundan geliyor mu?</summary>
    public bool IsTargetSubtree { get; set; }

    public ObservableCollection<ProcNode> Children { get; } = new();

    public string Badge => Signature switch
    {
        SignatureKind.Microsoft => "MS",
        SignatureKind.ThirdParty => "imzalı",
        SignatureKind.Unsigned => "imzasız",
        _ => "?"
    };

    DateTime? _exitedAt;
    public DateTime? ExitedAt
    {
        get => _exitedAt;
        set { _exitedAt = value; OnChanged(); OnChanged(nameof(IsAlive)); }
    }
    public bool IsAlive => _exitedAt is null;

    bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set { if (_isVisible != value) { _isVisible = value; OnChanged(); } }
    }

    bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnChanged(); } }
    }

    public bool Matches(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        if (Pid.ToString().Contains(filter, StringComparison.Ordinal)) return true;
        if (Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
        if (Path is not null && Path.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
        if (CommandLine is not null && CommandLine.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record ProcEvent(DateTime At, bool Started, ProcNode Node)
{
    public string Line =>
        $"{At:HH:mm:ss}  {(Started ? "+" : "-")}  {Node.Name} ({Node.Pid})" +
        (Started && Node.CommandLine is { Length: > 0 } c ? "  " + Trim(c) : "");

    static string Trim(string s) => s.Length <= 90 ? s : s[..90] + "…";
}
