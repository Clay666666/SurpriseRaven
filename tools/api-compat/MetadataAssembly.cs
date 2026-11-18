using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace ApiCompat;

internal sealed class MetadataAssembly : IDisposable
{
    private readonly FileStream _stream;
    private readonly PEReader _pe;
    internal MetadataReader Reader { get; }
    internal SignatureProvider Signatures { get; }
    internal string Name { get; }
    internal string Path { get; }
    internal string Version { get; }
    internal string Sha256 { get; }
    internal Dictionary<string, TypeDefinitionHandle> Types { get; } = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExportedTypeHandle> _exports = new(StringComparer.Ordinal);

    internal MetadataAssembly(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _stream = File.OpenRead(Path);
        _pe = new PEReader(_stream);
        Reader = _pe.GetMetadataReader();
        var definition = Reader.GetAssemblyDefinition();
        Name = Reader.GetString(definition.Name);
        Version = definition.Version.ToString();
        Signatures = new SignatureProvider(this);
        foreach (var handle in Reader.TypeDefinitions) Types.Add(TypeName(handle), handle);
        foreach (var handle in Reader.ExportedTypes) _exports.Add(ExportedName(handle), handle);
        using var hashStream = File.OpenRead(Path);
        Sha256 = Convert.ToHexString(SHA256.HashData(hashStream)).ToLowerInvariant();
    }

    internal string TypeName(TypeDefinitionHandle handle)
    {
        var type = Reader.GetTypeDefinition(handle);
        string name = Reader.GetString(type.Name);
        return type.IsNested ? TypeName(type.GetDeclaringType()) + "+" + name
            : Qualify(Reader.GetString(type.Namespace), name);
    }

    internal TypeKey TypeKey(TypeReferenceHandle handle)
    {
        var type = Reader.GetTypeReference(handle);
        string name = Reader.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var outer = TypeKey((TypeReferenceHandle)type.ResolutionScope);
            return outer with { Name = outer.Name + "+" + name };
        }
        string target = type.ResolutionScope.Kind == HandleKind.AssemblyReference
            ? Reader.GetString(Reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name) : Name;
        return new TypeKey(target, Qualify(Reader.GetString(type.Namespace), name));
    }

    internal TypeKey? TypeKey(EntityHandle handle) => handle.IsNil ? null : handle.Kind switch
    {
        HandleKind.TypeReference => TypeKey((TypeReferenceHandle)handle),
        HandleKind.TypeDefinition => new TypeKey(Name, TypeName((TypeDefinitionHandle)handle)),
        HandleKind.TypeSpecification => Reader.GetTypeSpecification((TypeSpecificationHandle)handle)
            .DecodeSignature(Signatures, (object?)null).Definition,
        _ => null
    };

    internal TypeKey? ForwardedType(string name)
    {
        if (!_exports.TryGetValue(name, out var handle)) return null;
        var implementation = Reader.GetExportedType(handle).Implementation;
        while (implementation.Kind == HandleKind.ExportedType)
            implementation = Reader.GetExportedType((ExportedTypeHandle)implementation).Implementation;
        return implementation.Kind == HandleKind.AssemblyReference
            ? new TypeKey(Reader.GetString(Reader.GetAssemblyReference((AssemblyReferenceHandle)implementation).Name), name) : null;
    }

    internal IEnumerable<(string Name, string Signature)> Members(TypeDefinitionHandle handle)
    {
        var type = Reader.GetTypeDefinition(handle);
        foreach (var methodHandle in type.GetMethods())
        {
            var method = Reader.GetMethodDefinition(methodHandle);
            yield return (Reader.GetString(method.Name), SignatureProvider.MethodKey(method.DecodeSignature(Signatures, (object?)null)));
        }
        foreach (var fieldHandle in type.GetFields())
        {
            var field = Reader.GetFieldDefinition(fieldHandle);
            yield return (Reader.GetString(field.Name), "field:" + field.DecodeSignature(Signatures, (object?)null).Text);
        }
    }

    internal IEnumerable<TypeKey> Parents(TypeDefinitionHandle handle)
    {
        var type = Reader.GetTypeDefinition(handle);
        if (TypeKey(type.BaseType) is { } parent) yield return parent;
        if ((type.Attributes & TypeAttributes.Interface) == 0) yield break;
        foreach (var item in type.GetInterfaceImplementations())
            if (TypeKey(Reader.GetInterfaceImplementation(item).Interface) is { } inherited) yield return inherited;
    }

    private string ExportedName(ExportedTypeHandle handle)
    {
        var type = Reader.GetExportedType(handle);
        string name = Reader.GetString(type.Name);
        return type.Implementation.Kind == HandleKind.ExportedType
            ? ExportedName((ExportedTypeHandle)type.Implementation) + "+" + name
            : Qualify(Reader.GetString(type.Namespace), name);
    }

    private static string Qualify(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
    public void Dispose() { _pe.Dispose(); _stream.Dispose(); }
}
