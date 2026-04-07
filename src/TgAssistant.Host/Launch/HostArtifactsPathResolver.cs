namespace TgAssistant.Host.Launch;

public static class HostArtifactsPathResolver
{
    public static string ResolveRepoRoot()
    {
        var cwd = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (cwd != null)
        {
            var hostArtifactsCandidate = Path.Combine(cwd.FullName, "src", "TgAssistant.Host", "artifacts");
            if (Directory.Exists(hostArtifactsCandidate))
            {
                return cwd.FullName;
            }

            cwd = cwd.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    public static string ResolveHostArtifactsRoot()
    {
        var repoRoot = ResolveRepoRoot();
        var candidate = Path.Combine(repoRoot, "src", "TgAssistant.Host", "artifacts");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        return Path.Combine(AppContext.BaseDirectory, "artifacts");
    }

    public static string ResolveOutputPath(string outputPath)
    {
        if (Path.IsPathRooted(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(ResolveRepoRoot(), outputPath));
    }
}
