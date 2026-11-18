using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ApiCompat;

internal sealed record TypeKey(string AssemblyName, string Name);
internal sealed record TypeSignature(string Text, TypeKey? Definition = null);

// Decode both MemberRef and MethodDef signatures with the same provider. Framework
// facade names are omitted for System types; non-framework type identity is retained.
internal sealed class SignatureProvider(MetadataAssembly assembly)
    : ISignatureTypeProvider<TypeSignature, object?>
{
    internal Func<TypeKey, TypeKey> CanonicalType { get; set; } = key => key;

    private TypeSignature Named(TypeKey key, byte kind)
    {
        var identity = CanonicalType(key);
        return new($"{kind:x2}:" + (identity.Name.StartsWith("System.", StringComparison.Ordinal)
            ? identity.Name : $"[{identity.AssemblyName}]{identity.Name}"), key);
    }

    public TypeSignature GetArrayType(TypeSignature element, ArrayShape shape) => new(
        $"{element.Text}[rank={shape.Rank};sizes={string.Join(',', shape.Sizes)};lower={string.Join(',', shape.LowerBounds)}]");
    public TypeSignature GetByReferenceType(TypeSignature element) => new(element.Text + "&");
    public TypeSignature GetFunctionPointerType(MethodSignature<TypeSignature> signature) => new("fn:" + MethodKey(signature));
    public TypeSignature GetGenericInstantiation(TypeSignature generic, ImmutableArray<TypeSignature> arguments) =>
        new(generic.Text + "<" + string.Join(',', arguments.Select(a => a.Text)) + ">", generic.Definition);
    public TypeSignature GetGenericMethodParameter(object? context, int index) => new("!!" + index);
    public TypeSignature GetGenericTypeParameter(object? context, int index) => new("!" + index);
    public TypeSignature GetModifiedType(TypeSignature modifier, TypeSignature element, bool required) =>
        new($"{(required ? "modreq" : "modopt")}({modifier.Text}){element.Text}", element.Definition);
    public TypeSignature GetPinnedType(TypeSignature element) => new("pinned:" + element.Text);
    public TypeSignature GetPointerType(TypeSignature element) => new(element.Text + "*");
    public TypeSignature GetPrimitiveType(PrimitiveTypeCode code) => new(code.ToString());
    public TypeSignature GetSZArrayType(TypeSignature element) => new(element.Text + "[]");
    public TypeSignature GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte kind) =>
        Named(new TypeKey(assembly.Name, assembly.TypeName(handle)), kind);
    public TypeSignature GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte kind) =>
        Named(assembly.TypeKey(handle), kind);
    public TypeSignature GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte kind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, context);

    public static string MethodKey(MethodSignature<TypeSignature> signature) =>
        $"method:{signature.Header.RawValue}:{signature.GenericParameterCount}:{signature.RequiredParameterCount}:" +
        signature.ReturnType.Text + "(" + string.Join(',', signature.ParameterTypes.Select(p => p.Text)) + ")";
}
