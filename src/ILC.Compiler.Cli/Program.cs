using ILC.Compiler.Binding;
using ILC.Compiler.Bytecode;
using ILC.Compiler.Core;
using ILC.Compiler.Lowering;
using ILC.Compiler.Syntax;
using System.Diagnostics;
using System.Text;

var debugEnabled = args.Contains("--debug", StringComparer.Ordinal);
var timingsEnabled = args.Contains("--timings", StringComparer.Ordinal);
var loweringProfileEnabled = args.Contains("--profile-lowering", StringComparer.Ordinal);
var positionalArgs = args
    .Where(argument =>
        !string.Equals(argument, "--debug", StringComparison.Ordinal) &&
        !string.Equals(argument, "--timings", StringComparison.Ordinal) &&
        !string.Equals(argument, "--profile-lowering", StringComparison.Ordinal))
    .ToArray();
var timing = new CompilationTiming();

if (positionalArgs.Length == 0)
{
    Console.Error.WriteLine("usage: ilc [--debug] [--timings] [--profile-lowering] <main-source-file> [additional-source-files...]");
    return 1;
}

var sourcePath = positionalArgs[0];
if (!File.Exists(sourcePath))
{
    Console.Error.WriteLine($"error: file not found: {sourcePath}");
    return 2;
}

var sourceText = await timing.MeasureAsync("read source", () => File.ReadAllTextAsync(sourcePath));
var syntaxTree = timing.Measure("parse source", () => SyntaxTree.Parse(sourceText));
var sourceInputs = new List<(string Path, string Text, SyntaxTree Tree)>
{
    (sourcePath, sourceText, syntaxTree)
};

var importedSyntaxTrees = new List<SyntaxTree>();
if (positionalArgs.Length > 1)
{
    var importedNamespaces = syntaxTree.Root.Uses?.Imports.Select(importSyntax => importSyntax.NamespaceName.ToDisplayString()).ToHashSet(StringComparer.Ordinal)
        ?? [];
    for (var index = 1; index < positionalArgs.Length; index++)
    {
        var importedPath = positionalArgs[index];
        if (!File.Exists(importedPath))
        {
            Console.Error.WriteLine($"error: file not found: {importedPath}");
            return 2;
        }

        var importedText = await timing.MeasureAsync("read imports", () => File.ReadAllTextAsync(importedPath));
        var importedTree = timing.Measure("parse imports", () => SyntaxTree.Parse(importedText));
        sourceInputs.Add((importedPath, importedText, importedTree));
        var importedNamespace = importedTree.Root.Namespace?.Name.ToDisplayString();
        if (importedNamespace is not null && importedNamespaces.Contains(importedNamespace))
        {
            importedSyntaxTrees.Add(importedTree);
        }
    }
}

var mergedSyntaxTree = timing.Measure("merge syntax trees", () => SyntaxTree.Merge(syntaxTree, importedSyntaxTrees));
var bindingResult = timing.Measure("bind symbols", () => new Binder().Bind(mergedSyntaxTree));
var timingDetailsEnabled = debugEnabled || timingsEnabled || loweringProfileEnabled;
var syntaxDumpPath = Path.ChangeExtension(sourcePath, ".syntax.txt");
var bindingDumpPath = Path.ChangeExtension(sourcePath, ".binding.txt");
var symbolsDumpPath = Path.ChangeExtension(sourcePath, ".symbols.txt");
var irDumpPath = Path.ChangeExtension(sourcePath, ".ir.txt");
var ilbPath = Path.ChangeExtension(sourcePath, ".ilb");
var ildbgPath = Path.ChangeExtension(sourcePath, ".ildbg");
var listingPath = Path.ChangeExtension(sourcePath, ".listing.txt");

if (debugEnabled)
{
    await timing.MeasureAsync("write syntax/binding/symbol dumps", async () =>
    {
        await File.WriteAllTextAsync(
            syntaxDumpPath,
            BuildSyntaxDump(
                sourcePath,
                syntaxTree,
                importedSyntaxTrees,
                mergedSyntaxTree));

        await File.WriteAllTextAsync(
            bindingDumpPath,
            BuildBindingDump(
                sourcePath,
                bindingResult));

        await File.WriteAllTextAsync(
            symbolsDumpPath,
            BuildSymbolDump(
                sourcePath,
                bindingResult));
    });
}

timing.Measure("report diagnostics", () =>
{
    if (bindingResult.Diagnostics.Count > 0)
    {
        foreach (var diagnostic in bindingResult.Diagnostics)
        {
            WriteDiagnostic(sourcePath, sourceText, diagnostic);
        }
    }
});

var (declaredMethods, declaredFields, declaredProperties, declaredConstants) = timing.Measure("collect symbols", () => (
    bindingResult.Compilation.GetAllMethods(),
    bindingResult.Compilation.GetAllFields(),
    bindingResult.Compilation.GetAllProperties(),
    bindingResult.Compilation.GetAllConstants()));

var moduleMethods = declaredMethods;
if (bindingResult.HasErrors)
{
    Console.WriteLine($"compiled {Path.GetFileName(sourcePath)} in {timing.FormatTotal()}");
    if (debugEnabled)
    {
        Console.WriteLine($"namespace: {bindingResult.Compilation.Namespace ?? "<global>"}");
        Console.WriteLine($"members: {mergedSyntaxTree.Root.Members.Count}");
        Console.WriteLine($"globals: {bindingResult.Compilation.Globals.Count}");
        Console.WriteLine($"declared types: {bindingResult.Compilation.Types.OfType<NamedTypeSymbol>().Count()}");
        Console.WriteLine($"declared methods: {declaredMethods.Count}");
        Console.WriteLine($"declared fields: {declaredFields.Count}");
        Console.WriteLine("module functions: 0");
        Console.WriteLine("entry point: <none>");
        timing.WriteTimings(Console.Out);
    }
    else if (timingDetailsEnabled)
    {
        timing.WriteTimings(Console.Out);
    }

    return 1;
}

