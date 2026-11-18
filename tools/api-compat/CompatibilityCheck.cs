using System.Reflection.Metadata;

namespace ApiCompat;

internal sealed record CompatibilityFailure(string Kind, string Assembly, string Type, string? Member,
    string? Expected, string[] Available);
internal sealed record AssemblyEvidence(string Name, string Version, string Path, string Sha256);
internal sealed record CompatibilityReport(string Status, AssemblyEvidence Mod, AssemblyEvidence[] References,
    int CheckedTypes, int CheckedMembers, CompatibilityFailure[] Failures);

internal sealed class CompatibilityCheck(IEnumerable<MetadataAssembly> references, string[] scopes)
{
    private readonly Dictionary<string, MetadataAssembly> _references = references.ToDictionary(a => a.Name, StringComparer.Ordinal);

    internal CompatibilityReport Check(MetadataAssembly mod)
    {
        foreach (var assembly in _references.Values.Append(mod))
            assembly.Signatures.CanonicalType = key => Resolve(key) is { } resolved
                ? new TypeKey(resolved.Assembly.Name, resolved.Assembly.TypeName(resolved.Handle)) : key;
        List<CompatibilityFailure> failures = [];
        int types = 0, members = 0;
        foreach (var handle in mod.Reader.TypeReferences)
        {
            var key = mod.TypeKey(handle);
            if (!InScope(key.AssemblyName)) continue;
            types++;
            if (Resolve(key) is null)
                failures.Add(new("missing_type", key.AssemblyName, key.Name, null, null, []));
        }
        foreach (var handle in mod.Reader.MemberReferences)
        {
            var member = mod.Reader.GetMemberReference(handle);
            var key = mod.TypeKey(member.Parent);
            if (key is null || !InScope(key.AssemblyName)) continue;
            members++;
            string name = mod.Reader.GetString(member.Name);
            string expected = member.GetKind() == MemberReferenceKind.Method
                ? SignatureProvider.MethodKey(member.DecodeMethodSignature(mod.Signatures, (object?)null))
                : "field:" + member.DecodeFieldSignature(mod.Signatures, (object?)null).Text;
            var available = FindMembers(key, name).Distinct(StringComparer.Ordinal).ToArray();
            if (!available.Contains(expected, StringComparer.Ordinal))
                failures.Add(new("missing_member", key.AssemblyName, key.Name, name, expected, available));
        }
        return new(failures.Count == 0 ? "passed" : "failed", Evidence(mod),
            _references.Values.OrderBy(a => a.Name, StringComparer.Ordinal).Select(Evidence).ToArray(),
            types, members, failures.ToArray());
    }

    private bool InScope(string name) => scopes.Any(scope => scope.EndsWith('*')
        ? name.StartsWith(scope[..^1], StringComparison.Ordinal) : name == scope);

    private (MetadataAssembly Assembly, TypeDefinitionHandle Handle)? Resolve(TypeKey key)
    {
        HashSet<TypeKey> visited = [];
        while (visited.Add(key) && _references.TryGetValue(key.AssemblyName, out var assembly))
        {
            if (assembly.Types.TryGetValue(key.Name, out var handle)) return (assembly, handle);
            var forwarded = assembly.ForwardedType(key.Name);
            if (forwarded is null) break;
            key = forwarded;
        }
        return null;
    }

    private IEnumerable<string> FindMembers(TypeKey key, string name)
    {
        Stack<TypeKey> pending = new([key]);
        HashSet<TypeKey> visited = [];
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current) || Resolve(current) is not { } resolved) continue;
            foreach (var member in resolved.Assembly.Members(resolved.Handle))
                if (member.Name == name) yield return member.Signature;
            foreach (var parent in resolved.Assembly.Parents(resolved.Handle)) pending.Push(parent);
        }
    }

    private static AssemblyEvidence Evidence(MetadataAssembly assembly) =>
        new(assembly.Name, assembly.Version, assembly.Path, assembly.Sha256);
}
