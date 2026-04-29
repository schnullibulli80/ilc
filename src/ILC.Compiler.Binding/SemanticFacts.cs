namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public static partial class SemanticFacts
{
    public static string GetProjectorTypeName(string signature)
    {
        const ulong basis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = basis;
        foreach (var ch in signature)
        {
            hash ^= ch;
            hash *= prime;
        }

        return $"__Projector_{hash:x16}";
    }

    private static NamedTypeSymbol? ResolveNamedType(TypeSymbol type, IEnumerable<TypeSymbol> knownTypes) =>
        knownTypes.OfType<NamedTypeSymbol>().FirstOrDefault(candidate => candidate.Name == type.Name) ??
        (ResolveTypeReference(type.Name, knownTypes) as NamedTypeSymbol) ??
        (type as NamedTypeSymbol);

    private static string GetProjectorSignature(
        ProjectorExpressionSyntax projector,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod) =>
        string.Join(
            "|",
            projector.Members.Select(member =>
                $"{member.Identifier.Text}:{InferExpressionType(member.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Name}={GetExpressionDisplayName(member.Expression)}"));

    public static NamedTypeSymbol? ResolveProjectorType(
        ProjectorExpressionSyntax projector,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol>? knownFields,
        IEnumerable<ConstantSymbol>? knownConstants,
        IEnumerable<PropertySymbol>? knownProperties,
        MethodSymbol? currentMethod)
    {
        var knownTypeList = knownTypes.ToArray();
        var signature = GetProjectorSignature(
            projector,
            localTypes,
            knownTypeList,
            knownMethods.ToArray(),
            (knownFields ?? []).ToArray(),
            (knownConstants ?? []).ToArray(),
            (knownProperties ?? []).ToArray(),
            currentMethod);

        return knownTypeList
            .OfType<NamedTypeSymbol>()
            .FirstOrDefault(type => type.Name == GetProjectorTypeName(signature));
    }

    private static IEnumerable<NamedTypeSymbol> GetTypeHierarchy(TypeSymbol? type, IEnumerable<TypeSymbol> knownTypes)
    {
        var current = ResolveNamedType(type ?? TypeSymbol.Object, knownTypes);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (current is not null && visited.Add(current.Name))
        {
            yield return current;
            current = ResolveNamedType(current.BaseType ?? TypeSymbol.Object, knownTypes);
        }
    }

    private static IEnumerable<NamedTypeSymbol> GetInterfaceHierarchy(TypeSymbol interfaceType, IEnumerable<TypeSymbol> knownTypes)
    {
        var pending = new Queue<NamedTypeSymbol>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        if (ResolveNamedType(interfaceType, knownTypes) is { IsInterface: true } rootInterface)
        {
            pending.Enqueue(rootInterface);
        }

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current.Name))
            {
                continue;
            }

            yield return current;
            foreach (var inheritedInterface in current.InterfaceTypes)
            {
                if (ResolveNamedType(inheritedInterface, knownTypes) is { IsInterface: true } nextInterface)
                {
                    pending.Enqueue(nextInterface);
                }
            }
        }
    }

    private static IEnumerable<NamedTypeSymbol> GetReceiverTypeHierarchy(TypeSymbol? type, IEnumerable<TypeSymbol> knownTypes)
    {
        var resolvedType = ResolveNamedType(type ?? TypeSymbol.Object, knownTypes);
        if (resolvedType is null)
        {
            yield break;
        }

        if (resolvedType.IsInterface)
        {
            foreach (var interfaceType in GetInterfaceHierarchy(resolvedType, knownTypes))
            {
                yield return interfaceType;
            }

            yield break;
        }

        foreach (var candidate in GetTypeHierarchy(resolvedType, knownTypes))
        {
            yield return candidate;
        }
    }

}