if (moduleMethods.Count > 0)
{
    var loweringProfiler = loweringProfileEnabled ? new LoweringProfiler() : null;
    var reachableClosure = timing.Measure("reachability", () => ReachableCompilationBuilder.Build(
        bindingResult.Compilation.EntryPoint is null ? moduleMethods : [bindingResult.Compilation.EntryPoint],
        moduleMethods,
        declaredFields,
        bindingResult.Compilation.Types,
        declaredProperties,
        declaredConstants,
        loweringProfiler));
    var closure = reachableClosure!;
    var (knownFields, knownTypes, knownProperties) = timing.Measure("prepare closure symbols", () =>
    {
        var closureFields = SymbolLists.CreateFields(
            declaredFields
                .Concat(closure.Fields)
                .GroupBy(field => $"{field.DeclaringTypeName ?? "<global>"}::{field.Name}", StringComparer.Ordinal)
                .Select(group => group.First()));
        var closureTypes = SymbolLists.CreateTypes(
            bindingResult.Compilation.Types
                .Concat(closure.Types)
                .GroupBy(
                    type => type is NamedTypeSymbol namedType
                        ? $"{namedType.Name}`{namedType.GenericArity}:{type.Name}"
                        : type.Name,
                    StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(type => type is NamedTypeSymbol namedType
                        ? (namedType.Methods?.Count ?? 0) +
                          (namedType.Fields?.Count ?? 0) +
                          (namedType.Properties?.Count ?? 0) +
                          (namedType.InterfaceTypes?.Count ?? 0) +
                          (namedType.TypeArguments?.Count ?? 0)
                        : 0)
                    .First()));
        var closureProperties = SymbolLists.CreateProperties(
            declaredProperties
                .Concat(closure.Properties)
                .GroupBy(property => $"{property.DeclaringTypeName ?? "<global>"}::{property.Name}", StringComparer.Ordinal)
                .Select(group => group.First()));

        return (closureFields, closureTypes, closureProperties);
    });
    var lowerer = timing.Measure("create lowerer", () => new Lowerer(
        moduleMethods,
        knownFields,
        knownTypes,
        knownProperties,
        declaredConstants));
    if (debugEnabled)
    {
        await timing.MeasureAsync("write ir dump", () => File.WriteAllTextAsync(
            irDumpPath,
            BuildIrDump(
                sourcePath,
                closure.Methods,
                lowerer)));
    }

    var module = timing.Measure("lower and emit bytecode", () => new BytecodeEmitter().EmitModule(closure.Methods, closure.Fields, closure.Types, lowerer));
    var ilbImage = timing.Measure("serialize ilb", () => new IlbSerializer().Serialize(module, closure.Methods, closure.Fields, closure.Types, bindingResult.Compilation.EntryPoint));
    var functionCodeOffsets = timing.Measure("build code offsets", () => BuildFunctionCodeOffsets(module.Functions));
    await timing.MeasureAsync("write ilb", () => File.WriteAllBytesAsync(ilbPath, ilbImage.Bytes));
    var entryPoint = bindingResult.Compilation.EntryPoint;

    if (debugEnabled)
    {
        await timing.MeasureAsync("write listing/debug symbols", async () =>
        {
            await File.WriteAllTextAsync(
                listingPath,
                BuildListing(
                    sourcePath,
                    mergedSyntaxTree,
                    bindingResult,
                    closure.Methods,
                    closure.Fields,
                    module,
                    functionCodeOffsets,
                    ilbImage,
                    entryPoint));

            await File.WriteAllTextAsync(
                ildbgPath,
                BuildDebugSymbols(
                    sourcePath,
                    closure.Methods,
                    module,
                    lowerer,
                    sourceInputs));
        });
    }

    Console.WriteLine($"compiled {Path.GetFileName(sourcePath)} in {timing.FormatTotal()}");
    Console.WriteLine($"ilb file: {Path.GetFileName(ilbPath)}");

    if (debugEnabled)
    {
        Console.WriteLine($"namespace: {bindingResult.Compilation.Namespace ?? "<global>"}");
        Console.WriteLine($"members: {mergedSyntaxTree.Root.Members.Count}");
        Console.WriteLine($"globals: {bindingResult.Compilation.Globals.Count}");
        Console.WriteLine($"declared types: {bindingResult.Compilation.Types.OfType<NamedTypeSymbol>().Count()}");
        Console.WriteLine($"declared methods: {declaredMethods.Count}");
        Console.WriteLine($"declared fields: {declaredFields.Count}");
        Console.WriteLine($"module functions: {module.Functions.Count}");
        Console.WriteLine($"module array-shapes: {module.ArrayShapes.Count}");
        Console.WriteLine($"ilb file: {Path.GetFileName(ilbPath)} bytes={ilbImage.Bytes.Length} sections={ilbImage.Sections.Count}");
        Console.WriteLine($"listing file: {Path.GetFileName(listingPath)}");
        Console.WriteLine($"syntax dump: {Path.GetFileName(syntaxDumpPath)}");
        Console.WriteLine($"binding dump: {Path.GetFileName(bindingDumpPath)}");
        Console.WriteLine($"symbol dump: {Path.GetFileName(symbolsDumpPath)}");
        Console.WriteLine($"ir dump: {Path.GetFileName(irDumpPath)}");
        Console.WriteLine($"debug symbols: {Path.GetFileName(ildbgPath)}");
        Console.WriteLine($"entry point: {FormatMethod(entryPoint)}");
        WriteReachabilityStats(Console.Out, closure.Stats);
        timing.WriteTimings(Console.Out);

        foreach (var shape in module.ArrayShapes)
        {
            Console.WriteLine($"module-array-shape fn={shape.FunctionId} r{shape.ArrayRegister} extents=[{string.Join(", ", shape.ExtentRegisters.Select(index => $"r{index}"))}]");
        }

        foreach (var function in module.Functions)
        {
            var (codeOffset, codeSize) = functionCodeOffsets.TryGetValue(function.FunctionId, out var codeInfo)
                ? codeInfo
                : (0u, 0u);
            Console.WriteLine($"function {function.FunctionId}: {function.Name} regs={function.RegisterCount} argc={function.ArgumentCount} instr={function.Instructions.Count} vm-ip-range=0..{Math.Max(function.Instructions.Count - 1, 0)} code-offset={codeOffset} code-size={codeSize}");
            foreach (var shape in function.ArrayShapes)
            {
                Console.WriteLine($"  array-shape r{shape.ArrayRegister} extents=[{string.Join(", ", shape.ExtentRegisters.Select(index => $"r{index}"))}]");
            }

            for (var instructionIndex = 0; instructionIndex < function.Instructions.Count; instructionIndex++)
            {
                var instruction = function.Instructions[instructionIndex];
                var instructionIp = codeOffset + (uint)(instructionIndex * 11);
                Console.WriteLine($"  vm-ip={instructionIndex} code-ip={instructionIp} {instruction.OpCode} dst={instruction.Destination} left={instruction.Left} right={instruction.Right} imm={instruction.Immediate}");
            }
        }
    }
    else if (timingDetailsEnabled)
    {
        WriteReachabilityStats(Console.Out, closure.Stats);
        timing.WriteTimings(Console.Out);
    }
}
else
{
    Console.WriteLine($"compiled {Path.GetFileName(sourcePath)} in {timing.FormatTotal()}");
    if (debugEnabled)
    {
        Console.WriteLine($"namespace: {bindingResult.Compilation.Namespace ?? "<global>"}");
        Console.WriteLine($"members: {mergedSyntaxTree.Root.Members.Count}");
        Console.WriteLine($"globals: {bindingResult.Compilation.Globals.Count}");
        Console.WriteLine($"declared types: {bindingResult.Compilation.Types.OfType<NamedTypeSymbol>().Count()}");
        Console.WriteLine($"declared methods: {declaredMethods.Count}");
        Console.WriteLine($"declared fields: {declaredFields.Count}");
        Console.WriteLine("module functions: 0");
        Console.WriteLine("entry point: <none>");
        timing.WriteTimings(Console.Out);
    }
    else if (timingDetailsEnabled)
    {
        timing.WriteTimings(Console.Out);
    }
}

return 0;

static string FormatMethod(MethodSymbol? method) =>
    method is null
        ? "<none>"
        : method.DeclaringTypeName is null
            ? method.Name
            : $"{method.DeclaringTypeName}.{method.Name}";

