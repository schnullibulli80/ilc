using ILC.Compiler.Binding;
using ILC.Compiler.Bytecode;
using ILC.Compiler.Core;
using ILC.Compiler.Lowering;
using ILC.Compiler.Syntax;

var failures = new List<string>();
var debugEnabled = args.Contains("--debug", StringComparer.Ordinal);
var bootstrapFixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "fixtures", "compiler-bootstrap.ilc"));
var systemFixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "libs", "shipped", "system.ilc"));
var diagnosticsFixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "libs", "shipped", "diagnostics.ilc"));
var textFixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "libs", "shipped", "text.ilc"));
var jsonFixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "libs", "shipped", "json.ilc"));
var netFixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "libs", "shipped", "net.ilc"));
var threadingFixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "libs", "shipped", "threading.ilc"));
var collectionsFixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "libs", "shipped", "collections.ilc"));
var uiFixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "libs", "shipped", "ui.ilc"));
var uiHostingFixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "libs", "shipped", "ui-hosting.ilc"));
var uiQtQuickFixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "libs", "shipped", "ui-backends-qtquick.ilc"));
var demoCoreFixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "fixtures", "demo-core.ilc"));
var bootstrapSource = File.ReadAllText(bootstrapFixturePath);
var systemSource = File.ReadAllText(systemFixturePath);
var diagnosticsSource = File.ReadAllText(diagnosticsFixturePath);
var textSource = File.ReadAllText(textFixturePath);
var jsonSource = File.ReadAllText(jsonFixturePath);
var netSource = File.ReadAllText(netFixturePath);
var threadingSource = File.ReadAllText(threadingFixturePath);
var collectionsSource = File.ReadAllText(collectionsFixturePath);
var uiSource = File.ReadAllText(uiFixturePath);
var uiHostingSource = File.ReadAllText(uiHostingFixturePath);
var uiQtQuickSource = File.ReadAllText(uiQtQuickFixturePath);
var demoCoreSource = File.ReadAllText(demoCoreFixturePath);

var tree = SyntaxTree.Parse(bootstrapSource);
var systemTree = SyntaxTree.Parse(systemSource);
var diagnosticsTree = SyntaxTree.Parse(diagnosticsSource);
var textTree = SyntaxTree.Parse(textSource);
var jsonTree = SyntaxTree.Parse(jsonSource);
var netTree = SyntaxTree.Parse(netSource);
var threadingTree = SyntaxTree.Parse(threadingSource);
var collectionsTree = SyntaxTree.Parse(collectionsSource);
var uiTree = SyntaxTree.Parse(uiSource);
var uiHostingTree = SyntaxTree.Parse(uiHostingSource);
var uiQtQuickTree = SyntaxTree.Parse(uiQtQuickSource);
var demoCoreTree = SyntaxTree.Parse(demoCoreSource);
var mergedTree = SyntaxTree.Merge(tree, [systemTree, diagnosticsTree, textTree, jsonTree, netTree, threadingTree, collectionsTree, uiTree, uiHostingTree, uiQtQuickTree, demoCoreTree]);

if (tree.Root.Tokens.Count == 0)
{
    failures.Add("SyntaxTree.Parse should produce tokens.");
}

if (tree.Diagnostics.Count > 0)
{
    if (debugEnabled)
    {
        Console.Error.WriteLine(
            "debug.parser.diagnostics=" +
            string.Join(
                " | ",
                tree.Diagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}@{diagnostic.Span.Start}")));
    }
}

if (tree.Root.Namespace?.Name.ToDisplayString() != "Demo.App")
{
    failures.Add("Parser should capture the namespace declaration.");
}

if (tree.Root.Uses?.Imports.Count != 11)
{
    failures.Add("Parser should capture the uses clause.");
}
else if (tree.Root.Uses.Imports[0].NamespaceName.ToDisplayString() != "System" ||
         tree.Root.Uses.Imports[1].NamespaceName.ToDisplayString() != "System.Diagnostics" ||
         tree.Root.Uses.Imports[2].NamespaceName.ToDisplayString() != "System.Text" ||
         tree.Root.Uses.Imports[3].NamespaceName.ToDisplayString() != "System.Text.Json" ||
         tree.Root.Uses.Imports[4].NamespaceName.ToDisplayString() != "System.Net" ||
         tree.Root.Uses.Imports[5].NamespaceName.ToDisplayString() != "System.Threading" ||
         tree.Root.Uses.Imports[6].NamespaceName.ToDisplayString() != "System.Collections" ||
         tree.Root.Uses.Imports[7].NamespaceName.ToDisplayString() != "System.Ui" ||
         tree.Root.Uses.Imports[8].NamespaceName.ToDisplayString() != "System.Ui.Hosting" ||
         tree.Root.Uses.Imports[9].NamespaceName.ToDisplayString() != "System.Ui.Backends.QtQuick" ||
         tree.Root.Uses.Imports[10].NamespaceName.ToDisplayString() != "Demo.Core")
{
    failures.Add("Parser should capture imported namespaces.");
}

if (tree.Root.Members.Count != 16)
{
    failures.Add("Parser should capture top-level members.");
}

if (tree.Root.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault(member => member.Identifier.Text == "Program") is not ClassDeclarationSyntax classDeclaration)
{
    failures.Add("Parser should capture a class declaration.");
}
else
{
    if (classDeclaration.Identifier.Text != "Program")
    {
        failures.Add("Class declaration should retain its identifier.");
    }

    if (classDeclaration.Members.Count < 18)
    {
        failures.Add("Class declaration should capture field, property and method members.");
    }

    if (debugEnabled)
    {
        Console.Error.WriteLine(
            "debug.parser.program.methods=" +
            string.Join(
                ",",
                classDeclaration.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Select(method => method.Identifier.Text)));
    }

    var parsedMain = classDeclaration.Members
        .OfType<MethodDeclarationSyntax>()
        .FirstOrDefault(method => method.Identifier.Text == "Main");
    if (debugEnabled && parsedMain?.Body is not null)
    {
        Console.Error.WriteLine($"debug.parser.main.statementCount={parsedMain.Body.Statements.Count}");
        Console.Error.WriteLine(
            "debug.parser.main.tailStatements=" +
            string.Join(
                ",",
                parsedMain.Body.Statements
                    .TakeLast(8)
                    .Select(statement => statement.Kind.ToString())));

        Console.Error.WriteLine(
            "debug.parser.main.caseLabels=" +
            string.Join(
                " | ",
                parsedMain.Body.Statements
                    .OfType<CaseStatementSyntax>()
                    .SelectMany(caseStatement => caseStatement.Clauses)
                    .SelectMany(clause => clause.Labels)
                    .Select(label => $"{label.Kind}:{SemanticFacts.GetExpressionDisplayName(label)}")));
    }
}

var binding = new Binder().Bind(mergedTree);
if (debugEnabled && binding.Diagnostics.Count > 0)
{
    Console.Error.WriteLine(
        "debug.binding.diagnostics=" +
        string.Join(
            " | ",
            binding.Diagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}@{diagnostic.Span.Start}")));
}
if (binding.HasErrors)
{
    failures.Add(
        "Valid fixture code should not produce binding errors. Diagnostics: " +
        string.Join(
            " | ",
            binding.Diagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}@{diagnostic.Span.Start}")));
}

if (binding.Diagnostics.Any(diagnostic => diagnostic.Id is "ILC2100" or "ILC2101" or "ILC2102" or "ILC2103" or "ILC2104" or "ILC2105" or "ILC2106"))
{
    failures.Add(
        "Valid fixture code should not produce bootstrap name or assignment diagnostics. Diagnostics: " +
        string.Join(
            " | ",
            binding.Diagnostics
                .Where(diagnostic => diagnostic.Id is "ILC2100" or "ILC2101" or "ILC2102" or "ILC2103" or "ILC2104" or "ILC2105" or "ILC2106")
                .Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}@{diagnostic.Span.Start}")));
}

if (binding.Diagnostics.Any(diagnostic => diagnostic.Id is "ILC2151" or "ILC2152" or "ILC2153" or "ILC2154" or "ILC2155" or "ILC2156" or "ILC2157"))
{
    failures.Add("Valid fixture code should not produce bootstrap set diagnostics.");
}

var topLevelMethods = binding.Compilation.Methods.Where(method => method.Name == "__TopLevelMain").ToArray();
if (topLevelMethods.Length != 1)
{
    failures.Add(
        "Binder should synthesize exactly one top-level entry method when top-level code exists. Actual top-level methods: " +
        string.Join(", ", binding.Compilation.Methods.Select(method => method.Name)));
}
else if (topLevelMethods[0].Name != "__TopLevelMain")
{
    failures.Add("Synthetic top-level entry stubs should not collide with declared Main methods.");
}

if (binding.Compilation.Globals.Count != 1 || binding.Compilation.Globals[0].Name != "value")
{
    failures.Add("Binder should surface top-level global variables.");
}
else if (binding.Compilation.Globals[0].Type != TypeSymbol.Integer)
{
    failures.Add("Binder should infer Integer for top-level 'var value := 42'.");
}

if (binding.Compilation.EntryPoint?.Name != "Main" || binding.Compilation.EntryPoint.DeclaringTypeName != "Program")
{
    failures.Add("Binder should select Program.Main as the unique explicit entry point.");
}

var programType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Program");
var modeType = binding.Compilation.Types.FirstOrDefault(type => type.Name == "Mode");
if (modeType is null || modeType.IsReferenceType)
{
    failures.Add("Binder should surface declared enums as non-reference named types.");
}

var pointType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Point");
if (pointType is null || pointType.Fields.Count != 2 || !pointType.IsReferenceType || !pointType.IsRecord)
{
    failures.Add("Binder should surface records on the existing object/field path and mark them as records.");
}

var intProjectorType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "IntProjector");
if (intProjectorType is null || !intProjectorType.IsReferenceType || !intProjectorType.IsDelegate)
{
    failures.Add("Binder should surface nominal delegate declarations as reference delegate types.");
}
else if (!intProjectorType.Methods.Any(method =>
             method.Name == "Invoke" &&
             !method.IsStatic &&
             method.Parameters.Count == 1 &&
             method.Parameters[0].Type == TypeSymbol.Integer &&
             method.ReturnType == TypeSymbol.Integer &&
             method.HostImportKind == HostImportKind.DelegateInvoke) ||
         !intProjectorType.Methods.Any(method =>
             method.Name == ".ctor" &&
             method.IsConstructor &&
             method.Parameters.Count == 2 &&
             method.Parameters[0].Type == TypeSymbol.Object &&
             method.Parameters[1].Type == TypeSymbol.Integer &&
             method.HostImportKind == HostImportKind.DelegateBind) ||
         !intProjectorType.Fields.Any(field => field.Name == "TargetObjectValue" && field.Type == TypeSymbol.Object) ||
         !intProjectorType.Fields.Any(field => field.Name == "TargetFunctionIdValue" && field.Type == TypeSymbol.Integer))
{
    failures.Add("Binder should synthesize the expected delegate storage and Invoke/constructor surface for delegate declarations.");
}

var mathType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Math");
if (mathType is null || mathType.Methods.Count != 4 || mathType.Methods.Any(method => !method.IsStatic))
{
    failures.Add("Binder should surface System.Math with four static Integer helper methods.");
}
else if (!mathType.Methods.Any(method => method.Name == "Min" && method.Parameters.Count == 2 && method.ReturnType == TypeSymbol.Integer) ||
         !mathType.Methods.Any(method => method.Name == "Max" && method.Parameters.Count == 2 && method.ReturnType == TypeSymbol.Integer) ||
         !mathType.Methods.Any(method => method.Name == "Abs" && method.Parameters.Count == 1 && method.ReturnType == TypeSymbol.Integer) ||
         !mathType.Methods.Any(method => method.Name == "Clamp" && method.Parameters.Count == 3 && method.ReturnType == TypeSymbol.Integer))
{
    failures.Add("Binder should expose the expected System.Math helper signatures.");
}

var convertType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Convert");
if (convertType is null || convertType.Methods.Count != 4 || convertType.Methods.Any(method => !method.IsStatic))
{
    failures.Add("Binder should surface System.Convert with four static conversion helper methods.");
}
else if (!convertType.Methods.Any(method => method.Name == "ToInteger" && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.Integer) ||
         !convertType.Methods.Any(method => method.Name == "TryToInteger" && method.Parameters.Count == 2 && method.Parameters[0].Type == TypeSymbol.String && method.Parameters[1].Type == TypeSymbol.Integer && method.Parameters[1].PassingKind == ParameterPassingKind.Out && method.ReturnType == TypeSymbol.Boolean) ||
         !convertType.Methods.Any(method => method.Name == "ToString" && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.String) ||
         !convertType.Methods.Any(method => method.Name == "ToBoolean" && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.Boolean))
{
    failures.Add("Binder should expose the expected System.Convert helper signatures.");
}

var fileType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "File");
if (fileType is null)
{
    failures.Add("Binder should surface System.File in the shipped library.");
}
else if (!fileType.Methods.Any(method => method.Name == "ReadAllLines" && method.IsStatic && method.Parameters.Count == 1 && method.ReturnType.Name == "String[]") ||
         !fileType.Methods.Any(method => method.Name == "WriteAllLines" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type == TypeSymbol.String && method.Parameters[1].Type.Name == "String[]") ||
         !fileType.Methods.Any(method => method.Name == "AppendLine" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters.All(parameter => parameter.Type == TypeSymbol.String)))
{
    failures.Add("Binder should expose the expected line-oriented System.File helper signatures.");
}

var pathType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Path");
if (pathType is null)
{
    failures.Add("Binder should surface System.Path in the shipped library.");
}
else if (!pathType.Methods.Any(method => method.Name == "HasExtension" && method.IsStatic && method.Parameters.Count == 1 && method.ReturnType == TypeSymbol.Boolean) ||
         !pathType.Methods.Any(method => method.Name == "GetFileNameWithoutExtension" && method.IsStatic && method.Parameters.Count == 1 && method.ReturnType == TypeSymbol.String) ||
         !pathType.Methods.Any(method => method.Name == "ChangeExtension" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters.All(parameter => parameter.Type == TypeSymbol.String) && method.ReturnType == TypeSymbol.String))
{
    failures.Add("Binder should expose the expected higher-level System.Path helper signatures.");
}

var textType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Text");
if (textType is null)
{
    failures.Add("Binder should surface System.Text.Text in the shipped library.");
}
else if (!textType.Methods.Any(method => method.Name == "IsNullOrEmpty" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.Boolean) ||
         !textType.Methods.Any(method => method.Name == "NullIfEmpty" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.String) ||
         !textType.Methods.Any(method => method.Name == "TrimToNull" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.String) ||
         !textType.Methods.Any(method => method.Name == "CollapseWhitespace" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.String) ||
         !textType.Methods.Any(method => method.Name == "Indent" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters.All(parameter => parameter.Type == TypeSymbol.String) && method.ReturnType == TypeSymbol.String) ||
         !textType.Methods.Any(method => method.Name == "Join" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type == TypeSymbol.String && method.Parameters[1].Type.Name == "String[]" && method.ReturnType == TypeSymbol.String) ||
         !textType.Methods.Any(method => method.Name == "Repeat" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type == TypeSymbol.String && method.Parameters[1].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.String) ||
         !textType.Methods.Any(method => method.Name == "PadLeft" && method.IsStatic && method.Parameters.Count == 3 && method.Parameters[0].Type == TypeSymbol.String && method.Parameters[1].Type == TypeSymbol.Integer && method.Parameters[2].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.String) ||
         !textType.Methods.Any(method => method.Name == "PadRight" && method.IsStatic && method.Parameters.Count == 3 && method.Parameters[0].Type == TypeSymbol.String && method.Parameters[1].Type == TypeSymbol.Integer && method.Parameters[2].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.String) ||
         !textType.Methods.Any(method => method.Name == "Center" && method.IsStatic && method.Parameters.Count == 3 && method.Parameters[0].Type == TypeSymbol.String && method.Parameters[1].Type == TypeSymbol.Integer && method.Parameters[2].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.String) ||
         !textType.Methods.Any(method => method.Name == "StartsWithIgnoreCase" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters.All(parameter => parameter.Type == TypeSymbol.String) && method.ReturnType == TypeSymbol.Boolean) ||
         !textType.Methods.Any(method => method.Name == "EndsWithIgnoreCase" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters.All(parameter => parameter.Type == TypeSymbol.String) && method.ReturnType == TypeSymbol.Boolean) ||
         !textType.Methods.Any(method => method.Name == "ContainsIgnoreCase" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters.All(parameter => parameter.Type == TypeSymbol.String) && method.ReturnType == TypeSymbol.Boolean) ||
         !textType.Methods.Any(method => method.Name == "Split" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters.All(parameter => parameter.Type == TypeSymbol.String) && method.ReturnType.Name == "String[]") ||
         !textType.Methods.Any(method => method.Name == "Lines" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType.Name == "String[]"))
{
    failures.Add("Binder should expose the expected System.Text.Text helper signatures.");
}

var jsonKindType = binding.Compilation.Types.FirstOrDefault(type => type.Name == "JsonKind");
if (jsonKindType is null || jsonKindType.IsReferenceType)
{
    failures.Add("Binder should surface System.Text.Json.JsonKind as a non-reference enum type.");
}

var jsonValueType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "JsonValue");
if (jsonValueType is null || !jsonValueType.IsReferenceType)
{
    failures.Add("Binder should surface System.Text.Json.JsonValue as a reference type.");
}
else if (!jsonValueType.Properties.Any(property => property.Name == "Kind" && property.Type.Name == "JsonKind") ||
         !jsonValueType.Properties.Any(property => property.Name == "IsNull" && property.Type == TypeSymbol.Boolean))
{
    failures.Add("Binder should expose the expected System.Text.Json.JsonValue surface.");
}

var jsonObjectType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "JsonObject");
if (jsonObjectType is null || jsonObjectType.BaseType?.Name != "JsonValue")
{
    failures.Add("Binder should surface System.Text.Json.JsonObject as a JsonValue subtype.");
}
else if (!jsonObjectType.Properties.Any(property => property.Name == "Count" && property.Type == TypeSymbol.Integer) ||
         !jsonObjectType.Properties.Any(property => property.Name == "Item" && property.IsIndexer && property.Type.Name == "JsonValue" && property.IndexParameter?.Type == TypeSymbol.String) ||
         !jsonObjectType.Methods.Any(method => method.Name == "Add" && method.Parameters.Count == 2 && method.Parameters[0].Type == TypeSymbol.String && method.Parameters[1].Type.Name == "JsonValue") ||
         !jsonObjectType.Methods.Any(method => method.Name == "Contains" && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.Boolean) ||
         !jsonObjectType.Methods.Any(method => method.Name == "NameAt" && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.String) ||
         !jsonObjectType.Methods.Any(method => method.Name == "ValueAt" && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.Integer && method.ReturnType.Name == "JsonValue"))
{
    failures.Add("Binder should expose the expected System.Text.Json.JsonObject surface.");
}

var jsonArrayType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "JsonArray");
if (jsonArrayType is null || jsonArrayType.BaseType?.Name != "JsonValue")
{
    failures.Add("Binder should surface System.Text.Json.JsonArray as a JsonValue subtype.");
}
else if (!jsonArrayType.Properties.Any(property => property.Name == "Count" && property.Type == TypeSymbol.Integer) ||
         !jsonArrayType.Properties.Any(property => property.Name == "Item" && property.IsIndexer && property.Type.Name == "JsonValue" && property.IndexParameter?.Type == TypeSymbol.Integer) ||
         !jsonArrayType.Methods.Any(method => method.Name == "Add" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "JsonValue"))
{
    failures.Add("Binder should expose the expected System.Text.Json.JsonArray surface.");
}

var jsonType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Json");
if (jsonType is null)
{
    failures.Add("Binder should surface System.Text.Json.Json in the shipped library.");
}
else if (!jsonType.Methods.Any(method => method.Name == "Parse" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType.Name == "JsonValue"))
{
    failures.Add("Binder should expose the expected System.Text.Json.Json.Parse signature.");
}
else if (!jsonType.Methods.Any(method => method.Name == "Stringify" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "JsonValue" && method.ReturnType == TypeSymbol.String))
{
    failures.Add("Binder should expose the expected System.Text.Json.Json.Stringify signature.");
}

var uriType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Uri");
if (uriType is null || !uriType.IsReferenceType)
{
    failures.Add("Binder should surface System.Net.Uri as a reference type.");
}
else if (!uriType.Properties.Any(property => property.Name == "OriginalString" && property.Type == TypeSymbol.String) ||
         !uriType.Properties.Any(property => property.Name == "Scheme" && property.Type == TypeSymbol.String) ||
         !uriType.Properties.Any(property => property.Name == "Host" && property.Type == TypeSymbol.String) ||
         !uriType.Properties.Any(property => property.Name == "Path" && property.Type == TypeSymbol.String) ||
         !uriType.Properties.Any(property => property.Name == "Query" && property.Type == TypeSymbol.String) ||
         !uriType.Properties.Any(property => property.Name == "HasQuery" && property.Type == TypeSymbol.Boolean) ||
         !uriType.Properties.Any(property => property.Name == "Fragment" && property.Type == TypeSymbol.String) ||
         !uriType.Properties.Any(property => property.Name == "Authority" && property.Type == TypeSymbol.String) ||
         !uriType.Properties.Any(property => property.Name == "PathAndQuery" && property.Type == TypeSymbol.String) ||
         !uriType.Properties.Any(property => property.Name == "Port" && property.Type == TypeSymbol.Integer) ||
         !uriType.Properties.Any(property => property.Name == "IsAbsoluteUri" && property.Type == TypeSymbol.Boolean) ||
         !uriType.Methods.Any(method => method.Name == "Parse" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType.Name == "Uri") ||
         !uriType.Methods.Any(method => method.Name == "TryParse" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type == TypeSymbol.String && method.Parameters[1].PassingKind == ParameterPassingKind.Out && method.Parameters[1].Type.Name == "Uri" && method.ReturnType == TypeSymbol.Boolean) ||
         !uriType.Methods.Any(method => method.Name == "ContainsQueryParameter" && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.Boolean) ||
         !uriType.Methods.Any(method => method.Name == "GetQueryParameter" && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.String))
{
    failures.Add("Binder should expose the expected System.Net.Uri surface.");
}

var tcpClientType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "TcpClient");
if (tcpClientType is null || !tcpClientType.IsReferenceType)
{
    failures.Add("Binder should surface System.Net.TcpClient as a reference type.");
}
else if (!tcpClientType.Properties.Any(property => property.Name == "IsConnected" && property.Type == TypeSymbol.Boolean) ||
         !tcpClientType.Methods.Any(method => method.Name == "Connect" && !method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type == TypeSymbol.String && method.Parameters[1].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.Boolean) ||
         !tcpClientType.Methods.Any(method => method.Name == "ReadLine" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.String) ||
         !tcpClientType.Methods.Any(method => method.Name == "WriteLine" && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.Void) ||
         !tcpClientType.Methods.Any(method => method.Name == "Close" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Void) ||
         !tcpClientType.Methods.Any(method => method.Name == "ConnectCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.TcpConnect) ||
         !tcpClientType.Methods.Any(method => method.Name == "ReadLineCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.TcpReadLine) ||
         !tcpClientType.Methods.Any(method => method.Name == "WriteLineCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.TcpWriteLine) ||
         !tcpClientType.Methods.Any(method => method.Name == "CloseCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.TcpClose))
{
    failures.Add("Binder should expose the expected System.Net.TcpClient surface.");
}

var httpClientType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "HttpClient");
if (httpClientType is null || !httpClientType.IsReferenceType)
{
    failures.Add("Binder should surface System.Net.HttpClient as a reference type.");
}
else if (!httpClientType.Methods.Any(method => method.Name == "GetString" && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.String) ||
         !httpClientType.Methods.Any(method => method.Name == "GetStringCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.HttpGetString))
{
    failures.Add("Binder should expose the expected System.Net.HttpClient surface.");
}

var websocketClientType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "WebSocketClient");
if (websocketClientType is null || !websocketClientType.IsReferenceType)
{
    failures.Add("Binder should surface System.Net.WebSocketClient as a reference type.");
}
else if (!websocketClientType.Properties.Any(property => property.Name == "IsConnected" && property.Type == TypeSymbol.Boolean) ||
         !websocketClientType.Methods.Any(method => method.Name == "Connect" && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.Boolean) ||
         !websocketClientType.Methods.Any(method => method.Name == "ReceiveText" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.String) ||
         !websocketClientType.Methods.Any(method => method.Name == "SendText" && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.Void) ||
         !websocketClientType.Methods.Any(method => method.Name == "Close" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Void) ||
         !websocketClientType.Methods.Any(method => method.Name == "ConnectCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.WebSocketConnect) ||
         !websocketClientType.Methods.Any(method => method.Name == "ReceiveTextCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.WebSocketReceiveText) ||
         !websocketClientType.Methods.Any(method => method.Name == "SendTextCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.WebSocketSendText) ||
         !websocketClientType.Methods.Any(method => method.Name == "CloseCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.WebSocketClose))
{
    failures.Add("Binder should expose the expected System.Net.WebSocketClient surface.");
}

var threadType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Thread");
if (threadType is null || !threadType.IsReferenceType)
{
    failures.Add("Binder should surface System.Threading.Thread as a reference type.");
}
else if (!threadType.Properties.Any(property => property.Name == "CurrentManagedId" && property.Type == TypeSymbol.Integer) ||
         !threadType.Properties.Any(property => property.Name == "IsAlive" && property.Type == TypeSymbol.Boolean) ||
         !threadType.Methods.Any(method => method.Name == "Start" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IRunnable" && method.ReturnType.Name == "Thread") ||
         !threadType.Methods.Any(method => method.Name == "Join" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Void) ||
         !threadType.Methods.Any(method => method.Name == "Sleep" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.Void) ||
         !threadType.Methods.Any(method => method.Name == "SleepCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.ThreadSleep) ||
         !threadType.Methods.Any(method => method.Name == "GetCurrentManagedIdCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.ThreadGetCurrentManagedId) ||
         !threadType.Methods.Any(method => method.Name == "StartCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.ThreadStartRunnable) ||
         !threadType.Methods.Any(method => method.Name == "JoinCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.ThreadJoin) ||
         !threadType.Methods.Any(method => method.Name == "IsAliveCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.ThreadIsAlive))
{
    failures.Add("Binder should expose the expected System.Threading.Thread surface.");
}

var applicationType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Application");
var windowType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Window");
var dialogType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Dialog");
var colorType = binding.Compilation.Types.FirstOrDefault(type => type.Name == "Color");
var thicknessType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Thickness");
var styleType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Style");
var buttonType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Button");
var checkBoxType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "CheckBox");
var sliderType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Slider");
var textBoxType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "TextBox");
var contentControlType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "ContentControl");
var itemsControlType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "ItemsControl");
var listViewType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "ListView");
var gridType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Grid");
var borderType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Border");
var scrollViewerType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "ScrollViewer");
var menuItemType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "MenuItem");
var menuBarType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "MenuBar");
var panelType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Panel");
var stackPanelType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "StackPanel");
var pageType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Page");
var navigationHostType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "NavigationHost");
var commandSurfaceType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Command");
var observableObjectType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "ObservableObject");
var observableTextType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "ObservableText");
var textBindingType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "TextBinding");
var uiBackendType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "IUiBackend");
var windowHostType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "IWindowHost");
var viewHostType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "IViewHost");
var debugUiBackendType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "DebugUiBackend");
var qtQuickBackendType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "QtQuickBackend");
var qtQuickRenderModeType = binding.Compilation.Types.FirstOrDefault(type => type.Name == "QtQuickRenderMode");
var qtQuickNativeType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "QtQuickNative");
var qtQuickNativeViewHostType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "QtQuickNativeViewHost");
var qtQuickNativeWindowHostType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "QtQuickNativeWindowHost");
var dialogResultType = binding.Compilation.Types.FirstOrDefault(type => type.Name == "DialogResult");
var textBlockType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "TextBlock");
var viewType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "View");

if (applicationType is null || !applicationType.Methods.Any(method => method.Name == "Run" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Integer))
{
    failures.Add("Binder should surface System.Ui.Application with a minimal Run() entry point.");
}

if (colorType is null || colorType.IsReferenceType)
{
    failures.Add("Binder should surface System.Ui.Color as a value enum.");
}

if (thicknessType is null || !thicknessType.IsReferenceType || !thicknessType.Properties.Any(property => property.Name == "Left" && property.Type == TypeSymbol.Integer) || !thicknessType.Properties.Any(property => property.Name == "Top" && property.Type == TypeSymbol.Integer) || !thicknessType.Properties.Any(property => property.Name == "Right" && property.Type == TypeSymbol.Integer) || !thicknessType.Properties.Any(property => property.Name == "Bottom" && property.Type == TypeSymbol.Integer) || !thicknessType.Properties.Any(property => property.Name == "Horizontal" && property.Type == TypeSymbol.Integer) || !thicknessType.Properties.Any(property => property.Name == "Vertical" && property.Type == TypeSymbol.Integer))
{
    failures.Add("Binder should surface System.Ui.Thickness with edge and aggregate members.");
}

if (styleType is null || !styleType.IsReferenceType || !styleType.Properties.Any(property => property.Name == "Foreground" && property.Type.Name == "Color") || !styleType.Properties.Any(property => property.Name == "Background" && property.Type.Name == "Color") || !styleType.Properties.Any(property => property.Name == "Margin" && property.Type.Name == "Thickness") || !styleType.Properties.Any(property => property.Name == "Padding" && property.Type.Name == "Thickness") || !styleType.Methods.Any(method => method.Name == "ApplyToTextBlock" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "TextBlock" && method.ReturnType == TypeSymbol.Void) || !styleType.Methods.Any(method => method.Name == "ApplyToTextBox" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "TextBox" && method.ReturnType == TypeSymbol.Void) || !styleType.Methods.Any(method => method.Name == "ApplyToBorder" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "Border" && method.ReturnType == TypeSymbol.Void) || !styleType.Methods.Any(method => method.Name == "ApplyToWindow" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "Window" && method.ReturnType == TypeSymbol.Void))
{
    failures.Add("Binder should surface System.Ui.Style with shared visual properties and apply helpers.");
}

if (viewType is null || !viewType.Properties.Any(property => property.Name == "Margin" && property.Type.Name == "Thickness"))
{
    failures.Add("Binder should surface System.Ui.View.Margin for the spacing slice.");
}

