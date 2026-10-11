namespace FoxyVPN.Windows.Models;

public sealed record TunnelReadinessCheck(string Name, bool Passed, string Detail);

public sealed record TunnelReadinessReport(IReadOnlyList<TunnelReadinessCheck> Checks)
{
    public bool CanConnect => Checks.Count > 0 && Checks.All(check => check.Passed);
    public int PassedCount => Checks.Count(check => check.Passed);
    public int BlockedCount => Checks.Count - PassedCount;

    public string ToDisplayText()
    {
        var lines = new List<string>
        {
            $"FoxyVPN Windows readiness: {(CanConnect ? "READY" : "BLOCKED")}",
            $"{PassedCount} passed · {BlockedCount} blocked",
            string.Empty
        };

        foreach (var check in Checks)
            lines.Add($"{(check.Passed ? "[READY]" : "[BLOCKED]")} {check.Name}\n  {check.Detail}");

        if (!CanConnect)
        {
            lines.Add(string.Empty);
            lines.Add("The application will not claim a VPN connection until all required components are implemented and verified.");
        }
        return string.Join(Environment.NewLine, lines);
    }
}

public sealed record TunRuntimeStatus(bool Present, string Detail);