static void WriteReachabilityStats(TextWriter writer, ReachabilityStats stats)
{
    writer.WriteLine(
        "reachability: roots={0} declaredMethods={1} declaredFields={2} declaredProperties={3} declaredTypes={4}",
        stats.RootMethods,
        stats.DeclaredMethods,
        stats.DeclaredFields,
        stats.DeclaredProperties,
        stats.DeclaredTypes);
    writer.WriteLine(
        "reachability: methods enqueued={0} processed={1} lowered={2} cacheHits={3} replacements={4}",
        stats.MethodsEnqueued,
        stats.MethodsProcessed,
        stats.MethodsLowered,
        stats.MethodCacheHits,
        stats.MethodReplacements);
    writer.WriteLine(
        "reachability: methodsByReason root={0} directCall={1} methodConstant={2} propertyAccessor={3} nativeCallback={4} declaringType={5} typeMember={6} interfaceDispatch={7}",
        stats.RootMethodsEnqueued,
        stats.DirectCallMethodsEnqueued,
        stats.MethodConstantMethodsEnqueued,
        stats.PropertyAccessorMethodsEnqueued,
        stats.NativeCallbackMethodsEnqueued,
        stats.DeclaringTypeMethodsEnqueued,
        stats.TypeMemberMethodsEnqueued,
        stats.InterfaceDispatchMethodsEnqueued);
    writer.WriteLine(
        "reachability: dependencies instructions={0} typeReferenceCacheHits={1} typeReferenceCacheMisses={2} fieldsAdded={3} propertiesAdded={4} fieldsPreseeded={5} propertiesPreseeded={6} typesAddedOrReplaced={7} typesPreseededOrReplaced={8}",
        stats.InstructionDependenciesVisited,
        stats.TypeReferenceCacheHits,
        stats.TypeReferenceCacheMisses,
        stats.FieldsAdded,
        stats.PropertiesAdded,
        stats.FieldsPreseeded,
        stats.PropertiesPreseeded,
        stats.TypesAddedOrReplaced,
        stats.TypesPreseededOrReplaced);
    writer.WriteLine(
        "reachability: expansions declaringTypes={0} typeMembers={1} interfaceDispatch={2}",
        stats.DeclaringTypeExpansions,
        stats.TypeMemberExpansions,
        stats.InterfaceDispatchExpansions);
    writer.WriteLine(
        "reachability: lowerer rebuilds={0} cacheInvalidationRequests={1} cacheInvalidations={2}",
        stats.LowererRebuilds,
        stats.LowererCacheInvalidationRequests,
        stats.LowererCacheInvalidations);
    writer.WriteLine(
        "reachability: lowerer invalidationRequestsByReason field={0} property={1} type={2}",
        stats.LowererFieldInvalidationRequests,
        stats.LowererPropertyInvalidationRequests,
        stats.LowererTypeInvalidationRequests);
    writer.WriteLine(
        "reachability: lowerer invalidationsByReason field={0} property={1} type={2}",
        stats.LowererFieldInvalidations,
        stats.LowererPropertyInvalidations,
        stats.LowererTypeInvalidations);
    writer.WriteLine(
        "reachability: lowerer closedGenericTypeInvalidationsSuppressed={0}",
        stats.LowererClosedGenericTypeInvalidationsSuppressed);
    writer.WriteLine(
        "reachability: timings seedDeclaredTypes={0} preseedTypeSurfaces={1} seedGlobalFields={2} seedGlobalProperties={3} seedRootMethods={4} lowererBuild={5} methodLowering={6} dependencyScan={7} handlerScan={8}",
        FormatReachabilityDuration(stats.SeedDeclaredTypesTime),
        FormatReachabilityDuration(stats.PreseedTypeSurfacesTime),
        FormatReachabilityDuration(stats.SeedGlobalFieldsTime),
        FormatReachabilityDuration(stats.SeedGlobalPropertiesTime),
        FormatReachabilityDuration(stats.SeedRootMethodsTime),
        FormatReachabilityDuration(stats.LowererBuildTime),
        FormatReachabilityDuration(stats.MethodLoweringTime),
        FormatReachabilityDuration(stats.DependencyScanTime),
        FormatReachabilityDuration(stats.HandlerScanTime));
    WriteReachabilityCounterLine(writer, "fieldInvalidationNames", stats.LowererFieldInvalidationNames);
    WriteReachabilityCounterLine(writer, "propertyInvalidationNames", stats.LowererPropertyInvalidationNames);
    WriteReachabilityCounterLine(writer, "typeInvalidationNames", stats.LowererTypeInvalidationNames);
    WriteReachabilityDurationCounterLine(writer, "methodLoweringDurations", stats.MethodLoweringDurations);
    WriteReachabilityDurationCounterLine(writer, "dependencyScanDurations", stats.DependencyScanDurations);
    WriteLoweringProfile(writer, stats.LoweringProfile);
}

static string FormatReachabilityDuration(TimeSpan elapsed) =>
    elapsed.TotalSeconds >= 1
        ? $"{elapsed.TotalSeconds:F3}s"
        : $"{elapsed.TotalMilliseconds:F1}ms";

static void WriteReachabilityCounterLine(TextWriter writer, string name, IReadOnlyList<ReachabilityCounter> counters)
{
    if (counters.Count == 0)
    {
        return;
    }

    writer.WriteLine(
        "reachability: lowerer {0} {1}",
        name,
        string.Join(
            " ",
            counters
                .Take(16)
                .Select(counter => $"{counter.Name}={counter.Count}")));
}

static void WriteReachabilityDurationCounterLine(TextWriter writer, string name, IReadOnlyList<ReachabilityDurationCounter> counters)
{
    if (counters.Count == 0)
    {
        return;
    }

    writer.WriteLine(
        "reachability: lowerer {0} {1}",
        name,
        string.Join(
            " | ",
            counters
                .Take(16)
                .Select(counter => $"{counter.Name}={FormatReachabilityDuration(counter.Elapsed)}#{counter.Count}")));
}

static void WriteLoweringProfile(TextWriter writer, IReadOnlyList<LoweringProfileEntry> entries)
{
    if (entries.Count == 0)
    {
        return;
    }

    writer.WriteLine("lowering-profile: top hierarchical scopes");
    foreach (var entry in entries.Take(32))
    {
        writer.WriteLine(
            "lowering-profile: {0} total={1} max={2} count={3}",
            entry.Path,
            FormatReachabilityDuration(entry.Elapsed),
            FormatReachabilityDuration(entry.MaxElapsed),
            entry.Count);
    }
}