if (viewType is null || !viewType.Properties.Any(property => property.Name == "DataContext" && property.Type == TypeSymbol.Object) || !viewType.Properties.Any(property => property.Name == "HasDataContext" && property.Type == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.View.DataContext and HasDataContext for the data-context slice.");
}

if (contentControlType is null || contentControlType.BaseType?.Name != "View" || !contentControlType.Properties.Any(property => property.Name == "Content" && property.Type.Name == "View") || !contentControlType.Properties.Any(property => property.Name == "HasContent" && property.Type == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.ContentControl as the shared single-content base type.");
}

if (panelType is null || panelType.BaseType?.Name != "Container")
{
    failures.Add("Binder should surface System.Ui.Panel as the shared multi-child base type.");
}

if (windowType is null || !windowType.Properties.Any(property => property.Name == "Title" && property.Type == TypeSymbol.String) || !windowType.Properties.Any(property => property.Name == "Content" && property.Type.Name == "View") || !windowType.Properties.Any(property => property.Name == "Width" && property.Type == TypeSymbol.Integer) || !windowType.Properties.Any(property => property.Name == "Height" && property.Type == TypeSymbol.Integer) || !windowType.Properties.Any(property => property.Name == "MinWidth" && property.Type == TypeSymbol.Integer) || !windowType.Properties.Any(property => property.Name == "MinHeight" && property.Type == TypeSymbol.Integer) || !windowType.Methods.Any(method => method.Name == "Resize" && method.Parameters.Count == 2 && method.Parameters[0].Type == TypeSymbol.Integer && method.Parameters[1].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.Void) || !windowType.Methods.Any(method => method.Name == "SetMinimumSize" && method.Parameters.Count == 2 && method.Parameters[0].Type == TypeSymbol.Integer && method.Parameters[1].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.Void))
{
    failures.Add("Binder should surface System.Ui.Window with Title, Content and minimal sizing members.");
}

if (windowType is null || !windowType.Properties.Any(property => property.Name == "Background" && property.Type.Name == "Color"))
{
    failures.Add("Binder should surface System.Ui.Window.Background for the styling slice.");
}

if (dialogResultType is null || dialogResultType.IsReferenceType)
{
    failures.Add("Binder should surface System.Ui.DialogResult as a value enum.");
}

if (dialogType is null || dialogType.BaseType?.Name != "Window" || !dialogType.Properties.Any(property => property.Name == "Message" && property.Type == TypeSymbol.String) || !dialogType.Properties.Any(property => property.Name == "Result" && property.Type.Name == "DialogResult") || !dialogType.Properties.Any(property => property.Name == "IsModal" && property.Type.Name == "Boolean") || !dialogType.Methods.Any(method => method.Name == "ShowDialog" && method.Parameters.Count == 0 && method.ReturnType.Name == "DialogResult") || !dialogType.Methods.Any(method => method.Name == "Accept" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Void) || !dialogType.Methods.Any(method => method.Name == "Cancel" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Void))
{
    failures.Add("Binder should surface System.Ui.Dialog with Result and modal control members.");
}

if (textBlockType is null || !textBlockType.Properties.Any(property => property.Name == "Foreground" && property.Type.Name == "Color"))
{
    failures.Add("Binder should surface System.Ui.TextBlock.Foreground for the styling slice.");
}

if (buttonType is null || !buttonType.Properties.Any(property => property.Name == "Command" && property.Type.Name == "ICommand") || !buttonType.Properties.Any(property => property.Name == "Background" && property.Type.Name == "Color") || !buttonType.Methods.Any(method => method.Name == "Click" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.Button with Command, Background and Click().");
}

if (checkBoxType is null || !checkBoxType.Properties.Any(property => property.Name == "Text" && property.Type == TypeSymbol.String) || !checkBoxType.Properties.Any(property => property.Name == "IsChecked" && property.Type == TypeSymbol.Boolean) || !checkBoxType.Properties.Any(property => property.Name == "ToggledCommand" && property.Type.Name == "ICommand") || !checkBoxType.Properties.Any(property => property.Name == "CanNotifyToggled" && property.Type == TypeSymbol.Boolean) || !checkBoxType.Methods.Any(method => method.Name == "Toggle" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Boolean) || !checkBoxType.Methods.Any(method => method.Name == "NotifyToggled" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.CheckBox with toggle state, command hook and notification helpers.");
}

if (sliderType is null || !sliderType.Properties.Any(property => property.Name == "Minimum" && property.Type == TypeSymbol.Integer) || !sliderType.Properties.Any(property => property.Name == "Maximum" && property.Type == TypeSymbol.Integer) || !sliderType.Properties.Any(property => property.Name == "Value" && property.Type == TypeSymbol.Integer) || !sliderType.Properties.Any(property => property.Name == "ValueChangedCommand" && property.Type.Name == "ICommand") || !sliderType.Properties.Any(property => property.Name == "CanNotifyValueChanged" && property.Type == TypeSymbol.Boolean) || !sliderType.Methods.Any(method => method.Name == "SetRange" && method.Parameters.Count == 2 && method.Parameters[0].Type == TypeSymbol.Integer && method.Parameters[1].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.Void) || !sliderType.Methods.Any(method => method.Name == "NotifyValueChanged" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.Slider with range, value and notification members.");
}

if (textBoxType is null || !textBoxType.Properties.Any(property => property.Name == "Text" && property.Type == TypeSymbol.String) || !textBoxType.Properties.Any(property => property.Name == "PlaceholderText" && property.Type == TypeSymbol.String) || !textBoxType.Properties.Any(property => property.Name == "IsReadOnly" && property.Type == TypeSymbol.Boolean) || !textBoxType.Properties.Any(property => property.Name == "TextChangedCommand" && property.Type.Name == "ICommand") || !textBoxType.Properties.Any(property => property.Name == "CanNotifyTextChanged" && property.Type == TypeSymbol.Boolean) || !textBoxType.Methods.Any(method => method.Name == "NotifyTextChanged" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.TextBox with the expected text editing and change notification properties.");
}

if (textBoxType is null || !textBoxType.Properties.Any(property => property.Name == "Padding" && property.Type.Name == "Thickness"))
{
    failures.Add("Binder should surface System.Ui.TextBox.Padding for the spacing slice.");
}

if (itemsControlType is null || itemsControlType.GenericArity != 1 || itemsControlType.BaseType?.Name != "View" || !itemsControlType.Properties.Any(property => property.Name == "ItemsSource" && property.Type.Name == "IEnumerable<T>") || !itemsControlType.Properties.Any(property => property.Name == "ItemTemplate" && property.Type.Name == "Selector<T, View>") || !itemsControlType.Properties.Any(property => property.Name == "ItemCount" && property.Type == TypeSymbol.Integer) || !itemsControlType.Properties.Any(property => property.Name == "HasItems" && property.Type == TypeSymbol.Boolean) || !itemsControlType.Properties.Any(property => property.Name == "HasItemTemplate" && property.Type == TypeSymbol.Boolean) || !itemsControlType.Methods.Any(method => method.Name == "BuildItems" && method.Parameters.Count == 0 && method.ReturnType.Name == "List<View>"))
{
    failures.Add("Binder should surface System.Ui.ItemsControl<T> as the common items and template base type.");
}

if (listViewType is null || listViewType.GenericArity != 1 || !listViewType.Properties.Any(property => property.Name == "SelectedIndex" && property.Type == TypeSymbol.Integer) || !listViewType.Properties.Any(property => property.Name == "HasSelection" && property.Type == TypeSymbol.Boolean) || !listViewType.Properties.Any(property => property.Name == "SelectedItem" && property.Type.Name == "T") || !listViewType.Methods.Any(method => method.Name == "SelectIndex" && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.Boolean) || !listViewType.Methods.Any(method => method.Name == "ClearSelection" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Void))
{
    failures.Add("Binder should surface System.Ui.ListView<T> with the expected selection members on top of ItemsControl<T>.");
}

if (gridType is null || gridType.BaseType?.Name != "Panel" || !gridType.Properties.Any(property => property.Name == "Rows" && property.Type == TypeSymbol.Integer) || !gridType.Properties.Any(property => property.Name == "Columns" && property.Type == TypeSymbol.Integer) || !gridType.Properties.Any(property => property.Name == "CellCount" && property.Type == TypeSymbol.Integer) || !gridType.Properties.Any(property => property.Name == "ShowGridLines" && property.Type == TypeSymbol.Boolean) || !gridType.Methods.Any(method => method.Name == "SetDimensions" && method.Parameters.Count == 2 && method.Parameters[0].Type == TypeSymbol.Integer && method.Parameters[1].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.Void))
{
    failures.Add("Binder should surface System.Ui.Grid with basic dimension and cell-count members.");
}

if (borderType is null || borderType.BaseType?.Name != "ContentControl" || !borderType.Properties.Any(property => property.Name == "Child" && property.Type.Name == "View") || !borderType.Properties.Any(property => property.Name == "BorderThickness" && property.Type == TypeSymbol.Integer) || !borderType.Properties.Any(property => property.Name == "HasChild" && property.Type == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.Border with child and thickness members.");
}

if (borderType is null || !borderType.Properties.Any(property => property.Name == "Background" && property.Type.Name == "Color"))
{
    failures.Add("Binder should surface System.Ui.Border.Background for the styling slice.");
}

if (borderType is null || !borderType.Properties.Any(property => property.Name == "Padding" && property.Type.Name == "Thickness"))
{
    failures.Add("Binder should surface System.Ui.Border.Padding for the spacing slice.");
}

if (scrollViewerType is null || scrollViewerType.BaseType?.Name != "ContentControl" || !scrollViewerType.Properties.Any(property => property.Name == "HorizontalOffset" && property.Type == TypeSymbol.Integer) || !scrollViewerType.Properties.Any(property => property.Name == "VerticalOffset" && property.Type == TypeSymbol.Integer) || !scrollViewerType.Properties.Any(property => property.Name == "ViewportWidth" && property.Type == TypeSymbol.Integer) || !scrollViewerType.Properties.Any(property => property.Name == "ViewportHeight" && property.Type == TypeSymbol.Integer) || !scrollViewerType.Properties.Any(property => property.Name == "CanScroll" && property.Type == TypeSymbol.Boolean) || !scrollViewerType.Methods.Any(method => method.Name == "ScrollTo" && method.Parameters.Count == 2 && method.Parameters[0].Type == TypeSymbol.Integer && method.Parameters[1].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.Void))
{
    failures.Add("Binder should surface System.Ui.ScrollViewer with viewport and offset members.");
}

if (menuItemType is null || menuItemType.BaseType?.Name != "View" || !menuItemType.Properties.Any(property => property.Name == "Header" && property.Type == TypeSymbol.String) || !menuItemType.Properties.Any(property => property.Name == "Command" && property.Type.Name == "ICommand") || !menuItemType.Properties.Any(property => property.Name == "Items" && property.Type.Name == "List<MenuItem>") || !menuItemType.Properties.Any(property => property.Name == "ItemCount" && property.Type == TypeSymbol.Integer) || !menuItemType.Properties.Any(property => property.Name == "CanExecute" && property.Type == TypeSymbol.Boolean) || !menuItemType.Methods.Any(method => method.Name == "AddItem" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "MenuItem" && method.ReturnType == TypeSymbol.Void) || !menuItemType.Methods.Any(method => method.Name == "Invoke" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.MenuItem with header, child items and invoke support.");
}

if (menuBarType is null || menuBarType.BaseType?.Name != "View" || !menuBarType.Properties.Any(property => property.Name == "Items" && property.Type.Name == "List<MenuItem>") || !menuBarType.Properties.Any(property => property.Name == "ItemCount" && property.Type == TypeSymbol.Integer) || !menuBarType.Methods.Any(method => method.Name == "AddItem" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "MenuItem" && method.ReturnType == TypeSymbol.Void))
{
    failures.Add("Binder should surface System.Ui.MenuBar with top-level menu item support.");
}

if (stackPanelType is null || stackPanelType.BaseType?.Name != "Panel")
{
    failures.Add("Binder should surface System.Ui.StackPanel as a Container subtype.");
}

if (pageType is null || pageType.BaseType?.Name != "ContentControl" || !pageType.Properties.Any(property => property.Name == "Title" && property.Type == TypeSymbol.String))
{
    failures.Add("Binder should surface System.Ui.Page as a container with Title and Content.");
}

if (navigationHostType is null || navigationHostType.BaseType?.Name != "View" || !navigationHostType.Properties.Any(property => property.Name == "CurrentPage" && property.Type.Name == "Page") || !navigationHostType.Properties.Any(property => property.Name == "HasPage" && property.Type == TypeSymbol.Boolean) || !navigationHostType.Properties.Any(property => property.Name == "CanGoBack" && property.Type == TypeSymbol.Boolean) || !navigationHostType.Methods.Any(method => method.Name == "Navigate" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "Page" && method.ReturnType == TypeSymbol.Boolean) || !navigationHostType.Methods.Any(method => method.Name == "GoBack" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.NavigationHost with a minimal navigation history API.");
}

if (commandSurfaceType is null || !commandSurfaceType.Methods.Any(method => method.Name == "Execute" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Void) || !commandSurfaceType.Methods.Any(method => method.Name == "CanExecute" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.Command with Execute and CanExecute.");
}

if (observableObjectType is null || !observableObjectType.Properties.Any(property => property.Name == "ChangeVersion" && property.Type == TypeSymbol.Integer) || !observableObjectType.Properties.Any(property => property.Name == "HasChanges" && property.Type == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.ObservableObject with minimal change tracking.");
}

if (observableTextType is null || observableTextType.BaseType?.Name != "ObservableObject" || !observableTextType.Properties.Any(property => property.Name == "Text" && property.Type == TypeSymbol.String))
{
    failures.Add("Binder should surface System.Ui.ObservableText as a simple observable text model.");
}

if (textBindingType is null || !textBindingType.Properties.Any(property => property.Name == "IsDirty" && property.Type == TypeSymbol.Boolean) || !textBindingType.Methods.Any(method => method.Name == "Apply" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "TextBlock") || !textBindingType.Methods.Any(method => method.Name == "ApplyToTextBox" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "TextBox"))
{
    failures.Add("Binder should surface System.Ui.TextBinding with the expected first binding operations.");
}

if (viewHostType is null || !viewHostType.IsInterface || !viewHostType.Properties.Any(property => property.Name == "View" && property.Type.Name == "View") || !viewHostType.Methods.Any(method => method.Name == "Describe" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.String))
{
    failures.Add("Binder should surface System.Ui.Hosting.IViewHost with view inspection members.");
}

if (windowHostType is null || !windowHostType.IsInterface || !windowHostType.Properties.Any(property => property.Name == "Window" && property.Type.Name == "Window") || !windowHostType.Properties.Any(property => property.Name == "RootViewHost" && property.Type.Name == "IViewHost") || !windowHostType.Methods.Any(method => method.Name == "Show" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.Hosting.IWindowHost with window hosting members.");
}

if (uiBackendType is null || !uiBackendType.IsInterface || !uiBackendType.Methods.Any(method => method.Name == "CreateWindowHost" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "Window" && method.ReturnType.Name == "IWindowHost") || !uiBackendType.Methods.Any(method => method.Name == "Run" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "Application" && method.ReturnType == TypeSymbol.Integer))
{
    failures.Add("Binder should surface System.Ui.Hosting.IUiBackend with backend bootstrap members.");
}

if (debugUiBackendType is null || !debugUiBackendType.IsReferenceType || !debugUiBackendType.Properties.Any(property => property.Name == "HostedWindowCount" && property.Type == TypeSymbol.Integer) || !debugUiBackendType.Properties.Any(property => property.Name == "LastRootDescription" && property.Type == TypeSymbol.String) || !debugUiBackendType.Methods.Any(method => method.Name == "CreateWindowHost" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "Window" && method.ReturnType.Name == "IWindowHost") || !debugUiBackendType.Methods.Any(method => method.Name == "Run" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "Application" && method.ReturnType == TypeSymbol.Integer))
{
    failures.Add("Binder should surface System.Ui.Hosting.DebugUiBackend as the first managed backend adapter.");
}

if (qtQuickRenderModeType is null || qtQuickRenderModeType.IsReferenceType)
{
    failures.Add("Binder should surface System.Ui.Backends.QtQuick.QtQuickRenderMode as a value enum.");
}

if (qtQuickBackendType is null || !qtQuickBackendType.IsReferenceType || !qtQuickBackendType.Properties.Any(property => property.Name == "BackendName" && property.Type == TypeSymbol.String) || !qtQuickBackendType.Properties.Any(property => property.Name == "RenderMode" && property.Type.Name == "QtQuickRenderMode") || !qtQuickBackendType.Properties.Any(property => property.Name == "IsAvailable" && property.Type == TypeSymbol.Boolean) || !qtQuickBackendType.Properties.Any(property => property.Name == "LastHostedWindowTitle" && property.Type == TypeSymbol.String) || !qtQuickBackendType.Properties.Any(property => property.Name == "LastHostedRootDescription" && property.Type == TypeSymbol.String) || !qtQuickBackendType.Methods.Any(method => method.Name == "InitializeEngine" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Boolean) || !qtQuickBackendType.Methods.Any(method => method.Name == "CreateWindowHost" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "Window" && method.ReturnType.Name == "IWindowHost") || !qtQuickBackendType.Methods.Any(method => method.Name == "Run" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "Application" && method.ReturnType == TypeSymbol.Integer))
{
    failures.Add("Binder should surface System.Ui.Backends.QtQuick.QtQuickBackend as the first Qt hosting adapter.");
}

if (qtQuickNativeType is null || !qtQuickNativeType.IsReferenceType || !qtQuickNativeType.Methods.Any(method => method.Name == "CreateBackend" && method.IsStatic && method.Parameters.Count == 0 && method.ReturnType.Name == "NativeHandle") || !qtQuickNativeType.Methods.Any(method => method.Name == "CreateWindow" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "NativeHandle" && method.Parameters[1].Type == TypeSymbol.String && method.ReturnType.Name == "NativeHandle") || !qtQuickNativeType.Methods.Any(method => method.Name == "SetWindowSize" && method.IsStatic && method.Parameters.Count == 3 && method.Parameters[0].Type.Name == "NativeHandle" && method.Parameters[1].Type == TypeSymbol.Integer && method.Parameters[2].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.Integer) || !qtQuickNativeType.Methods.Any(method => method.Name == "SetWindowMinimumSize" && method.IsStatic && method.Parameters.Count == 3 && method.Parameters[0].Type.Name == "NativeHandle" && method.Parameters[1].Type == TypeSymbol.Integer && method.Parameters[2].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.Integer) || !qtQuickNativeType.Methods.Any(method => method.Name == "SetWindowBackground" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "NativeHandle" && method.Parameters[1].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.Integer) || !qtQuickNativeType.Methods.Any(method => method.Name == "SetElementText" && method.IsStatic && method.Parameters.Count == 3 && method.Parameters[0].Type.Name == "NativeHandle" && method.Parameters[1].Type == TypeSymbol.String && method.Parameters[2].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.Integer) || !qtQuickNativeType.Methods.Any(method => method.Name == "SetElementColor" && method.IsStatic && method.Parameters.Count == 3 && method.Parameters[0].Type.Name == "NativeHandle" && method.Parameters[1].Type == TypeSymbol.String && method.Parameters[2].Type == TypeSymbol.String && method.ReturnType == TypeSymbol.Integer) || !qtQuickNativeType.Methods.Any(method => method.Name == "RunBackend" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "NativeHandle" && method.ReturnType == TypeSymbol.Integer) || !qtQuickNativeType.Methods.Any(method => method.Name == "ReadTextInputValue" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "NativeHandle" && method.Parameters[1].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.String) || !qtQuickNativeType.Methods.Any(method => method.Name == "ReadSliderValue" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "NativeHandle" && method.Parameters[1].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.Integer) || !qtQuickNativeType.Methods.Any(method => method.Name == "FocusTextInput" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "NativeHandle" && method.Parameters[1].Type == TypeSymbol.Integer && method.ReturnType == TypeSymbol.Integer))
{
    failures.Add("Binder should surface System.Ui.Backends.QtQuick.QtQuickNative as the native DllImport shim contract, including window sizing, minimum window sizing, incremental text/color patching, text-input, slider readback and text-focus restore.");
}
else
{
    var createBackendMethod = qtQuickNativeType.Methods.First(method => method.Name == "CreateBackend");
    if (createBackendMethod.DllImport is null || createBackendMethod.DllImport.LibraryName != "libilc_qtbridge.so" || createBackendMethod.DllImport.EntryPoint != "ilc_qtquick_backend_create")
    {
        failures.Add("Binder should preserve the QtQuick native shim DllImport metadata.");
    }

    var readTextInputValueMethod = qtQuickNativeType.Methods.FirstOrDefault(method => method.Name == "ReadTextInputValue");
    if (readTextInputValueMethod?.DllImport is null ||
        readTextInputValueMethod.DllImport.EntryPoint != "ilc_qtquick_backend_read_text_input_value" ||
        readTextInputValueMethod.DllImport.StringReturnMarshalling != NativeStringReturnMarshalling.Utf8Owned ||
        readTextInputValueMethod.DllImport.StringFreeEntryPoint != "ilc_qtquick_string_free")
    {
        failures.Add("Binder should preserve owned UTF-8 DllImport metadata for QtQuick text-input readback.");
    }
}

if (qtQuickNativeViewHostType is null || !qtQuickNativeViewHostType.IsReferenceType || !qtQuickNativeViewHostType.Properties.Any(property => property.Name == "NativeViewHandle" && property.Type.Name == "NativeHandle") || !qtQuickNativeViewHostType.Properties.Any(property => property.Name == "HasNativeViewHandle" && property.Type == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.Backends.QtQuick.QtQuickNativeViewHost with native handle metadata.");
}

if (qtQuickNativeWindowHostType is null || !qtQuickNativeWindowHostType.IsReferenceType || !qtQuickNativeWindowHostType.Properties.Any(property => property.Name == "NativeWindowHandle" && property.Type.Name == "NativeHandle") || !qtQuickNativeWindowHostType.Properties.Any(property => property.Name == "HasNativeWindowHandle" && property.Type == TypeSymbol.Boolean))
{
    failures.Add("Binder should surface System.Ui.Backends.QtQuick.QtQuickNativeWindowHost with native handle metadata.");
}

var runnableType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "IRunnable");
if (runnableType is null || !runnableType.IsInterface)
{
    failures.Add("Binder should surface System.Threading.IRunnable as an interface type.");
}
else if (!runnableType.Methods.Any(method => method.Name == "Run" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Void))
{
    failures.Add("Binder should expose the expected System.Threading.IRunnable surface.");
}

var taskRunnableType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "ITaskRunnable" && type.GenericArity == 1);
if (taskRunnableType is null || !taskRunnableType.IsInterface || taskRunnableType.GenericParameters?.Count != 1 || taskRunnableType.GenericParameters[0].Name != "T")
{
    failures.Add("Binder should surface System.Threading.ITaskRunnable<T> as an interface type.");
}
else if (!taskRunnableType.Methods.Any(method => method.Name == "Run" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType.Name == "T"))
{
    failures.Add("Binder should expose the expected System.Threading.ITaskRunnable<T> surface.");
}

var taskResultSinkType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "ITaskResultSink" && type.GenericArity == 1);
if (taskResultSinkType is null || !taskResultSinkType.IsInterface || taskResultSinkType.GenericParameters?.Count != 1 || taskResultSinkType.GenericParameters[0].Name != "T")
{
    failures.Add("Binder should surface System.Threading.ITaskResultSink<T> as an interface type.");
}
else if (!taskResultSinkType.Methods.Any(method => method.Name == "Complete" && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "T") ||
         !taskResultSinkType.Methods.Any(method => method.Name == "Fault" && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String))
{
    failures.Add("Binder should expose the expected System.Threading.ITaskResultSink<T> surface.");
}

var taskType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Task" && type.GenericArity == 0);
if (taskType is null || !taskType.IsReferenceType)
{
    failures.Add("Binder should surface System.Threading.Task as a reference type.");
}
else if (!taskType.Properties.Any(property => property.Name == "IsCompleted" && property.Type == TypeSymbol.Boolean) ||
         !taskType.Properties.Any(property => property.Name == "IsFaulted" && property.Type == TypeSymbol.Boolean) ||
         !taskType.Properties.Any(property => property.Name == "ErrorMessage" && property.Type == TypeSymbol.String) ||
         !taskType.Methods.Any(method => method.Name == "Run" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IRunnable" && method.ReturnType.Name == "Task") ||
         !taskType.Methods.Any(method => method.Name == "Wait" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Void))
{
    failures.Add("Binder should expose the expected System.Threading.Task surface.");
}

var genericTaskType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Task" && type.GenericArity == 1);
if (genericTaskType is null || !genericTaskType.IsReferenceType || genericTaskType.GenericParameters?.Count != 1 || genericTaskType.GenericParameters[0].Name != "T")
{
    failures.Add("Binder should surface System.Threading.Task<T> as a generic reference type.");
}
else if (!genericTaskType.InterfaceTypes.Any(type => type.Name == "ITaskResultSink<T>"))
{
    failures.Add("Binder should model System.Threading.Task<T> as an ITaskResultSink<T> implementation.");
}
else if (!genericTaskType.Properties.Any(property => property.Name == "IsCompleted" && property.Type == TypeSymbol.Boolean) ||
         !genericTaskType.Properties.Any(property => property.Name == "IsFaulted" && property.Type == TypeSymbol.Boolean) ||
         !genericTaskType.Properties.Any(property => property.Name == "ErrorMessage" && property.Type == TypeSymbol.String) ||
         !genericTaskType.Properties.Any(property => property.Name == "Result" && property.Type.Name == "T") ||
         !genericTaskType.Methods.Any(method => method.IsConstructor && method.Parameters.Count == 0) ||
         !genericTaskType.Methods.Any(method => method.IsConstructor && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "ITaskRunnable<T>") ||
         !genericTaskType.Methods.Any(method => method.Name == "Wait" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Void) ||
         !genericTaskType.Methods.Any(method => method.Name == "Complete" && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "T") ||
         !genericTaskType.Methods.Any(method => method.Name == "Fault" && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type == TypeSymbol.String))
{
    failures.Add("Binder should expose the expected System.Threading.Task<T> surface.");
}

var mutexType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Mutex");
if (mutexType is null || !mutexType.IsReferenceType)
{
    failures.Add("Binder should surface System.Threading.Mutex as a reference type.");
}
else if (!mutexType.Properties.Any(property => property.Name == "IsValid" && property.Type == TypeSymbol.Boolean) ||
         !mutexType.Methods.Any(method => method.Name == "WaitOne" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Boolean) ||
         !mutexType.Methods.Any(method => method.Name == "Release" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Void) ||
         !mutexType.Methods.Any(method => method.Name == "Close" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Void) ||
         !mutexType.Methods.Any(method => method.Name == "CreateCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.MutexCreate) ||
         !mutexType.Methods.Any(method => method.Name == "WaitOneCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.MutexWaitOne) ||
         !mutexType.Methods.Any(method => method.Name == "ReleaseCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.MutexRelease) ||
         !mutexType.Methods.Any(method => method.Name == "CloseCore" && method.IsStatic && method.IsExtern && method.HostImportKind == HostImportKind.MutexClose))
{
    failures.Add("Binder should expose the expected System.Threading.Mutex surface.");
}

var stringListType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "StringList");
if (stringListType is null || !stringListType.IsReferenceType)
{
    failures.Add("Binder should surface System.Collections.StringList as a reference type.");
}
else if (stringListType.BaseType?.Name != "List<String>")
{
    failures.Add("Binder should model System.Collections.StringList as a thin subclass of List<String>.");
}

var listInterfaceType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "IList");
if (listInterfaceType is null || !listInterfaceType.IsInterface || listInterfaceType.GenericArity != 1 || listInterfaceType.GenericParameters?.Count != 1 || listInterfaceType.GenericParameters[0].Name != "T")
{
    failures.Add("Binder should surface System.Collections.IList<T> as a generic interface definition.");
}
else if (!listInterfaceType.InterfaceTypes.Any(type => type.Name == "IReadOnlyList<T>"))
{
    failures.Add("Binder should model System.Collections.IList<T> as an IReadOnlyList<T> implementation.");
}
else if (!listInterfaceType.InterfaceTypes.Any(type => type.Name == "ICollection<T>"))
{
    failures.Add("Binder should model System.Collections.IList<T> as an ICollection<T> implementation.");
}
else if (!listInterfaceType.Methods.Any(method => method.Name == "AddRange" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "T[]") ||
         !listInterfaceType.Methods.Any(method => method.Name == "IndexOf" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "T" && method.ReturnType == TypeSymbol.Integer) ||
         !listInterfaceType.Methods.Any(method => method.Name == "ToArray" && method.Parameters.Count == 0 && method.ReturnType.Name == "T[]"))
{
    failures.Add("Binder should expose the expected System.Collections.IList<T> generic surface.");
}

var readOnlyListInterfaceType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "IReadOnlyList");
if (readOnlyListInterfaceType is null || !readOnlyListInterfaceType.IsInterface || readOnlyListInterfaceType.GenericArity != 1 || readOnlyListInterfaceType.GenericParameters?.Count != 1 || readOnlyListInterfaceType.GenericParameters[0].Name != "T")
{
    failures.Add("Binder should surface System.Collections.IReadOnlyList<T> as a generic interface definition.");
}
else if (!readOnlyListInterfaceType.Properties.Any(property => property.Name == "Count" && property.Type == TypeSymbol.Integer) ||
         !readOnlyListInterfaceType.Properties.Any(property => property.Name == "Item" && property.IsIndexer && property.Type.Name == "T" && property.SetterMethod is null && property.WriteField is null))
{
    failures.Add("Binder should expose the expected System.Collections.IReadOnlyList<T> generic surface.");
}

var enumeratorInterfaceType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "IEnumerator");
if (enumeratorInterfaceType is null || !enumeratorInterfaceType.IsInterface || enumeratorInterfaceType.GenericArity != 1 || enumeratorInterfaceType.GenericParameters?.Count != 1 || enumeratorInterfaceType.GenericParameters[0].Name != "T")
{
    failures.Add("Binder should surface System.Collections.IEnumerator<T> as a generic interface definition.");
}
else if (!enumeratorInterfaceType.Properties.Any(property => property.Name == "Current" && property.Type.Name == "T" && property.SetterMethod is null && property.WriteField is null) ||
         !enumeratorInterfaceType.Methods.Any(method => method.Name == "MoveNext" && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Boolean) ||
         !enumeratorInterfaceType.Methods.Any(method => method.Name == "Reset" && method.Parameters.Count == 0))
{
    failures.Add("Binder should expose the expected System.Collections.IEnumerator<T> generic surface.");
}

var enumerableInterfaceType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "IEnumerable");
if (enumerableInterfaceType is null || !enumerableInterfaceType.IsInterface || enumerableInterfaceType.GenericArity != 1 || enumerableInterfaceType.GenericParameters?.Count != 1 || enumerableInterfaceType.GenericParameters[0].Name != "T")
{
    failures.Add("Binder should surface System.Collections.IEnumerable<T> as a generic interface definition.");
}
else if (!enumerableInterfaceType.Methods.Any(method => method.Name == "GetEnumerator" && method.Parameters.Count == 0 && method.ReturnType.Name == "IEnumerator<T>"))
{
    failures.Add("Binder should expose the expected System.Collections.IEnumerable<T> generic surface.");
}

var collectionInterfaceType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "ICollection");
if (collectionInterfaceType is null || !collectionInterfaceType.IsInterface || collectionInterfaceType.GenericArity != 1 || collectionInterfaceType.GenericParameters?.Count != 1 || collectionInterfaceType.GenericParameters[0].Name != "T")
{
    failures.Add("Binder should surface System.Collections.ICollection<T> as a generic interface definition.");
}
else if (!collectionInterfaceType.InterfaceTypes.Any(type => type.Name == "IEnumerable<T>"))
{
    failures.Add("Binder should model System.Collections.ICollection<T> as an IEnumerable<T> implementation.");
}
else if (!collectionInterfaceType.Properties.Any(property => property.Name == "Count" && property.Type == TypeSymbol.Integer) ||
         !collectionInterfaceType.Methods.Any(method => method.Name == "Add" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "T") ||
         !collectionInterfaceType.Methods.Any(method => method.Name == "Clear" && method.Parameters.Count == 0) ||
         !collectionInterfaceType.Methods.Any(method => method.Name == "Contains" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "T" && method.ReturnType == TypeSymbol.Boolean))
{
    failures.Add("Binder should expose the expected System.Collections.ICollection<T> generic surface.");
}

var listDefinitionType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "List");
if (listDefinitionType is null || !listDefinitionType.IsReferenceType || listDefinitionType.GenericArity != 1 || listDefinitionType.GenericParameters?.Count != 1 || listDefinitionType.GenericParameters[0].Name != "T")
{
    failures.Add("Binder should surface System.Collections.List<T> as a generic reference type definition.");
}
else if (!listDefinitionType.InterfaceTypes.Any(type => type.Name == "IList<T>"))
{
    failures.Add("Binder should model System.Collections.List<T> as an IList<T> implementation.");
}
else if (!listDefinitionType.Methods.Any(method => method.IsConstructor) ||
         !listDefinitionType.Methods.Any(method => method.Name == "GetEnumerator" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType.Name == "IEnumerator<T>") ||
         !listDefinitionType.Methods.Any(method => method.Name == "Add" && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "T") ||
         !listDefinitionType.Methods.Any(method => method.Name == "AddRange" && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "T[]") ||
         !listDefinitionType.Methods.Any(method => method.Name == "Contains" && !method.IsStatic && method.ReturnType == TypeSymbol.Boolean && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "T") ||
         !listDefinitionType.Methods.Any(method => method.Name == "IndexOf" && !method.IsStatic && method.ReturnType == TypeSymbol.Integer && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "T") ||
         !listDefinitionType.Methods.Any(method => method.Name == "ToArray" && !method.IsStatic && method.ReturnType.Name == "T[]") ||
         !listDefinitionType.Properties.Any(property => property.Name == "Count" && !property.IsStatic && property.Type == TypeSymbol.Integer) ||
         !listDefinitionType.Properties.Any(property => property.Name == "Item" && property.IsIndexer && property.Type.Name == "T"))
{
    failures.Add("Binder should expose the expected System.Collections.List<T> generic surface.");
}

var listEnumeratorType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "ListEnumerator");
if (listEnumeratorType is null || !listEnumeratorType.IsReferenceType || listEnumeratorType.GenericArity != 1 || listEnumeratorType.GenericParameters?.Count != 1 || listEnumeratorType.GenericParameters[0].Name != "T")
{
    failures.Add("Binder should surface System.Collections.ListEnumerator<T> as a generic reference type definition.");
}
else if (!listEnumeratorType.InterfaceTypes.Any(type => type.Name == "IEnumerator<T>"))
{
    failures.Add("Binder should model System.Collections.ListEnumerator<T> as an IEnumerator<T> implementation.");
}
else if (!listEnumeratorType.Methods.Any(method => method.IsConstructor && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "T[]" && method.Parameters[1].Type == TypeSymbol.Integer) ||
         !listEnumeratorType.Methods.Any(method => method.Name == "MoveNext" && !method.IsStatic && method.Parameters.Count == 0 && method.ReturnType == TypeSymbol.Boolean) ||
         !listEnumeratorType.Methods.Any(method => method.Name == "Reset" && !method.IsStatic && method.Parameters.Count == 0) ||
         !listEnumeratorType.Properties.Any(property => property.Name == "Current" && property.Type.Name == "T"))
{
    failures.Add("Binder should expose the expected System.Collections.ListEnumerator<T> generic surface.");
}

var dictionaryType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Dictionary");
if (dictionaryType is null || !dictionaryType.IsReferenceType || dictionaryType.GenericArity != 2 || dictionaryType.GenericParameters?.Count != 2 ||
    dictionaryType.GenericParameters[0].Name != "TKey" || dictionaryType.GenericParameters[1].Name != "TValue")
{
    failures.Add("Binder should surface System.Collections.Dictionary<TKey, TValue> as a generic reference type definition.");
}
else if (!dictionaryType.Methods.Any(method => method.IsConstructor && method.Parameters.Count == 0) ||
         !dictionaryType.Methods.Any(method => method.Name == "Add" && !method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "TKey" && method.Parameters[1].Type.Name == "TValue") ||
         !dictionaryType.Methods.Any(method => method.Name == "ContainsKey" && !method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "TKey" && method.ReturnType == TypeSymbol.Boolean) ||
         !dictionaryType.Methods.Any(method => method.Name == "TryGetValue" && !method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "TKey" && method.Parameters[1].Type.Name == "TValue" && method.Parameters[1].PassingKind == ParameterPassingKind.Out && method.ReturnType == TypeSymbol.Boolean) ||
         !dictionaryType.Methods.Any(method => method.Name == "Clear" && !method.IsStatic && method.Parameters.Count == 0) ||
         !dictionaryType.Properties.Any(property => property.Name == "Count" && !property.IsStatic && property.Type == TypeSymbol.Integer) ||
         !dictionaryType.Properties.Any(property => property.Name == "Item" && property.IsIndexer && property.Type.Name == "TValue" && property.IndexParameter?.Type.Name == "TKey"))
{
    failures.Add("Binder should expose the expected System.Collections.Dictionary<TKey, TValue> generic surface.");
}

var predicateType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Predicate" && type.GenericArity == 1);
if (predicateType is null || !predicateType.IsDelegate || predicateType.GenericParameters?.Count != 1 || predicateType.GenericParameters[0].Name != "T")
{
    failures.Add("Binder should surface System.Collections.Predicate<T> as a generic delegate definition.");
}
else if (!predicateType.Methods.Any(method => method.Name == "Invoke" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "T" && method.ReturnType == TypeSymbol.Boolean))
{
    failures.Add("Binder should expose the expected System.Collections.Predicate<T> delegate surface.");
}

var selectorType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Selector" && type.GenericArity == 2);
if (selectorType is null || !selectorType.IsDelegate || selectorType.GenericParameters?.Count != 2 ||
    selectorType.GenericParameters[0].Name != "TSource" || selectorType.GenericParameters[1].Name != "TResult")
{
    failures.Add("Binder should surface System.Collections.Selector<TSource, TResult> as a generic delegate definition.");
}
else if (!selectorType.Methods.Any(method => method.Name == "Invoke" && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "TSource" && method.ReturnType.Name == "TResult"))
{
    failures.Add("Binder should expose the expected System.Collections.Selector<TSource, TResult> delegate surface.");
}

var enumerableFilterType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Enumerable" && type.GenericArity == 1);
if (enumerableFilterType is null || !enumerableFilterType.IsReferenceType || enumerableFilterType.GenericParameters?.Count != 1 || enumerableFilterType.GenericParameters[0].Name != "T")
{
    failures.Add("Binder should surface System.Collections.Enumerable<T> as the filtering helper type.");
}
else if (!enumerableFilterType.Methods.Any(method => method.Name == "Where" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type.Name == "Predicate<T>" && method.ReturnType.Name == "IEnumerable<T>"))
{
    failures.Add("Binder should expose Enumerable<T>.Where(source, predicate).");
}
else if (!enumerableFilterType.Methods.Any(method => method.Name == "Take" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type == TypeSymbol.Integer && method.ReturnType.Name == "IEnumerable<T>") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Skip" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type == TypeSymbol.Integer && method.ReturnType.Name == "IEnumerable<T>") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Concat" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type.Name == "IEnumerable<T>" && method.ReturnType.Name == "IEnumerable<T>") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Distinct" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.ReturnType.Name == "IEnumerable<T>") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Append" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type.Name == "T" && method.ReturnType.Name == "IEnumerable<T>") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Prepend" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type.Name == "T" && method.ReturnType.Name == "IEnumerable<T>") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Reverse" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.ReturnType.Name == "IEnumerable<T>") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Contains" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type.Name == "T" && method.ReturnType == TypeSymbol.Boolean))
{
    failures.Add("Binder should expose Enumerable<T>.Take(source, count), Skip(source, count), Concat(first, second), Distinct(source), Append(source, value), Prepend(source, value), Reverse(source), and Contains(source, value).");
}
else if (!enumerableFilterType.Methods.Any(method => method.Name == "Any" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.ReturnType == TypeSymbol.Boolean) ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Any" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type.Name == "Predicate<T>" && method.ReturnType == TypeSymbol.Boolean) ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Count" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.ReturnType == TypeSymbol.Integer) ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Count" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type.Name == "Predicate<T>" && method.ReturnType == TypeSymbol.Integer) ||
         !enumerableFilterType.Methods.Any(method => method.Name == "First" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.ReturnType.Name == "T") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "First" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type.Name == "Predicate<T>" && method.ReturnType.Name == "T") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "FirstOrDefault" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.ReturnType.Name == "T") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "FirstOrDefault" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type.Name == "Predicate<T>" && method.ReturnType.Name == "T") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Single" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.ReturnType.Name == "T") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Single" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type.Name == "Predicate<T>" && method.ReturnType.Name == "T") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "SingleOrDefault" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.ReturnType.Name == "T") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "SingleOrDefault" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type.Name == "Predicate<T>" && method.ReturnType.Name == "T") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Last" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.ReturnType.Name == "T") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "Last" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type.Name == "Predicate<T>" && method.ReturnType.Name == "T") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "LastOrDefault" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.ReturnType.Name == "T") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "LastOrDefault" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.Parameters[1].Type.Name == "Predicate<T>" && method.ReturnType.Name == "T") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "ToList" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.ReturnType.Name == "List<T>") ||
         !enumerableFilterType.Methods.Any(method => method.Name == "ToArray" && method.IsStatic && method.Parameters.Count == 1 && method.Parameters[0].Type.Name == "IEnumerable<T>" && method.ReturnType.Name == "T[]"))
{
    failures.Add("Binder should expose Enumerable<T>.Any/Count/First/FirstOrDefault/Single/SingleOrDefault/Last/LastOrDefault/ToList/ToArray overloads.");
}

