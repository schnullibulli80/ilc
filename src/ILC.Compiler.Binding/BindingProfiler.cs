namespace ILC.Compiler.Binding;

using System.Diagnostics;

public sealed record BindingProfileEntry(
    string Path,
    string Name,
    int Depth,
    TimeSpan Elapsed,
    TimeSpan MaxElapsed,
    int Count);

public sealed class BindingProfiler
{
    private readonly Stack<ProfileFrame> frames = new();
    private readonly Dictionary<string, ProfileAggregate> aggregates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> pathCache = new(StringComparer.Ordinal);

    public ProfileScope Enter(string name)
    {
        var parentPath = frames.Count > 0 ? frames.Peek().Path : string.Empty;
        var path = GetPath(parentPath, name);
        frames.Push(new ProfileFrame(name, path, frames.Count, Stopwatch.GetTimestamp()));
        return new ProfileScope(this);
    }

    public IReadOnlyList<BindingProfileEntry> Snapshot() =>
        aggregates.Values
            .OrderByDescending(entry => entry.Elapsed)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal)
            .Select(entry => new BindingProfileEntry(entry.Path, entry.Name, entry.Depth, entry.Elapsed, entry.MaxElapsed, entry.Count))
            .ToArray();

    public void AddFlatSample(string name, TimeSpan elapsed)
    {
        if (!aggregates.TryGetValue(name, out var aggregate))
        {
            aggregate = new ProfileAggregate(name, name, 0);
            aggregates.Add(name, aggregate);
        }

        aggregate.Elapsed += elapsed;
        aggregate.Count++;
        if (elapsed > aggregate.MaxElapsed)
        {
            aggregate.MaxElapsed = elapsed;
        }
    }

    private void Exit()
    {
        if (frames.Count == 0)
        {
            return;
        }

        var frame = frames.Pop();
        var elapsed = Stopwatch.GetElapsedTime(frame.StartedAt);
        if (!aggregates.TryGetValue(frame.Path, out var aggregate))
        {
            aggregate = new ProfileAggregate(frame.Path, frame.Name, frame.Depth);
            aggregates.Add(frame.Path, aggregate);
        }

        aggregate.Elapsed += elapsed;
        aggregate.Count++;
        if (elapsed > aggregate.MaxElapsed)
        {
            aggregate.MaxElapsed = elapsed;
        }
    }

    private string GetPath(string parentPath, string name)
    {
        if (string.IsNullOrEmpty(parentPath))
        {
            return name;
        }

        if (!pathCache.TryGetValue(parentPath, out var childPaths))
        {
            childPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            pathCache.Add(parentPath, childPaths);
        }

        if (childPaths.TryGetValue(name, out var path))
        {
            return path;
        }

        path = $"{parentPath} > {name}";
        childPaths.Add(name, path);
        return path;
    }

    private readonly record struct ProfileFrame(string Name, string Path, int Depth, long StartedAt);

    private sealed class ProfileAggregate(string path, string name, int depth)
    {
        public string Path { get; } = path;
        public string Name { get; } = name;
        public int Depth { get; } = depth;
        public TimeSpan Elapsed { get; set; }
        public TimeSpan MaxElapsed { get; set; }
        public int Count { get; set; }
    }

    public readonly struct ProfileScope : IDisposable
    {
        private readonly BindingProfiler? profiler;

        internal ProfileScope(BindingProfiler profiler)
        {
            this.profiler = profiler;
        }

        public void Dispose()
        {
            profiler?.Exit();
        }
    }
}