static string BuildListing(
    string sourcePath,
    SyntaxTree mergedSyntaxTree,
    BindingResult bindingResult,
    IReadOnlyList<MethodSymbol> moduleMethods,
    IReadOnlyList<FieldSymbol> moduleFields,
    BytecodeModule module,
    IReadOnlyDictionary<uint, (uint CodeOffset, uint CodeSize)> functionCodeOffsets,
    IlbImage ilbImage,
    MethodSymbol? entryPoint)
{
    var builder = new StringBuilder();
    builder.AppendLine($"source: {Path.GetFileName(sourcePath)}");
    builder.AppendLine($"namespace: {bindingResult.Compilation.Namespace ?? "<global>"}");
    builder.AppendLine($"members: {mergedSyntaxTree.Root.Members.Count}");
    builder.AppendLine($"globals: {bindingResult.Compilation.Globals.Count}");
    builder.AppendLine($"declared types: {bindingResult.Compilation.Types.OfType<NamedTypeSymbol>().Count()}");
    builder.AppendLine($"declared methods: {moduleMethods.Count}");
    builder.AppendLine($"declared fields: {moduleFields.Count}");
    builder.AppendLine($"module functions: {module.Functions.Count}");
    builder.AppendLine($"module array-shapes: {module.ArrayShapes.Count}");
    builder.AppendLine($"ilb bytes: {ilbImage.Bytes.Length}");
    builder.AppendLine($"ilb sections: {ilbImage.Sections.Count}");
    builder.AppendLine($"entry point: {FormatMethod(entryPoint)}");
    builder.AppendLine();

    var moduleTypes = module.Types.Count > 0
        ? module.Types
        : CollectListingTypes(moduleMethods, moduleFields, bindingResult.Compilation.Types);
    var typeIds = moduleTypes
        .Select((type, index) => (type, id: index + 1))
        .ToDictionary(pair => pair.type.Name, pair => pair.id, StringComparer.Ordinal);
    builder.AppendLine("[module types]");
    for (var typeIndex = 0; typeIndex < moduleTypes.Count; typeIndex++)
    {
        var type = moduleTypes[typeIndex];
        builder.AppendLine($"type {typeIndex + 1}: {type.Name}");
    }

    builder.AppendLine();
    builder.AppendLine("[module fields]");
    for (var fieldIndex = 0; fieldIndex < moduleFields.Count; fieldIndex++)
    {
        var field = moduleFields[fieldIndex];
        var ownerTypeId = !string.IsNullOrWhiteSpace(field.DeclaringTypeName) && typeIds.TryGetValue(field.DeclaringTypeName, out var resolvedOwnerTypeId)
            ? resolvedOwnerTypeId
            : 0;
        var fieldTypeId = typeIds.TryGetValue(field.Type.Name, out var resolvedFieldTypeId)
            ? resolvedFieldTypeId
            : 0;
        builder.AppendLine($"field {fieldIndex + 1}: owner-type={ownerTypeId} owner-name={field.DeclaringTypeName ?? "<global>"} name={field.Name} type-id={fieldTypeId} type-name={field.Type.Name} static={field.IsStatic}");
    }

    builder.AppendLine();
    builder.AppendLine("[module methods]");
    for (var methodIndex = 0; methodIndex < moduleMethods.Count; methodIndex++)
    {
        var method = moduleMethods[methodIndex];
        var ownerTypeId = !string.IsNullOrWhiteSpace(method.DeclaringTypeName) && typeIds.TryGetValue(method.DeclaringTypeName, out var resolvedOwnerTypeId)
            ? resolvedOwnerTypeId
            : 0;
        var returnTypeId = method.ReturnType != TypeSymbol.Void && typeIds.TryGetValue(method.ReturnType.Name, out var resolvedReturnTypeId)
            ? resolvedReturnTypeId
            : 0;
        builder.AppendLine($"method {methodIndex + 1}: owner-type={ownerTypeId} owner-name={method.DeclaringTypeName ?? "<global>"} name={method.Name} argc={method.Parameters.Count} return-type={returnTypeId} return-name={method.ReturnType.Name} static={method.IsStatic} ctor={method.IsConstructor}");
    }

    builder.AppendLine();

    foreach (var shape in module.ArrayShapes)
    {
        builder.AppendLine($"module-array-shape fn={shape.FunctionId} r{shape.ArrayRegister} extents=[{string.Join(", ", shape.ExtentRegisters.Select(index => $"r{index}"))}]");
    }

    if (module.ArrayShapes.Count > 0)
    {
        builder.AppendLine();
    }

    foreach (var function in module.Functions)
    {
        var (codeOffset, codeSize) = functionCodeOffsets.TryGetValue(function.FunctionId, out var codeInfo)
            ? codeInfo
            : (0u, 0u);
        builder.AppendLine($"function {function.FunctionId}: {function.Name} regs={function.RegisterCount} argc={function.ArgumentCount} instr={function.Instructions.Count} vm-ip-range=0..{Math.Max(function.Instructions.Count - 1, 0)} code-offset={codeOffset} code-size={codeSize}");

        foreach (var shape in function.ArrayShapes)
        {
            builder.AppendLine($"  array-shape r{shape.ArrayRegister} extents=[{string.Join(", ", shape.ExtentRegisters.Select(index => $"r{index}"))}]");
        }

        for (var instructionIndex = 0; instructionIndex < function.Instructions.Count; instructionIndex++)
        {
            var instruction = function.Instructions[instructionIndex];
            var instructionIp = codeOffset + (uint)(instructionIndex * 11);
            builder.AppendLine($"  vm-ip={instructionIndex} code-ip={instructionIp} {instruction.OpCode} dst={instruction.Destination} left={instruction.Left} right={instruction.Right} imm={instruction.Immediate}");
        }

        builder.AppendLine();
    }

    return builder.ToString();
}

static IReadOnlyList<TypeSymbol> CollectListingTypes(
    IReadOnlyList<MethodSymbol> methods,
    IReadOnlyList<FieldSymbol> fields,
    IReadOnlyList<TypeSymbol> declaredTypes)
{
    var declaredTypeList = declaredTypes.ToArray();
    var collected = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);

    void AddOrPreferRicherType(TypeSymbol candidate)
    {
        if (!collected.TryGetValue(candidate.Name, out var existing))
        {
            collected[candidate.Name] = candidate;
            return;
        }

        if (candidate is NamedTypeSymbol && existing is not NamedTypeSymbol)
        {
            collected[candidate.Name] = candidate;
            return;
        }

        if (candidate is not NamedTypeSymbol candidateNamed || existing is not NamedTypeSymbol existingNamed)
        {
            return;
        }

        var candidateScore =
            candidateNamed.Methods.Count +
            candidateNamed.Fields.Count +
            candidateNamed.Properties.Count +
            candidateNamed.InterfaceTypes.Count +
            (candidateNamed.BaseType is null ? 0 : 1) +
            (candidateNamed.GenericParameters?.Count ?? 0) +
            (candidateNamed.TypeArguments?.Count ?? 0);
        var existingScore =
            existingNamed.Methods.Count +
            existingNamed.Fields.Count +
            existingNamed.Properties.Count +
            existingNamed.InterfaceTypes.Count +
            (existingNamed.BaseType is null ? 0 : 1) +
            (existingNamed.GenericParameters?.Count ?? 0) +
            (existingNamed.TypeArguments?.Count ?? 0);

        if (candidateScore > existingScore)
        {
            collected[candidate.Name] = candidate;
        }
    }

    foreach (var type in declaredTypeList)
    {
        AddOrPreferRicherType(type);
    }

    foreach (var field in fields)
    {
        AddOrPreferRicherType(field.Type);
        if (!string.IsNullOrEmpty(field.DeclaringTypeName) &&
            SemanticFacts.ResolveTypeReference(field.DeclaringTypeName, declaredTypeList) is { } resolvedFieldDeclaringType)
        {
            AddOrPreferRicherType(resolvedFieldDeclaringType);
        }
    }

    foreach (var method in methods)
    {
        if (!string.IsNullOrEmpty(method.DeclaringTypeName) &&
            SemanticFacts.ResolveTypeReference(method.DeclaringTypeName, declaredTypeList) is { } resolvedMethodDeclaringType)
        {
            AddOrPreferRicherType(resolvedMethodDeclaringType);
        }

        if (method.ReturnType != TypeSymbol.Void)
        {
            AddOrPreferRicherType(method.ReturnType);
        }

        foreach (var parameter in method.Parameters)
        {
            AddOrPreferRicherType(parameter.Type);
        }
    }

    return collected.Values.OrderBy(type => type.Name, StringComparer.Ordinal).ToArray();
}