var enumerableProjectionType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Enumerable" && type.GenericArity == 2);
if (enumerableProjectionType is null || !enumerableProjectionType.IsReferenceType || enumerableProjectionType.GenericParameters?.Count != 2 ||
    enumerableProjectionType.GenericParameters[0].Name != "TSource" || enumerableProjectionType.GenericParameters[1].Name != "TResult")
{
    failures.Add("Binder should surface System.Collections.Enumerable<TSource, TResult> as the projection helper type.");
}
else if (!enumerableProjectionType.Methods.Any(method => method.Name == "Select" && method.IsStatic && method.Parameters.Count == 2 && method.Parameters[0].Type.Name == "IEnumerable<TSource>" && method.Parameters[1].Type.Name == "Selector<TSource, TResult>" && method.ReturnType.Name == "IEnumerable<TResult>"))
{
    failures.Add("Binder should expose Enumerable<TSource, TResult>.Select(source, selector).");
}

var exceptionInterfaceType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "IException");
if (exceptionInterfaceType is null || !exceptionInterfaceType.IsInterface)
{
    failures.Add("Binder should surface IException as a System interface type.");
}
else if (!exceptionInterfaceType.Properties.Any(property => property.Name == "Message" && property.Type == TypeSymbol.String))
{
    failures.Add("Binder should expose IException.Message as a String property.");
}

var exceptionType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Exception");
if (exceptionType is null || !exceptionType.InterfaceTypes.Any(type => type.Name == "IException"))
{
    failures.Add("Binder should surface Exception as an IException implementation.");
}

var notSupportedExceptionType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "NotSupportedException");
if (notSupportedExceptionType is null || notSupportedExceptionType.BaseType?.Name != "Exception")
{
    failures.Add("Binder should surface NotSupportedException as a System.Exception subtype.");
}

var nativeHandleType = binding.Compilation.Types.FirstOrDefault(type => type.Name == "NativeHandle");
if (nativeHandleType is null || nativeHandleType.IsReferenceType)
{
    failures.Add("Binder should surface NativeHandle as a non-reference System enum type.");
}

var stopwatchType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Stopwatch");
if (stopwatchType is null || !stopwatchType.IsReferenceType)
{
    failures.Add("Binder should surface System.Diagnostics.Stopwatch as a reference type.");
}
else if (!stopwatchType.Methods.Any(method => method.IsConstructor) ||
         !stopwatchType.Methods.Any(method => method.Name == "StartNew" && method.IsStatic && method.ReturnType.Name == "Stopwatch") ||
         !stopwatchType.Methods.Any(method => method.Name == "Start" && !method.IsStatic) ||
         !stopwatchType.Methods.Any(method => method.Name == "Stop" && !method.IsStatic) ||
         !stopwatchType.Methods.Any(method => method.Name == "Restart" && !method.IsStatic) ||
         !stopwatchType.Methods.Any(method => method.Name == "Reset" && !method.IsStatic) ||
         !stopwatchType.Properties.Any(property => property.Name == "IsRunning" && !property.IsStatic && property.Type == TypeSymbol.Boolean) ||
         !stopwatchType.Properties.Any(property => property.Name == "ElapsedMilliseconds" && !property.IsStatic && property.Type == TypeSymbol.Integer))
{
    failures.Add("Binder should expose the expected System.Diagnostics.Stopwatch members.");
}

var traceLevelType = binding.Compilation.Types.FirstOrDefault(type => type.Name == "TraceLevel");
if (traceLevelType is null || traceLevelType.IsReferenceType)
{
    failures.Add("Binder should surface TraceLevel as a non-reference enum type.");
}

var traceTargetType = binding.Compilation.Types.FirstOrDefault(type => type.Name == "TraceTarget");
if (traceTargetType is null || traceTargetType.IsReferenceType)
{
    failures.Add("Binder should surface TraceTarget as a non-reference enum type.");
}

var traceInterfaceType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "ITrace");
if (traceInterfaceType is null || !traceInterfaceType.IsInterface)
{
    failures.Add("Binder should surface ITrace as an interface type.");
}
else if (!traceInterfaceType.Properties.Any(property => property.Name == "Targets" && property.Type.Name == "TraceTarget[]") ||
         !traceInterfaceType.Properties.Any(property => property.Name == "MinimumLevel" && property.Type.Name == "TraceLevel") ||
         !traceInterfaceType.Methods.Any(method => method.Name == "Write") ||
         !traceInterfaceType.Methods.Any(method => method.Name == "WriteLine") ||
         !traceInterfaceType.Methods.Any(method => method.Name == "WriteInformation") ||
         !traceInterfaceType.Methods.Any(method => method.Name == "WriteWarning") ||
         !traceInterfaceType.Methods.Any(method => method.Name == "WriteError") ||
         !traceInterfaceType.Methods.Any(method => method.Name == "WriteDebug") ||
         !traceInterfaceType.Methods.Any(method => method.Name == "WriteErrorWithStackTrace"))
{
    failures.Add("Binder should expose the expected ITrace properties and methods.");
}

var consoleTraceType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "ConsoleTrace");
if (consoleTraceType is null || !consoleTraceType.InterfaceTypes.Any(type => type.Name == "ITrace"))
{
    failures.Add("Binder should surface ConsoleTrace as an ITrace implementation.");
}

var fileTraceType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "FileTrace");
if (fileTraceType is null || !fileTraceType.InterfaceTypes.Any(type => type.Name == "ITrace"))
{
    failures.Add("Binder should surface FileTrace as an ITrace implementation.");
}
else if (!fileTraceType.Properties.Any(property => property.Name == "FilePath" && property.Type == TypeSymbol.String))
{
    failures.Add("Binder should expose FileTrace.FilePath as a String property.");
}

var nullTraceType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "NullTrace");
if (nullTraceType is null || !nullTraceType.InterfaceTypes.Any(type => type.Name == "ITrace"))
{
    failures.Add("Binder should surface NullTrace as an ITrace implementation.");
}

var compositeTraceType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "CompositeTrace");
if (compositeTraceType is null || !compositeTraceType.InterfaceTypes.Any(type => type.Name == "ITrace"))
{
    failures.Add("Binder should surface CompositeTrace as an ITrace implementation.");
}
else if (!compositeTraceType.Properties.Any(property => property.Name == "Entries" && property.Type.Name == "ITrace[]"))
{
    failures.Add("Binder should expose CompositeTrace.Entries as an ITrace[] property.");
}

var traceType = binding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Trace");
if (traceType is null ||
    !traceType.Properties.Any(property => property.Name == "Current" && property.IsStatic && property.Type.Name == "ITrace") ||
    !traceType.Methods.Any(method => method.Name == "Write" && method.IsStatic) ||
    !traceType.Methods.Any(method => method.Name == "WriteLine" && method.IsStatic) ||
    !traceType.Methods.Any(method => method.Name == "WriteInformation" && method.IsStatic) ||
    !traceType.Methods.Any(method => method.Name == "WriteWarning" && method.IsStatic) ||
    !traceType.Methods.Any(method => method.Name == "WriteError" && method.IsStatic) ||
    !traceType.Methods.Any(method => method.Name == "WriteDebug" && method.IsStatic) ||
    !traceType.Methods.Any(method => method.Name == "WriteErrorWithStackTrace" && method.IsStatic))
{
    failures.Add("Binder should expose the expected static System.Diagnostics.Trace facade.");
}

var bootstrapMethods = binding.Compilation.GetAllMethods();
var bootstrapFields = binding.Compilation.GetAllFields();
var bootstrapProperties = binding.Compilation.GetAllProperties();
var bootstrapConstants = binding.Compilation.GetAllConstants();

if (!bootstrapConstants.Any(constant => constant.DeclaringTypeName == "Mode" && constant.Name == "Busy" && Equals(constant.Value, 1)))
{
    failures.Add("Binder should surface enum members as typed constants with sequential values.");
}

if (programType is null)
{
    failures.Add("Binder should surface declared classes as named types.");
}
else if (programType.Methods.Count != 19)
{
    failures.Add(
        "Binder should surface declared methods and synthesized property accessors for classes. Actual methods: " +
        string.Join(", ", programType.Methods.Select(method => method.Name)));
}
else
{
    if (debugEnabled)
    {
        Console.Error.WriteLine(
            "debug.binder.program.methods=" +
            string.Join(",", programType.Methods.Select(method => method.Name)));
    }

    if (programType.Fields.Count != 10 ||
        !programType.Fields.Any(field => field.Name == "Accumulator" && field.IsStatic) ||
        !programType.Fields.Any(field => field.Name == "Counter" && !field.IsStatic) ||
        !programType.Fields.Any(field => field.Name == "Items" && !field.IsStatic) ||
        !programType.Fields.Any(field => field.Name == "Children" && !field.IsStatic && field.Type.Name == "Program[]") ||
        !programType.Fields.Any(field => field.Name == "Matrix" && !field.IsStatic && field.Type.Name == "Integer[,]") ||
        !programType.Fields.Any(field => field.Name == "__auto_AutoTotal" && field.IsStatic) ||
        !programType.Fields.Any(field => field.Name == "__auto_AutoValue" && !field.IsStatic) ||
        !programType.Fields.Any(field => field.Name == "__auto_HiddenTotal" && field.IsStatic) ||
        !programType.Fields.Any(field => field.Name == "__auto_SecretRead" && field.IsStatic) ||
        !programType.Fields.Any(field => field.Name == "__auto_Created" && !field.IsStatic))
    {
        failures.Add("Binder should surface declared and synthesized backing fields for classes.");
    }

    if (programType.Properties.Count != 11 ||
        !programType.Properties.Any(property => property.Name == "Total" && property.IsStatic) ||
        !programType.Properties.Any(property => property.Name == "Value" && !property.IsStatic) ||
        !programType.Properties.Any(property => property.Name == "Grid" && !property.IsStatic && property.Type.Name == "Integer[,]") ||
        !programType.Properties.Any(property => property.Name == "Item" && !property.IsStatic && property.IsIndexer) ||
        !programType.Properties.Any(property => property.Name == "Child" && !property.IsStatic && property.IsIndexer && property.Type.Name == "Program") ||
        !programType.Properties.Any(property => property.Name == "AutoTotal" && property.IsStatic) ||
        !programType.Properties.Any(property => property.Name == "AutoValue" && !property.IsStatic) ||
        !programType.Properties.Any(property => property.Name == "HiddenTotal" && property.IsStatic && property.IsSetterPrivate) ||
        !programType.Properties.Any(property => property.Name == "SecretRead" && property.IsStatic && property.IsGetterPrivate) ||
        !programType.Properties.Any(property => property.Name == "Created" && !property.IsStatic && property.IsInitOnly) ||
        !programType.Properties.Any(property => property.Name == "Adjusted" && !property.IsStatic))
    {
        failures.Add("Binder should surface declared classic, auto and accessor-block properties with accessor visibility and init semantics.");
    }

    if (!programType.Methods.Any(method => method.Name == "get_Adjusted") ||
        !programType.Methods.Any(method => method.Name == "set_Adjusted"))
    {
        failures.Add("Binder should synthesize accessor methods for block properties.");
    }

    var constructor = programType.Methods.FirstOrDefault(method => method.IsConstructor);
    if (constructor is null || constructor.Name != ".ctor" || constructor.ReturnType != TypeSymbol.Void)
    {
        failures.Add("Binder should surface constructors as void .ctor methods.");
    }

    var addMethod = programType.Methods.FirstOrDefault(method => method.Name == "Add");
    if (addMethod is null)
    {
        failures.Add("Declared class methods should be bindable by name.");
    }
    else
    {
        var lowerer = new Lowerer(bootstrapMethods, bootstrapFields, binding.Compilation.Types, bootstrapProperties, bootstrapConstants);
        var declaredIr = lowerer.Lower(addMethod);
        if (declaredIr.Blocks.Count != 1)
        {
            failures.Add("Lowerer should emit a single entry block for declared methods.");
        }

        if (declaredIr.Registers.Count < 4)
        {
            failures.Add("Lowerer should allocate parameter, local and return registers for declared methods.");
        }

        if (declaredIr.Registers.FirstOrDefault(register => register.Name == "r2")?.Type != TypeSymbol.Integer)
        {
            failures.Add("Lowerer should infer Integer for local arithmetic temporaries and locals.");
        }

        if (!declaredIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.CompareGreater))
        {
            failures.Add("Lowerer should emit comparison IR for 'if left > right then'.");
        }

        if (!declaredIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.BranchIfFalse))
        {
            failures.Add("Lowerer should emit conditional branches for if-statements.");
        }

        var declaredBytecode = new BytecodeEmitter().Emit(1, declaredIr, addMethod);
        if (!declaredBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.AddI32) ||
            !declaredBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.CmpGtI32) ||
            !declaredBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.BrFalse) ||
            declaredBytecode.Instructions.Last().OpCode != OpCode.Ret)
        {
            failures.Add("Declared method lowering should emit arithmetic, comparison, branching and terminate with ret.");
        }
    }

    var mainMethod = programType.Methods.FirstOrDefault(method => method.Name == "Main");
    if (mainMethod is null)
    {
        failures.Add("Declared void methods should be bindable by name.");
    }
    else
    {
        var lowerer = new Lowerer(bootstrapMethods, bootstrapFields, binding.Compilation.Types, bootstrapProperties, bootstrapConstants);
        var mainModule = new BytecodeEmitter().EmitModule(programType.Methods, programType.Fields, binding.Compilation.Types, lowerer);
        var mainBytecode = mainModule.Functions.First(function => function.Name == "Main");
        var callInstruction = mainBytecode.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCode.Call);
        if (callInstruction is null)
        {
            failures.Add("Lowering should emit a call opcode for invocation expressions in local initializers.");
        }
        else if (callInstruction.Immediate == 0)
        {
            failures.Add("Bound call sites should carry a resolved non-zero target function id.");
        }
        else if (mainMethod.Declaration?.Body?.Statements.OfType<ExpressionStatementSyntax>().Skip(1).FirstOrDefault()?.Expression is not AssignmentExpressionSyntax accumulatorAssignment ||
            accumulatorAssignment.Target is not NameExpressionSyntax accumulatorTargetName ||
            accumulatorTargetName.Name.ToDisplayString() != "Program.Total")
        {
            failures.Add("The test fixture expects the second expression statement to assign Program.Total after the trace banner.");
        }
        else if (mainMethod.Declaration?.Body?.Statements.OfType<LocalVariableDeclarationStatementSyntax>().FirstOrDefault()?.Declarators[0].Initializer is not NameExpressionSyntax fieldRead ||
            fieldRead.Name.ToDisplayString() != "Program.Total")
        {
            failures.Add("The test fixture expects the local initializer to read Program.Total.");
        }
        else if (mainMethod.Declaration?.Body?.Statements.OfType<LocalVariableDeclarationStatementSyntax>().FirstOrDefault()?.Declarators[0].TypeName is not null)
        {
            failures.Add("The test fixture expects local 'var localResult := Program.Total;' to remain implicitly typed.");
        }

        var invalidMainInstanceFieldLoad = mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.LdField && instruction.Left == 0);
        var invalidMainInstanceFieldStore = mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.StField && instruction.Destination == 0);
        if (invalidMainInstanceFieldLoad || invalidMainInstanceFieldStore)
        {
            failures.Add("Static Main lowering must not emit instance field access with receiver register 0.");
        }

        var mainIr = new Lowerer(bootstrapMethods, bootstrapFields, binding.Compilation.Types, bootstrapProperties, bootstrapConstants).Lower(mainMethod);
        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.BranchIfFalse))
        {
            failures.Add("Lowerer should emit conditional branches for while-statements.");
        }

        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.Subtract))
        {
            failures.Add("Lowerer should emit arithmetic for assignment expressions inside loops.");
        }

        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.NewObject) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.CallVirtual))
        {
            failures.Add("Lowerer should emit object construction and virtual instance calls.");
        }

        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.NewArray) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.LoadElement) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.StoreElement) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.LoadLength))
        {
            failures.Add("Lowerer should emit array allocation, element access and length operations.");
        }

        if (!mainIr.ArrayShapes.Any(shape => shape.Extents.Count > 1))
        {
            failures.Add("Lowerer should surface multi-dimensional array shapes explicitly in IR.");
        }

        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction =>
                instruction.OpCode == IrOpCode.CallVirtual &&
                instruction.Operand is IrCallTarget ctorTarget &&
                ctorTarget.Method?.IsConstructor == true))
        {
            failures.Add("Lowerer should emit a constructor call after object construction.");
        }

        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction =>
                instruction.OpCode == IrOpCode.CallVirtual &&
                instruction.Operand is IrCallTarget accessorTarget &&
                accessorTarget.Method?.Name == "get_Adjusted") ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction =>
                instruction.OpCode == IrOpCode.CallVirtual &&
                instruction.Operand is IrCallTarget accessorTarget &&
                accessorTarget.Method?.Name == "set_Adjusted"))
        {
            failures.Add("Lowerer should emit virtual accessor calls for block properties.");
        }

        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.LoadField) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.StoreField))
        {
            failures.Add("Lowerer should emit instance field load/store for property-backed object receivers like p.Value and p.AutoValue.");
        }

        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.LoadStaticField) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.StoreStaticField))
        {
            failures.Add("Lowerer should emit static field load/store IR for Program.Total and Program.AutoTotal.");
        }

        if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.BrFalse) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.Br))
        {
            failures.Add("Bytecode emission should represent while-loops with conditional and unconditional branches.");
        }

        if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.SubI32))
        {
            failures.Add("Bytecode emission should lower 'localResult := localResult - 1' to subtraction.");
        }

        if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.NewObj) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.CallVirt))
        {
            failures.Add("Bytecode emission should lower object construction, instance calls and accessor calls.");
        }
        else
        {
            var packedVirtualCall = mainBytecode.Instructions
                .Select((instruction, index) => (instruction, index))
                .FirstOrDefault(pair =>
                    pair.instruction.OpCode == OpCode.CallVirt &&
                    pair.instruction.Right > 0);
            if (packedVirtualCall.instruction is null || packedVirtualCall.instruction.OpCode != OpCode.CallVirt)
            {
                failures.Add("Fixture should contain at least one virtual call with an explicit argument.");
            }
            else
            {
                var callVirt = packedVirtualCall.instruction;
                var callVirtIndex = packedVirtualCall.index;
                var expectedFrameSize = callVirt.Right + 1;
                var stagedVirtualMoves = mainBytecode.Instructions
                    .Take(callVirtIndex)
                    .Where(instruction =>
                        instruction.OpCode == OpCode.Mov &&
                        instruction.Destination >= callVirt.Left &&
                        instruction.Destination < callVirt.Left + expectedFrameSize)
                    .ToArray();
                var frameAlreadyContiguous = stagedVirtualMoves.Length == 0;
                if (!frameAlreadyContiguous && stagedVirtualMoves.Length != expectedFrameSize)
                {
                    failures.Add("Virtual calls should stage receiver and arguments into a contiguous call frame.");
                }
            }
        }

        if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.NewArr) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.LdElem) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.StElem) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.LdLen))
        {
            failures.Add("Bytecode emission should lower array allocation, element access and length operations.");
        }
        else
        {
            var collectFunction = mainModule.Functions.FirstOrDefault(function => function.Name == "Collect");
            var collectCallEntry = collectFunction is null
                ? default
                : mainBytecode.Instructions
                    .Select((instruction, index) => (instruction, index))
                    .FirstOrDefault(pair =>
                        pair.instruction.OpCode == OpCode.Call &&
                        pair.instruction.Immediate == (int)collectFunction.FunctionId);
            if (collectFunction is null)
            {
                failures.Add("Module emission should retain the Collect function for params verification.");
            }
            else if (collectCallEntry.instruction is null || collectCallEntry.instruction.OpCode != OpCode.Call)
            {
                failures.Add("Bytecode emission should retain a direct call to Collect for params verification.");
            }
            else
            {
                var collectCallIndex = collectCallEntry.index;
                var collectCall = collectCallEntry.instruction;
                if (collectCall.Right != 1)
                {
                    failures.Add("Params calls should pass exactly one packed array argument to Collect.");
                }

                var paramsArrayAllocation = mainBytecode.Instructions
                    .Take(collectCallIndex)
                    .Select((instruction, index) => (instruction, index))
                    .LastOrDefault(pair => pair.instruction.OpCode == OpCode.NewArr);
                if (paramsArrayAllocation.instruction is null || paramsArrayAllocation.instruction.OpCode != OpCode.NewArr)
                {
                    failures.Add("Params calls should allocate a synthetic array before calling Collect.");
                }
                else
                {
                    var paramsArrayRegister = paramsArrayAllocation.instruction.Destination;
                    var paramsStores = mainBytecode.Instructions
                        .Skip(paramsArrayAllocation.index + 1)
                        .Take(collectCallIndex - paramsArrayAllocation.index - 1)
                        .Count(instruction =>
                            instruction.OpCode == OpCode.StElem &&
                            instruction.Destination == paramsArrayRegister);
                    if (paramsStores != 3)
                    {
                        failures.Add("Params calls should pack each trailing argument into the synthetic Collect array.");
                    }

                    var packedArrayMovedIntoFrame = mainBytecode.Instructions
                        .Skip(paramsArrayAllocation.index + 1)
                        .Take(collectCallIndex - paramsArrayAllocation.index - 1)
                        .Any(instruction =>
                            instruction.OpCode == OpCode.Mov &&
                            instruction.Destination == collectCall.Left &&
                            instruction.Left == paramsArrayRegister);
                    var packedArrayAlreadyAtFrameBase = collectCall.Left == paramsArrayRegister;
                    if (!packedArrayMovedIntoFrame && !packedArrayAlreadyAtFrameBase)
                    {
                        failures.Add("Params calls should stage the packed array into the Collect call frame.");
                    }
                }
            }
        }

        if (!mainBytecode.ArrayShapes.Any(shape => shape.ExtentRegisters.Count > 1))
        {
            failures.Add("Bytecode emission should preserve multi-dimensional array shape metadata.");
        }

        if (!mainModule.ArrayShapes.Any(shape => shape.FunctionId == mainBytecode.FunctionId && shape.ExtentRegisters.Count > 1))
        {
            failures.Add("Module emission should surface multi-dimensional array shape metadata at module scope.");
        }

        if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.LdStr))
        {
            failures.Add("Bytecode emission should lower string literals with ld_str.");
        }
        else if (!mainBytecode.StringLiterals.Contains("abcd"))
        {
            failures.Add("Bytecode emission should retain function-local string literal metadata.");
        }

        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.CompareEqualString) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.CompareNotEqualString))
        {
            failures.Add("Lowerer should emit string-specific comparison IR for string equality and inequality.");
        }
        else if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.SliceString))
        {
            failures.Add("Lowerer should emit dedicated IR for string slices.");
        }
        else if (mainIr.Blocks.SelectMany(block => block.Instructions).Count(instruction => instruction.OpCode == IrOpCode.SliceString) < 2)
        {
            failures.Add("Substring should lower through the existing string slice IR path.");
        }

        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.CompareEqualReference))
        {
            failures.Add("Lowerer should emit reference-specific comparison IR for object or nil equality.");
        }

        if (mainIr.Blocks.SelectMany(block => block.Instructions).Count(instruction => instruction.OpCode == IrOpCode.LoadField) < 3)
        {
            failures.Add("Lowerer should emit field loads for record value equality.");
        }

        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.CompareNotEqualReference))
        {
            failures.Add("Lowerer should emit reference-specific comparison IR for exact-type tests on reference values.");
        }

        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.Throw) ||
            mainIr.ExceptionHandlers.Count == 0)
        {
            failures.Add("Lowerer should emit throw IR and structured exception metadata for try/except.");
        }
        else if (!mainIr.ExceptionHandlers.Any(handler => handler.CatchTypeName == "Program" && handler.TargetRegister != ushort.MaxValue))
        {
            failures.Add("Lowerer should preserve typed exception handler metadata for except on ex: Program do.");
        }
        else if (!mainIr.ExceptionHandlers.Any(handler => handler.CatchTypeName is null))
        {
            failures.Add("Lowerer should preserve catch-all exception handler metadata after typed clauses.");
        }

        if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.CmpEqStr) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.CmpNeStr))
        {
            failures.Add("Bytecode emission should lower string equality and inequality with dedicated string comparison opcodes.");
        }
        else if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.ConcatString) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.ConcatStr))
        {
            failures.Add("String concatenation should lower through dedicated IR and bytecode opcodes.");
        }
        else if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.ShiftLeft) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.ShiftRight) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.ShlI32) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.ShrI32))
        {
            failures.Add("Shift operators should lower through dedicated IR and bytecode opcodes.");
        }
        else if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.Modulo) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.ModI32))
        {
            failures.Add("Modulo operators should lower through dedicated IR and bytecode opcodes.");
        }
        else if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.ReplaceString) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.ReplaceStr))
        {
            failures.Add("String replace should lower through dedicated IR and bytecode opcodes.");
        }
        else if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.InsertString) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.InsertStr))
        {
            failures.Add("String insert should lower through dedicated IR and bytecode opcodes.");
        }
        else if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.RemoveString) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.RemoveStr))
        {
            failures.Add("String remove should lower through dedicated IR and bytecode opcodes.");
        }
        else if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.ToUpperString) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.ToLowerString) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.ToUpperStr) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.ToLowerStr))
        {
            failures.Add("String case transforms should lower through dedicated IR and bytecode opcodes.");
        }
        else if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.TrimString) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.TrimStartString) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.TrimEndString) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.TrimStr) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.TrimStartStr) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.TrimEndStr))
        {
            failures.Add("String trim transforms should lower through dedicated IR and bytecode opcodes.");
        }
        else if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.ParseStringToInteger) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.TryParseStringToInteger) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.ConvertIntegerToString) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.StrToI32) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.TryStrToI32) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.I32ToStr))
        {
            failures.Add("String/integer parse and conversion paths should lower through dedicated IR and bytecode opcodes.");
        }
        else if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.StartsWithString) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.EndsWithString) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.ContainsString) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.IndexOfString) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.LastIndexOfString) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.StartsWithStr) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.EndsWithStr) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.ContainsStr) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.IndexOfStr) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.LastIndexOfStr))
        {
            failures.Add("String intrinsic methods should lower through dedicated IR and bytecode opcodes.");
        }
        else if (mainBytecode.Instructions.Count(instruction => instruction.OpCode == OpCode.AddI32) < 3 ||
            mainBytecode.Instructions.Count(instruction => instruction.OpCode == OpCode.SubI32) < 2 ||
            mainBytecode.Instructions.Count(instruction => instruction.OpCode == OpCode.MulI32) < 1 ||
            mainBytecode.Instructions.Count(instruction => instruction.OpCode == OpCode.DivI32) < 1)
        {
            failures.Add("Compound arithmetic assignments should lower through the existing integer arithmetic opcodes.");
        }
        else if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.SliceStr))
        {
            failures.Add("Bytecode emission should lower string slices with a dedicated opcode.");
        }
        else if (mainBytecode.Instructions.Count(instruction => instruction.OpCode == OpCode.SliceStr) < 2)
        {
            failures.Add("Substring should lower through the existing string slice bytecode path.");
        }
        else if (mainBytecode.Instructions.Count(instruction => instruction.OpCode == OpCode.CmpEqI32) < 2)
        {
            failures.Add("Bytecode emission should lower integer case arms with integer equality comparisons.");
        }

        if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.CmpEqRef))
        {
            failures.Add("Bytecode emission should lower object or nil equality with dedicated reference comparison opcodes.");
        }

        if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.CmpNeRef))
        {
            failures.Add("Bytecode emission should lower reference type tests with dedicated reference comparison opcodes.");
        }

        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.BitwiseAnd) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.BitwiseOr) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.BitwiseNot) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.AndI32) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.OrI32) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.NotI32))
        {
            failures.Add("Set membership and set operators should lower to integer bitmask operations.");
        }

        if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.Throw) ||
            mainBytecode.ExceptionHandlers.Count == 0 ||
            !mainModule.ExceptionHandlers.Any(handler => handler.FunctionId == mainBytecode.FunctionId))
        {
            failures.Add("Bytecode emission should lower raise and preserve exception handler metadata.");
        }
        else if (!mainBytecode.ExceptionHandlers.Any(handler => handler.CatchTypeId != 0) ||
            !mainModule.ExceptionHandlers.Any(handler => handler.FunctionId == mainBytecode.FunctionId && handler.CatchTypeId != 0))
        {
            failures.Add("Bytecode emission should preserve typed exception handler catch type ids.");
        }
        else if (!mainBytecode.ExceptionHandlers.Any(handler => handler.CatchTypeId == 0) ||
            !mainModule.ExceptionHandlers.Any(handler => handler.FunctionId == mainBytecode.FunctionId && handler.CatchTypeId == 0))
        {
            failures.Add("Bytecode emission should preserve catch-all exception handler metadata after typed clauses.");
        }

        if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.LdI32 && instruction.Immediate == 0))
        {
            failures.Add("Bytecode emission should lower nil literals to a zero reference constant.");
        }

        if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.LdField) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.StField))
        {
            failures.Add("Bytecode emission should lower p.Value and p.AutoValue access to instance field opcodes.");
        }

        if (!mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.CompareLessOrEqual) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.CompareGreaterOrEqual) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.CompareLess) ||
            !mainIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.CompareGreater) ||
            mainBytecode.Instructions.Count(instruction => instruction.OpCode == OpCode.LdElem) < 2 ||
            mainBytecode.Instructions.Count(instruction => instruction.OpCode == OpCode.LdLen) < 2 ||
            mainBytecode.Instructions.Count(instruction => instruction.OpCode == OpCode.Br) < 6 ||
            mainBytecode.Instructions.Count(instruction => instruction.OpCode == OpCode.BrFalse) < 6 ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.SubI32) ||
            mainBytecode.Instructions.Count(instruction => instruction.OpCode == OpCode.AddI32) < 2)
        {
            failures.Add("Lowerer should emit while/for/foreach/repeat comparison, branch, length and element access arithmetic including break/continue control flow.");
        }

        if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.LdSField) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.StSField))
        {
            failures.Add("Bytecode emission should lower Program.Total and Program.AutoTotal access to static field opcodes.");
        }

        if (mainBytecode.Instructions.Count(instruction => instruction.OpCode == OpCode.Ret) != 1)
        {
            failures.Add("Explicitly terminated methods should not end with duplicate ret instructions.");
        }
    }

    var incrementMethod = programType.Methods.FirstOrDefault(method => method.Name == "Increment");
    var parseSeedMethod = programType.Methods.FirstOrDefault(method => method.Name == "ParseSeed");
    var bumpMethod = programType.Methods.FirstOrDefault(method => method.Name == "Bump");
    var readOnlyMethod = programType.Methods.FirstOrDefault(method => method.Name == "ReadOnly");
    var collectMethod = programType.Methods.FirstOrDefault(method => method.Name == "Collect");
    if (incrementMethod is null)
    {
        failures.Add("Declared instance methods should be bindable by name.");
    }
    else if (parseSeedMethod is null ||
        parseSeedMethod.Parameters.Count != 2 ||
        parseSeedMethod.Parameters[1].PassingKind != ParameterPassingKind.Out)
    {
        failures.Add("Binder should preserve out parameter metadata on declared methods.");
    }
    else if (bumpMethod is null ||
        bumpMethod.Parameters.Count != 1 ||
        bumpMethod.Parameters[0].PassingKind != ParameterPassingKind.Ref)
    {
        failures.Add("Binder should preserve ref parameter metadata on declared methods.");
    }
    else if (readOnlyMethod is null ||
        readOnlyMethod.Parameters.Count != 1 ||
        readOnlyMethod.Parameters[0].PassingKind != ParameterPassingKind.In)
    {
        failures.Add("Binder should preserve in parameter metadata on declared methods.");
    }
    else if (collectMethod is null ||
        collectMethod.Parameters.Count != 1 ||
        collectMethod.Parameters[0].PassingKind != ParameterPassingKind.Params)
    {
        failures.Add("Binder should preserve params parameter metadata on declared methods.");
    }
    else
    {
        var incrementIr = new Lowerer(programType.Methods, programType.Fields, binding.Compilation.Types, programType.Properties, bootstrapConstants).Lower(incrementMethod);
        if (!incrementIr.Registers.Any(register => register.Name == "r0" && register.Type.Name == "Program"))
        {
            failures.Add("Instance methods should receive an implicit self register.");
        }

        if (!incrementIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.LoadField) ||
            !incrementIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.StoreField))
        {
            failures.Add("Instance methods should lower Counter/self.Counter access to field IR.");
        }

        var incrementBytecode = new BytecodeEmitter().Emit(1, incrementIr, incrementMethod);
        if (!incrementBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.LdField) ||
            !incrementBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.StField))
        {
            failures.Add("Instance field access should emit ld_field/st_field bytecode.");
        }
    }

    var currentMethod = programType.Methods.FirstOrDefault(method => method.Name == "Current");
    if (currentMethod is null)
    {
        failures.Add("Declared instance functions should be bindable by name.");
    }
    else
    {
        var currentIr = new Lowerer(programType.Methods, programType.Fields, binding.Compilation.Types, programType.Properties, bootstrapConstants).Lower(currentMethod);
        if (!currentIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.LoadField))
        {
            failures.Add("Instance field reads through self should lower to field load IR.");
        }
    }
}

