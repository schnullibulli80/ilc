using ILC.Compiler.Binding;
using ILC.Compiler.Bytecode;
using ILC.Compiler.Core;
using ILC.Compiler.Lowering;
using ILC.Compiler.Syntax;

var debugEnabled = args.Contains("--debug", StringComparer.Ordinal);
var positionalArgs = args.Where(argument => !string.Equals(argument, "--debug", StringComparison.Ordinal)).ToArray();

if (positionalArgs.Length == 0)
{
    Console.Error.WriteLine("usage: ilc [--debug] <main-source-file> [additional-source-files...]");
    return 1;
}

var sourcePath = positionalArgs[0];
if (!File.Exists(sourcePath))
{
    Console.Error.WriteLine($"error: file not found: {sourcePath}");
    return 2;
}

var sourceText = await File.ReadAllTextAsync(sourcePath);
var syntaxTree = SyntaxTree.Parse(sourceText);

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

        var importedText = await File.ReadAllTextAsync(importedPath);
        var importedTree = SyntaxTree.Parse(importedText);
        var importedNamespace = importedTree.Root.Namespace?.Name.ToDisplayString();
        if (importedNamespace is not null && importedNamespaces.Contains(importedNamespace))
        {
            importedSyntaxTrees.Add(importedTree);
        }
    }
}

var mergedSyntaxTree = SyntaxTree.Merge(syntaxTree, importedSyntaxTrees);
var bindingResult = new Binder().Bind(mergedSyntaxTree);

if (bindingResult.Diagnostics.Count > 0)
{
    foreach (var diagnostic in bindingResult.Diagnostics)
    {
        WriteDiagnostic(sourcePath, sourceText, diagnostic);
    }
}

var declaredMethods = bindingResult.Compilation.Types
    .OfType<NamedTypeSymbol>()
    .Where(type => !SemanticFacts.IsOpenGenericDefinition(type))
    .SelectMany(type => type.Methods)
    .ToArray();
var declaredFields = bindingResult.Compilation.GetAllFields();
var declaredProperties = bindingResult.Compilation.GetAllProperties();

var moduleMethods = bindingResult.Compilation.Methods.Concat(declaredMethods).ToArray();
if (bindingResult.HasErrors)
{
    if (debugEnabled)
    {
        Console.WriteLine($"compiled {Path.GetFileName(sourcePath)}");
        Console.WriteLine($"namespace: {bindingResult.Compilation.Namespace ?? "<global>"}");
        Console.WriteLine($"members: {mergedSyntaxTree.Root.Members.Count}");
        Console.WriteLine($"globals: {bindingResult.Compilation.Globals.Count}");
        Console.WriteLine($"declared types: {bindingResult.Compilation.Types.OfType<NamedTypeSymbol>().Count()}");
        Console.WriteLine($"declared methods: {declaredMethods.Length}");
        Console.WriteLine($"declared fields: {declaredFields.Count}");
        Console.WriteLine("module functions: 0");
        Console.WriteLine("entry point: <none>");
    }

    return 1;
}

if (moduleMethods.Length > 0)
{
    var lowerer = new Lowerer(moduleMethods, declaredFields, bindingResult.Compilation.Types, declaredProperties, bindingResult.Compilation.GetAllConstants());
    var module = new BytecodeEmitter().EmitModule(moduleMethods, declaredFields, bindingResult.Compilation.Types, lowerer);
    var ilbImage = new IlbSerializer().Serialize(module, moduleMethods, declaredFields, bindingResult.Compilation.Types, bindingResult.Compilation.EntryPoint);
    var ilbPath = Path.ChangeExtension(sourcePath, ".ilb");
    await File.WriteAllBytesAsync(ilbPath, ilbImage.Bytes);
    var entryPoint = bindingResult.Compilation.EntryPoint;
    Console.WriteLine($"compiled {Path.GetFileName(sourcePath)}");
    Console.WriteLine($"ilb file: {Path.GetFileName(ilbPath)}");

    if (debugEnabled)
    {
        Console.WriteLine($"namespace: {bindingResult.Compilation.Namespace ?? "<global>"}");
        Console.WriteLine($"members: {mergedSyntaxTree.Root.Members.Count}");
        Console.WriteLine($"globals: {bindingResult.Compilation.Globals.Count}");
        Console.WriteLine($"declared types: {bindingResult.Compilation.Types.OfType<NamedTypeSymbol>().Count()}");
        Console.WriteLine($"declared methods: {declaredMethods.Length}");
        Console.WriteLine($"declared fields: {declaredFields.Count}");
        Console.WriteLine($"module functions: {module.Functions.Count}");
        Console.WriteLine($"module array-shapes: {module.ArrayShapes.Count}");
        Console.WriteLine($"ilb file: {Path.GetFileName(ilbPath)} bytes={ilbImage.Bytes.Length} sections={ilbImage.Sections.Count}");
        Console.WriteLine($"entry point: {FormatMethod(entryPoint)}");

        foreach (var shape in module.ArrayShapes)
        {
            Console.WriteLine($"module-array-shape fn={shape.FunctionId} r{shape.ArrayRegister} extents=[{string.Join(", ", shape.ExtentRegisters.Select(index => $"r{index}"))}]");
        }

        foreach (var function in module.Functions)
        {
            Console.WriteLine($"function {function.FunctionId}: {function.Name} regs={function.RegisterCount} argc={function.ArgumentCount} instr={function.Instructions.Count}");
            foreach (var shape in function.ArrayShapes)
            {
                Console.WriteLine($"  array-shape r{shape.ArrayRegister} extents=[{string.Join(", ", shape.ExtentRegisters.Select(index => $"r{index}"))}]");
            }

            foreach (var instruction in function.Instructions)
            {
                Console.WriteLine($"  {instruction.OpCode} dst={instruction.Destination} left={instruction.Left} right={instruction.Right} imm={instruction.Immediate}");
            }
        }
    }
}
else
{
    Console.WriteLine($"compiled {Path.GetFileName(sourcePath)}");
    if (debugEnabled)
    {
        Console.WriteLine($"namespace: {bindingResult.Compilation.Namespace ?? "<global>"}");
        Console.WriteLine($"members: {mergedSyntaxTree.Root.Members.Count}");
        Console.WriteLine($"globals: {bindingResult.Compilation.Globals.Count}");
        Console.WriteLine($"declared types: {bindingResult.Compilation.Types.OfType<NamedTypeSymbol>().Count()}");
        Console.WriteLine($"declared methods: {declaredMethods.Length}");
        Console.WriteLine($"declared fields: {declaredFields.Count}");
        Console.WriteLine("module functions: 0");
        Console.WriteLine("entry point: <none>");
    }
}

return 0;

static string FormatMethod(MethodSymbol? method) =>
    method is null
        ? "<none>"
        : method.DeclaringTypeName is null
            ? method.Name
            : $"{method.DeclaringTypeName}.{method.Name}";

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

static string ExpandTabs(string text) => text.Replace("\t", "    ");