static string BuildSyntaxDump(
    string sourcePath,
    SyntaxTree syntaxTree,
    IReadOnlyList<SyntaxTree> importedSyntaxTrees,
    SyntaxTree mergedSyntaxTree)
{
    var builder = new StringBuilder();
    builder.AppendLine($"source: {Path.GetFileName(sourcePath)}");
    builder.AppendLine($"source-namespace: {syntaxTree.Root.Namespace?.Name.ToDisplayString() ?? "<global>"}");
    builder.AppendLine($"source-members: {syntaxTree.Root.Members.Count}");
    builder.AppendLine($"source-uses: {syntaxTree.Root.Uses?.Imports.Count ?? 0}");
    builder.AppendLine($"source-diagnostics: {syntaxTree.Diagnostics.Count}");
    builder.AppendLine($"imported-trees: {importedSyntaxTrees.Count}");
    builder.AppendLine($"merged-namespace: {mergedSyntaxTree.Root.Namespace?.Name.ToDisplayString() ?? "<global>"}");
    builder.AppendLine($"merged-members: {mergedSyntaxTree.Root.Members.Count}");
    builder.AppendLine($"merged-diagnostics: {mergedSyntaxTree.Diagnostics.Count}");
    builder.AppendLine();

    builder.AppendLine("[source uses]");
    foreach (var importSyntax in syntaxTree.Root.Uses?.Imports ?? [])
    {
        builder.AppendLine($"- {importSyntax.NamespaceName.ToDisplayString()}");
    }

    builder.AppendLine();
    builder.AppendLine("[merged top-level members]");
    foreach (var member in mergedSyntaxTree.Root.Members)
    {
        builder.AppendLine($"- {DescribeTopLevelMember(member)}");
    }

    builder.AppendLine();
    builder.AppendLine("[source diagnostics]");
    foreach (var diagnostic in syntaxTree.Diagnostics)
    {
        builder.AppendLine($"- {diagnostic.Id}: {diagnostic.Message} @{diagnostic.Span.Start}:{diagnostic.Span.Length}");
    }

    builder.AppendLine();
    builder.AppendLine("[merged diagnostics]");
    foreach (var diagnostic in mergedSyntaxTree.Diagnostics)
    {
        builder.AppendLine($"- {diagnostic.Id}: {diagnostic.Message} @{diagnostic.Span.Start}:{diagnostic.Span.Length}");
    }

    return builder.ToString();
}

static string BuildBindingDump(
    string sourcePath,
    BindingResult bindingResult)
{
    var builder = new StringBuilder();
    var methods = bindingResult.Compilation.GetAllMethods();
    var fields = bindingResult.Compilation.GetAllFields();
    var properties = bindingResult.Compilation.GetAllProperties();
    var constants = bindingResult.Compilation.GetAllConstants();
    builder.AppendLine($"source: {Path.GetFileName(sourcePath)}");
    builder.AppendLine($"namespace: {bindingResult.Compilation.Namespace ?? "<global>"}");
    builder.AppendLine($"has-errors: {bindingResult.HasErrors}");
    builder.AppendLine($"diagnostics: {bindingResult.Diagnostics.Count}");
    builder.AppendLine($"globals: {bindingResult.Compilation.Globals.Count}");
    builder.AppendLine($"types: {bindingResult.Compilation.Types.Count}");
    builder.AppendLine($"methods: {methods.Count}");
    builder.AppendLine($"fields: {fields.Count}");
    builder.AppendLine($"properties: {properties.Count}");
    builder.AppendLine($"constants: {constants.Count}");
    builder.AppendLine($"entry-point: {FormatMethod(bindingResult.Compilation.EntryPoint)}");
    builder.AppendLine();

    builder.AppendLine("[diagnostics]");
    foreach (var diagnostic in bindingResult.Diagnostics)
    {
        builder.AppendLine($"- {diagnostic.Severity} {diagnostic.Id}: {diagnostic.Message} @{diagnostic.Span.Start}:{diagnostic.Span.Length}");
    }

    return builder.ToString();
}

static string BuildSymbolDump(
    string sourcePath,
    BindingResult bindingResult)
{
    var builder = new StringBuilder();
    builder.AppendLine($"source: {Path.GetFileName(sourcePath)}");
    builder.AppendLine($"namespace: {bindingResult.Compilation.Namespace ?? "<global>"}");
    builder.AppendLine();

    builder.AppendLine("[types]");
    foreach (var type in bindingResult.Compilation.Types.OfType<NamedTypeSymbol>().OrderBy(type => type.Name, StringComparer.Ordinal))
    {
        var kind = type.IsInterface
            ? "interface"
            : type.IsDelegate
                ? "delegate"
            : SemanticFacts.IsEnumType(type)
                ? "enum"
                : type.IsRecord
                    ? "record"
                    : "class";
        builder.AppendLine($"type {type.Name} kind={kind} base={(type.BaseType?.Name ?? "<none>")} generic-arity={type.GenericArity} args=[{string.Join(", ", type.TypeArguments?.Select(arg => arg.Name) ?? [])}]");

        if (type.InterfaceTypes.Count > 0)
        {
            builder.AppendLine($"  interfaces: {string.Join(", ", type.InterfaceTypes.Select(interfaceType => interfaceType.Name))}");
        }

        foreach (var field in type.Fields.OrderBy(field => field.Name, StringComparer.Ordinal))
        {
            builder.AppendLine($"  field {(field.IsStatic ? "static " : string.Empty)}{field.Name}: {field.Type.Name}");
        }

        foreach (var property in type.Properties.OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            var indexText = property.IsIndexer
                ? $" index({property.IndexParameter?.Name}:{property.IndexParameter?.Type.Name})"
                : string.Empty;
            builder.AppendLine($"  property {(property.IsStatic ? "static " : string.Empty)}{property.Name}: {property.Type.Name}{indexText} getter={(property.GetterMethod?.Name ?? "<none>")} setter={(property.SetterMethod?.Name ?? "<none>")}");
        }

        foreach (var method in type.Methods.OrderBy(method => method.Name, StringComparer.Ordinal))
        {
            builder.AppendLine($"  method {(method.IsStatic ? "static " : string.Empty)}{method.Name}({string.Join(", ", method.Parameters.Select(FormatParameter))}): {method.ReturnType.Name} virtual={method.IsVirtual} override={method.IsOverride} ctor={method.IsConstructor}");
        }
    }

    return builder.ToString();
}