var method = binding.Compilation.Methods[0];
var ir = new Lowerer(bootstrapMethods, bootstrapFields, binding.Compilation.Types, bootstrapProperties, bootstrapConstants).Lower(method);
if (ir.Blocks.Count != 1)
{
    failures.Add("Lowerer should emit a single entry block.");
}

var bytecode = new BytecodeEmitter().Emit(1, ir, method);
if (bytecode.Instructions.Count <= 2)
{
    failures.Add("Synthetic top-level entry lowering should emit more than a placeholder load + ret.");
}

if (!bytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.Call))
{
    failures.Add("Synthetic top-level entry lowering should emit call instructions for top-level expressions.");
}

var allMethods = binding.Compilation.GetAllMethods();
var allFields = binding.Compilation.GetAllFields();
var allProperties = binding.Compilation.GetAllProperties();
var allConstants = binding.Compilation.GetAllConstants();
var module = new BytecodeEmitter().EmitModule(allMethods, allFields, binding.Compilation.Types, new Lowerer(allMethods, allFields, binding.Compilation.Types, allProperties, allConstants));
if (module.Functions.Count != allMethods.Count)
{
    failures.Add("Module emission should include all bound methods from the merged bootstrap fixture.");
}
else if (module.Functions.Count(function => function.Name == "Main") != 1)
{
    failures.Add("Module emission should contain only one declared Main function name.");
}
else if (!module.ExceptionHandlers.Any())
{
    failures.Add("Module emission should surface exception handler metadata when try/except is present.");
}

var ilbImage = new IlbSerializer().Serialize(module, allMethods.ToArray(), allFields, binding.Compilation.Types, binding.Compilation.EntryPoint);
if (ilbImage.Bytes.Length <= 64)
{
    failures.Add("ILB serialization should emit a non-trivial binary image.");
}
else if (System.Text.Encoding.ASCII.GetString(ilbImage.Bytes, 0, 4) != "ILB1")
{
    failures.Add("ILB serialization should start with the ILB1 magic header.");
}
else if (!ilbImage.Sections.Any(section => section.Kind == IlbSectionKind.StringTable) ||
    !ilbImage.Sections.Any(section => section.Kind == IlbSectionKind.TypeTable) ||
    !ilbImage.Sections.Any(section => section.Kind == IlbSectionKind.MethodTable) ||
    !ilbImage.Sections.Any(section => section.Kind == IlbSectionKind.CodeSection))
{
    failures.Add("ILB serialization should include the required core sections.");
}
else if (!ilbImage.Sections.Any(section => section.Kind == IlbSectionKind.EntryPoint))
{
    failures.Add("Executable ILB serialization should include an entry point section.");
}
else if (!ilbImage.Sections.Any(section => section.Kind == IlbSectionKind.ExceptionTable))
{
    failures.Add("ILB serialization should include an exception table section when handlers are present.");
}
else if (!System.Text.Encoding.UTF8.GetString(ilbImage.Bytes).Contains("abcd", StringComparison.Ordinal))
{
    failures.Add("ILB serialization should include emitted string literals in the string table.");
}

var callPackingTree = SyntaxTree.Parse("""
public class Program
begin
  public static function Add(left: Integer; right: Integer): Integer;
  begin
    return left + right;
  end;

  public static function Main: Integer;
  begin
    var numbers: array of Integer := new Integer[4];
    numbers[0] := 7;
    numbers[1] := Program.Add(numbers[0], 2);
    return numbers[1];
  end;
end;
""");

var callPackingBinding = new Binder().Bind(callPackingTree);
if (callPackingBinding.HasErrors)
{
    failures.Add("Call packing fixture should bind without errors.");
}
else
{
    var packedProgram = callPackingBinding.Compilation.Types.OfType<NamedTypeSymbol>().First(type => type.Name == "Program");
    var packedMethods = packedProgram.Methods.ToArray();
    var packedLowerer = new Lowerer(packedMethods, packedProgram.Fields, callPackingBinding.Compilation.Types, packedProgram.Properties, packedProgram.Constants);
    var packedModule = new BytecodeEmitter().EmitModule(packedMethods, packedProgram.Fields, callPackingBinding.Compilation.Types, packedLowerer);
    var packedMain = packedModule.Functions.First(function => function.Name == "Main");
    var packedCallEntry = packedMain.Instructions
        .Select((instruction, index) => (instruction, index))
        .FirstOrDefault(pair => pair.instruction.OpCode == OpCode.Call);
    if (packedCallEntry.instruction is null || packedCallEntry.instruction.OpCode != OpCode.Call)
    {
        failures.Add("Call packing fixture should emit a call instruction.");
    }
    else
    {
        var packedCallIndex = packedCallEntry.index;
        var packedCall = packedMain.Instructions[packedCallIndex];
        if (packedCall.Left == 0)
        {
            failures.Add("Call packing should stage non-trivial argument lists into dedicated registers.");
        }

        var stagedMoves = packedMain.Instructions
            .Take(packedCallIndex)
            .Where(instruction => instruction.OpCode == OpCode.Mov && instruction.Destination >= packedCall.Left && instruction.Destination < packedCall.Left + packedCall.Right)
            .ToArray();
        if (stagedMoves.Length != packedCall.Right)
        {
            failures.Add("Call packing should move each argument into the contiguous call frame.");
        }

        if (packedMain.RegisterCount <= packedMain.Instructions.Max(instruction => Math.Max(instruction.Destination, instruction.Left)))
        {
            failures.Add("Bytecode register count should account for staged call-frame registers.");
        }
    }
}

var readonlyInParameterTree = SyntaxTree.Parse("""
public class Box
begin
  public var Value: Integer;
end;

public class Program
begin
  public static procedure TakeOut(out value: Integer);
  begin
    value := 1;
  end;

  public static procedure TakeRef(ref value: Integer);
  begin
    value := value + 1;
  end;

  public static function ReadOnlyUse(in value: Integer): Integer;
  begin
    return value + 1;
  end;

  public static procedure BadAssign(in value: Integer);
  begin
    value := 1;
  end;

  public static procedure BadCompound(in value: Integer);
  begin
    value += 1;
  end;

  public static procedure BadInc(in value: Integer);
  begin
    inc(value);
  end;

  public static procedure BadForward(in value: Integer);
  begin
    Program.TakeOut(out value);
    Program.TakeRef(ref value);
  end;

  public static procedure BadIndex(in values: array of Integer);
  begin
    values[0] := 1;
  end;

  public static procedure BadMember(in box: Box);
  begin
    box.Value := 1;
  end;
end;
""");