static string BuildIrDump(
    string sourcePath,
    IReadOnlyList<MethodSymbol> methods,
    Lowerer lowerer)
{
    var builder = new StringBuilder();
    builder.AppendLine($"source: {Path.GetFileName(sourcePath)}");
    builder.AppendLine($"methods: {methods.Count}");
    builder.AppendLine();

    foreach (var method in methods.OrderBy(method => $"{method.DeclaringTypeName ?? "<global>"}.{method.Name}", StringComparer.Ordinal))
    {
        builder.AppendLine($"method {FormatMethod(method)}({string.Join(", ", method.Parameters.Select(FormatParameter))}): {method.ReturnType.Name}");
        AppendMethodLoweringContext(builder, method);
        try
        {
            var ir = lowerer.Lower(method);
            builder.AppendLine($"  registers: {ir.Registers.Count}");
            foreach (var register in ir.Registers)
            {
                builder.AppendLine($"    reg r{register.Index} {register.Name}: {register.Type.Name}");
            }

            if (ir.ArrayShapes.Count > 0)
            {
                builder.AppendLine("  array-shapes:");
                foreach (var shape in ir.ArrayShapes)
                {
                    builder.AppendLine($"    [{string.Join(", ", shape.Extents.Select(ext => $"r{ext.Index}:{ext.Type.Name}"))}]");
                }
            }

            if (ir.ExceptionHandlers.Count > 0)
            {
                builder.AppendLine("  exception-handlers:");
                foreach (var handler in ir.ExceptionHandlers)
                {
                    builder.AppendLine($"    try={handler.TryStartLabel}..{handler.TryEndLabel} catch={handler.CatchTypeName ?? "<any>"} handler={handler.HandlerStartLabel}..{handler.HandlerEndLabel} target={handler.TargetRegister}");
                }
            }

            foreach (var block in ir.Blocks)
            {
                builder.AppendLine($"  block {block.Name}");
                var instructionIndex = 0;
                foreach (var instruction in block.Instructions)
                {
                    builder.AppendLine($"    vm-ip={instructionIndex} {FormatIrInstruction(instruction)}");
                    instructionIndex++;
                }
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine($"  lowering-error: {ex.GetType().Name}: {ex.Message}");
        }

        builder.AppendLine();
    }

    return builder.ToString();
}

static void AppendMethodLoweringContext(StringBuilder builder, MethodSymbol method)
{
    builder.AppendLine($"  debug-context: declaring={method.DeclaringTypeName ?? "<global>"} static={method.IsStatic} ctor={method.IsConstructor} synthetic={method.IsSynthetic}");
    builder.AppendLine($"  debug-context: return={method.ReturnType.Name}");
    if (method.Parameters.Count > 0)
    {
        builder.AppendLine($"  debug-context: params=[{string.Join(", ", method.Parameters.Select(parameter => $"{parameter.Name}:{parameter.Type.Name}"))}]");
    }

    if (!IsEnumerablePipelineMethod(method))
    {
        return;
    }

    builder.AppendLine("  debug-context: enumerable-pipeline-method=true");
    builder.AppendLine($"  debug-context: declaring-open-generic={!string.IsNullOrWhiteSpace(method.DeclaringTypeName) && !method.DeclaringTypeName!.Contains('<', StringComparison.Ordinal)}");
    if (method.Declaration?.Body is null)
    {
        return;
    }

    foreach (var statement in method.Declaration.Body.Statements)
    {
        AppendEnumerableNewExpressionDiagnostics(builder, statement);
    }
}

static bool IsEnumerablePipelineMethod(MethodSymbol method) =>
    method.DeclaringTypeName is not null &&
    (method.Name == "Where" || method.Name == "Select") &&
    method.DeclaringTypeName.StartsWith("Enumerable", StringComparison.Ordinal);

static void AppendEnumerableNewExpressionDiagnostics(StringBuilder builder, StatementSyntax statement)
{
    switch (statement)
    {
        case ReturnStatementSyntax { Expression: NewExpressionSyntax newExpression }:
            builder.AppendLine($"  debug-new: type={newExpression.TypeName.ToDisplayString()} args={newExpression.Arguments.Count}");
            break;
        case LocalVariableDeclarationStatementSyntax localVariableDeclaration:
            foreach (var declarator in localVariableDeclaration.Declarators)
            {
                if (declarator.Initializer is NewExpressionSyntax newExpression)
                {
                    builder.AppendLine($"  debug-new: local={declarator.Identifier.Text} type={newExpression.TypeName.ToDisplayString()} args={newExpression.Arguments.Count}");
                }
            }
            break;
    }
}

static string BuildDebugSymbols(
    string sourcePath,
    IReadOnlyList<MethodSymbol> methods,
    BytecodeModule module,
    Lowerer lowerer,
    IReadOnlyList<(string Path, string Text, SyntaxTree Tree)> sourceInputs)
{
    var builder = new StringBuilder();
    builder.AppendLine($"module\t{sourcePath}");
    var functionIdsByMethod = methods
        .Select((method, index) => (method, functionId: (uint)(index + 1)))
        .ToDictionary(pair => pair.method, pair => pair.functionId);
    var functionsById = module.Functions.ToDictionary(function => function.FunctionId);

    foreach (var method in methods.OrderBy(method => $"{method.DeclaringTypeName ?? "<global>"}.{method.Name}", StringComparer.Ordinal))
    {
        var ir = lowerer.Lower(method);
        var functionDisplayName = FormatMethod(method);
        var functionId = functionIdsByMethod.TryGetValue(method, out var resolvedFunctionId) ? resolvedFunctionId : 0u;
        if (!functionsById.TryGetValue(functionId, out var bytecodeFunction))
        {
            continue;
        }

        var irToBytecodeRanges = bytecodeFunction.DebugVmIpRanges.ToDictionary(range => range.IrVmIp);
        var lastVmIp = Math.Max(bytecodeFunction.Instructions.Count - 1, 0);
        var (methodSourcePath, methodSourceText) = ResolveMethodSourceInfo(method, sourcePath, sourceInputs);
        builder.Append("function");
        builder.Append('\t').Append(functionId);
        builder.Append('\t').Append(method.Name);
        builder.Append('\t').Append(functionDisplayName);
        builder.Append('\t').Append(method.DeclaringTypeName ?? "<global>");
        builder.Append('\t').Append(bytecodeFunction.Instructions.Count);
        builder.Append('\t').Append(lastVmIp);
        builder.Append('\t').Append(methodSourcePath);
        builder.AppendLine();

        foreach (var variable in ir.DebugVariables)
        {
            if (!TryMapVmIpRange(variable.VmIpStart, variable.VmIpEnd, irToBytecodeRanges, out var vmIpStart, out var vmIpEnd))
            {
                continue;
            }

            builder.Append(variable.Kind == IrDebugVariableKind.Parameter || variable.Kind == IrDebugVariableKind.Self ? "arg" : "local");
            builder.Append('\t').Append(functionId);
            builder.Append('\t').Append(functionDisplayName);
            builder.Append('\t').Append(variable.Name);
            builder.Append('\t').Append(variable.Type.Name);
            builder.Append('\t').Append(variable.RegisterIndex);
            builder.Append('\t').Append(vmIpStart);
            builder.Append('\t').Append(vmIpEnd);
            builder.AppendLine();
        }

        foreach (var sourceMap in ir.DebugSourceMaps)
        {
            if (!TryMapVmIpRange(sourceMap.VmIpStart, sourceMap.VmIpEnd, irToBytecodeRanges, out var vmIpStart, out var vmIpEnd))
            {
                continue;
            }

            var start = GetPositionInfo(methodSourceText, sourceMap.Span.Start);
            var end = GetPositionInfo(methodSourceText, Math.Max(sourceMap.Span.End, sourceMap.Span.Start));
            builder.Append("map");
            builder.Append('\t').Append(functionId);
            builder.Append('\t').Append(functionDisplayName);
            builder.Append('\t').Append(vmIpStart);
            builder.Append('\t').Append(vmIpEnd);
            builder.Append('\t').Append(methodSourcePath);
            builder.Append('\t').Append(start.Line);
            builder.Append('\t').Append(start.Column);
            builder.Append('\t').Append(end.Line);
            builder.Append('\t').Append(end.Column);
            builder.Append('\t').Append(sourceMap.Span.Start);
            builder.Append('\t').Append(sourceMap.Span.Length);
            builder.AppendLine();
        }
    }

    return builder.ToString();
}

static bool TryMapVmIpRange(
    int irVmIpStart,
    int irVmIpEnd,
    IReadOnlyDictionary<int, BytecodeDebugVmIpRange> irToBytecodeRanges,
    out int vmIpStart,
    out int vmIpEnd)
{
    vmIpStart = 0;
    vmIpEnd = 0;

    if (!irToBytecodeRanges.TryGetValue(irVmIpStart, out var startRange) ||
        !irToBytecodeRanges.TryGetValue(irVmIpEnd, out var endRange))
    {
        return false;
    }

    vmIpStart = startRange.BytecodeVmIpStart;
    vmIpEnd = endRange.BytecodeVmIpEnd;
    if (vmIpEnd < vmIpStart)
    {
        vmIpEnd = vmIpStart;
    }

    return true;
}

static (string Path, string Text) ResolveMethodSourceInfo(
    MethodSymbol method,
    string defaultSourcePath,
    IReadOnlyList<(string Path, string Text, SyntaxTree Tree)> sourceInputs)
{
    if (method.Declaration is null)
    {
        var defaultText = sourceInputs.FirstOrDefault(input => string.Equals(input.Path, defaultSourcePath, StringComparison.Ordinal)).Text;
        return (defaultSourcePath, defaultText);
    }

    foreach (var sourceInput in sourceInputs)
    {
        if (ContainsMethodDeclarationInMembers(sourceInput.Tree.Root.Members, method.Declaration))
        {
            return (sourceInput.Path, sourceInput.Text);
        }
    }

    var fallbackText = sourceInputs.FirstOrDefault(input => string.Equals(input.Path, defaultSourcePath, StringComparison.Ordinal)).Text;
    return (defaultSourcePath, fallbackText);
}

static bool ContainsMethodDeclarationInMembers(IReadOnlyList<MemberSyntax> members, MethodDeclarationSyntax declaration)
{
    foreach (var member in members)
    {
        if (member is ClassDeclarationSyntax classDeclaration)
        {
            if (ContainsMethodDeclarationInTypeMembers(classDeclaration.Members, declaration))
            {
                return true;
            }

            continue;
        }

        if (member is InterfaceDeclarationSyntax interfaceDeclaration)
        {
            if (ContainsMethodDeclarationInTypeMembers(interfaceDeclaration.Members, declaration))
            {
                return true;
            }
        }
    }

    return false;
}

static bool ContainsMethodDeclarationInTypeMembers(IReadOnlyList<TypeMemberSyntax> members, MethodDeclarationSyntax declaration)
{
    foreach (var member in members)
    {
        if (ReferenceEquals(member, declaration))
        {
            return true;
        }
    }

    return false;
}

static IReadOnlyDictionary<uint, (uint CodeOffset, uint CodeSize)> BuildFunctionCodeOffsets(IReadOnlyList<BytecodeFunction> functions)
{
    var offsets = new Dictionary<uint, (uint CodeOffset, uint CodeSize)>();
    uint nextOffset = 0;
    foreach (var function in functions)
    {
        var codeSize = (uint)(function.Instructions.Count * 11);
        offsets[function.FunctionId] = (nextOffset, codeSize);
        nextOffset += codeSize;
    }

    return offsets;
}

static string DescribeTopLevelMember(MemberSyntax member) =>
    member switch
    {
        ClassDeclarationSyntax classDeclaration => $"{(classDeclaration.ClassKeyword.Kind == SyntaxKind.RecordKeyword ? "record" : "class")} {classDeclaration.Identifier.Text}",
        InterfaceDeclarationSyntax interfaceDeclaration => $"interface {interfaceDeclaration.Identifier.Text}",
        EnumDeclarationSyntax enumDeclaration => $"enum {enumDeclaration.Identifier.Text}",
        DelegateDeclarationSyntax delegateDeclaration => $"{(delegateDeclaration.SignatureKeyword.Kind == SyntaxKind.ProcedureKeyword ? "delegate procedure" : "delegate function")} {delegateDeclaration.Identifier.Text}",
        TopLevelVariableDeclarationSyntax variableDeclaration => $"global {string.Join(", ", variableDeclaration.Declarators.Select(declarator => declarator.Identifier.Text))}",
        TopLevelConstantDeclarationSyntax constantDeclaration => $"top-const {string.Join(", ", constantDeclaration.Declarators.Select(declarator => declarator.Identifier.Text))}",
        TopLevelExpressionStatementSyntax expressionStatement => $"expr {expressionStatement.Expression.Kind}",
        _ => member.Kind.ToString()
    };

static string FormatParameter(ParameterSymbol parameter) =>
    $"{(parameter.PassingKind == ParameterPassingKind.Value ? string.Empty : parameter.PassingKind.ToString().ToLowerInvariant() + " ")}{parameter.Name}: {parameter.Type.Name}";

static string FormatIrInstruction(IrInstruction instruction)
{
    var destination = instruction.Destination is null
        ? "-"
        : $"r{instruction.Destination.Index}:{instruction.Destination.Type.Name}";
    var operand = instruction.Operand switch
    {
        null => "<none>",
        IrValue value => $"r{value.Index}:{value.Type.Name}",
        (IrValue left, IrValue right) => $"(r{left.Index}:{left.Type.Name}, r{right.Index}:{right.Type.Name})",
        IrCallTarget call => $"call target={FormatMethod(call.Method)} display={call.DisplayName} recv={(call.Receiver is null ? "<none>" : $"r{call.Receiver.Index}:{call.Receiver.Type.Name}")} args=[{string.Join(", ", call.Arguments.Select(arg => $"r{arg.Index}:{arg.Type.Name}"))}] virtual={call.IsVirtual}",
        IrFieldTarget field => $"field {field.Field.DeclaringTypeName}.{field.Field.Name}:{field.Field.Type.Name} recv={(field.Receiver is null ? "<none>" : $"r{field.Receiver.Index}:{field.Receiver.Type.Name}")}",
        IrNewArrayTarget newArray => $"newarr {newArray.ElementTypeName} len=r{newArray.LengthRegister.Index}:{newArray.LengthRegister.Type.Name} shape=[{string.Join(", ", newArray.Shape.Extents.Select(ext => $"r{ext.Index}:{ext.Type.Name}"))}]",
        IrArrayTarget array => $"array r{array.Array.Index}:{array.Array.Type.Name} index={(array.Index is null ? "<none>" : $"r{array.Index.Index}:{array.Index.Type.Name}")} shape={(array.Shape is null ? "<none>" : "[" + string.Join(", ", array.Shape.Extents.Select(ext => $"r{ext.Index}:{ext.Type.Name}")) + "]")}",
        IrStringSliceTarget slice => $"slice src=r{slice.Source.Index}:{slice.Source.Type.Name} start=r{slice.Start.Index}:{slice.Start.Type.Name} end=r{slice.End.Index}:{slice.End.Type.Name}",
        IrStringReplaceTarget replace => $"replace src=r{replace.Source.Index}:{replace.Source.Type.Name} old=r{replace.OldValue.Index}:{replace.OldValue.Type.Name} new=r{replace.NewValue.Index}:{replace.NewValue.Type.Name}",
        IrStringInsertTarget insert => $"insert src=r{insert.Source.Index}:{insert.Source.Type.Name} idx=r{insert.Index.Index}:{insert.Index.Type.Name} value=r{insert.Value.Index}:{insert.Value.Type.Name}",
        IrStringRemoveTarget remove => $"remove src=r{remove.Source.Index}:{remove.Source.Type.Name} idx=r{remove.Index.Index}:{remove.Index.Type.Name} len=r{remove.Length.Index}:{remove.Length.Type.Name}",
        IrStringTryParseTarget tryParse => $"tryparse src=r{tryParse.Source.Index}:{tryParse.Source.Type.Name} parsed=r{tryParse.ParsedValue.Index}:{tryParse.ParsedValue.Type.Name}",
        IrTypeCheckTarget typeCheck => $"typecheck value=r{typeCheck.Value.Index}:{typeCheck.Value.Type.Name} type={typeCheck.TypeName}",
        string text => text,
        _ => instruction.Operand.ToString() ?? "<unknown>"
    };

    return $"{instruction.OpCode} dst={destination} op={operand}";
}

static void WriteDiagnostic(string sourcePath, string sourceText, Diagnostic diagnostic)
{
    var location = GetLineInfo(sourceText, diagnostic.Span);
    var fileName = Path.GetFileName(sourcePath);
    Console.WriteLine($"{fileName}({location.Line},{location.Column}): {diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Id}: {diagnostic.Message}");

    if (location.LineText.Length == 0)
    {
        return;
    }

    var expandedLineText = ExpandTabs(location.LineText);
    var lineNumberText = location.Line.ToString();
    Console.WriteLine($"  {lineNumberText} | {expandedLineText}");

    var expandedPrefix = ExpandTabs(location.LineText[..Math.Min(location.Column - 1, location.LineText.Length)]);
    var markerColumn = expandedPrefix.Length;
    var sourceRemainderLength = Math.Max(expandedLineText.Length - markerColumn, 1);
    var markerLength = diagnostic.Span.Length <= 0 ? 1 : Math.Min(Math.Max(diagnostic.Span.Length, 1), sourceRemainderLength);
    var marker = markerLength == 1 ? "^" : "^" + new string('~', markerLength - 1);
    Console.WriteLine($"  {new string(' ', lineNumberText.Length)} | {new string(' ', markerColumn)}{marker}");
}

static (int Line, int Column, string LineText) GetLineInfo(string sourceText, TextSpan span)
{
    if (sourceText.Length == 0)
    {
        return (1, 1, string.Empty);
    }

    var start = Math.Clamp(span.Start, 0, sourceText.Length);
    var line = 1;
    var column = 1;
    var lineStart = 0;

    for (var index = 0; index < start; index++)
    {
        if (sourceText[index] == '\n')
        {
            line++;
            column = 1;
            lineStart = index + 1;
        }
        else
        {
            column++;
        }
    }

    var lineEnd = lineStart;
    while (lineEnd < sourceText.Length && sourceText[lineEnd] is not '\r' and not '\n')
    {
        lineEnd++;
    }

    return (line, column, sourceText[lineStart..lineEnd]);
}

static (int Line, int Column) GetPositionInfo(string sourceText, int position)
{
    if (sourceText.Length == 0)
    {
        return (1, 1);
    }

    var clampedPosition = Math.Clamp(position, 0, sourceText.Length);
    var line = 1;
    var column = 1;

    for (var index = 0; index < clampedPosition; index++)
    {
        if (sourceText[index] == '\n')
        {
            line++;
            column = 1;
        }
        else
        {
            column++;
        }
    }

    return (line, column);
}

static string ExpandTabs(string text) => text.Replace("\t", "    ");

sealed class CompilationTiming
{
    private readonly Stopwatch total = Stopwatch.StartNew();
    private readonly List<TimingEntry> entries = [];

    public T Measure<T>(string name, Func<T> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return action();
        }
        finally
        {
            stopwatch.Stop();
            Add(name, stopwatch.Elapsed);
        }
    }

    public void Measure(string name, Action action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            action();
        }
        finally
        {
            stopwatch.Stop();
            Add(name, stopwatch.Elapsed);
        }
    }

    public async Task<T> MeasureAsync<T>(string name, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await action();
        }
        finally
        {
            stopwatch.Stop();
            Add(name, stopwatch.Elapsed);
        }
    }

    public async Task MeasureAsync(string name, Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await action();
        }
        finally
        {
            stopwatch.Stop();
            Add(name, stopwatch.Elapsed);
        }
    }

    public string FormatTotal() => FormatDuration(total.Elapsed);

    public void WriteTimings(TextWriter writer)
    {
        foreach (var entry in entries)
        {
            writer.WriteLine($"timing: {entry.Name}: {FormatDuration(entry.Elapsed)} count={entry.Count}");
        }

        writer.WriteLine($"timing: total: {FormatDuration(total.Elapsed)}");
    }

    private void Add(string name, TimeSpan elapsed)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (string.Equals(entries[index].Name, name, StringComparison.Ordinal))
            {
                entries[index] = entries[index] with
                {
                    Count = entries[index].Count + 1,
                    Elapsed = entries[index].Elapsed + elapsed
                };
                return;
            }
        }

        entries.Add(new TimingEntry(name, 1, elapsed));
    }

    private static string FormatDuration(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:F3}s"
            : $"{elapsed.TotalMilliseconds:F1}ms";

    private sealed record TimingEntry(string Name, int Count, TimeSpan Elapsed);
}