var readonlyInParameterBinding = new Binder().Bind(readonlyInParameterTree);
var readonlyInParameterDiagnostics = readonlyInParameterBinding.Diagnostics.ToArray();
if (readonlyInParameterDiagnostics.Count(diagnostic => diagnostic.Id == "ILC2253") != 7)
{
    failures.Add(
        "Binder should report direct writes and ref/out forwarding of readonly in parameters. Actual: " +
        string.Join(" | ", readonlyInParameterDiagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}")));
}

var requiredOutputAssignmentTree = SyntaxTree.Parse("""
public class Program
begin
  public static function GoodResult(value: Integer): Integer;
  begin
    if value > 0 then
    begin
      Result := value;
    end
    else
    begin
      return 0;
    end;
  end;

  public static function GoodOut(text: String; out value: Integer): Boolean;
  begin
    value := Integer.Parse(text);
    return true;
  end;

  public static function BadBareReturn(value: Integer): Integer;
  begin
    if value > 0 then
    begin
      return;
    end;

    return value;
  end;

  public static function BadFallthrough(value: Integer): Integer;
  begin
    if value > 0 then
    begin
      Result := value;
    end;
  end;

  public static function BadOutReturn(out value: Integer): Boolean;
  begin
    return false;
  end;

  public static function BadOutBranch(flag: Boolean; out value: Integer): Boolean;
  begin
    if flag then
    begin
      value := 1;
    end;

    return true;
  end;
end;
""");

var requiredOutputAssignmentBinding = new Binder().Bind(requiredOutputAssignmentTree);
var requiredOutputAssignmentDiagnostics = requiredOutputAssignmentBinding.Diagnostics.ToArray();
if (requiredOutputAssignmentDiagnostics.Count(diagnostic => diagnostic.Id == "ILC2255") != 2)
{
    failures.Add(
        "Binder should report function result exits that can occur before Result is assigned. Actual: " +
        string.Join(" | ", requiredOutputAssignmentDiagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}")));
}

if (requiredOutputAssignmentDiagnostics.Count(diagnostic => diagnostic.Id == "ILC2256") != 2)
{
    failures.Add(
        "Binder should report out parameter exits that can occur before assignment. Actual: " +
        string.Join(" | ", requiredOutputAssignmentDiagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}")));
}

var localDefiniteAssignmentTree = SyntaxTree.Parse("""
public class Program
begin
  public static function GoodBranch(flag: Boolean): Integer;
  begin
    var value: Integer;
    if flag then
    begin
      value := 1;
    end
    else
    begin
      value := 2;
    end;

    return value;
  end;

  public static function GoodNestedReuse(flag: Boolean): Integer;
  begin
    if flag then
    begin
      var value := 1;
      return value;
    end;

    var value := 2;
    return value;
  end;

  public static function BadDirectRead: Integer;
  begin
    var value: Integer;
    return value;
  end;

  public static function BadBranch(flag: Boolean): Integer;
  begin
    var value: Integer;
    if flag then
    begin
      value := 1;
    end;

    return value;
  end;

  public static function BadLoop: Integer;
  begin
    var value: Integer;
    while 1 = 2 do
    begin
      value := 1;
    end;

    return value;
  end;

  public static function BadCompound: Integer;
  begin
    var value: Integer;
    value += 1;
    return value;
  end;
end;
""");

var localDefiniteAssignmentBinding = new Binder().Bind(localDefiniteAssignmentTree);
var localDefiniteAssignmentDiagnostics = localDefiniteAssignmentBinding.Diagnostics.ToArray();
if (localDefiniteAssignmentDiagnostics.Count(diagnostic => diagnostic.Id == "ILC2257") != 4)
{
    failures.Add(
        "Binder should report local variables that can be read before assignment. Actual: " +
        string.Join(" | ", localDefiniteAssignmentDiagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}")));
}

var resultAliasTree = SyntaxTree.Parse("""
public class Program
begin
  public static function Accumulate(value: Integer): Integer;
  begin
    Result := 1;
    Result := Result + value;
    return Result;
  end;

  public static function AccumulateWithExit(value: Integer): Integer;
  begin
    Result := 1;
    exit Result + value;
  end;

  public static function ReturnAlias(value: Integer): Integer;
  begin
    return value + 1;
  end;

  public static function ReturnCurrentResult(value: Integer): Integer;
  begin
    Result := value;
    return;
  end;

  public static function ExitCurrentResult(value: Integer): Integer;
  begin
    Result := value;
    exit;
  end;

  public static procedure ExitProcedure;
  begin
    exit;
  end;

  public static procedure ReturnProcedure;
  begin
    return;
  end;

  public static method InvalidProcedureResult;
  begin
    Result := 1;
  end;

  public static function InvalidLocalResult: Integer;
  begin
    var Result := 1;
    return Result;
  end;

  public static procedure InvalidProcedureReturnValue;
  begin
    return 1;
  end;

  public static function InvalidReturnType: Integer;
  begin
    return 'bad';
  end;
end;
""");

var resultAliasBinding = new Binder().Bind(resultAliasTree);
var resultAliasProgram = resultAliasBinding.Compilation.Types.OfType<NamedTypeSymbol>().First(type => type.Name == "Program");
var resultAliasMethod = resultAliasProgram.Methods.First(method => method.Name == "Accumulate");
var exitAliasMethod = resultAliasProgram.Methods.First(method => method.Name == "AccumulateWithExit");
var returnAliasMethod = resultAliasProgram.Methods.First(method => method.Name == "ReturnAlias");
var returnCurrentResultMethod = resultAliasProgram.Methods.First(method => method.Name == "ReturnCurrentResult");
var exitCurrentResultMethod = resultAliasProgram.Methods.First(method => method.Name == "ExitCurrentResult");
var exitProcedureMethod = resultAliasProgram.Methods.First(method => method.Name == "ExitProcedure");
var returnProcedureMethod = resultAliasProgram.Methods.First(method => method.Name == "ReturnProcedure");
var resultAliasDiagnostics = resultAliasBinding.Diagnostics.ToArray();
if (resultAliasDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id is not ("ILC2240" or "ILC2241" or "ILC2242" or "ILC2250")))
{
    failures.Add("Result alias fixture should only report the expected invalid Result usages.");
}

if (!resultAliasDiagnostics.Any(diagnostic => diagnostic.Id == "ILC2242") ||
    !resultAliasDiagnostics.Any(diagnostic => diagnostic.Id == "ILC2241") ||
    !resultAliasDiagnostics.Any(diagnostic => diagnostic.Id == "ILC2250") ||
    !resultAliasDiagnostics.Any(diagnostic => diagnostic.Id == "ILC2240"))
{
    failures.Add("Binder should allow implicit Result in functions and reject Result or return values in invalid contexts.");
}

var resultAliasIr = new Lowerer(
    resultAliasProgram.Methods,
    resultAliasProgram.Fields,
    resultAliasBinding.Compilation.Types,
    resultAliasProgram.Properties,
    resultAliasProgram.Constants).Lower(resultAliasMethod);
var exitAliasIr = new Lowerer(
    resultAliasProgram.Methods,
    resultAliasProgram.Fields,
    resultAliasBinding.Compilation.Types,
    resultAliasProgram.Properties,
    resultAliasProgram.Constants).Lower(exitAliasMethod);
var returnAliasIr = new Lowerer(
    resultAliasProgram.Methods,
    resultAliasProgram.Fields,
    resultAliasBinding.Compilation.Types,
    resultAliasProgram.Properties,
    resultAliasProgram.Constants).Lower(returnAliasMethod);
var returnCurrentResultIr = new Lowerer(
    resultAliasProgram.Methods,
    resultAliasProgram.Fields,
    resultAliasBinding.Compilation.Types,
    resultAliasProgram.Properties,
    resultAliasProgram.Constants).Lower(returnCurrentResultMethod);
var exitCurrentResultIr = new Lowerer(
    resultAliasProgram.Methods,
    resultAliasProgram.Fields,
    resultAliasBinding.Compilation.Types,
    resultAliasProgram.Properties,
    resultAliasProgram.Constants).Lower(exitCurrentResultMethod);
var exitProcedureIr = new Lowerer(
    resultAliasProgram.Methods,
    resultAliasProgram.Fields,
    resultAliasBinding.Compilation.Types,
    resultAliasProgram.Properties,
    resultAliasProgram.Constants).Lower(exitProcedureMethod);
var returnProcedureIr = new Lowerer(
    resultAliasProgram.Methods,
    resultAliasProgram.Fields,
    resultAliasBinding.Compilation.Types,
    resultAliasProgram.Properties,
    resultAliasProgram.Constants).Lower(returnProcedureMethod);
var resultAliasInstructions = resultAliasIr.Blocks.SelectMany(block => block.Instructions).ToArray();
var exitAliasInstructions = exitAliasIr.Blocks.SelectMany(block => block.Instructions).ToArray();
var returnAliasInstructions = returnAliasIr.Blocks.SelectMany(block => block.Instructions).ToArray();
var returnCurrentResultInstructions = returnCurrentResultIr.Blocks.SelectMany(block => block.Instructions).ToArray();
var exitCurrentResultInstructions = exitCurrentResultIr.Blocks.SelectMany(block => block.Instructions).ToArray();
var exitProcedureInstructions = exitProcedureIr.Blocks.SelectMany(block => block.Instructions).ToArray();
var returnProcedureInstructions = returnProcedureIr.Blocks.SelectMany(block => block.Instructions).ToArray();
if (resultAliasInstructions.Count(instruction => instruction.OpCode == IrOpCode.Return) != 1 ||
    resultAliasInstructions.Last().OpCode != IrOpCode.Return ||
    !resultAliasInstructions.Any(instruction => instruction.OpCode == IrOpCode.LoadConstant && Equals(instruction.Operand, 1)) ||
    !resultAliasInstructions.Any(instruction => instruction.OpCode == IrOpCode.Add))
{
    failures.Add("Lowerer should map implicit Result reads and writes to the function return register.");
}

if (exitAliasInstructions.Count(instruction => instruction.OpCode == IrOpCode.Return) != 1 ||
    exitAliasInstructions.Last().OpCode != IrOpCode.Return ||
    !exitAliasInstructions.Any(instruction => instruction.OpCode == IrOpCode.Add))
{
    failures.Add("Lowerer should treat exit expressions as early routine exits that assign the function return register.");
}

if (returnAliasInstructions.Count(instruction => instruction.OpCode == IrOpCode.Return) != 1 ||
    returnAliasInstructions.Last().OpCode != IrOpCode.Return ||
    !returnAliasInstructions.Any(instruction => instruction.OpCode == IrOpCode.Add))
{
    failures.Add("Lowerer should treat return expressions as compatibility aliases for exit expressions.");
}

if (returnCurrentResultInstructions.Count(instruction => instruction.OpCode == IrOpCode.Return) != 1 ||
    returnCurrentResultInstructions.Last().OpCode != IrOpCode.Return ||
    exitCurrentResultInstructions.Count(instruction => instruction.OpCode == IrOpCode.Return) != 1 ||
    exitCurrentResultInstructions.Last().OpCode != IrOpCode.Return)
{
    failures.Add("Lowerer should let bare return/exit terminate value-returning routines with the current Result value.");
}

if (exitProcedureInstructions.Count(instruction => instruction.OpCode == IrOpCode.Return) != 1 ||
    returnProcedureInstructions.Count(instruction => instruction.OpCode == IrOpCode.Return) != 1)
{
    failures.Add("Lowerer should allow bare exit/return in procedures and void methods.");
}

var logicalOperatorTree = SyntaxTree.Parse("""
public class Program
begin
  public static function TrueValue: Boolean;
  begin
    return true;
  end;

  public static function LogicalAnd: Boolean;
  begin
    return false and TrueValue();
  end;

  public static function LogicalOr: Boolean;
  begin
    return true or TrueValue();
  end;

  public static function InvalidLogical: Boolean;
  begin
    return 1 and true;
  end;
end;
""");

var logicalOperatorBinding = new Binder().Bind(logicalOperatorTree);
if (!logicalOperatorBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2251"))
{
    failures.Add("Binder should require Boolean operands for logical infix operators.");
}

var logicalProgram = logicalOperatorBinding.Compilation.Types.OfType<NamedTypeSymbol>().First(type => type.Name == "Program");
var logicalLowerer = new Lowerer(logicalProgram.Methods, logicalProgram.Fields, logicalOperatorBinding.Compilation.Types, logicalProgram.Properties, logicalProgram.Constants);
var logicalAndInstructions = logicalLowerer.Lower(logicalProgram.Methods.First(method => method.Name == "LogicalAnd")).Blocks.SelectMany(block => block.Instructions).ToArray();
var logicalOrInstructions = logicalLowerer.Lower(logicalProgram.Methods.First(method => method.Name == "LogicalOr")).Blocks.SelectMany(block => block.Instructions).ToArray();
if (!logicalAndInstructions.Any(instruction => instruction.OpCode == IrOpCode.BranchIfFalse && instruction.Operand is string label && label.StartsWith("logical_and_end_", StringComparison.Ordinal)) ||
    logicalAndInstructions.Any(instruction => instruction.OpCode == IrOpCode.Add))
{
    failures.Add("Lowerer should lower logical 'and' through a short-circuit branch, not arithmetic addition.");
}

if (!logicalOrInstructions.Any(instruction => instruction.OpCode == IrOpCode.BranchIfFalse && instruction.Operand is string label && label.StartsWith("logical_or_right_", StringComparison.Ordinal)) ||
    !logicalOrInstructions.Any(instruction => instruction.OpCode == IrOpCode.Branch && instruction.Operand is string label && label.StartsWith("logical_or_end_", StringComparison.Ordinal)) ||
    logicalOrInstructions.Any(instruction => instruction.OpCode == IrOpCode.Add))
{
    failures.Add("Lowerer should lower logical 'or' through a short-circuit branch, not arithmetic addition.");
}

var routineKeywordTree = SyntaxTree.Parse("""
public class Program
begin
  public static method FlexibleProcedure;
  begin
  end;

  public static method FlexibleFunction: Integer;
  begin
    Result := 1;
  end;

  public static function MissingFunctionReturn;
  begin
  end;

  public static procedure InvalidProcedureReturn: Integer;
  begin
  end;

  public static function InvalidVoidReturn: Void;
  begin
  end;
end;
""");

var routineKeywordBinding = new Binder().Bind(routineKeywordTree);
var routineKeywordDiagnostics = routineKeywordBinding.Diagnostics.ToArray();
if (!routineKeywordDiagnostics.Any(diagnostic => diagnostic.Id == "ILC2245") ||
    !routineKeywordDiagnostics.Any(diagnostic => diagnostic.Id == "ILC2246") ||
    !routineKeywordDiagnostics.Any(diagnostic => diagnostic.Id == "ILC2247"))
{
    failures.Add("Binder should enforce function/procedure return-type rules while keeping method flexible.");
}

if (routineKeywordDiagnostics.Any(diagnostic =>
        diagnostic.Severity == DiagnosticSeverity.Error &&
        diagnostic.Id is not ("ILC2245" or "ILC2246" or "ILC2247")))
{
    failures.Add("Routine keyword fixture should only report the expected keyword return-type diagnostics.");
}

var readonlySelfTree = SyntaxTree.Parse("""
public class Program
begin
  private var _count: Integer;
  private var _name: String;
  public property Name: String read _name write _name;

  public method Mutate;
  begin
    _count := _count + 1;
  end;

  public method MutatingValue: Integer;
  begin
    Result := _count + 1;
  end;

  public function ReadCount: Integer;
  begin
    return _count;
  end;

  public function BadFieldAssignment: Integer;
  begin
    _count := 5;
    return _count;
  end;

  public procedure BadPropertyAssignment;
  begin
    self.Name := 'Test';
  end;

  public function BadCompoundAssignment: Integer;
  begin
    _count += 1;
    return _count;
  end;

  public procedure BadIncrement;
  begin
    inc(_count);
  end;

  public function BadMutatingCall: Integer;
  begin
    return MutatingValue();
  end;
end;
""");

var readonlySelfBinding = new Binder().Bind(readonlySelfTree);
var readonlySelfDiagnostics = readonlySelfBinding.Diagnostics.ToArray();
if (readonlySelfDiagnostics.Count(diagnostic => diagnostic.Id == "ILC2248") != 4 ||
    readonlySelfDiagnostics.Count(diagnostic => diagnostic.Id == "ILC2249") != 1)
{
    failures.Add("Binder should reject self mutations and self mutating method calls from functions/procedures. Actual: " +
        string.Join(" | ", readonlySelfDiagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}")));
}

if (readonlySelfDiagnostics.Any(diagnostic =>
        diagnostic.Severity == DiagnosticSeverity.Error &&
        diagnostic.Id is not ("ILC2248" or "ILC2249")))
{
    failures.Add("Readonly self fixture should only report the expected mutation diagnostics. Actual: " +
        string.Join(" | ", readonlySelfDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}")));
}

var caseInsensitiveTree = SyntaxTree.Parse("""
PUBLIC CLASS Program
BEGIN
  public static var Counter: integer;

  PUBLIC STATIC FUNCTION Add(Value: INTEGER): Integer;
  BEGIN
    result := value + counter;
    RETURN result;
  END;

  PUBLIC STATIC METHOD Main;
  BEGIN
    counter := 2;
    var total: integer := ADD(3);
    if TOTAL <> 5 then
    begin
      raise 'case-insensitive lookup failed';
    end;
  END;
END;
""");

var caseInsensitiveBinding = new Binder().Bind(caseInsensitiveTree);
if (caseInsensitiveBinding.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
{
    failures.Add("Compiler should parse and bind keywords and identifiers case-insensitively.");
}

var caseInsensitiveProgram = caseInsensitiveBinding.Compilation.Types.OfType<NamedTypeSymbol>().First(type => type.Name == "Program");
var caseInsensitiveMain = caseInsensitiveProgram.Methods.First(method => method.Name == "Main");
_ = new Lowerer(
    caseInsensitiveProgram.Methods,
    caseInsensitiveProgram.Fields,
    caseInsensitiveBinding.Compilation.Types,
    caseInsensitiveProgram.Properties,
    caseInsensitiveProgram.Constants).Lower(caseInsensitiveMain);

var caseInsensitiveCollisionTree = SyntaxTree.Parse("""
public var GlobalValue: Integer;
public var globalValue: Integer;

public enum Mode
begin
  Ready;
  ready;
end;

public interface program
begin
end;

public interface PROGRAM
begin
end;

public delegate signature Mapper<TInput, tinput>(value: TInput): TInput;

public class Program<TValue, tvalue>
begin
  public var Value: Integer;
  public var value: Integer;

  public function Calculate(Name: String; name: String): Integer;
  begin
    var TEMP := 1;
    var temp := TEMP + 1;
    Result := temp;
    return Result;
  end;
end;
""");

var caseInsensitiveCollisionBinding = new Binder().Bind(caseInsensitiveCollisionTree);
if (caseInsensitiveCollisionBinding.Diagnostics.Count(diagnostic => diagnostic.Id == "ILC2243") < 8)
{
    failures.Add("Binder should warn when names differ only by case in case-insensitive declaration and local scopes.");
}

var duplicateNameTree = SyntaxTree.Parse("""
public var DuplicateValue: Integer;
public var DuplicateValue: Integer;

public enum DuplicateMode
begin
  Ready;
  Ready;
end;

public class DuplicateNames<T, T>
begin
  public constructor;
  begin
  end;

  public constructor;
  begin
  end;

  public var Item: Integer;
  public var Item: Integer;

  public method Use(value: Integer; value: Integer);
  begin
  end;

  public method Repeat(value: Integer);
  begin
  end;

  public method Repeat(value: Integer);
  begin
  end;
end;
""");

var duplicateNameBinding = new Binder().Bind(duplicateNameTree);
if (duplicateNameBinding.Diagnostics.Count(diagnostic => diagnostic.Id == "ILC2244") < 7)
{
    failures.Add("Binder should reject exact duplicate names in case-insensitive declaration scopes.");
}

var duplicateLocalScopeTree = SyntaxTree.Parse("""
public class Program
begin
  public method Main;
  begin
    var localValue := 1;
    var localValue := 2;
  end;
end;
""");

var duplicateLocalScopeBinding = new Binder().Bind(duplicateLocalScopeTree);
if (!duplicateLocalScopeBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2244"))
{
    failures.Add("Binder should reject exact duplicate local names in the same scope.");
}

var nestedLocalShadowTree = SyntaxTree.Parse("""
public class Program
begin
  public method Main;
  begin
    var localValue := 1;
    begin
      var localValue := 2;
    end;
  end;
end;
""");

var nestedLocalShadowBinding = new Binder().Bind(nestedLocalShadowTree);
if (!nestedLocalShadowBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2244"))
{
    failures.Add("Binder should reject local names that shadow an active outer scope.");
}

var siblingLocalReuseTree = SyntaxTree.Parse("""
public class Program
begin
  public method Main;
  begin
    begin
      var localValue := 1;
    end;
    var localValue := 2;
  end;
end;
""");

var siblingLocalReuseBinding = new Binder().Bind(siblingLocalReuseTree);
if (siblingLocalReuseBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2244"))
{
    failures.Add("Binder should allow local names to be reused after the earlier scope has ended.");
}

var implicitLocalShadowTree = SyntaxTree.Parse("""
public class Program
begin
  public method Main;
  begin
    var localValue := 1;
    for var localValue := 0 to 1 do
    begin
    end;

    foreach var localValue in 'ab' do
    begin
    end;

    var source: String := 'abcd';
    match source with
      String localValue => localValue := localValue;
      _ => return;
    end;
  end;
end;
""");

var implicitLocalShadowBinding = new Binder().Bind(implicitLocalShadowTree);
if (implicitLocalShadowBinding.Diagnostics.Count(diagnostic => diagnostic.Id == "ILC2244") < 3)
{
    failures.Add("Binder should reject implicit local names that shadow an active outer scope.");
}

var exceptionLocalShadowTree = SyntaxTree.Parse("""
public class Exception
begin
end;

public class Program
begin
  public method Main;
  begin
    var localValue := 1;
    try
      raise 'boom';
    except
      on localValue: Exception do
      begin
      end;
    end;
  end;
end;
""");

var exceptionLocalShadowBinding = new Binder().Bind(exceptionLocalShadowTree);
if (!exceptionLocalShadowBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2244"))
{
    failures.Add("Binder should reject exception handler names that shadow an active outer scope.");
}

var duplicateLambdaParameterTree = SyntaxTree.Parse("""
public delegate signature Combiner(left: Integer; right: Integer): Integer;

public class Program
begin
  public method Main;
  begin
    var combiner: Combiner := function(value: Integer; value: Integer): Integer => value;
  end;
end;
""");

var duplicateLambdaParameterBinding = new Binder().Bind(duplicateLambdaParameterTree);
if (!duplicateLambdaParameterBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2244"))
{
    failures.Add("Binder should reject exact duplicate lambda parameter names.");
}

var invalidAssignmentTree = SyntaxTree.Parse("""
public class Program
begin
  public method Main;
  begin
    missing := 1;
    Demo.Program.Value := 2;
    return;
  end;
end;
""");

var invalidBinding = new Binder().Bind(invalidAssignmentTree);
if (!invalidBinding.HasErrors)
{
    failures.Add("Invalid assignment targets should prevent emission.");
}

if (!invalidBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2100"))
{
    failures.Add("Binder should report unknown assignment targets.");
}

if (!invalidBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2101"))
{
    failures.Add("Binder should report non-assignable qualified assignment targets.");
}

var invalidReferenceTree = SyntaxTree.Parse("""
public class Program
begin
  public static var Accumulator: Integer;
  public var Counter: Integer;
  public static property BrokenTotal: Integer read MissingField write Accumulator;
  public property WrongAccess: Integer read Accumulator write Counter;
  public property ReadOnlyValue: Integer { get; };
  public static property HiddenTotal: Integer { get; private set; };
  public static property SecretRead: Integer { private get; set; };
  public property Created: Integer { get; init; };

  public method Main;
  begin
    var value := missing + 1;
    UnknownCall(value);
    var scalar := 5;
    var badLength := scalar.Length;
    var numbers: array of Integer := new Integer[2];
    var badIndex := numbers[scalar > 0];
    var rows := 2;
    var dynamicMatrix := new Integer[rows, 2];
    dynamicMatrix[1, 1] := rows;
    var dynamicCell := dynamicMatrix[1, 1];
    var badCast := scalar as Integer;
    var wrongTypeTest := scalar is MissingType;
    var text := 'abc';
    var first := text[0];
    text[0] := 1;
    numbers[0, 1] := 2;
    scalar[0] := 1;
    Program;
    Program.Main;
    Demo.Program.Read;
    Program.Missing();
    Program.Increment();
    Program.Counter;
    self.Accumulator;
    var p := new Program();
    p.Accumulator;
    ReadOnlyValue := 1;
    Created := 42;
    return;
  end;

  public static method StaticMain;
  begin
    Counter := 1;
    Increment();
    return;
  end;

  public method Increment;
  begin
    return;
  end;
end;

Program.HiddenTotal := 1;
var leaked := Program.SecretRead;
""");

var invalidReferenceBinding = new Binder().Bind(invalidReferenceTree);
if (!invalidReferenceBinding.HasErrors)
{
    failures.Add("Invalid references should prevent emission.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2102"))
{
    failures.Add("Binder should report unknown name references.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2103"))
{
    failures.Add("Binder should report unresolved call targets.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2105"))
{
    failures.Add("Binder should report type references used as value expressions.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2106"))
{
    failures.Add("Binder should report member references used as value expressions.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2107"))
{
    failures.Add("Binder should report instance methods called through a type qualifier.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2110"))
{
    failures.Add("Binder should report instance fields accessed through a type qualifier.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2111"))
{
    failures.Add("Binder should report static fields accessed through self.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2112"))
{
    failures.Add("Binder should report instance fields accessed without a receiver in static methods.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2109"))
{
    failures.Add("Binder should report instance methods called without a receiver in static methods.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2113"))
{
    failures.Add("Binder should report static fields accessed through an instance receiver.");
}

var inheritanceBindingTree = SyntaxTree.Parse("""
public class Animal
begin
  public var Name: String;
  public virtual function Speak(): String;
  begin
    return 'generic';
  end;
end;

public class Dog: Animal
begin
  public var Breed: String;
  public override function Speak(): String;
  begin
    return 'woof';
  end;
end;

public class Program
begin
  public static method Main;
  begin
    var pet := new Dog();
    var inheritedName := pet.Name;
    var ownBreed := pet.Breed;
    var sound := pet.Speak();
    var asBase: Animal := pet;
    var baseName := asBase.Name;
    var baseSound := asBase.Speak();
  end;
end;
""");

var inheritanceBinding = new Binder().Bind(inheritanceBindingTree);
if (inheritanceBinding.HasErrors)
{
    failures.Add("Binder should accept valid inheritance and virtual dispatch member access in a single-pass hierarchy scenario.");
}

var interfaceBindingTree = SyntaxTree.Parse("""
public interface IWorker
begin
  public function Run(value: Integer): Integer;
end;

public interface IAdvancedWorker: IWorker
begin
  public function Stop(): Integer;
end;

public class Worker: IAdvancedWorker
begin
  public function Run(value: Integer): Integer;
  begin
    return value;
  end;

  public function Stop(): Integer;
  begin
    return 0;
  end;
end;
""");

if (interfaceBindingTree.Root.Members[0] is not InterfaceDeclarationSyntax parsedInterface ||
    parsedInterface.Identifier.Text != "IWorker" ||
    parsedInterface.Members.Count != 1)
{
    failures.Add("Parser should capture interface declarations and interface members.");
}

var interfaceBinding = new Binder().Bind(interfaceBindingTree);
if (interfaceBinding.HasErrors)
{
    failures.Add("Binder should accept valid interface declarations and class implementations.");
}
else
{
    var workerType = interfaceBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Worker");
    var iWorkerType = interfaceBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "IWorker");
    if (workerType is null || workerType.IsInterface || workerType.InterfaceTypes.Count != 1 || workerType.InterfaceTypes[0].Name != "IAdvancedWorker")
    {
        failures.Add("Binder should record implemented interfaces on classes.");
    }

    if (iWorkerType is null || !iWorkerType.IsInterface)
    {
        failures.Add("Binder should expose interfaces as dedicated named types.");
    }
}

var interfaceCompatibilityTree = SyntaxTree.Parse("""
public interface IWorker
begin
  public function Run(value: Integer): Integer;
end;

public interface IAdvancedWorker: IWorker
begin
end;

public class Worker: IAdvancedWorker
begin
  public function Run(value: Integer): Integer;
  begin
    return value;
  end;
end;

public class Program
begin
  public static function MatchWorker(worker: Worker): Integer;
  begin
    return match worker with
      IWorker w => 1
      _ => 0
    end;
  end;
end;
""");

var interfaceCompatibilityBinding = new Binder().Bind(interfaceCompatibilityTree);
if (interfaceCompatibilityBinding.HasErrors)
{
    failures.Add($"Binder should treat implementing classes as compatible with interface-typed match arms. Actual: {string.Join(", ", interfaceCompatibilityBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message))}");
}
else
{
    var interfaceMethods = interfaceCompatibilityBinding.Compilation.GetAllMethods().ToArray();
    var interfaceFields = interfaceCompatibilityBinding.Compilation.GetAllFields();
    var interfaceBytecodeModule = new BytecodeModule(
        interfaceMethods
            .Select((method, index) => new BytecodeFunction(
                (uint)(index + 1),
                method.Name,
                (ushort)Math.Max(method.Parameters.Count + (method.IsStatic ? 1 : 2), 1),
                (ushort)(method.Parameters.Count + (method.IsStatic ? 0 : 1)),
                method.HostImportKind,
                null,
                [new Instruction(OpCode.Ret)],
                [],
                [],
                [],
                []))
            .ToArray(),
        [],
        [],
        interfaceCompatibilityBinding.Compilation.Types);
    var interfaceIlbImage = new IlbSerializer().Serialize(
        interfaceBytecodeModule,
        interfaceMethods,
        interfaceFields,
        interfaceCompatibilityBinding.Compilation.Types,
        null);
if (!interfaceIlbImage.Sections.Any(section => section.Kind == IlbSectionKind.InterfaceDispatchTable))
    {
        failures.Add("ILB serialization should include an interface dispatch table section when interface implementations are present.");
    }
}

var genericBindingTree = SyntaxTree.Parse("""
public class Box<T>
begin
  private var Value: T;

  public property Item: T
  begin
    get
    begin
      return Value;
    end;

    set
    begin
      Value := value;
    end;
  end;
end;

public class Program
begin
  public static var LastBox: Box<String>;

  public static function Echo(box: Box<String>): Box<String>;
  begin
    return box;
  end;
end;
""");

if (genericBindingTree.Root.Members[0] is not ClassDeclarationSyntax parsedGenericClass ||
    parsedGenericClass.Identifier.Text != "Box" ||
    parsedGenericClass.TypeParameters?.Parameters.Count != 1 ||
    parsedGenericClass.TypeParameters.Parameters[0].Text != "T")
{
    failures.Add("Parser should capture generic class type parameters.");
}

var genericBinding = new Binder().Bind(genericBindingTree);
if (genericBinding.HasErrors)
{
    failures.Add($"Binder should accept generic type declarations and closed generic references. Actual: {string.Join(", ", genericBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message))}");
}
else
{
    var boxDefinition = genericBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Box");
    var genericProgramType = genericBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Program");
    if (debugEnabled)
    {
        Console.Error.WriteLine(
            "debug.generics.types=" +
            string.Join(
                " | ",
                genericBinding.Compilation.Types
                    .OfType<NamedTypeSymbol>()
                    .Select(type => $"{type.Name}#arity={type.GenericArity}#args={(type.TypeArguments is null ? "-" : string.Join(",", type.TypeArguments.Select(argument => argument.Name)))}#fields={type.Fields.Count}#properties={type.Properties.Count}")));

        var debugLastBoxField = genericProgramType?.Fields.FirstOrDefault(field => field.Name == "LastBox");
        var debugEchoMethod = genericProgramType?.Methods.FirstOrDefault(method => method.Name == "Echo");
        Console.Error.WriteLine($"debug.generics.lastBoxFieldType={debugLastBoxField?.Type.Name ?? "<null>"}");
        Console.Error.WriteLine($"debug.generics.lastBoxFieldRuntimeType={debugLastBoxField?.Type.GetType().Name ?? "<null>"}");
        Console.Error.WriteLine($"debug.generics.echoReturnType={debugEchoMethod?.ReturnType.Name ?? "<null>"}");
        Console.Error.WriteLine($"debug.generics.echoParamType={(debugEchoMethod is null || debugEchoMethod.Parameters.Count == 0 ? "<null>" : debugEchoMethod.Parameters[0].Type.Name)}");
    }

    if (boxDefinition is null || boxDefinition.GenericArity != 1 || boxDefinition.GenericParameters?.Count != 1 || boxDefinition.GenericParameters[0].Name != "T")
    {
        failures.Add("Binder should expose generic type definitions with their declared type parameters.");
    }
    else if (boxDefinition.Fields.Count != 1 || boxDefinition.Fields[0].Type.Name != "T" ||
             !boxDefinition.Properties.Any(property => property.Name == "Item" && property.Type.Name == "T"))
    {
        failures.Add("Binder should keep generic member signatures on the open generic definition.");
    }

    if (genericProgramType is null)
    {
        failures.Add("Binder should surface Program in the generic fixture.");
    }
    else
    {
        var lastBoxField = genericProgramType.Fields.FirstOrDefault(field => field.Name == "LastBox");
        var echoMethod = genericProgramType.Methods.FirstOrDefault(method => method.Name == "Echo");
        if (lastBoxField?.Type is not NamedTypeSymbol lastBoxType ||
            lastBoxType.Name != "Box<String>" ||
            lastBoxType.GenericDefinition?.Name != "Box" ||
            lastBoxType.TypeArguments?.Count != 1 ||
            lastBoxType.TypeArguments[0] != TypeSymbol.String)
        {
            failures.Add("Binder should construct closed generic type instances for field references like Box<String>.");
        }
        else if (lastBoxType.Fields.Count != 1 || lastBoxType.Fields[0].Type != TypeSymbol.String ||
                 !lastBoxType.Properties.Any(property => property.Name == "Item" && property.Type == TypeSymbol.String))
        {
            failures.Add("Binder should substitute generic members on closed generic type instances.");
        }

        if (echoMethod is null ||
            echoMethod.ReturnType is not NamedTypeSymbol echoReturnType ||
            echoMethod.Parameters.Count != 1 ||
            echoMethod.Parameters[0].Type is not NamedTypeSymbol echoParameterType ||
            echoReturnType.Name != "Box<String>" ||
            echoParameterType.Name != "Box<String>" ||
            echoReturnType.TypeArguments?.Count != 1 ||
            echoReturnType.TypeArguments[0] != TypeSymbol.String)
        {
            failures.Add("Binder should propagate closed generic type instances through method signatures.");
        }
    }
}

var genericReachabilityTree = SyntaxTree.Parse("""
public class Box<T>
begin
  private var Value: T;

  public property Item: T
  begin
    get
    begin
      return Value;
    end;

    set(value)
    begin
      Value := value;
    end;
  end;
end;

public class Program
begin
  public static function Main(): Integer;
  begin
    var box := new Box<Integer>();
    box.Item := 41;
    return box.Item + 1;
  end;
end;
""");

var genericReachabilityBinding = new Binder().Bind(genericReachabilityTree);
if (genericReachabilityBinding.Diagnostics.Count > 0)
{
    failures.Add(
        "Binder should accept closed generic construction with property accessor use. Actual: " +
        string.Join(", ", genericReachabilityBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message)));
}
else
{
    var genericMain = genericReachabilityBinding.Compilation.EntryPoint;
    if (genericMain is null)
    {
        failures.Add("Binder should resolve the generic reachability entry point.");
    }
    else
    {
        var genericClosure = ReachableCompilationBuilder.Build(
            [genericMain],
            genericReachabilityBinding.Compilation.GetAllMethods(),
            genericReachabilityBinding.Compilation.GetAllFields(),
            genericReachabilityBinding.Compilation.Types,
            genericReachabilityBinding.Compilation.GetAllProperties(),
            genericReachabilityBinding.Compilation.GetAllConstants());
        if (debugEnabled)
        {
            Console.Error.WriteLine(
                "debug.reachability.generic=" +
                $"methods={genericClosure.Methods.Count},functions={genericClosure.Functions.Count},propertyAccessors={genericClosure.Stats.PropertyAccessorMethodsEnqueued},types=" +
                string.Join("|", genericClosure.Types.Select(type => type.Name)) +
                ",methodNames=" +
                string.Join("|", genericClosure.Methods.Select(method => $"{method.DeclaringTypeName}.{method.Name}")));
        }

        if (!genericClosure.Types.Any(type => type.Name == "Box<Integer>") ||
            !genericClosure.Methods.Any(method => method.Name == "get_Item") ||
            !genericClosure.Methods.Any(method => method.Name == "set_Item") ||
            genericClosure.Functions.Count != genericClosure.Methods.Count)
        {
            failures.Add("Reachability should retain closed generic types, property accessors and one lowered IR function per reachable method.");
        }
    }
}

var interfaceMethodDispatchTree = SyntaxTree.Parse("""
public interface IWorker
begin
  public function Run(value: Integer): Integer;
end;

public class Worker: IWorker
begin
  public function Run(value: Integer): Integer;
  begin
    return value + 1;
  end;
end;

public class Program
begin
  public static function Probe(worker: IWorker): Integer;
  begin
    return worker.Run(1);
  end;
end;
""");

var interfaceMethodDispatchBinding = new Binder().Bind(interfaceMethodDispatchTree);
if (interfaceMethodDispatchBinding.HasErrors)
{
    failures.Add($"Binder should allow interface method dispatch through interface-typed receivers. Actual: {string.Join(", ", interfaceMethodDispatchBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message))}");
}

var interfacePropertyReadDispatchTree = SyntaxTree.Parse("""
public interface IWorker
begin
  public property Name: String { get; };
end;

public class Worker: IWorker
begin
  public property Name: String
  begin
    get
    begin
      return 'demo';
    end;
  end;
end;

public class Program
begin
  public static function Probe(worker: IWorker): Integer;
  begin
    if worker.Name = 'demo' then
    begin
      return worker.Name.Length;
    end;

    return 0;
  end;
end;
""");

var interfacePropertyReadDispatchBinding = new Binder().Bind(interfacePropertyReadDispatchTree);
if (interfacePropertyReadDispatchBinding.HasErrors)
{
    failures.Add($"Binder should allow interface property reads through interface-typed receivers. Actual: {string.Join(", ", interfacePropertyReadDispatchBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message))}");
}

var interfacePropertyWriteDispatchTree = SyntaxTree.Parse("""
public interface IWorker
begin
  public property Name: String { get; set; };
end;

public class Worker: IWorker
begin
  public property Name: String
  begin
    get
    begin
      return 'start';
    end;

    set(value)
    begin
    end;
  end;
end;

public class Program
begin
  public static function Probe(worker: IWorker): Integer;
  begin
    worker.Name := 'demo';
    return worker.Name.Length;
  end;
end;
""");

var interfacePropertyWriteDispatchBinding = new Binder().Bind(interfacePropertyWriteDispatchTree);
if (interfacePropertyWriteDispatchBinding.HasErrors)
{
    failures.Add($"Binder should allow interface property writes through interface-typed receivers. Actual: {string.Join(", ", interfacePropertyWriteDispatchBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message))}");
}

var inheritedInterfaceDispatchTree = SyntaxTree.Parse("""
public interface IWorker
begin
  public function Boost(value: Integer): Integer;
  public property Name: String { get; set; };
end;

public interface IAdvancedWorker: IWorker
begin
  public function Bonus(): Integer;
end;

public class Worker: IAdvancedWorker
begin
  private var NameValue: String;

  public property Name: String
  begin
    get
    begin
      return NameValue;
    end;

    set(value)
    begin
      NameValue := value;
    end;
  end;

  public constructor;
  begin
    NameValue := 'worker';
  end;

  public function Boost(value: Integer): Integer;
  begin
    return value + 4;
  end;

  public function Bonus(): Integer;
  begin
    return 7;
  end;
end;

public class Program
begin
  public static function Probe(worker: IAdvancedWorker): Integer;
  begin
    worker.Name := 'advanced';
    return worker.Bonus() + worker.Boost(3) + worker.Name.Length;
  end;
end;
""");

var inheritedInterfaceDispatchBinding = new Binder().Bind(inheritedInterfaceDispatchTree);
if (inheritedInterfaceDispatchBinding.HasErrors)
{
    failures.Add($"Binder should allow inherited interface method and property dispatch through sub-interface receivers. Actual: {string.Join(", ", inheritedInterfaceDispatchBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message))}");
}
else
{
    var probeMethod = inheritedInterfaceDispatchBinding.Compilation.GetAllMethods()
        .FirstOrDefault(method => method.Name == "Probe" && method.DeclaringTypeName == "Program");
    if (probeMethod is null)
    {
        failures.Add("Binder should expose the inherited interface dispatch probe method.");
    }
    else
    {
        var interfaceDispatchClosure = ReachableCompilationBuilder.Build(
            [probeMethod],
            inheritedInterfaceDispatchBinding.Compilation.GetAllMethods(),
            inheritedInterfaceDispatchBinding.Compilation.GetAllFields(),
            inheritedInterfaceDispatchBinding.Compilation.Types,
            inheritedInterfaceDispatchBinding.Compilation.GetAllProperties(),
            inheritedInterfaceDispatchBinding.Compilation.GetAllConstants());
        if (debugEnabled)
        {
            Console.Error.WriteLine(
                "debug.reachability.interfaceDispatch=" +
                $"methods={interfaceDispatchClosure.Methods.Count},functions={interfaceDispatchClosure.Functions.Count},interfaceDispatch={interfaceDispatchClosure.Stats.InterfaceDispatchMethodsEnqueued},methodNames=" +
                string.Join("|", interfaceDispatchClosure.Methods.Select(method => $"{method.DeclaringTypeName}.{method.Name}")));
        }

        if (interfaceDispatchClosure.Stats.InterfaceDispatchMethodsEnqueued == 0 ||
            !interfaceDispatchClosure.Methods.Any(method => method.Name == "Boost" && method.DeclaringTypeName == "IWorker") ||
            !interfaceDispatchClosure.Methods.Any(method => method.Name == "Boost" && method.DeclaringTypeName == "Worker") ||
            !interfaceDispatchClosure.Methods.Any(method => method.Name == "get_Name" && method.DeclaringTypeName == "Worker") ||
            interfaceDispatchClosure.Functions.Count != interfaceDispatchClosure.Methods.Count)
        {
            failures.Add("Reachability should retain inherited interface dispatch methods, concrete implementations, property accessors and matching lowered IR functions.");
        }
    }
}

var resolverRegressionTree = SyntaxTree.Parse("""
public class View
begin
end;

public class StackPanel: View
begin
  public function ChildCount(): Integer;
  begin
    return 1;
  end;
end;

public class Host
begin
  private var ContentValue: View;

  public property Content: View
  begin
    get
    begin
      return ContentValue;
    end;

    set(value)
    begin
      ContentValue := value;
    end;
  end;

  public method Probe(input: View): Integer;
  begin
    self.Content := input;
    var panel := (self.Content as StackPanel);
    var count := panel.ChildCount() + (self.Content as StackPanel).ChildCount();
    return count;
  end;
end;
""");

var resolverRegressionBinding = new Binder().Bind(resolverRegressionTree);
if (resolverRegressionBinding.Diagnostics.Count > 0)
{
    failures.Add(
        "Binder should resolve local names, self property access and casted receiver member calls without false diagnostics. Actual: " +
        string.Join(", ", resolverRegressionBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message)));
}
else
{
    var resolverProbe = resolverRegressionBinding.Compilation.GetAllMethods()
        .FirstOrDefault(method => method.Name == "Probe" && method.DeclaringTypeName == "Host");
    if (resolverProbe is null)
    {
        failures.Add("Binder should expose the resolver regression probe method.");
    }
    else
    {
        var resolverIr = new Lowerer(
            resolverRegressionBinding.Compilation.GetAllMethods(),
            resolverRegressionBinding.Compilation.GetAllFields(),
            resolverRegressionBinding.Compilation.Types,
            resolverRegressionBinding.Compilation.GetAllProperties(),
            resolverRegressionBinding.Compilation.GetAllConstants()).Lower(resolverProbe);
        var resolverInstructions = resolverIr.Blocks.SelectMany(block => block.Instructions).ToArray();
        if (debugEnabled)
        {
            Console.Error.WriteLine(
                "debug.resolver.regression.ir=" +
                string.Join("|", resolverInstructions.Select(instruction => instruction.OpCode.ToString())));
        }

        if (!resolverInstructions.Any(instruction => instruction.OpCode == IrOpCode.AsReference) ||
            !resolverInstructions.Any(instruction =>
                instruction.OpCode is IrOpCode.Call or IrOpCode.CallVirtual &&
                (instruction.Operand is not IrCallTarget callTarget || callTarget.Method?.Name == "ChildCount")))
        {
            failures.Add("Lowerer should preserve casted receiver access and virtual calls after self/property resolver fast paths.");
        }
    }
}

var invalidInheritanceTree = SyntaxTree.Parse("""
public enum Value
begin
  One;
end;

public class UnknownBase: MissingType
begin
end;

public class NotClassBase: Value
begin
end;

public class CycleA: CycleB
begin
end;

public class CycleB: CycleA
begin
end;

public class StaticVirtualBase
begin
  public static virtual function Mark(): Integer;
  begin
    return 1;
  end;
end;

public class StaticVirtualOverride: StaticVirtualBase
begin
  public static override function Mark(): Integer;
  begin
    return 2;
  end;
end;

public class BadOverride: Value
begin
  public virtual override function Mixed(): Integer;
  begin
    return 3;
  end;
end;

public class NoVirtualBase
begin
  public function BaseOnly(): Integer;
  begin
    return 1;
  end;
end;

public class MissingVirtualOverride: NoVirtualBase
begin
  public override function BaseOnly(): Integer;
  begin
    return 2;
  end;
end;

public class SignatureBase
begin
  public virtual function Sum(left: Integer; right: Integer): Integer;
  begin
    return left + right;
  end;
end;

public class SignatureMismatch: SignatureBase
begin
  public override function Sum(left: Integer): Integer;
  begin
    return left;
  end;
end;
""");

var invalidInheritanceBinding = new Binder().Bind(invalidInheritanceTree);

if (!invalidInheritanceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2193"))
{
    failures.Add("Binder should report unknown base types for classes.");
}

if (!invalidInheritanceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2194"))
{
    failures.Add("Binder should report non-class base types.");
}

if (!invalidInheritanceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2195"))
{
    failures.Add("Binder should report inheritance cycles.");
}

if (!invalidInheritanceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2196"))
{
    failures.Add("Binder should report static members marked virtual or override.");
}

if (!invalidInheritanceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2197"))
{
    failures.Add("Binder should report members marked as both virtual and override.");
}

if (!invalidInheritanceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2198"))
{
    failures.Add("Binder should report override members with no matching virtual base method.");
}

if (!invalidInheritanceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2199"))
{
    failures.Add("Binder should report overrides with signature mismatches.");
}

var invalidInterfaceTree = SyntaxTree.Parse("""
public enum NotAnInterface
begin
  One;
end;

public interface IMissingBase: UnknownInterface
begin
end;

public interface IWrongBase: NotAnInterface
begin
end;

public interface IInvalidMembers
begin
  public var Value: Integer;
  public const Fixed = 1;
  public constructor;
  public function Run(): Integer;
  begin
    return 1;
  end;
  public property Name: String
  begin
    get
    begin
      return 'demo';
    end;
  end;
end;

public interface IRequired
begin
  public function Run(value: Integer): Integer;
end;

public class BaseClass
begin
end;

public class WrongImplementation: BaseClass, NotAnInterface
begin
end;

public class UnknownInterfaceImplementation: BaseClass, UnknownInterface
begin
end;

public class MissingImplementation: IRequired
begin
end;
""");

var invalidInterfaceBinding = new Binder().Bind(invalidInterfaceTree);
if (!invalidInterfaceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2200"))
{
    failures.Add("Binder should report unknown implemented interfaces for classes.");
}

if (!invalidInterfaceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2201"))
{
    failures.Add("Binder should report non-interface implemented types for classes.");
}

if (!invalidInterfaceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2202"))
{
    failures.Add("Binder should report unknown base interfaces.");
}

if (!invalidInterfaceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2203"))
{
    failures.Add("Binder should report non-interface base interfaces.");
}

if (!invalidInterfaceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2204"))
{
    failures.Add("Binder should report interface fields.");
}

if (!invalidInterfaceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2205"))
{
    failures.Add("Binder should report interface constants.");
}

if (!invalidInterfaceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2206"))
{
    failures.Add("Binder should report interface constructors.");
}

if (!invalidInterfaceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2207"))
{
    failures.Add("Binder should report interface methods with bodies.");
}

if (!invalidInterfaceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2208"))
{
    failures.Add("Binder should report interface properties with bodies.");
}

if (!invalidInterfaceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2209"))
{
    failures.Add("Binder should report missing interface method implementations.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2117"))
{
    failures.Add("Binder should report unknown property backing fields.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2118"))
{
    failures.Add("Binder should report property backing fields with invalid static or instance semantics.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2120"))
{
    failures.Add("Binder should report assignments to read-only properties.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2121"))
{
    failures.Add("Binder should report inaccessible private property getters.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2122"))
{
    failures.Add("Binder should report inaccessible private property setters.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2123"))
{
    failures.Add("Binder should report assignments to init-only properties outside constructors.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2124"))
{
    failures.Add("Binder should report Length access on non-array values.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2125"))
{
    failures.Add("Binder should report indexing on non-array values.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2126"))
{
    failures.Add("Binder should report non-Integer array indices.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2127"))
{
    failures.Add("Binder should report assignments through string indices.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2129"))
{
    failures.Add("Binder should report array rank mismatches.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2131"))
{
    failures.Add("Binder should report unknown target types in type tests and casts.");
}

if (!invalidReferenceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2132"))
{
    failures.Add("Binder should report non-reference targets in as-casts.");
}

var invalidRethrowTree = SyntaxTree.Parse("""
public class Program
begin
  public method Main;
  begin
    raise;
  end;
end;
""");

var invalidRethrowBinding = new Binder().Bind(invalidRethrowTree);
if (!invalidRethrowBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2133"))
{
    failures.Add("Binder should report bare raise outside an except handler.");
}

var finallyExitTree = SyntaxTree.Parse("""
public class Program
begin
  public static function ReturnFromTry: Integer;
  begin
    Result := 0;
    try
      return 1;
    finally
      Result := Result + 10;
    end;
  end;

  public static function ExitFromTry: Integer;
  begin
    Result := 0;
    try
      exit 1;
    finally
      Result := 2;
    end;
  end;

  public static function NestedExit: Integer;
  begin
    Result := 0;
    try
      try
        exit 1;
      finally
        Result := Result + 10;
      end;
    finally
      Result := Result + 100;
    end;
  end;
end;
""");

var finallyExitBinding = new Binder().Bind(finallyExitTree);
if (finallyExitBinding.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
{
    failures.Add("Binder should allow early routine exit inside try/finally.");
}
else
{
    var finallyExitProgram = finallyExitBinding.Compilation.Types.OfType<NamedTypeSymbol>().First(type => type.Name == "Program");
    var finallyExitLowerer = new Lowerer(finallyExitProgram.Methods, finallyExitProgram.Fields, finallyExitBinding.Compilation.Types, finallyExitProgram.Properties, finallyExitProgram.Constants);
    var returnFromTryIr = finallyExitLowerer.Lower(finallyExitProgram.Methods.First(method => method.Name == "ReturnFromTry"));
    var exitFromTryIr = finallyExitLowerer.Lower(finallyExitProgram.Methods.First(method => method.Name == "ExitFromTry"));
    var nestedExitIr = finallyExitLowerer.Lower(finallyExitProgram.Methods.First(method => method.Name == "NestedExit"));
    var returnFromTryInstructions = returnFromTryIr.Blocks.SelectMany(block => block.Instructions).ToArray();
    var exitFromTryInstructions = exitFromTryIr.Blocks.SelectMany(block => block.Instructions).ToArray();
    var nestedExitInstructions = nestedExitIr.Blocks.SelectMany(block => block.Instructions).ToArray();
    if (!returnFromTryIr.ExceptionHandlers.Any() ||
        !exitFromTryIr.ExceptionHandlers.Any() ||
        !nestedExitIr.ExceptionHandlers.Any())
    {
        failures.Add("Lowerer should preserve exception handler metadata for try/finally exits.");
    }

    if (!returnFromTryInstructions.Any(instruction => instruction.OpCode == IrOpCode.Branch && instruction.Operand is string label && label.StartsWith("finally_exit_", StringComparison.Ordinal)) ||
        returnFromTryInstructions.Count(instruction => instruction.OpCode == IrOpCode.Return) != 1)
    {
        failures.Add("Lowerer should route return inside try/finally through a finally exit path.");
    }

    if (!exitFromTryInstructions.Any(instruction => instruction.OpCode == IrOpCode.Branch && instruction.Operand is string label && label.StartsWith("finally_exit_", StringComparison.Ordinal)) ||
        exitFromTryInstructions.Count(instruction => instruction.OpCode == IrOpCode.Return) != 1)
    {
        failures.Add("Lowerer should route exit inside try/finally through a finally exit path.");
    }

    if (nestedExitInstructions.Count(instruction => instruction.OpCode == IrOpCode.Branch && instruction.Operand is string label && label.StartsWith("finally_exit_", StringComparison.Ordinal)) < 2 ||
        nestedExitInstructions.Count(instruction => instruction.OpCode == IrOpCode.Return) != 1)
    {
        failures.Add("Lowerer should chain nested try/finally exit paths from inner to outer finally blocks.");
    }
}

var tryFlowAssignmentTree = SyntaxTree.Parse("""
public class Program
begin
  public static function ResultFromTry: Integer;
  begin
    try
      Result := 1;
    finally
    end;
  end;

  public static function ResultFromFinally: Integer;
  begin
    try
    finally
      Result := 1;
    end;
  end;

  public static function OutFromTry(out value: Integer): Boolean;
  begin
    try
      value := 1;
    finally
    end;

    return true;
  end;

  public static function OutFromTryExcept(out value: Integer): Boolean;
  begin
    try
      value := 1;
    except
      value := 2;
    end;

    return true;
  end;

  public static function LocalFromTry: Integer;
  begin
    var value: Integer;
    try
      value := 1;
    finally
    end;

    return value;
  end;

  public static function LocalFromFinally: Integer;
  begin
    var value: Integer;
    try
    finally
      value := 1;
    end;

    return value;
  end;

  public static function MissingOutInExcept(out value: Integer): Boolean;
  begin
    try
      value := 1;
    except
    end;

    return true;
  end;

  public static function MissingLocalInExcept: Integer;
  begin
    var value: Integer;
    try
      value := 1;
    except
    end;

    return value;
  end;
end;
""");

var tryFlowAssignmentBinding = new Binder().Bind(tryFlowAssignmentTree);
var tryFlowAssignmentDiagnostics = tryFlowAssignmentBinding.Diagnostics.ToArray();
if (tryFlowAssignmentDiagnostics.Count(diagnostic => diagnostic.Id == "ILC2256") != 1)
{
    failures.Add(
        "Binder should merge required out assignments through try/except/finally paths. Actual: " +
        string.Join(" | ", tryFlowAssignmentDiagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}")));
}

if (tryFlowAssignmentDiagnostics.Count(diagnostic => diagnostic.Id == "ILC2257") != 1)
{
    failures.Add(
        "Binder should merge local definite assignments through try/except/finally paths. Actual: " +
        string.Join(" | ", tryFlowAssignmentDiagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}")));
}

if (tryFlowAssignmentDiagnostics.Any(diagnostic => diagnostic.Id == "ILC2255"))
{
    failures.Add(
        "Binder should accept Result assignments made by continuing try/finally paths. Actual: " +
        string.Join(" | ", tryFlowAssignmentDiagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}")));
}

var invalidTypedCatchTree = SyntaxTree.Parse("""
public class Program
begin
  public method Main;
  begin
    try
      raise 1;
    except
      on ex: Integer do
        raise;
    end;
  end;
end;
""");

var invalidTypedCatchBinding = new Binder().Bind(invalidTypedCatchTree);
if (!invalidTypedCatchBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2135"))
{
    failures.Add("Binder should report non-reference typed exception handlers.");
}

var invalidForLoopTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    var text := 'abcd';
    for text := 0 to 2 do
    begin
    end;
  end;
end;
""");

var invalidForLoopBinding = new Binder().Bind(invalidForLoopTree);
if (!invalidForLoopBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2137"))
{
    failures.Add("Binder should report non-Integer for-loop variables.");
}

var invalidForeachTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    var scalar := 1;
    var item: Integer;
    foreach item in scalar do
    begin
    end;
  end;
end;
""");

var invalidForeachBinding = new Binder().Bind(invalidForeachTree);
if (!invalidForeachBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2141"))
{
    failures.Add("Binder should report non-enumerable foreach sources.");
}

var invalidLoopControlTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    break;
    continue;
  end;
end;
""");

var invalidLoopControlBinding = new Binder().Bind(invalidLoopControlTree);
if (!invalidLoopControlBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2143"))
{
    failures.Add("Binder should report break outside loops.");
}

if (!invalidLoopControlBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2144"))
{
    failures.Add("Binder should report continue outside loops.");
}

var invalidCaseTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    var scalar := 1;
    case scalar of
      'text':
        scalar := scalar + 1;
      scalar:
        scalar := scalar + 2;
    end;

    case true of
      1:
        scalar := scalar + 3;
    end;

    case 'ab' of
      'aa'..'ac':
        scalar := scalar + 4;
    end;

    case Mode.Busy of
      Mode.Busy when 1:
        scalar := scalar + 5;
    end;
  end;
end;
""");

var invalidCaseBinding = new Binder().Bind(invalidCaseTree);
if (!invalidCaseBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2145"))
{
    failures.Add("Binder should report unsupported case expression types.");
}

if (!invalidCaseBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2146"))
{
    failures.Add("Binder should report case label type mismatches.");
}

if (!invalidCaseBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2147"))
{
    failures.Add("Binder should report non-literal case labels.");
}

if (!invalidCaseBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2159"))
{
    failures.Add("Binder should report unsupported string case ranges.");
}

if (!invalidCaseBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2192"))
{
    failures.Add("Binder should report non-Boolean case guards.");
}

var invalidMatchTree = SyntaxTree.Parse("""
public enum Mode
begin
  Idle;
  Busy;
end;

public class Program
begin
  public static method Main;
  begin
    var flag := true;
    match flag with
      true => return;
    end match;

    var text := match Mode.Busy with
      Mode.Busy => 'busy'
    end;

    var source := 'abc';
    var guarded := match source with
      String s when 1 => s
      _ => 'x'
    end;

    var typed := match source with
      Program p => 'oops'
      _ => 'ok'
    end;

    var relational := match 'abc' with
      >= 1 => 'bad'
      _ => 'ok'
    end;

    var relationalOperand := match 5 with
      >= 'a' => 'bad'
      _ => 'ok'
    end;

    var relationalAnd := match 5 with
      >= 1 and <= 'a' => 'bad'
      _ => 'ok'
    end;

    var negatedRelational := match 'abc' with
      not < 1 => 'bad'
      _ => 'ok'
    end;
  end;
end;
""");

var invalidMatchBinding = new Binder().Bind(invalidMatchTree);
if (!invalidMatchBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2175"))
{
    failures.Add("Binder should report unsupported match expression types.");
}

if (!invalidMatchBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2176"))
{
    failures.Add("Binder should require a wildcard arm for match expressions.");
}

if (!invalidMatchBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2178"))
{
    failures.Add("Binder should report incompatible typed match arms.");
}

if (!invalidMatchBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2179"))
{
    failures.Add("Binder should report non-Boolean match guards.");
}

if (!invalidMatchBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2180"))
{
    failures.Add("Binder should report relational match patterns on non-Integer match expressions.");
}

if (!invalidMatchBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2181"))
{
    failures.Add("Binder should report non-Integer operands in relational match patterns.");
}

var externTree = SyntaxTree.Parse("""
namespace System;
uses Sys = System;

public class Console
begin
  public static extern method Write(text: String);
  public static extern method WriteLine(text: String);
end;

public class Environment
begin
  private static extern function GetCommandLineArgsCore: array of String;
  private static extern function GetCurrentDirectoryCore: String;
  private static extern function GetUserNameCore: String;
  private static extern function GetMachineNameCore: String;
  private static extern function GetHomeDirectoryCore: String;
  private static extern function GetTempDirectoryCore: String;
  private static extern function GetEnvironmentVariableCore(name: String): String;
  private static extern method SetEnvironmentVariableCore(name: String; value: String);

  public static property CommandLineArgs: array of String
  begin
    get
    begin
      return GetCommandLineArgsCore();
    end;
  end;

  public static property CurrentDirectory: String
  begin
    get
    begin
      return GetCurrentDirectoryCore();
    end;
  end;

  public static property UserName: String
  begin
    get
    begin
      return GetUserNameCore();
    end;
  end;

  public static property MachineName: String
  begin
    get
    begin
      return GetMachineNameCore();
    end;
  end;

  public static property HomeDirectory: String
  begin
    get
    begin
      return GetHomeDirectoryCore();
    end;
  end;

  public static property TempDirectory: String
  begin
    get
    begin
      return GetTempDirectoryCore();
    end;
  end;

  public static property Variables[name: String]: String
  begin
    get
    begin
      return GetEnvironmentVariableCore(name);
    end;

    set(value)
    begin
      SetEnvironmentVariableCore(name, value);
    end;
  end;
end;

public class Clock
begin
  private static extern function GetMonotonicMillisecondsTextCore: String;
  private static extern function GetWallMillisecondsTextCore: String;
  private static extern function GetWallDateTimeTextCore: String;

  public static property MonotonicMillisecondsText: String
  begin
    get
    begin
      return GetMonotonicMillisecondsTextCore();
    end;
  end;

  public static property WallMillisecondsText: String
  begin
    get
    begin
      return GetWallMillisecondsTextCore();
    end;
  end;

  public static property WallDateTimeText: String
  begin
    get
    begin
      return GetWallDateTimeTextCore();
    end;
  end;
end;

public class File
begin
  private static extern function ExistsCore(path: String): Boolean;
  private static extern function ReadAllTextCore(path: String): String;
  private static extern method WriteAllTextCore(path: String; text: String);
  private static extern method AppendAllTextCore(path: String; text: String);

  public static function Exists(path: String): Boolean;
  begin
    return ExistsCore(path);
  end;

  public static function ReadAllText(path: String): String;
  begin
    return ReadAllTextCore(path);
  end;

  public static method WriteAllText(path: String; text: String);
  begin
    WriteAllTextCore(path, text);
  end;

  public static method AppendAllText(path: String; text: String);
  begin
    AppendAllTextCore(path, text);
  end;
end;

public class Path
begin
  private static extern function CombineCore(left: String; right: String): String;
  private static extern function GetFileNameCore(path: String): String;
  private static extern function GetDirectoryNameCore(path: String): String;
  private static extern function GetExtensionCore(path: String): String;

  public static function Combine(left: String; right: String): String;
  begin
    return CombineCore(left, right);
  end;

  public static function GetFileName(path: String): String;
  begin
    return GetFileNameCore(path);
  end;

  public static function GetDirectoryName(path: String): String;
  begin
    return GetDirectoryNameCore(path);
  end;

  public static function GetExtension(path: String): String;
  begin
    return GetExtensionCore(path);
  end;
end;

public class Program
begin
  public static function Main: Integer;
  begin
    Sys.Console.Write('HELLO>');
    Sys.Console.WriteLine('Hello World');
    Sys.Console.WriteLine('ARGS=' + Sys.Environment.CommandLineArgs.Length.ToString());
    Sys.Console.WriteLine('CWD=' + Sys.Environment.CurrentDirectory);
    Sys.Console.WriteLine('USER=' + Sys.Environment.UserName);
    Sys.Console.WriteLine('HOST=' + Sys.Environment.MachineName);
    Sys.Console.WriteLine('HOME=' + Sys.Environment.HomeDirectory);
    Sys.Environment.Variables['HOME'] := '/alt-home';
    Sys.Console.WriteLine('ENV=' + Sys.Environment.Variables['HOME']);
    Sys.Console.WriteLine('TEMP=' + Sys.Environment.TempDirectory);
    Sys.Console.WriteLine('MONO=' + Sys.Clock.MonotonicMillisecondsText);
    Sys.Console.WriteLine('PATH=' + Sys.Path.Combine('/test', 'input.txt'));
    Sys.Console.WriteLine('NAME=' + Sys.Path.GetFileName('/test/input.txt'));
    Sys.Console.WriteLine('DIR=' + Sys.Path.GetDirectoryName('/test/input.txt'));
    Sys.Console.WriteLine('EXT=' + Sys.Path.GetExtension('/test/input.txt'));
    if Sys.File.Exists('/test/input.txt') then
    begin
      Sys.Console.WriteLine('FILE=' + Sys.File.ReadAllText('/test/input.txt'));
    end;
    Sys.File.WriteAllText('/test/output.txt', 'written');
    Sys.File.AppendAllText('/test/output.txt', '-more');
    return Sys.Environment.CommandLineArgs.Length;
  end;
end;
""");

var externBinding = new Binder().Bind(externTree);
if (externBinding.Diagnostics.Count > 0)
{
    failures.Add($"Binder should accept supported extern host methods without diagnostics. Actual: {string.Join(", ", externBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message))}");
}
else
{
    var externConsole = externBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Console");
    var externWrite = externConsole?.Methods.FirstOrDefault(method => method.Name == "Write");
    if (externWrite is null || !externWrite.IsExtern || externWrite.HostImportKind != HostImportKind.ConsoleWrite)
    {
        failures.Add("Binder should mark Console.Write(String) as an extern host import.");
    }

    var externWriteLine = externConsole?.Methods.FirstOrDefault(method => method.Name == "WriteLine");
    if (externWriteLine is null || !externWriteLine.IsExtern || externWriteLine.HostImportKind != HostImportKind.ConsoleWriteLine)
    {
        failures.Add("Binder should mark Console.WriteLine(String) as an extern host import.");
    }
    else
    {
        var externEnvironment = externBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Environment");
        var externGetCommandLineArgs = externEnvironment?.Methods.FirstOrDefault(method => method.Name == "GetCommandLineArgsCore");
        if (externGetCommandLineArgs is null || !externGetCommandLineArgs.IsExtern || externGetCommandLineArgs.HostImportKind != HostImportKind.EnvironmentGetCommandLineArgs)
        {
            failures.Add("Binder should mark Environment.GetCommandLineArgsCore() as an extern host import.");
        }

        var externGetCurrentDirectory = externEnvironment?.Methods.FirstOrDefault(method => method.Name == "GetCurrentDirectoryCore");
        if (externGetCurrentDirectory is null || !externGetCurrentDirectory.IsExtern || externGetCurrentDirectory.HostImportKind != HostImportKind.EnvironmentGetCurrentDirectory)
        {
            failures.Add("Binder should mark Environment.GetCurrentDirectoryCore() as an extern host import.");
        }

        var externGetUserName = externEnvironment?.Methods.FirstOrDefault(method => method.Name == "GetUserNameCore");
        if (externGetUserName is null || !externGetUserName.IsExtern || externGetUserName.HostImportKind != HostImportKind.EnvironmentGetUserName)
        {
            failures.Add("Binder should mark Environment.GetUserNameCore() as an extern host import.");
        }

        var externGetMachineName = externEnvironment?.Methods.FirstOrDefault(method => method.Name == "GetMachineNameCore");
        if (externGetMachineName is null || !externGetMachineName.IsExtern || externGetMachineName.HostImportKind != HostImportKind.EnvironmentGetMachineName)
        {
            failures.Add("Binder should mark Environment.GetMachineNameCore() as an extern host import.");
        }

        var externGetHomeDirectory = externEnvironment?.Methods.FirstOrDefault(method => method.Name == "GetHomeDirectoryCore");
        if (externGetHomeDirectory is null || !externGetHomeDirectory.IsExtern || externGetHomeDirectory.HostImportKind != HostImportKind.EnvironmentGetHomeDirectory)
        {
            failures.Add("Binder should mark Environment.GetHomeDirectoryCore() as an extern host import.");
        }

        var externGetTempDirectory = externEnvironment?.Methods.FirstOrDefault(method => method.Name == "GetTempDirectoryCore");
        if (externGetTempDirectory is null || !externGetTempDirectory.IsExtern || externGetTempDirectory.HostImportKind != HostImportKind.EnvironmentGetTempDirectory)
        {
            failures.Add("Binder should mark Environment.GetTempDirectoryCore() as an extern host import.");
        }

        var externGetEnvironmentVariable = externEnvironment?.Methods.FirstOrDefault(method => method.Name == "GetEnvironmentVariableCore");
        if (externGetEnvironmentVariable is null || !externGetEnvironmentVariable.IsExtern || externGetEnvironmentVariable.HostImportKind != HostImportKind.EnvironmentGetEnvironmentVariable)
        {
            failures.Add("Binder should mark Environment.GetEnvironmentVariableCore(String) as an extern host import.");
        }

        var externSetEnvironmentVariable = externEnvironment?.Methods.FirstOrDefault(method => method.Name == "SetEnvironmentVariableCore");
        if (externSetEnvironmentVariable is null || !externSetEnvironmentVariable.IsExtern || externSetEnvironmentVariable.HostImportKind != HostImportKind.EnvironmentSetEnvironmentVariable)
        {
            failures.Add("Binder should mark Environment.SetEnvironmentVariableCore(String, String) as an extern host import.");
        }

        var externClock = externBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Clock");
        var externGetMonotonicMillisecondsText = externClock?.Methods.FirstOrDefault(method => method.Name == "GetMonotonicMillisecondsTextCore");
        if (externGetMonotonicMillisecondsText is null || !externGetMonotonicMillisecondsText.IsExtern || externGetMonotonicMillisecondsText.HostImportKind != HostImportKind.ClockGetMonotonicMillisecondsText)
        {
            failures.Add("Binder should mark Clock.GetMonotonicMillisecondsTextCore() as an extern host import.");
        }

        var externGetWallMillisecondsText = externClock?.Methods.FirstOrDefault(method => method.Name == "GetWallMillisecondsTextCore");
        if (externGetWallMillisecondsText is null || !externGetWallMillisecondsText.IsExtern || externGetWallMillisecondsText.HostImportKind != HostImportKind.ClockGetWallMillisecondsText)
        {
            failures.Add("Binder should mark Clock.GetWallMillisecondsTextCore() as an extern host import.");
        }

        var externGetWallDateTimeText = externClock?.Methods.FirstOrDefault(method => method.Name == "GetWallDateTimeTextCore");
        if (externGetWallDateTimeText is null || !externGetWallDateTimeText.IsExtern || externGetWallDateTimeText.HostImportKind != HostImportKind.ClockGetWallDateTimeText)
        {
            failures.Add("Binder should mark Clock.GetWallDateTimeTextCore() as an extern host import.");
        }

        if (!(externEnvironment?.Properties.Any(property => property.Name == "CommandLineArgs" && property.IsStatic) ?? false))
        {
            failures.Add("Binder should expose Environment.CommandLineArgs as a static property.");
        }

        if (!(externEnvironment?.Properties.Any(property => property.Name == "CurrentDirectory" && property.IsStatic) ?? false))
        {
            failures.Add("Binder should expose Environment.CurrentDirectory as a static property.");
        }

        if (!(externEnvironment?.Properties.Any(property => property.Name == "Variables" && property.IsStatic && property.IsIndexer && property.IndexParameter?.Type == TypeSymbol.String) ?? false))
        {
            failures.Add("Binder should expose Environment.Variables as a static String indexer property.");
        }

        if (!(externClock?.Properties.Any(property => property.Name == "MonotonicMillisecondsText" && property.IsStatic) ?? false))
        {
            failures.Add("Binder should expose Clock.MonotonicMillisecondsText as a static property.");
        }
        else if (!(externClock?.Properties.Any(property => property.Name == "WallDateTimeText" && property.IsStatic) ?? false))
        {
            failures.Add("Binder should expose Clock.WallDateTimeText as a static property.");
        }

        var externFile = externBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "File");
        var externFileExists = externFile?.Methods.FirstOrDefault(method => method.Name == "ExistsCore");
        if (externFileExists is null || !externFileExists.IsExtern || externFileExists.HostImportKind != HostImportKind.FileExists)
        {
            failures.Add("Binder should mark File.ExistsCore(String) as an extern host import.");
        }

        var externFileReadAllText = externFile?.Methods.FirstOrDefault(method => method.Name == "ReadAllTextCore");
        if (externFileReadAllText is null || !externFileReadAllText.IsExtern || externFileReadAllText.HostImportKind != HostImportKind.FileReadAllText)
        {
            failures.Add("Binder should mark File.ReadAllTextCore(String) as an extern host import.");
        }

        var externFileWriteAllText = externFile?.Methods.FirstOrDefault(method => method.Name == "WriteAllTextCore");
        if (externFileWriteAllText is null || !externFileWriteAllText.IsExtern || externFileWriteAllText.HostImportKind != HostImportKind.FileWriteAllText)
        {
            failures.Add("Binder should mark File.WriteAllTextCore(String, String) as an extern host import.");
        }

        var externFileAppendAllText = externFile?.Methods.FirstOrDefault(method => method.Name == "AppendAllTextCore");
        if (externFileAppendAllText is null || !externFileAppendAllText.IsExtern || externFileAppendAllText.HostImportKind != HostImportKind.FileAppendAllText)
        {
            failures.Add("Binder should mark File.AppendAllTextCore(String, String) as an extern host import.");
        }

        var externPath = externBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Path");
        var externPathCombine = externPath?.Methods.FirstOrDefault(method => method.Name == "CombineCore");
        if (externPathCombine is null || !externPathCombine.IsExtern || externPathCombine.HostImportKind != HostImportKind.PathCombine)
        {
            failures.Add("Binder should mark Path.CombineCore(String, String) as an extern host import.");
        }

        var externPathGetFileName = externPath?.Methods.FirstOrDefault(method => method.Name == "GetFileNameCore");
        if (externPathGetFileName is null || !externPathGetFileName.IsExtern || externPathGetFileName.HostImportKind != HostImportKind.PathGetFileName)
        {
            failures.Add("Binder should mark Path.GetFileNameCore(String) as an extern host import.");
        }

        var externPathGetDirectoryName = externPath?.Methods.FirstOrDefault(method => method.Name == "GetDirectoryNameCore");
        if (externPathGetDirectoryName is null || !externPathGetDirectoryName.IsExtern || externPathGetDirectoryName.HostImportKind != HostImportKind.PathGetDirectoryName)
        {
            failures.Add("Binder should mark Path.GetDirectoryNameCore(String) as an extern host import.");
        }

        var externPathGetExtension = externPath?.Methods.FirstOrDefault(method => method.Name == "GetExtensionCore");
        if (externPathGetExtension is null || !externPathGetExtension.IsExtern || externPathGetExtension.HostImportKind != HostImportKind.PathGetExtension)
        {
            failures.Add("Binder should mark Path.GetExtensionCore(String) as an extern host import.");
        }

        var externProgram = externBinding.Compilation.Types.OfType<NamedTypeSymbol>().First(type => type.Name == "Program");
        var externMain = externProgram.Methods.First(method => method.Name == "Main");
        var externMethods = externBinding.Compilation.GetAllMethods();
        var externFields = externBinding.Compilation.GetAllFields();
        var externProperties = externBinding.Compilation.GetAllProperties();
        var externConstants = externBinding.Compilation.GetAllConstants();
        var externLowerer = new Lowerer(externMethods, externFields, externBinding.Compilation.Types, externProperties, externConstants);
        var externModule = new BytecodeEmitter().EmitModule(externMethods, externFields, externBinding.Compilation.Types, externLowerer);
        var importedWriteFunction = externModule.Functions.FirstOrDefault(function => function.Name == "Write");
        if (importedWriteFunction is null || importedWriteFunction.HostImportKind != HostImportKind.ConsoleWrite)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Console.Write(String).");
        }
        var importedFunction = externModule.Functions.FirstOrDefault(function => function.Name == "WriteLine");
        if (importedFunction is null || importedFunction.HostImportKind != HostImportKind.ConsoleWriteLine)
        {
            failures.Add("Bytecode emission should preserve host import metadata for extern methods.");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "GetCommandLineArgsCore")?.HostImportKind != HostImportKind.EnvironmentGetCommandLineArgs)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Environment.GetCommandLineArgsCore().");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "GetCurrentDirectoryCore")?.HostImportKind != HostImportKind.EnvironmentGetCurrentDirectory)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Environment.GetCurrentDirectoryCore().");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "GetUserNameCore")?.HostImportKind != HostImportKind.EnvironmentGetUserName)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Environment.GetUserNameCore().");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "GetMachineNameCore")?.HostImportKind != HostImportKind.EnvironmentGetMachineName)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Environment.GetMachineNameCore().");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "GetHomeDirectoryCore")?.HostImportKind != HostImportKind.EnvironmentGetHomeDirectory)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Environment.GetHomeDirectoryCore().");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "GetTempDirectoryCore")?.HostImportKind != HostImportKind.EnvironmentGetTempDirectory)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Environment.GetTempDirectoryCore().");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "GetEnvironmentVariableCore")?.HostImportKind != HostImportKind.EnvironmentGetEnvironmentVariable)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Environment.GetEnvironmentVariableCore(String).");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "SetEnvironmentVariableCore")?.HostImportKind != HostImportKind.EnvironmentSetEnvironmentVariable)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Environment.SetEnvironmentVariableCore(String, String).");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "GetMonotonicMillisecondsTextCore")?.HostImportKind != HostImportKind.ClockGetMonotonicMillisecondsText)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Clock.GetMonotonicMillisecondsTextCore().");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "GetWallMillisecondsTextCore")?.HostImportKind != HostImportKind.ClockGetWallMillisecondsText)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Clock.GetWallMillisecondsTextCore().");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "GetWallDateTimeTextCore")?.HostImportKind != HostImportKind.ClockGetWallDateTimeText)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Clock.GetWallDateTimeTextCore().");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "ExistsCore")?.HostImportKind != HostImportKind.FileExists)
        {
            failures.Add("Bytecode emission should preserve host import metadata for File.ExistsCore(String).");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "ReadAllTextCore")?.HostImportKind != HostImportKind.FileReadAllText)
        {
            failures.Add("Bytecode emission should preserve host import metadata for File.ReadAllTextCore(String).");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "WriteAllTextCore")?.HostImportKind != HostImportKind.FileWriteAllText)
        {
            failures.Add("Bytecode emission should preserve host import metadata for File.WriteAllTextCore(String, String).");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "AppendAllTextCore")?.HostImportKind != HostImportKind.FileAppendAllText)
        {
            failures.Add("Bytecode emission should preserve host import metadata for File.AppendAllTextCore(String, String).");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "CombineCore")?.HostImportKind != HostImportKind.PathCombine)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Path.CombineCore(String, String).");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "GetFileNameCore")?.HostImportKind != HostImportKind.PathGetFileName)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Path.GetFileNameCore(String).");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "GetDirectoryNameCore")?.HostImportKind != HostImportKind.PathGetDirectoryName)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Path.GetDirectoryNameCore(String).");
        }
        else if (externModule.Functions.FirstOrDefault(function => function.Name == "GetExtensionCore")?.HostImportKind != HostImportKind.PathGetExtension)
        {
            failures.Add("Bytecode emission should preserve host import metadata for Path.GetExtensionCore(String).");
        }

        var externMainFunction = externModule.Functions.First(function => function.Name == externMain.Name);
        if (externMainFunction.Instructions.Count(instruction => instruction.OpCode == OpCode.Call) < 17)
        {
            failures.Add("Calls to extern host methods should still lower through the call opcode.");
        }
        else if (!externMainFunction.Instructions.Any(instruction => instruction.OpCode == OpCode.LdLen))
        {
            failures.Add("Environment.CommandLineArgs.Length should lower to a length load in the extern fixture.");
        }
        else if (externMainFunction.Instructions.Any(instruction => instruction.OpCode == OpCode.LdField && instruction.Left == 0) ||
            externMainFunction.Instructions.Any(instruction => instruction.OpCode == OpCode.StField && instruction.Destination == 0))
        {
            failures.Add("Extern-backed static access in Main must not emit instance field access with receiver register 0.");
        }
    }
}

var invalidExternBodyTree = SyntaxTree.Parse("""
public class Console
begin
  public static extern method WriteLine(text: String);
  begin
    return;
  end;
end;
""");

var invalidExternBodyBinding = new Binder().Bind(invalidExternBodyTree);
if (!invalidExternBodyBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2182"))
{
    failures.Add("Binder should reject extern methods with bodies.");
}

var invalidExternHostTree = SyntaxTree.Parse("""
public class Os
begin
  public static extern method Shell(text: String);
end;
""");

var invalidExternHostBinding = new Binder().Bind(invalidExternHostTree);
if (!invalidExternHostBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2183"))
{
    failures.Add("Binder should reject unsupported extern host imports.");
}

var delegateMethodGroupTree = SyntaxTree.Parse("""
public delegate function IntProjector(value: Integer): Integer;

public class DelegateHost
begin
  public static function DoubleValue(value: Integer): Integer;
  begin
    return value * 2;
  end;

  public static function Apply(projector: IntProjector; value: Integer): Integer;
  begin
    return value;
  end;

  public static function Test(): Integer;
  begin
    var projector: IntProjector := DoubleValue;
    projector := DoubleValue;
    var projected := projector(5);
    return Apply(DoubleValue, projected);
  end;
end;
""");

if (delegateMethodGroupTree.Root.Members[0] is not DelegateDeclarationSyntax parsedDelegate ||
    parsedDelegate.Identifier.Text != "IntProjector" ||
    parsedDelegate.SignatureKeyword.Kind != SyntaxKind.FunctionKeyword ||
    parsedDelegate.Parameters.Count != 1 ||
    parsedDelegate.ReturnType?.ToDisplayString() != "Integer")
{
    failures.Add("Parser should capture nominal delegate declarations.");
}

var delegateMethodGroupBinding = new Binder().Bind(delegateMethodGroupTree);
if (delegateMethodGroupBinding.Diagnostics.Count > 0)
{
    failures.Add(
        "Binder should accept method-group conversion and direct delegate invocation for delegate-typed locals, assignments, and arguments. Diagnostics: " +
        string.Join(
            " | ",
            delegateMethodGroupBinding.Diagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}@{diagnostic.Span.Start}")));
}

var lambdaTree = SyntaxTree.Parse("""
public delegate function IntProjector(value: Integer): Integer;

public class LambdaHost
begin
  public static function Apply(projector: IntProjector; value: Integer): Integer;
  begin
    return projector(value);
  end;

  public static function Test(): Integer;
  begin
    var projector: IntProjector := function(value: Integer): Integer => value * 2;
    return Apply(function(value: Integer): Integer => value + 1, projector(5));
  end;
end;
""");

if (lambdaTree.Root.Members[1] is not ClassDeclarationSyntax lambdaClass ||
    lambdaClass.Members.OfType<MethodDeclarationSyntax>()
        .FirstOrDefault(method => method.Identifier.Text == "Test")?
        .Body?.Statements.OfType<LocalVariableDeclarationStatementSyntax>()
        .FirstOrDefault()?
        .Declarators[0].Initializer is not LambdaExpressionSyntax parsedLambda ||
    parsedLambda.SignatureKeyword.Kind != SyntaxKind.FunctionKeyword ||
    parsedLambda.Parameters.Count != 1 ||
    parsedLambda.ReturnType?.ToDisplayString() != "Integer")
{
    failures.Add("Parser should capture expression-bodied lambda expressions.");
}

var lambdaBinding = new Binder().Bind(lambdaTree);
if (lambdaBinding.Diagnostics.Count > 0)
{
    failures.Add(
        "Binder should accept target-typed non-capturing lambda expressions for delegate locals and arguments. Diagnostics: " +
        string.Join(
            " | ",
            lambdaBinding.Diagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}@{diagnostic.Span.Start}")));
}
else if (!lambdaBinding.Compilation.Methods.Any(method =>
             method.LambdaSource is not null &&
             method.IsStatic &&
             method.DeclaringTypeName == "LambdaHost" &&
             method.ReturnType == TypeSymbol.Integer &&
             method.Parameters.Count == 1 &&
             method.Parameters[0].Type == TypeSymbol.Integer))
{
    failures.Add("Binder should synthesize static helper methods for non-capturing lambda expressions.");
}

var capturingLambdaTree = SyntaxTree.Parse("""
public delegate function IntProjector(value: Integer): Integer;

public class LambdaCaptureHost
begin
  public static function Test(): Integer;
  begin
    var factor := 3;
    var projector: IntProjector := function(value: Integer): Integer => value * factor;
    return projector(5);
  end;
end;
""");

var capturingLambdaBinding = new Binder().Bind(capturingLambdaTree);
if (capturingLambdaBinding.Diagnostics.Count > 0)
{
    failures.Add(
        "Binder should accept capturing lambda expressions over outer locals. Diagnostics: " +
        string.Join(
            " | ",
            capturingLambdaBinding.Diagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}@{diagnostic.Span.Start}")));
}
else if (!capturingLambdaBinding.Compilation.Types.OfType<NamedTypeSymbol>().Any(type =>
             type.Name.StartsWith("__LambdaClosure_", StringComparison.Ordinal) &&
             type.Fields.Any(field => field.Name == "factor" && field.Type == TypeSymbol.Integer) &&
             type.Methods.Any(method => method.LambdaSource is not null && !method.IsStatic)))
{
    failures.Add("Binder should synthesize closure types with capture fields for capturing lambdas.");
}
else
{
    var captureTestMethod = capturingLambdaBinding.Compilation.GetAllMethods()
        .FirstOrDefault(method => method.Name == "Test" && method.DeclaringTypeName == "LambdaCaptureHost");
    if (captureTestMethod is null)
    {
        failures.Add("Binder should expose the capturing lambda test method.");
    }
    else
    {
        var captureClosure = ReachableCompilationBuilder.Build(
            [captureTestMethod],
            capturingLambdaBinding.Compilation.GetAllMethods(),
            capturingLambdaBinding.Compilation.GetAllFields(),
            capturingLambdaBinding.Compilation.Types,
            capturingLambdaBinding.Compilation.GetAllProperties(),
            capturingLambdaBinding.Compilation.GetAllConstants());
        if (debugEnabled)
        {
            Console.Error.WriteLine(
                "debug.reachability.lambdaCapture=" +
                $"methods={captureClosure.Methods.Count},functions={captureClosure.Functions.Count},types=" +
                string.Join("|", captureClosure.Types.Select(type => type.Name)));
        }

        if (!captureClosure.Types.OfType<NamedTypeSymbol>().Any(type => type.Name.StartsWith("__LambdaClosure_", StringComparison.Ordinal)) ||
            !captureClosure.Methods.Any(method => method.LambdaSource is not null && !method.IsStatic) ||
            captureClosure.Functions.Count != captureClosure.Methods.Count)
        {
            failures.Add("Reachability should retain capturing lambda closure types, instance lambda bodies and matching lowered IR functions.");
        }
    }
}

var enumerablePipelineTree = SyntaxTree.Parse("""
uses System.Collections;

public class EnumerableHost
begin
  public static function Test(): Integer;
  begin
    var words := new List<String>();
    words.Add('one');
    words.Add('two');
    words.Add('three');

    var filtered := Enumerable<String>.Where(words, function(value: String): Boolean => value.Contains('w'));
    var projected := Enumerable<String, Integer>.Select(filtered, function(value: String): Integer => value.Length);
    var enumerator := projected.GetEnumerator();
    var sum := 0;
    while enumerator.MoveNext() do
    begin
      sum := sum + enumerator.Current;
    end;

    return sum;
  end;
end;
""");

var queryExpressionTree = SyntaxTree.Parse("""
uses System.Collections;

public class QueryHost
begin
  public static function Test(): Integer;
  begin
    var words := new List<String>();
    words.Add('one');
    words.Add('two');
    words.Add('three');

    var projected :=
      from value in words
      join other in words on value equals other
      let projectedLength := other.Length
      where other.Contains('o')
      orderby projectedLength descending
      thenby value.Length
      select projectedLength
      into length
      let doubledLength := length * 2
      where doubledLength > 5
      orderby doubledLength descending
      thenby length
      select doubledLength
      take 1
      skip 0;

    var enumerator := projected.GetEnumerator();
    var sum := 0;
    while enumerator.MoveNext() do
    begin
      sum := sum + enumerator.Current;
    end;

    return sum;
  end;
end;
""");

var groupJoinQueryTree = SyntaxTree.Parse("""
uses System.Collections;

public class GroupJoinHost
begin
  public static function Test(): Integer;
  begin
    var words := new List<String>();
    words.Add('one');
    words.Add('two');
    words.Add('three');

    var grouped :=
      from value in words
      join other in words on value.Length equals other.Length into matches
      let matchCount := Enumerable<String>.Count(matches)
      where matchCount > 1
      select matchCount;

    var enumerator := grouped.GetEnumerator();
    var sum := 0;
    while enumerator.MoveNext() do
    begin
      sum := sum + enumerator.Current;
    end;

    return sum;
  end;
end;
""");

var groupByQueryTree = SyntaxTree.Parse("""
uses System.Collections;

public class GroupByHost
begin
  public static function Test(): Integer;
  begin
    var words := new List<String>();
    words.Add('one');
    words.Add('two');
    words.Add('three');

    var grouped :=
      from value in words
      group value by value.Length
      into grouping
      where Enumerable<String>.Count(grouping) > 1
      select grouping.Key;

    var enumerator := grouped.GetEnumerator();
    var sum := 0;
    while enumerator.MoveNext() do
    begin
      sum := sum + enumerator.Current;
    end;

    return sum;
  end;
end;
""");

var queryProjectionTree = SyntaxTree.Parse("""
uses System.Collections;

public class QueryProjectionHost
begin
  public static function Test(): Integer;
  begin
    var words := new List<String>();
    words.Add('one');
    words.Add('two');
    words.Add('three');

    var projected :=
      from value in words
      where value.Contains('w')
      select new { PrimaryLength := value.Length, SecondaryLength := value.Length + 1 };

    var enumerator := projected.GetEnumerator();
    var sum := 0;
    while enumerator.MoveNext() do
    begin
      sum := sum + enumerator.Current.PrimaryLength;
      sum := sum + enumerator.Current.SecondaryLength;
    end;

    return sum;
  end;
end;
""");

if (queryExpressionTree.Root.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault(type => type.Identifier.Text == "QueryHost") is not ClassDeclarationSyntax queryClass ||
    queryClass.Members.OfType<MethodDeclarationSyntax>()
        .FirstOrDefault(method => method.Identifier.Text == "Test")?
        .Body?.Statements.OfType<LocalVariableDeclarationStatementSyntax>()
        .FirstOrDefault(statement => statement.Declarators.Any(declarator => declarator.Identifier.Text == "projected"))?
        .Declarators.First(declarator => declarator.Identifier.Text == "projected").Initializer is not QueryExpressionSyntax parsedQuery ||
    parsedQuery.Identifier.Text != "value" ||
    parsedQuery.JoinIdentifier is null ||
    parsedQuery.JoinSourceExpression is null ||
    parsedQuery.JoinLeftExpression is null ||
    parsedQuery.JoinRightExpression is null ||
    parsedQuery.LetExpression is null ||
    parsedQuery.LetIdentifier is null ||
    parsedQuery.PredicateExpression is null ||
    parsedQuery.OrderByExpression is null ||
    parsedQuery.DescendingKeyword is null ||
    parsedQuery.ThenByKeyword is null ||
    parsedQuery.ThenByExpression is null ||
    parsedQuery.IntoIdentifier is null ||
    parsedQuery.ContinuationLetKeyword is null ||
    parsedQuery.ContinuationLetIdentifier is null ||
    parsedQuery.ContinuationLetExpression is null ||
    parsedQuery.ContinuationPredicateExpression is null ||
    parsedQuery.ContinuationOrderByExpression is null ||
    parsedQuery.ContinuationDescendingKeyword is null ||
    parsedQuery.ContinuationThenByKeyword is null ||
    parsedQuery.ContinuationThenByExpression is null ||
    parsedQuery.ContinuationSelectExpression is null ||
    parsedQuery.TakeExpression is null ||
    parsedQuery.SkipExpression is null)
{
    failures.Add("Parser should capture join/let/select-into/continuation-let/where/orderby/thenby descending/select/take/skip query expressions.");
}

if (groupJoinQueryTree.Root.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault(type => type.Identifier.Text == "GroupJoinHost") is not ClassDeclarationSyntax groupJoinClass ||
    groupJoinClass.Members.OfType<MethodDeclarationSyntax>()
        .FirstOrDefault(method => method.Identifier.Text == "Test")?
        .Body?.Statements.OfType<LocalVariableDeclarationStatementSyntax>()
        .FirstOrDefault(statement => statement.Declarators.Any(declarator => declarator.Identifier.Text == "grouped"))?
        .Declarators.First(declarator => declarator.Identifier.Text == "grouped").Initializer is not QueryExpressionSyntax parsedGroupJoinQuery ||
    parsedGroupJoinQuery.JoinIdentifier is null ||
    parsedGroupJoinQuery.JoinSourceExpression is null ||
    parsedGroupJoinQuery.JoinLeftExpression is null ||
    parsedGroupJoinQuery.JoinRightExpression is null ||
    parsedGroupJoinQuery.JoinIntoIdentifier is null ||
    parsedGroupJoinQuery.LetExpression is null ||
    parsedGroupJoinQuery.LetIdentifier is null ||
    parsedGroupJoinQuery.PredicateExpression is null)
{
    failures.Add("Parser should capture group-join query expressions.");
}

if (groupByQueryTree.Root.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault(type => type.Identifier.Text == "GroupByHost") is not ClassDeclarationSyntax groupByClass ||
    groupByClass.Members.OfType<MethodDeclarationSyntax>()
        .FirstOrDefault(method => method.Identifier.Text == "Test")?
        .Body?.Statements.OfType<LocalVariableDeclarationStatementSyntax>()
        .FirstOrDefault(statement => statement.Declarators.Any(declarator => declarator.Identifier.Text == "grouped"))?
        .Declarators.First(declarator => declarator.Identifier.Text == "grouped").Initializer is not QueryExpressionSyntax parsedGroupByQuery ||
    parsedGroupByQuery.GroupKeyword is null ||
    parsedGroupByQuery.GroupExpression is null ||
    parsedGroupByQuery.GroupByKeyword is null ||
    parsedGroupByQuery.GroupByExpression is null ||
    parsedGroupByQuery.IntoIdentifier is null ||
    parsedGroupByQuery.ContinuationPredicateExpression is null ||
    parsedGroupByQuery.ContinuationSelectExpression is null)
{
    failures.Add("Parser should capture group-by into query expressions.");
}

if (queryProjectionTree.Root.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault(type => type.Identifier.Text == "QueryProjectionHost") is not ClassDeclarationSyntax queryProjectionClass ||
    queryProjectionClass.Members.OfType<MethodDeclarationSyntax>()
        .FirstOrDefault(method => method.Identifier.Text == "Test")?
        .Body?.Statements.OfType<LocalVariableDeclarationStatementSyntax>()
        .FirstOrDefault(statement => statement.Declarators.Any(declarator => declarator.Identifier.Text == "projected"))?
        .Declarators.First(declarator => declarator.Identifier.Text == "projected").Initializer is not QueryExpressionSyntax parsedProjectionQuery ||
    parsedProjectionQuery.PredicateExpression is null ||
    parsedProjectionQuery.SelectExpression is not ProjectorExpressionSyntax)
{
    failures.Add("Parser should capture anonymous query projections using select new { ... } expressions.");
}

var invalidQueryOperatorTree = SyntaxTree.Parse("""
uses System.Collections;

public class InvalidQueryHost
begin
  public static method Test;
  begin
    var words := new List<String>();
    words.Add('one');
    var numbers := new List<Integer>();
    numbers.Add(1);

    var badSource :=
      from value in 1
      select value;

    var badWhere :=
      from value in words
      where value.Length
      select value;

    var badOrder :=
      from value in words
      orderby value
      select value;

    var badJoinMismatch :=
      from value in words
      join number in numbers on value equals number
      select number;

    var badJoinKey :=
      from value in words
      join other in words on value.Contains('o') equals other.Contains('o')
      select other;

    var badGroupKey :=
      from value in words
      group value by value.Contains('o')
      into grouping
      select grouping.Key;

    var badTake :=
      from value in words
      select value
      take 'one';

    var badSkip :=
      from value in words
      select value
      skip value.Contains('o');
  end;
end;
""");

var invalidQueryOperatorMergedTree = SyntaxTree.Merge(invalidQueryOperatorTree, [systemTree, collectionsTree]);
var invalidQueryOperatorBinding = new Binder().Bind(invalidQueryOperatorMergedTree);
if (!invalidQueryOperatorBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2141"))
{
    failures.Add("Binder should reject query sources that are not enumerable.");
}

if (!invalidQueryOperatorBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2227"))
{
    failures.Add("Binder should require Boolean query where clauses.");
}

if (!invalidQueryOperatorBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2230"))
{
    failures.Add("Binder should restrict query orderby keys to supported key types.");
}

if (!invalidQueryOperatorBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2231"))
{
    failures.Add("Binder should reject query join keys with mismatched types.");
}

if (!invalidQueryOperatorBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2232"))
{
    failures.Add("Binder should reject unsupported query join key types.");
}

if (!invalidQueryOperatorBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2233"))
{
    failures.Add("Binder should reject unsupported query group-by key types.");
}

if (!invalidQueryOperatorBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2228"))
{
    failures.Add("Binder should require Integer query take counts.");
}

if (!invalidQueryOperatorBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2229"))
{
    failures.Add("Binder should require Integer query skip counts.");
}

var queryExpressionMergedTree = SyntaxTree.Merge(queryExpressionTree, [systemTree, collectionsTree]);
var queryExpressionBinding = new Binder().Bind(queryExpressionMergedTree);
if (queryExpressionBinding.Diagnostics.Count > 0)
{
    failures.Add(
        "Binder should accept query expressions over Enumerable sources. Diagnostics: " +
        string.Join(
            " | ",
            queryExpressionBinding.Diagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}@{diagnostic.Span.Start}")));
}
else if (queryExpressionBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "QueryHost") is not NamedTypeSymbol queryHostType ||
         queryHostType.Methods.FirstOrDefault(method => method.Name == "Test") is not MethodSymbol queryHostTestMethod)
{
    failures.Add("Binder should surface QueryHost.Test for the query-expression scenario.");
}
else
{
    var queryMethods = queryExpressionBinding.Compilation.GetAllMethods().ToArray();
    var queryFields = queryExpressionBinding.Compilation.GetAllFields();
    var queryProperties = queryExpressionBinding.Compilation.GetAllProperties();
    var queryConstants = queryExpressionBinding.Compilation.GetAllConstants();
    var queryProjectedDeclaration = queryHostTestMethod.Declaration?.Body?.Statements
        .OfType<LocalVariableDeclarationStatementSyntax>()
        .FirstOrDefault(statement => statement.Declarators.Any(declarator => declarator.Identifier.Text == "projected"));
    var queryProjectedInitializer = queryProjectedDeclaration?.Declarators
        .First(declarator => declarator.Identifier.Text == "projected")
        .Initializer;
    if (queryProjectedInitializer is null)
    {
        failures.Add("QueryHost.Test should contain a 'projected' query initializer.");
    }

    try
    {
        var queryLowerer = new Lowerer(
            queryMethods,
            queryFields,
            queryExpressionBinding.Compilation.Types,
            queryProperties,
            queryConstants);
        var queryIr = queryLowerer.Lower(queryHostTestMethod);
        var queryCalls = queryIr.Blocks
            .SelectMany(block => block.Instructions)
            .Where(instruction =>
                instruction.OpCode is IrOpCode.Call or IrOpCode.CallVirtual &&
                instruction.Operand is IrCallTarget { Method: not null })
            .Select(instruction => ((IrCallTarget)instruction.Operand!).Method!)
            .ToArray();

        if (!queryCalls.Any(method => method.Name == "Where" && method.IsStatic && method.DeclaringTypeName == "Enumerable<String>"))
        {
            failures.Add("Lowerer should translate query where-clauses to Enumerable<String>.Where(...).");
        }

        if (!queryCalls.Any(method => method.Name == "Join" && method.IsStatic && method.DeclaringTypeName == "Enumerable<String, String, String>"))
        {
            failures.Add("Lowerer should translate query join-clauses to Enumerable<String, String, String>.Join(...).");
        }

        if (!queryCalls.Any(method => method.Name == "Select" && method.IsStatic && method.DeclaringTypeName == "Enumerable<String, Integer>"))
        {
            failures.Add("Lowerer should translate query select-clauses to Enumerable<String, Integer>.Select(...).");
        }

        if (!queryCalls.Any(method => method.Name == "Select" && method.IsStatic && method.DeclaringTypeName == "Enumerable<Integer, Integer>"))
        {
            failures.Add("Lowerer should translate query into-continuations to Enumerable<Integer, Integer>.Select(...).");
        }

        if (!queryCalls.Any(method => method.Name == "OrderByDescending" && method.IsStatic && method.DeclaringTypeName == "Enumerable<String>"))
        {
            failures.Add("Lowerer should translate descending query orderby-clauses to Enumerable<String>.OrderByDescending(...).");
        }

        if (queryCalls.Count(method => method.Name == "OrderBy" && method.IsStatic && method.DeclaringTypeName == "Enumerable<String>") < 1)
        {
            failures.Add("Lowerer should translate secondary query orderby-keys to an additional Enumerable<String>.OrderBy(...).");
        }

        if (queryCalls.Count(method => method.Name == "OrderByDescending" && method.IsStatic && method.DeclaringTypeName == "Enumerable<Integer>") < 1)
        {
            failures.Add("Lowerer should translate continuation primary descending orderby-keys to Enumerable<Integer>.OrderByDescending(...).");
        }

        if (queryCalls.Count(method => method.Name == "OrderBy" && method.IsStatic && method.DeclaringTypeName == "Enumerable<Integer>") < 1)
        {
            failures.Add("Lowerer should translate continuation secondary orderby-keys to Enumerable<Integer>.OrderBy(...).");
        }

        if (!queryCalls.Any(method => method.Name == "Take" && method.IsStatic && method.DeclaringTypeName == "Enumerable<Integer>"))
        {
            failures.Add("Lowerer should translate query take-clauses to Enumerable<Integer>.Take(...).");
        }

        if (!queryCalls.Any(method => method.Name == "Skip" && method.IsStatic && method.DeclaringTypeName == "Enumerable<Integer>"))
        {
            failures.Add("Lowerer should translate query skip-clauses to Enumerable<Integer>.Skip(...).");
        }
    }
    catch (Exception ex)
    {
        failures.Add($"Lowerer should accept query expressions without throwing. Actual: {ex.Message}");
    }
}

var enumerablePipelineMergedTree = SyntaxTree.Merge(enumerablePipelineTree, [systemTree, collectionsTree]);
var enumerablePipelineBinding = new Binder().Bind(enumerablePipelineMergedTree);
if (enumerablePipelineBinding.Diagnostics.Count > 0)
{
    failures.Add(
        "Binder should accept Enumerable<T>.Where and Enumerable<TSource, TResult>.Select with lambda arguments. Diagnostics: " +
        string.Join(
            " | ",
            enumerablePipelineBinding.Diagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}@{diagnostic.Span.Start}")));
}
else if (enumerablePipelineBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "EnumerableHost") is not NamedTypeSymbol enumerableHostType ||
         enumerableHostType.Methods.FirstOrDefault(method => method.Name == "Test") is not MethodSymbol enumerableHostTestMethod)
{
    failures.Add("Binder should surface EnumerableHost.Test for the Enumerable pipeline scenario.");
}
else
{
    var enumerablePipelineMethods = enumerablePipelineBinding.Compilation.GetAllMethods().ToArray();
    var enumerablePipelineFields = enumerablePipelineBinding.Compilation.GetAllFields();
    var enumerablePipelineProperties = enumerablePipelineBinding.Compilation.GetAllProperties();
    var enumerablePipelineConstants = enumerablePipelineBinding.Compilation.GetAllConstants();
    static string FormatMethodDiagnostic(MethodSymbol method) =>
        $"{method.DeclaringTypeName}.{method.Name}({string.Join(", ", method.Parameters.Select(parameter => parameter.Type.Name))}):{method.ReturnType.Name}";

    BoundCall? BindEnumerableInitializerCall(string localName, out string diagnostic)
    {
        diagnostic = string.Empty;
        if (enumerableHostTestMethod.Declaration?.Body is null)
        {
            diagnostic = "method body is missing";
            return null;
        }

        var locals = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        foreach (var parameter in enumerableHostTestMethod.Parameters)
        {
            locals[parameter.Name] = parameter.Type;
        }

        foreach (var statement in enumerableHostTestMethod.Declaration.Body.Statements)
        {
            if (statement is not LocalVariableDeclarationStatementSyntax localVariableDeclaration)
            {
                continue;
            }

            foreach (var declarator in localVariableDeclaration.Declarators)
            {
                if (declarator.Initializer is CallExpressionSyntax callExpression &&
                    declarator.Identifier.Text == localName)
                {
                    var boundCall = SemanticFacts.BindCall(
                        callExpression,
                        locals,
                        enumerablePipelineBinding.Compilation.Types,
                        enumerablePipelineMethods,
                        enumerablePipelineFields,
                        enumerablePipelineConstants,
                        enumerablePipelineProperties,
                        enumerableHostTestMethod);
                    if (boundCall is null)
                    {
                        var flattenedTarget = callExpression.Target switch
                        {
                            NameExpressionSyntax nameExpression => nameExpression.Name.ToDisplayString(),
                            MemberAccessExpressionSyntax memberAccess => SemanticFacts.GetExpressionDisplayName(memberAccess),
                            _ => callExpression.Target.Kind.ToString()
                        };
                        var targetCandidates = enumerablePipelineMethods
                            .Where(method => method.Name == (callExpression.Target switch
                            {
                                NameExpressionSyntax nameExpression => nameExpression.Name.Parts[^1].Text,
                                MemberAccessExpressionSyntax memberAccess => memberAccess.MemberName.Text,
                                _ => string.Empty
                            }))
                            .Select(FormatMethodDiagnostic)
                            .ToArray();
                        diagnostic =
                            $"BindCall returned null for local '{localName}' target='{flattenedTarget}' locals=[{string.Join(", ", locals.Select(entry => $"{entry.Key}:{entry.Value.Name}"))}] candidates=[{string.Join(" | ", targetCandidates)}]";
                    }

                    return boundCall;
                }

                if (declarator.Initializer is not null)
                {
                    locals[declarator.Identifier.Text] = SemanticFacts.InferExpressionType(
                        declarator.Initializer,
                        locals,
                        enumerablePipelineMethods,
                        enumerablePipelineFields,
                        enumerablePipelineConstants,
                        enumerablePipelineProperties,
                        enumerableHostTestMethod,
                        enumerablePipelineBinding.Compilation.Types);
        }
    }
}

var groupJoinMergedTree = SyntaxTree.Merge(groupJoinQueryTree, [systemTree, collectionsTree]);
var groupJoinBinding = new Binder().Bind(groupJoinMergedTree);
if (groupJoinBinding.Diagnostics.Count > 0)
{
    failures.Add(
        "Binder should accept group-join query expressions. Diagnostics: " +
        string.Join(
            " | ",
            groupJoinBinding.Diagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}@{diagnostic.Span.Start}")));
}
else if (groupJoinBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "GroupJoinHost") is not NamedTypeSymbol groupJoinHostType ||
         groupJoinHostType.Methods.FirstOrDefault(method => method.Name == "Test") is not MethodSymbol groupJoinHostMethod)
{
    failures.Add("Binder should surface GroupJoinHost.Test for the group-join scenario.");
}
else
{
    try
    {
        var groupJoinLowerer = new Lowerer(
            groupJoinBinding.Compilation.GetAllMethods().ToArray(),
            groupJoinBinding.Compilation.GetAllFields(),
            groupJoinBinding.Compilation.Types,
            groupJoinBinding.Compilation.GetAllProperties(),
            groupJoinBinding.Compilation.GetAllConstants());
        var groupJoinIr = groupJoinLowerer.Lower(groupJoinHostMethod);
        if (!groupJoinIr.Blocks
                .SelectMany(block => block.Instructions)
                .Any(instruction =>
                    instruction.OpCode is IrOpCode.Call or IrOpCode.CallVirtual &&
                    instruction.Operand is MethodSymbol { DeclaringTypeName: "Enumerable<String, String, IEnumerable<String>>", Name: "GroupJoin" }))
        {
            failures.Add("Lowerer should translate group-join queries to Enumerable<String, String, IEnumerable<String>>.GroupJoin(...).");
        }
    }
    catch (Exception ex)
    {
        failures.Add("Lowerer should handle group-join queries without throwing. Actual: " + ex.Message);
    }
}

var groupByMergedTree = SyntaxTree.Merge(groupByQueryTree, [systemTree, collectionsTree]);
var groupByBinding = new Binder().Bind(groupByMergedTree);
if (groupByBinding.Diagnostics.Count > 0)
{
    failures.Add(
        "Binder should accept group-by query expressions. Diagnostics: " +
        string.Join(
            " | ",
            groupByBinding.Diagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}@{diagnostic.Span.Start}")));
}
else if (groupByBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "GroupByHost") is not NamedTypeSymbol groupByHostType ||
         groupByHostType.Methods.FirstOrDefault(method => method.Name == "Test") is not MethodSymbol groupByHostMethod)
{
    failures.Add("Binder should surface GroupByHost.Test for the group-by scenario.");
}
else
{
    try
    {
        var groupByLowerer = new Lowerer(
            groupByBinding.Compilation.GetAllMethods().ToArray(),
            groupByBinding.Compilation.GetAllFields(),
            groupByBinding.Compilation.Types,
            groupByBinding.Compilation.GetAllProperties(),
            groupByBinding.Compilation.GetAllConstants());
        var groupByIr = groupByLowerer.Lower(groupByHostMethod);
        if (!groupByIr.Blocks
                .SelectMany(block => block.Instructions)
                .Any(instruction =>
                    instruction.OpCode is IrOpCode.Call or IrOpCode.CallVirtual &&
                    instruction.Operand is MethodSymbol { DeclaringTypeName: "Enumerable<String, Integer, String>", Name: "GroupBy" }))
        {
            failures.Add("Lowerer should translate group-by queries to Enumerable<String, Integer, String>.GroupBy(...).");
        }

        if (!groupByIr.Blocks
                .SelectMany(block => block.Instructions)
                .Any(instruction =>
                    instruction.OpCode is IrOpCode.Call or IrOpCode.CallVirtual &&
                    instruction.Operand is MethodSymbol { DeclaringTypeName: "Enumerable<Grouping<Integer, String>>", Name: "Where" }))
        {
            failures.Add("Lowerer should translate group-by continuation filters to Enumerable<Grouping<Integer, String>>.Where(...).");
        }

        if (!groupByIr.Blocks
                .SelectMany(block => block.Instructions)
                .Any(instruction =>
                    instruction.OpCode is IrOpCode.Call or IrOpCode.CallVirtual &&
                    instruction.Operand is MethodSymbol { DeclaringTypeName: "Enumerable<Grouping<Integer, String>, Integer>", Name: "Select" }))
        {
            failures.Add("Lowerer should translate group-by into-continuations to Enumerable<Grouping<Integer, String>, Integer>.Select(...).");
        }
    }
    catch (Exception ex)
    {
        failures.Add("Lowerer should handle group-by queries without throwing. Actual: " + ex.Message);
    }
}

var queryProjectionMergedTree = SyntaxTree.Merge(queryProjectionTree, [systemTree, collectionsTree]);
var queryProjectionBinding = new Binder().Bind(queryProjectionMergedTree);
if (queryProjectionBinding.Diagnostics.Count > 0)
{
    failures.Add(
        "Binder should accept query projections with select new expressions. Diagnostics: " +
        string.Join(
            " | ",
            queryProjectionBinding.Diagnostics.Select(diagnostic => $"{diagnostic.Id}:{diagnostic.Message}@{diagnostic.Span.Start}")));
}
else if (queryProjectionBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "QueryProjectionHost") is not NamedTypeSymbol queryProjectionHostType ||
         queryProjectionHostType.Methods.FirstOrDefault(method => method.Name == "Test") is not MethodSymbol queryProjectionHostMethod)
{
    failures.Add("Binder should surface QueryProjectionHost.Test for the projection scenario.");
}
else
{
    try
    {
        var queryProjectionLowerer = new Lowerer(
            queryProjectionBinding.Compilation.GetAllMethods().ToArray(),
            queryProjectionBinding.Compilation.GetAllFields(),
            queryProjectionBinding.Compilation.Types,
            queryProjectionBinding.Compilation.GetAllProperties(),
            queryProjectionBinding.Compilation.GetAllConstants());
        var queryProjectionIr = queryProjectionLowerer.Lower(queryProjectionHostMethod);
        if (!queryProjectionIr.Blocks
                .SelectMany(block => block.Instructions)
                .Any(instruction =>
                    instruction.OpCode is IrOpCode.Call or IrOpCode.CallVirtual &&
                    instruction.Operand is MethodSymbol { Name: "Select", DeclaringTypeName: var declaringTypeName } &&
                    declaringTypeName is not null &&
                    declaringTypeName.StartsWith("Enumerable<String, __Projector_", StringComparison.Ordinal)))
        {
            failures.Add("Lowerer should translate anonymous query projections to Enumerable<String, __Projector_...>.Select(...).");
        }

        if (!queryProjectionIr.Blocks
                .SelectMany(block => block.Instructions)
                .Any(instruction =>
                    instruction.OpCode is IrOpCode.Call or IrOpCode.CallVirtual &&
                    instruction.Operand is MethodSymbol { Name: ".ctor", DeclaringTypeName: var projectorTypeName } &&
                    projectorTypeName is not null &&
                    projectorTypeName.StartsWith("__Projector_", StringComparison.Ordinal)))
        {
            failures.Add("Lowerer should materialize anonymous query projections through synthetic projector constructors.");
        }
    }
    catch (Exception ex)
    {
        failures.Add("Lowerer should handle select-new query projections without throwing. Actual: " + ex.Message);
    }
}

        diagnostic = $"local '{localName}' was not found in EnumerableHost.Test";
        return null;
    }

    var boundWhereCall = BindEnumerableInitializerCall("filtered", out var whereDiagnostic);
    if (boundWhereCall is null)
    {
        failures.Add($"Binder should bind Enumerable<String>.Where(...) to a concrete static generic method. {whereDiagnostic}");
    }
    else if (boundWhereCall.Method.DeclaringTypeName != "Enumerable<String>" ||
             boundWhereCall.Method.Parameters.Select(parameter => parameter.Type.Name).SequenceEqual(["IEnumerable<String>", "Predicate<String>"]) is false ||
             boundWhereCall.Method.ReturnType.Name != "IEnumerable<String>")
    {
        failures.Add(
            "Binder should fully close Enumerable<String>.Where(...). Actual: " +
            FormatMethodDiagnostic(boundWhereCall.Method));
    }

    var boundSelectCall = BindEnumerableInitializerCall("projected", out var selectDiagnostic);
    if (boundSelectCall is null)
    {
        failures.Add($"Binder should bind Enumerable<String, Integer>.Select(...) to a concrete static generic method. {selectDiagnostic}");
    }
    else if (boundSelectCall.Method.DeclaringTypeName != "Enumerable<String, Integer>" ||
             boundSelectCall.Method.Parameters.Select(parameter => parameter.Type.Name).SequenceEqual(["IEnumerable<String>", "Selector<String, Integer>"]) is false ||
             boundSelectCall.Method.ReturnType.Name != "IEnumerable<Integer>")
    {
        failures.Add(
            "Binder should fully close Enumerable<String, Integer>.Select(...). Actual: " +
            FormatMethodDiagnostic(boundSelectCall.Method));
    }

    try
    {
        var enumerablePipelineLowerer = new Lowerer(
            enumerablePipelineMethods,
            enumerablePipelineFields,
            enumerablePipelineBinding.Compilation.Types,
            enumerablePipelineProperties,
            enumerablePipelineConstants);
        var enumerablePipelineIr = enumerablePipelineLowerer.Lower(enumerableHostTestMethod);
        var enumerablePipelineCalls = enumerablePipelineIr.Blocks
            .SelectMany(block => block.Instructions)
            .Where(instruction =>
                instruction.OpCode is IrOpCode.Call or IrOpCode.CallVirtual &&
                instruction.Operand is IrCallTarget { Method: not null })
            .Select(instruction => ((IrCallTarget)instruction.Operand!).Method!)
            .ToArray();

        if (!enumerablePipelineCalls.Any(method => method.Name == "Where" && method.IsStatic && method.DeclaringTypeName == "Enumerable<String>"))
        {
            failures.Add("Lowerer should resolve Enumerable<String>.Where(...) on a static generic receiver.");
        }

        if (!enumerablePipelineCalls.Any(method => method.Name == "Select" && method.IsStatic && method.DeclaringTypeName == "Enumerable<String, Integer>"))
        {
            failures.Add("Lowerer should resolve Enumerable<String, Integer>.Select(...) on a static generic receiver.");
        }
    }
    catch (Exception ex)
    {
        failures.Add($"Lowerer should accept static generic Enumerable pipeline calls without throwing. Actual: {ex.Message}");
    }
}

var dllImportTree = SyntaxTree.Parse("""
public enum CallingConvention
begin
  Cdecl,
  StdCall
end;

public enum StringReturn
begin
  None,
  Utf8Owned
end;

public class Native
begin
  [DllImport('libc.so.6', EntryPoint := 'puts', CallingConvention := CallingConvention.Cdecl)]
  public static extern function Puts(text: String): Integer;
end;
""");

var dllImportBinding = new Binder().Bind(dllImportTree);
if (dllImportBinding.Diagnostics.Count > 0)
{
    failures.Add($"Binder should accept a valid DllImport declaration without diagnostics. Actual: {string.Join(", ", dllImportBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message))}");
}
else
{
    var nativeType = dllImportBinding.Compilation.Types.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == "Native");
    var putsMethod = nativeType?.Methods.FirstOrDefault(method => method.Name == "Puts");
    if (putsMethod is null || !putsMethod.IsExtern || putsMethod.IsStatic is false)
    {
        failures.Add("Binder should preserve extern/static metadata for DllImport methods.");
    }
    else if (putsMethod.HostImportKind != HostImportKind.None)
    {
        failures.Add("DllImport methods must stay separate from built-in host import mappings.");
    }
    else if (putsMethod.DllImport is null)
    {
        failures.Add("Binder should attach DllImport metadata to extern methods.");
    }
    else if (putsMethod.DllImport.LibraryName != "libc.so.6" ||
        putsMethod.DllImport.EntryPoint != "puts" ||
        putsMethod.DllImport.CallingConvention != NativeCallingConvention.Cdecl)
    {
        failures.Add("Binder should capture library name, entry point, and calling convention for DllImport.");
    }

    var syntaxMethod = dllImportTree.Root.Members
        .OfType<ClassDeclarationSyntax>()
        .First(type => type.Identifier.Text == "Native")
        .Members
        .OfType<MethodDeclarationSyntax>()
        .First(method => method.Identifier.Text == "Puts");
    if (syntaxMethod.Attributes.Count != 1 ||
        syntaxMethod.Attributes[0].Name.ToDisplayString() != "DllImport" ||
        syntaxMethod.Attributes[0].Arguments.Count != 3)
    {
        failures.Add("Syntax parser should preserve the DllImport attribute and its arguments on methods.");
    }

    var dllImportMethods = dllImportBinding.Compilation.GetAllMethods().ToArray();
    var dllImportFields = dllImportBinding.Compilation.GetAllFields();
    var dllImportProperties = dllImportBinding.Compilation.GetAllProperties();
    var dllImportConstants = dllImportBinding.Compilation.GetAllConstants();
    var dllImportModule = new BytecodeEmitter().EmitModule(
        dllImportMethods,
        dllImportFields,
        dllImportBinding.Compilation.Types,
        new Lowerer(
            dllImportMethods,
            dllImportFields,
            dllImportBinding.Compilation.Types,
            dllImportProperties,
            dllImportConstants));
    var emittedPuts = dllImportModule.Functions.FirstOrDefault(function => function.Name == "Puts");
    if (emittedPuts?.DllImport is null)
    {
        failures.Add("Bytecode emission should preserve DllImport metadata for extern native methods.");
    }
    else if (emittedPuts.DllImport.LibraryName != "libc.so.6" ||
        emittedPuts.DllImport.EntryPoint != "puts" ||
        emittedPuts.DllImport.CallingConvention != NativeCallingConvention.Cdecl)
    {
        failures.Add("Bytecode emission should keep the bound DllImport metadata unchanged.");
    }

    var dllImportIlb = new IlbSerializer().Serialize(
        dllImportModule,
        dllImportMethods,
        dllImportFields,
        dllImportBinding.Compilation.Types,
        null);
    var stringSection = dllImportIlb.Sections.First(section => section.Kind == IlbSectionKind.StringTable);
    var methodSection = dllImportIlb.Sections.First(section => section.Kind == IlbSectionKind.MethodTable);
    static uint ReadU32(byte[] bytes, int offset) =>
        (uint)(bytes[offset] |
            (bytes[offset + 1] << 8) |
            (bytes[offset + 2] << 16) |
            (bytes[offset + 3] << 24));
    static List<string> ReadStringTable(byte[] bytes, int offset)
    {
        var count = (int)ReadU32(bytes, offset);
        var cursor = offset + 4;
        var strings = new List<string> { string.Empty };
        for (var index = 0; index < count; index++)
        {
            var length = (int)ReadU32(bytes, cursor);
            cursor += 4;
            strings.Add(System.Text.Encoding.UTF8.GetString(bytes, cursor, length));
            cursor += length;
        }

        return strings;
    }

    var ilbStrings = ReadStringTable(dllImportIlb.Bytes, (int)stringSection.Offset);
    var putsRowOffset = (int)methodSection.Offset;
    var libraryStringId = ReadU32(dllImportIlb.Bytes, putsRowOffset + 46);
    var entryPointStringId = ReadU32(dllImportIlb.Bytes, putsRowOffset + 50);
    var callingConvention = ReadU32(dllImportIlb.Bytes, putsRowOffset + 54);
    if (libraryStringId == 0 || entryPointStringId == 0)
    {
        failures.Add("ILB method rows should carry string-table ids for DllImport library and entry point.");
    }
    else if (ilbStrings[(int)libraryStringId] != "libc.so.6" || ilbStrings[(int)entryPointStringId] != "puts")
    {
        failures.Add("ILB serialization should persist DllImport string metadata into the string table.");
    }
    else if (callingConvention != (uint)NativeCallingConvention.Cdecl)
    {
        failures.Add("ILB serialization should persist the native calling convention for DllImport methods.");
    }
}

var invalidDllImportExternTree = SyntaxTree.Parse("""
public class Native
begin
  [DllImport('libc.so.6')]
  public static function Puts(text: String): Integer;
  begin
    return 0;
  end;
end;
""");

var invalidDllImportExternBinding = new Binder().Bind(invalidDllImportExternTree);
if (!invalidDllImportExternBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2212"))
{
    failures.Add("Binder should reject DllImport methods that are not declared extern.");
}

var invalidDllImportStaticTree = SyntaxTree.Parse("""
public class Native
begin
  [DllImport('libc.so.6')]
  public extern function Puts(text: String): Integer;
end;
""");

var invalidDllImportStaticBinding = new Binder().Bind(invalidDllImportStaticTree);
if (!invalidDllImportStaticBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2213"))
{
    failures.Add("Binder should reject instance DllImport methods.");
}

var validDllImportAbiTree = SyntaxTree.Parse("""
public enum CallingConvention
begin
  Cdecl,
  StdCall
end;

public enum StringReturn
begin
  None,
  Utf8Owned
end;

public enum NativeHandle
begin
  Null = 0;
end;

public enum ErrorCode
begin
  Ok = 0;
  Failed = 1;
end;

public delegate function CompletionCallback(code: Integer): Integer;

public class Native
begin
  [DllImport('libffi-demo.so', EntryPoint := 'open_point', CallingConvention := CallingConvention.Cdecl)]
  public static extern function OpenPoint(handle: NativeHandle; code: ErrorCode; callback: CompletionCallback): NativeHandle;
end;
""");

var validDllImportAbiBinding = new Binder().Bind(validDllImportAbiTree);
if (validDllImportAbiBinding.Diagnostics.Count > 0)
{
    failures.Add($"Binder should accept FFI v1 ABI-safe DllImport signatures. Actual: {string.Join(", ", validDllImportAbiBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message))}");
}
else if (debugEnabled)
{
    var openPoint = validDllImportAbiBinding.Compilation.Types
        .OfType<NamedTypeSymbol>()
        .First(type => type.Name == "Native")
        .Methods
        .First(method => method.Name == "OpenPoint");
    var completionCallback = validDllImportAbiBinding.Compilation.Types
        .OfType<NamedTypeSymbol>()
        .First(type => type.Name == "CompletionCallback");
    var completionInvoke = completionCallback.Methods.First(method => method.Name == "Invoke" && !method.IsStatic);
    Console.Error.WriteLine(
        "debug.dllimport.abi.valid=" +
        $"{openPoint.Name}(" +
        string.Join(", ", openPoint.Parameters.Select(parameter => $"{parameter.PassingKind}:{parameter.Type.Name}")) +
        $"):{openPoint.ReturnType.Name}");
    Console.Error.WriteLine(
        "debug.dllimport.callback.valid=" +
        $"{completionCallback.Name}(" +
        string.Join(", ", completionInvoke.Parameters.Select(parameter => $"{parameter.PassingKind}:{parameter.Type.Name}")) +
        $"):{completionInvoke.ReturnType.Name}");
}

if (!validDllImportAbiBinding.HasErrors)
{
    var openPoint = validDllImportAbiBinding.Compilation.GetAllMethods()
        .FirstOrDefault(method => method.Name == "OpenPoint" && method.DeclaringTypeName == "Native");
    if (openPoint is null)
    {
        failures.Add("Binder should expose the DllImport callback root method.");
    }
    else
    {
        var callbackClosure = ReachableCompilationBuilder.Build(
            [openPoint],
            validDllImportAbiBinding.Compilation.GetAllMethods(),
            validDllImportAbiBinding.Compilation.GetAllFields(),
            validDllImportAbiBinding.Compilation.Types,
            validDllImportAbiBinding.Compilation.GetAllProperties(),
            validDllImportAbiBinding.Compilation.GetAllConstants());
        if (debugEnabled)
        {
            Console.Error.WriteLine(
                "debug.reachability.nativeCallback=" +
                $"methods={callbackClosure.Methods.Count},functions={callbackClosure.Functions.Count},nativeCallbacks={callbackClosure.Stats.NativeCallbackMethodsEnqueued},methodNames=" +
                string.Join("|", callbackClosure.Methods.Select(method => $"{method.DeclaringTypeName}.{method.Name}:{method.HostImportKind}")));
        }

        if (callbackClosure.Stats.NativeCallbackMethodsEnqueued == 0 ||
            !callbackClosure.Methods.Any(method =>
                method.Name == "Invoke" &&
                method.DeclaringTypeName == "CompletionCallback" &&
                method.HostImportKind == HostImportKind.DelegateInvoke) ||
            callbackClosure.Functions.Count != callbackClosure.Methods.Count)
        {
            failures.Add("Reachability should retain delegate Invoke methods passed through DllImport callback parameters.");
        }
    }
}

var validDllImportVoidCallbackTree = SyntaxTree.Parse("""
public enum CallingConvention
begin
  Cdecl
end;

public delegate procedure CompletionCallback(code: Integer);

public class Native
begin
  [DllImport('libffi-demo.so', EntryPoint := 'consume_point', CallingConvention := CallingConvention.Cdecl)]
  public static extern function Consume(callback: CompletionCallback): Integer;
end;
""");

var validDllImportVoidCallbackBinding = new Binder().Bind(validDllImportVoidCallbackTree);
if (validDllImportVoidCallbackBinding.Diagnostics.Count > 0)
{
    failures.Add($"Binder should accept DllImport callbacks with the supported void(Integer) signature. Actual: {string.Join(", ", validDllImportVoidCallbackBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message))}");
}

var validDllImportBoolCallbackTree = SyntaxTree.Parse("""
public enum CallingConvention
begin
  Cdecl
end;

public delegate function CompletionCallback(flag: Boolean): Integer;

public class Native
begin
  [DllImport('libffi-demo.so', EntryPoint := 'consume_bool', CallingConvention := CallingConvention.Cdecl)]
  public static extern function Consume(callback: CompletionCallback): Integer;
end;
""");

var validDllImportBoolCallbackBinding = new Binder().Bind(validDllImportBoolCallbackTree);
if (validDllImportBoolCallbackBinding.Diagnostics.Count > 0)
{
    failures.Add($"Binder should accept DllImport callbacks with the supported Integer(Boolean) signature. Actual: {string.Join(", ", validDllImportBoolCallbackBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message))}");
}

var invalidDllImportClassTree = SyntaxTree.Parse("""
public class NativeWidget
begin
end;

public class Native
begin
  [DllImport('libffi-demo.so')]
  public static extern function Attach(widget: NativeWidget): Integer;
end;
""");

var invalidDllImportClassBinding = new Binder().Bind(invalidDllImportClassTree);
if (!invalidDllImportClassBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2235"))
{
    failures.Add("Binder should reject DllImport parameters that use complex reference types.");
}

var invalidDllImportRecordTree = SyntaxTree.Parse("""
public record BadRecord
begin
  public var Name: String;
end;

public class Native
begin
  [DllImport('libffi-demo.so')]
  public static extern function Send(value: BadRecord): Integer;
end;
""");

var invalidDllImportRecordBinding = new Binder().Bind(invalidDllImportRecordTree);
if (!invalidDllImportRecordBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2235"))
{
    failures.Add("Binder should reject DllImport records that are not POD-like.");
}

var invalidDllImportRefTree = SyntaxTree.Parse("""
public class Native
begin
  [DllImport('libffi-demo.so')]
  public static extern function Fill(ref value: Integer): Integer;
end;
""");

var invalidDllImportRefBinding = new Binder().Bind(invalidDllImportRefTree);
if (!invalidDllImportRefBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2234"))
{
    failures.Add("Binder should reject non-value DllImport parameters in the current FFI v1 contract.");
}

var invalidDllImportStringReturnTree = SyntaxTree.Parse("""
public enum StringReturn
begin
  None,
  Utf8Owned
end;

public class Native
begin
  [DllImport('libffi-demo.so')]
  public static extern function ReadName(): String;
end;
""");

var invalidDllImportStringReturnBinding = new Binder().Bind(invalidDllImportStringReturnTree);
if (!invalidDllImportStringReturnBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2237"))
{
    failures.Add("Binder should reject DllImport string return types that do not declare explicit ownership and free metadata.");
}

var validDllImportStringReturnTree = SyntaxTree.Parse("""
public enum CallingConvention
begin
  Cdecl
end;

public enum StringReturn
begin
  None,
  Utf8Owned
end;

public class Native
begin
  [DllImport('libffi-demo.so', EntryPoint := 'read_name', CallingConvention := CallingConvention.Cdecl, StringReturn := StringReturn.Utf8Owned, StringFreeEntryPoint := 'free_name')]
  public static extern function ReadName(): String;
end;
""");

var validDllImportStringReturnBinding = new Binder().Bind(validDllImportStringReturnTree);
if (validDllImportStringReturnBinding.Diagnostics.Count > 0)
{
    failures.Add($"Binder should accept DllImport string returns with explicit Utf8Owned ownership. Actual: {string.Join(", ", validDllImportStringReturnBinding.Diagnostics.Select(diagnostic => diagnostic.Id + ':' + diagnostic.Message))}");
}
else
{
    var nativeType = validDllImportStringReturnBinding.Compilation.Types.OfType<NamedTypeSymbol>().First(type => type.Name == "Native");
    var readNameMethod = nativeType.Methods.First(method => method.Name == "ReadName");
    if (readNameMethod.DllImport is null ||
        readNameMethod.DllImport.StringReturnMarshalling != NativeStringReturnMarshalling.Utf8Owned ||
        readNameMethod.DllImport.StringFreeEntryPoint != "free_name")
    {
        failures.Add("Binder should preserve DllImport string return ownership metadata.");
    }

    var methods = validDllImportStringReturnBinding.Compilation.GetAllMethods().ToArray();
    var fields = validDllImportStringReturnBinding.Compilation.GetAllFields();
    var stringReturnModule = new BytecodeEmitter().EmitModule(
        methods,
        fields,
        validDllImportStringReturnBinding.Compilation.Types,
        new Lowerer(
            methods,
            fields,
            validDllImportStringReturnBinding.Compilation.Types,
            validDllImportStringReturnBinding.Compilation.GetAllProperties(),
            validDllImportStringReturnBinding.Compilation.GetAllConstants()));
    var emittedReadName = stringReturnModule.Functions.FirstOrDefault(function => function.Name == "ReadName");
    if (emittedReadName?.DllImport is null ||
        emittedReadName.DllImport.StringReturnMarshalling != NativeStringReturnMarshalling.Utf8Owned ||
        emittedReadName.DllImport.StringFreeEntryPoint != "free_name")
    {
        failures.Add("Bytecode emission should preserve DllImport string return ownership metadata.");
    }

    var ilb = new IlbSerializer().Serialize(
        stringReturnModule,
        methods,
        fields,
        validDllImportStringReturnBinding.Compilation.Types,
        null);
    static uint ReadDllImportU32(byte[] bytes, int offset) =>
        (uint)(bytes[offset] |
            (bytes[offset + 1] << 8) |
            (bytes[offset + 2] << 16) |
            (bytes[offset + 3] << 24));
    static List<string> ReadDllImportStringTable(byte[] bytes, int offset)
    {
        var count = (int)ReadDllImportU32(bytes, offset);
        var cursor = offset + 4;
        var values = new List<string> { string.Empty };
        for (var index = 0; index < count; index++)
        {
            var length = (int)ReadDllImportU32(bytes, cursor);
            cursor += 4;
            values.Add(System.Text.Encoding.UTF8.GetString(bytes, cursor, length));
            cursor += length;
        }

        return values;
    }
    var stringSection = ilb.Sections.First(section => section.Kind == IlbSectionKind.StringTable);
    var methodSection = ilb.Sections.First(section => section.Kind == IlbSectionKind.MethodTable);
    var strings = ReadDllImportStringTable(ilb.Bytes, (int)stringSection.Offset);
    var rowOffset = (int)methodSection.Offset;
    var stringReturnMarshalling = ReadDllImportU32(ilb.Bytes, rowOffset + 58);
    var freeEntryPointStringId = ReadDllImportU32(ilb.Bytes, rowOffset + 62);
    if (stringReturnMarshalling != (uint)NativeStringReturnMarshalling.Utf8Owned ||
        freeEntryPointStringId == 0 ||
        strings[(int)freeEntryPointStringId] != "free_name")
    {
        failures.Add("ILB serialization should persist DllImport string return ownership metadata.");
    }
}

var invalidDllImportCallbackClassTree = SyntaxTree.Parse("""
public class NativeWidget
begin
end;

public delegate function BadCallback(widget: NativeWidget): Integer;

public class Native
begin
  [DllImport('libffi-demo.so')]
  public static extern function Register(callback: BadCallback): Integer;
end;
""");

var invalidDllImportCallbackClassBinding = new Binder().Bind(invalidDllImportCallbackClassTree);
if (!invalidDllImportCallbackClassBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2235"))
{
    failures.Add("Binder should reject DllImport callbacks whose delegate signature uses complex reference types.");
}

var invalidDllImportCallbackRefTree = SyntaxTree.Parse("""
public delegate function BadCallback(ref value: Integer): Integer;

public class Native
begin
  [DllImport('libffi-demo.so')]
  public static extern function Register(callback: BadCallback): Integer;
end;
""");

var invalidDllImportCallbackRefBinding = new Binder().Bind(invalidDllImportCallbackRefTree);
if (!invalidDllImportCallbackRefBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2235"))
{
    failures.Add("Binder should reject DllImport callbacks whose delegate signature uses non-value parameters.");
}

var invalidDllImportCallbackStringTree = SyntaxTree.Parse("""
public delegate function BadCallback(value: String): Integer;

public class Native
begin
  [DllImport('libffi-demo.so')]
  public static extern function Register(callback: BadCallback): Integer;
end;
""");

var invalidDllImportCallbackStringBinding = new Binder().Bind(invalidDllImportCallbackStringTree);
if (!invalidDllImportCallbackStringBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2235"))
{
    failures.Add("Binder should reject DllImport callbacks whose delegate signature uses string marshalling before callback ownership rules exist.");
}

var invalidDllImportCallbackArityTree = SyntaxTree.Parse("""
public delegate function BadCallback(left: Integer; right: Integer): Integer;

public class Native
begin
  [DllImport('libffi-demo.so')]
  public static extern function Register(callback: BadCallback): Integer;
end;
""");

var invalidDllImportCallbackArityBinding = new Binder().Bind(invalidDllImportCallbackArityTree);
if (!invalidDllImportCallbackArityBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2235"))
{
    failures.Add("Binder should reject DllImport callbacks whose delegate signature exceeds the current runtime callback slice.");
}

var ambiguousImportPrimaryTree = SyntaxTree.Parse("""
namespace Demo.App;
uses Alpha, Beta;

public class Program
begin
end;
""");
var alphaImportTree = SyntaxTree.Parse("""
namespace Alpha;
public class Console
begin
end;
""");
var betaImportTree = SyntaxTree.Parse("""
namespace Beta;
public class Console
begin
end;
""");
var ambiguousImportMergedTree = SyntaxTree.Merge(ambiguousImportPrimaryTree, [alphaImportTree, betaImportTree]);
if (!ambiguousImportMergedTree.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2184"))
{
    failures.Add("SyntaxTree.Merge should report ambiguous unaliased imports with duplicate type names.");
}

var aliasedImportPrimaryTree = SyntaxTree.Parse("""
namespace Demo.App;
uses Alpha, BetaAlias = Beta;

public class Program
begin
  public static method Main;
  begin
    BetaAlias.Console.WriteLine('ok');
  end;
end;
""");
var aliasedImportMergedTree = SyntaxTree.Merge(aliasedImportPrimaryTree, [alphaImportTree, betaImportTree]);
if (aliasedImportMergedTree.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2184"))
{
    failures.Add("SyntaxTree.Merge should not report ambiguity when a conflicting import is only brought in through an alias.");
}

var invalidConstantTree = SyntaxTree.Parse("""
const BadValue = Program.Add(1, 2);
public class Program
begin
  public static const Seed: Integer = 'oops';
  public static method Main;
  begin
    Seed := 1;
    return;
  end;

  public static function Add(left: Integer; right: Integer): Integer;
  begin
    return left + right;
  end;
end;
""");

var invalidConstantBinding = new Binder().Bind(invalidConstantTree);
if (!invalidConstantBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2148"))
{
    failures.Add("Binder should report non-constant constant initializers.");
}

if (!invalidConstantBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2149"))
{
    failures.Add("Binder should report constant type mismatches.");
}

if (!invalidConstantBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2150"))
{
    failures.Add("Binder should report assignments to constants.");
}

var invalidSetTree = SyntaxTree.Parse("""
public enum Mode
begin
  Idle;
  Busy;
end;

public class Program
begin
  public static method Main;
  begin
    var modes: set of Mode := [1];
    if Mode.Busy in 1 then
    begin
    end;

    var value := 1;
    if value in modes then
    begin
    end;
  end;
end;
""");

var invalidSetBinding = new Binder().Bind(invalidSetTree);
if (!invalidSetBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2153"))
{
    failures.Add("Binder should report non-enum set literal elements.");
}

if (!invalidSetBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2155"))
{
    failures.Add("Binder should report non-set right-hand operands for 'in'.");
}

if (!invalidSetBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2157"))
{
    failures.Add("Binder should report left-hand operands of the wrong enum type for 'in'.");
}

var invalidSetBinaryTree = SyntaxTree.Parse("""
public enum Mode
begin
  Idle;
  Busy;
end;

public enum Other
begin
  A;
  B;
end;

public class Program
begin
  public static method Main;
  begin
    var left: set of Mode := [Mode.Busy];
    var right: set of Other := [Other.A];
    var invalid := left + right;
  end;
end;
""");

var invalidSetBinaryBinding = new Binder().Bind(invalidSetBinaryTree);
if (!invalidSetBinaryBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2158"))
{
    failures.Add("Binder should report mismatched set element types for set operators.");
}

var invalidSliceTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    var scalar := 1;
    var slice := scalar[0..1];
    scalar[0..1] := 1;
    var numbers := new Integer[2, 2];
    var mixed := numbers[0..1, 0];
    var standalone := 0..1;
  end;
end;
""");

var invalidSliceBinding = new Binder().Bind(invalidSliceTree);
if (!invalidSliceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2161"))
{
    failures.Add("Binder should report unsupported slicing targets.");
}

if (!invalidSliceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2162"))
{
    failures.Add("Binder should report unsupported slice assignment.");
}

if (!invalidSliceBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2254"))
{
    failures.Add("Binder should reject standalone range expressions outside slice, set and case contexts.");
}

var invalidNullCoalescingAssignmentTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    var scalar := 1;
    scalar ??= 2;
  end;
end;
""");

var invalidNullCoalescingAssignmentBinding = new Binder().Bind(invalidNullCoalescingAssignmentTree);
if (!invalidNullCoalescingAssignmentBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2165"))
{
    failures.Add("Binder should report ??= on non-reference assignment targets.");
}

var invalidNullCoalescingTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    var scalar := 1;
    var text := scalar ?? 'x';
  end;
end;
""");

var invalidNullCoalescingBinding = new Binder().Bind(invalidNullCoalescingTree);
if (!invalidNullCoalescingBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2166"))
{
    failures.Add("Binder should report non-reference left operands for ??.");
}

var invalidUnaryNotTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    var text := 'abc';
    var bad := not text;
  end;
end;
""");

var invalidUnaryNotBinding = new Binder().Bind(invalidUnaryNotTree);
if (!invalidUnaryNotBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2168"))
{
    failures.Add("Binder should report invalid operands for unary not.");
}

var invalidShiftTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    var text := 'abc';
    var value := text shl 1;
  end;
end;
""");

var invalidShiftBinding = new Binder().Bind(invalidShiftTree);
if (!invalidShiftBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2169"))
{
    failures.Add("Binder should report non-Integer left operands for shift expressions.");
}

var invalidShiftAssignmentTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    var text := 'abc';
    text shl= 1;
  end;
end;
""");

var invalidShiftAssignmentBinding = new Binder().Bind(invalidShiftAssignmentTree);
if (!invalidShiftAssignmentBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2169"))
{
    failures.Add("Binder should report non-Integer left operands for shift assignments.");
}

var invalidModuloTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    var text := 'abc';
    var value := text mod 2;
  end;
end;
""");

var invalidModuloBinding = new Binder().Bind(invalidModuloTree);
if (!invalidModuloBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2171"))
{
    failures.Add("Binder should report non-Integer left operands for modulo expressions.");
}

var invalidDivisionTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    var text := 'abc';
    var value := text div 2;
  end;
end;
""");

var invalidDivisionBinding = new Binder().Bind(invalidDivisionTree);
if (!invalidDivisionBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2173"))
{
    failures.Add("Binder should report non-Integer left operands for div expressions.");
}

var invalidUnaryMinusTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    var text := 'abc';
    var value := -text;
  end;
end;
""");

var invalidUnaryMinusBinding = new Binder().Bind(invalidUnaryMinusTree);
if (!invalidUnaryMinusBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2185"))
{
    failures.Add("Binder should report unary '-' on non-Integer operands.");
}

var invalidIncTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    var text := 'abc';
    inc(text);
    dec(text);
  end;
end;
""");
var invalidIncBinding = new Binder().Bind(invalidIncTree);
if (!invalidIncBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2187"))
{
    failures.Add("Binder should report 'inc' on non-Integer targets.");
}

if (!invalidIncBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2188"))
{
    failures.Add("Binder should report 'dec' on non-Integer targets.");
}

var invalidForStepTree = SyntaxTree.Parse("""
public class Program
begin
  public static method Main;
  begin
    for var index := 0 to 4 step 'x' do
    begin
    end;
  end;
end;
""");
var invalidForStepBinding = new Binder().Bind(invalidForStepTree);
if (!invalidForStepBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2189"))
{
    failures.Add("Binder should report non-Integer for-loop steps.");
}

var invalidIncludeExcludeTree = SyntaxTree.Parse("""
public enum Mode
begin
  Idle;
  Busy;
end;

public enum Other
begin
  A;
  B;
end;

public class Program
begin
  public static method Main;
  begin
    var number := 0;
    var modes: set of Mode := [Mode.Busy];
    include(number, Mode.Busy);
    exclude(modes, Other.A);
  end;
end;
""");
var invalidIncludeExcludeBinding = new Binder().Bind(invalidIncludeExcludeTree);
if (!invalidIncludeExcludeBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2190"))
{
    failures.Add("Binder should report non-set targets for 'include' and 'exclude'.");
}

if (!invalidIncludeExcludeBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2191"))
{
    failures.Add("Binder should report mismatched element types for 'include' and 'exclude'.");
}


try
{
    var invalidMethod = new MethodSymbol("Broken", TypeSymbol.Void, [], "Program", false, null, false, true, [
        new TopLevelExpressionStatementSyntax(
            new CallExpressionSyntax(
                new NameExpressionSyntax(new QualifiedNameSyntax([new SyntaxToken(SyntaxKind.IdentifierToken, "MissingCall", null, new TextSpan(0, 11))])),
                new SyntaxToken(SyntaxKind.OpenParenToken, "(", null, new TextSpan(11, 1)),
                [],
                new SyntaxToken(SyntaxKind.CloseParenToken, ")", null, new TextSpan(12, 1))),
            new SyntaxToken(SyntaxKind.SemicolonToken, ";", null, new TextSpan(13, 1)))
    ]);

    _ = new Lowerer([], [], []).Lower(invalidMethod);
    failures.Add("Lowerer should not silently lower unresolved calls.");
}
catch (InvalidOperationException)
{
}

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"FAIL: {failure}");
    }

    return 1;
}

Console.WriteLine("All bootstrap compiler checks passed.");
return 0;
