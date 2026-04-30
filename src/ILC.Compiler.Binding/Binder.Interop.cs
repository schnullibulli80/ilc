namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public sealed partial class Binder
{
    private static void ValidateDllImportMethod(
        string declaringTypeName,
        MethodDeclarationSyntax declaration,
        MethodSymbol method,
        IReadOnlyList<TypeSymbol> knownTypes,
        DiagnosticBag diagnostics)
    {
        if (!method.IsExtern)
        {
            diagnostics.Report(
                "ILC2212",
                $"DllImport method '{declaringTypeName}.{declaration.Identifier.Text}' must be declared extern.",
                DiagnosticSeverity.Error,
                declaration.Keyword.Span);
        }

        if (!method.IsStatic)
        {
            diagnostics.Report(
                "ILC2213",
                $"DllImport method '{declaringTypeName}.{declaration.Identifier.Text}' must be declared static.",
                DiagnosticSeverity.Error,
                declaration.Keyword.Span);
        }

        if (method.IsConstructor)
        {
            diagnostics.Report(
                "ILC2214",
                $"DllImport constructor '{declaringTypeName}' is not supported.",
                DiagnosticSeverity.Error,
                declaration.Keyword.Span);
        }

        if (method.DllImport is null)
        {
            diagnostics.Report(
                "ILC2215",
                $"DllImport method '{declaringTypeName}.{declaration.Identifier.Text}' must declare a string library name.",
                DiagnosticSeverity.Error,
                declaration.Keyword.Span);
        }

        foreach (var parameter in method.Parameters)
        {
            if (parameter.PassingKind != ParameterPassingKind.Value)
            {
                diagnostics.Report(
                    "ILC2234",
                    $"DllImport method '{declaringTypeName}.{declaration.Identifier.Text}' must use value parameters only in the current FFI v1 contract. Parameter '{parameter.Name}' uses '{parameter.PassingKind}'.",
                    DiagnosticSeverity.Error,
                    declaration.Identifier.Span);
            }
            else if (!IsSupportedDllImportAbiType(parameter.Type, knownTypes))
            {
                diagnostics.Report(
                    "ILC2235",
                    $"DllImport parameter '{declaringTypeName}.{declaration.Identifier.Text}({parameter.Name}: {parameter.Type.Name})' is not supported by the current FFI v1 contract.",
                    DiagnosticSeverity.Error,
                    declaration.Identifier.Span);
            }
        }

        if (!IsSupportedDllImportAbiReturnType(method.ReturnType, knownTypes))
        {
            diagnostics.Report(
                "ILC2236",
                $"DllImport return type '{declaringTypeName}.{declaration.Identifier.Text}: {method.ReturnType.Name}' is not supported by the current FFI v1 contract.",
                DiagnosticSeverity.Error,
                declaration.Identifier.Span);
        }
        else if (method.ReturnType == TypeSymbol.String &&
            (method.DllImport is null ||
             method.DllImport.StringReturnMarshalling != NativeStringReturnMarshalling.Utf8Owned ||
             string.IsNullOrWhiteSpace(method.DllImport.StringFreeEntryPoint)))
        {
            diagnostics.Report(
                "ILC2237",
                $"DllImport string return '{declaringTypeName}.{declaration.Identifier.Text}' must declare StringReturn := Utf8Owned and StringFreeEntryPoint := '...'.",
                DiagnosticSeverity.Error,
                declaration.Identifier.Span);
        }
    }

    private static bool IsSupportedDllImportAbiReturnType(TypeSymbol type, IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (type == TypeSymbol.Void)
        {
            return true;
        }

        return IsSupportedDllImportAbiType(type, knownTypes);
    }

    private static bool IsSupportedDllImportAbiType(TypeSymbol type, IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (type == TypeSymbol.Boolean ||
            type == TypeSymbol.Char ||
            type == TypeSymbol.Integer ||
            type == TypeSymbol.UInt128 ||
            type == TypeSymbol.UInt256 ||
            type == TypeSymbol.UInt512 ||
            type == TypeSymbol.UInt1024 ||
            type == TypeSymbol.UInt2048 ||
            type == TypeSymbol.String)
        {
            return true;
        }

        if (type is TypeParameterSymbol)
        {
            return false;
        }

        if (!type.IsReferenceType)
        {
            return true;
        }

        var namedType = ResolveDllImportAbiNamedType(type, knownTypes);
        if (namedType is null)
        {
            return false;
        }

        if (namedType.IsDelegate)
        {
            return IsSupportedDllImportAbiDelegate(namedType, knownTypes, new HashSet<string>(StringComparer.Ordinal));
        }

        if (!namedType.IsReferenceType && !namedType.IsRecord)
        {
            return true;
        }

        if (namedType.IsRecord && !namedType.IsReferenceType)
        {
            return IsSupportedDllImportAbiRecord(namedType, knownTypes, new HashSet<string>(StringComparer.Ordinal));
        }

        return false;
    }

    private static bool IsSupportedDllImportAbiRecord(
        NamedTypeSymbol recordType,
        IReadOnlyList<TypeSymbol> knownTypes,
        HashSet<string> visited)
    {
        if (!visited.Add(recordType.Name))
        {
            return true;
        }

        foreach (var field in recordType.Fields.Where(field => !field.IsStatic))
        {
            if (!IsSupportedDllImportAbiRecordFieldType(field.Type, knownTypes))
            {
                return false;
            }

            if (ResolveNamedType(field.Type, knownTypes) is { IsRecord: true, IsReferenceType: false } nestedRecord &&
                !IsSupportedDllImportAbiRecord(nestedRecord, knownTypes, visited))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSupportedDllImportAbiRecordFieldType(TypeSymbol type, IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (type == TypeSymbol.Boolean ||
            type == TypeSymbol.Char ||
            type == TypeSymbol.Integer ||
            type == TypeSymbol.UInt128 ||
            type == TypeSymbol.UInt256 ||
            type == TypeSymbol.UInt512 ||
            type == TypeSymbol.UInt1024 ||
            type == TypeSymbol.UInt2048)
        {
            return true;
        }

        if (type is TypeParameterSymbol)
        {
            return false;
        }

        if (!type.IsReferenceType)
        {
            return true;
        }

        var namedType = ResolveDllImportAbiNamedType(type, knownTypes);
        if (namedType is null)
        {
            return false;
        }

        if (!namedType.IsReferenceType && !namedType.IsRecord)
        {
            return true;
        }

        if (namedType.IsRecord && !namedType.IsReferenceType)
        {
            return IsSupportedDllImportAbiRecord(namedType, knownTypes, new HashSet<string>(StringComparer.Ordinal));
        }

        return false;
    }

    private static NamedTypeSymbol? ResolveDllImportAbiNamedType(TypeSymbol type, IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (type is NamedTypeSymbol named)
        {
            return named;
        }

        return ResolveNamedType(type, knownTypes);
    }

    private static bool IsSupportedDllImportAbiDelegate(
        NamedTypeSymbol delegateType,
        IReadOnlyList<TypeSymbol> knownTypes,
        HashSet<string> visited)
    {
        if (!visited.Add(delegateType.Name))
        {
            return true;
        }

        var invokeMethod = delegateType.Methods.FirstOrDefault(method => method.Name == "Invoke" && !method.IsStatic);
        if (invokeMethod is null)
        {
            return false;
        }

        // Runtime FFI callback execution currently supports only the compact
        // synchronous single-argument trampoline slice.
        if (invokeMethod.Parameters.Count != 1)
        {
            return false;
        }

        foreach (var parameter in invokeMethod.Parameters)
        {
            if (parameter.PassingKind != ParameterPassingKind.Value)
            {
                return false;
            }

            if (!IsSupportedDllImportCallbackAbiType(parameter.Type, knownTypes, visited))
            {
                return false;
            }
        }

        if (invokeMethod.ReturnType == TypeSymbol.Void)
        {
            return true;
        }

        return IsSupportedDllImportCallbackAbiType(invokeMethod.ReturnType, knownTypes, visited);
    }

    private static bool IsSupportedDllImportCallbackAbiType(
        TypeSymbol type,
        IReadOnlyList<TypeSymbol> knownTypes,
        HashSet<string> visited)
    {
        if (type == TypeSymbol.Boolean ||
            type == TypeSymbol.Integer)
        {
            return true;
        }

        if (type is TypeParameterSymbol)
        {
            return false;
        }

        if (SemanticFacts.IsEnumType(type))
        {
            return true;
        }

        var namedType = ResolveDllImportAbiNamedType(type, knownTypes);
        if (namedType is null)
        {
            return false;
        }

        if (namedType.IsDelegate)
        {
            return false;
        }

        return false;
    }

    private static DllImportMetadata? BindDllImportMetadata(MethodDeclarationSyntax methodDeclaration)
    {
        var dllImportAttribute = methodDeclaration.Attributes.FirstOrDefault(attribute => IsDllImportAttribute(attribute));
        if (dllImportAttribute is null)
        {
            return null;
        }

        var libraryName = dllImportAttribute.Arguments
            .FirstOrDefault(argument => argument.ModifierKeyword is null)?.Expression switch
        {
            LiteralExpressionSyntax { LiteralToken.Kind: SyntaxKind.StringToken, LiteralToken.Value: string value } => value,
            _ => null
        };

        var entryPoint = methodDeclaration.Identifier.Text;
        var callingConvention = NativeCallingConvention.Cdecl;
        var stringReturnMarshalling = NativeStringReturnMarshalling.None;
        string? stringFreeEntryPoint = null;

        foreach (var argument in dllImportAttribute.Arguments.Select(argument => argument.Expression).OfType<AssignmentExpressionSyntax>())
        {
            var targetName = argument.Target switch
            {
                NameExpressionSyntax { Name.Parts.Count: > 0 } name => name.Name.Parts[^1].Text,
                MemberAccessExpressionSyntax memberAccess => memberAccess.MemberName.Text,
                _ => null
            };

            if (string.Equals(targetName, "EntryPoint", StringComparison.Ordinal))
            {
                if (argument.Expression is LiteralExpressionSyntax { LiteralToken.Kind: SyntaxKind.StringToken, LiteralToken.Value: string value })
                {
                    entryPoint = value;
                }
            }
            else if (string.Equals(targetName, "CallingConvention", StringComparison.Ordinal))
            {
                switch (argument.Expression)
                {
                    case NameExpressionSyntax { Name.Parts.Count: > 0 } name when string.Equals(name.Name.Parts[^1].Text, "StdCall", StringComparison.Ordinal):
                    case MemberAccessExpressionSyntax { MemberName.Text: "StdCall" }:
                        callingConvention = NativeCallingConvention.StdCall;
                        break;
                    case NameExpressionSyntax { Name.Parts.Count: > 0 } name when string.Equals(name.Name.Parts[^1].Text, "Cdecl", StringComparison.Ordinal):
                    case MemberAccessExpressionSyntax { MemberName.Text: "Cdecl" }:
                        callingConvention = NativeCallingConvention.Cdecl;
                        break;
                }
            }
            else if (string.Equals(targetName, "StringReturn", StringComparison.Ordinal))
            {
                switch (argument.Expression)
                {
                    case NameExpressionSyntax { Name.Parts.Count: > 0 } name when string.Equals(name.Name.Parts[^1].Text, "Utf8Owned", StringComparison.Ordinal):
                    case MemberAccessExpressionSyntax { MemberName.Text: "Utf8Owned" }:
                        stringReturnMarshalling = NativeStringReturnMarshalling.Utf8Owned;
                        break;
                    case NameExpressionSyntax { Name.Parts.Count: > 0 } name when string.Equals(name.Name.Parts[^1].Text, "None", StringComparison.Ordinal):
                    case MemberAccessExpressionSyntax { MemberName.Text: "None" }:
                        stringReturnMarshalling = NativeStringReturnMarshalling.None;
                        break;
                }
            }
            else if (string.Equals(targetName, "StringFreeEntryPoint", StringComparison.Ordinal))
            {
                if (argument.Expression is LiteralExpressionSyntax { LiteralToken.Kind: SyntaxKind.StringToken, LiteralToken.Value: string value })
                {
                    stringFreeEntryPoint = value;
                }
            }
        }

        return libraryName is null
            ? null
            : new DllImportMetadata(libraryName, entryPoint, callingConvention, stringReturnMarshalling, stringFreeEntryPoint);
    }

    private static bool IsDllImportAttribute(AttributeSyntax attribute) =>
        string.Equals(attribute.Name.Parts[^1].Text, "DllImport", StringComparison.Ordinal);

    private sealed record HostImportSignature(
        HostImportKind Kind,
        string DeclaringTypeName,
        string MethodName,
        string ReturnTypeName,
        IReadOnlyList<string> ParameterTypeNames);

    private static readonly IReadOnlyList<HostImportSignature> HostImportSignatures =
    [
        new(HostImportKind.ConsoleWrite, "Console", "Write", "Void", ["String"]),
        new(HostImportKind.ConsoleWriteLine, "Console", "WriteLine", "Void", ["String"]),
        new(HostImportKind.ConsoleReadLine, "Console", "ReadLine", "String", []),
        new(HostImportKind.EnvironmentGetCommandLineArgs, "Environment", "GetCommandLineArgsCore", "String[]", []),
        new(HostImportKind.EnvironmentGetCurrentDirectory, "Environment", "GetCurrentDirectoryCore", "String", []),
        new(HostImportKind.EnvironmentGetUserName, "Environment", "GetUserNameCore", "String", []),
        new(HostImportKind.EnvironmentGetMachineName, "Environment", "GetMachineNameCore", "String", []),
        new(HostImportKind.EnvironmentGetHomeDirectory, "Environment", "GetHomeDirectoryCore", "String", []),
        new(HostImportKind.EnvironmentGetTempDirectory, "Environment", "GetTempDirectoryCore", "String", []),
        new(HostImportKind.EnvironmentGetEnvironmentVariable, "Environment", "GetEnvironmentVariableCore", "String", ["String"]),
        new(HostImportKind.EnvironmentSetEnvironmentVariable, "Environment", "SetEnvironmentVariableCore", "Void", ["String", "String"]),
        new(HostImportKind.ClockGetMonotonicMillisecondsText, "Clock", "GetMonotonicMillisecondsTextCore", "String", []),
        new(HostImportKind.ClockGetWallMillisecondsText, "Clock", "GetWallMillisecondsTextCore", "String", []),
        new(HostImportKind.ClockGetWallDateTimeText, "Clock", "GetWallDateTimeTextCore", "String", []),
        new(HostImportKind.ExceptionGetCurrentStackTrace, "Exception", "GetCurrentStackTraceCore", "String[]", []),
        new(HostImportKind.FileExists, "File", "ExistsCore", "Boolean", ["String"]),
        new(HostImportKind.FileReadAllText, "File", "ReadAllTextCore", "String", ["String"]),
        new(HostImportKind.FileWriteAllText, "File", "WriteAllTextCore", "Void", ["String", "String"]),
        new(HostImportKind.FileAppendAllText, "File", "AppendAllTextCore", "Void", ["String", "String"]),
        new(HostImportKind.PathCombine, "Path", "CombineCore", "String", ["String", "String"]),
        new(HostImportKind.PathGetFileName, "Path", "GetFileNameCore", "String", ["String"]),
        new(HostImportKind.PathGetDirectoryName, "Path", "GetDirectoryNameCore", "String", ["String"]),
        new(HostImportKind.PathGetExtension, "Path", "GetExtensionCore", "String", ["String"]),
        new(HostImportKind.TcpConnect, "TcpClient", "ConnectCore", "Integer", ["String", "Integer"]),
        new(HostImportKind.TcpReadLine, "TcpClient", "ReadLineCore", "String", ["Integer"]),
        new(HostImportKind.TcpWriteLine, "TcpClient", "WriteLineCore", "Void", ["Integer", "String"]),
        new(HostImportKind.TcpClose, "TcpClient", "CloseCore", "Void", ["Integer"]),
        new(HostImportKind.HttpGetString, "HttpClient", "GetStringCore", "String", ["String"]),
        new(HostImportKind.WebSocketConnect, "WebSocketClient", "ConnectCore", "Integer", ["String"]),
        new(HostImportKind.WebSocketReceiveText, "WebSocketClient", "ReceiveTextCore", "String", ["Integer"]),
        new(HostImportKind.WebSocketSendText, "WebSocketClient", "SendTextCore", "Void", ["Integer", "String"]),
        new(HostImportKind.WebSocketClose, "WebSocketClient", "CloseCore", "Void", ["Integer"]),
        new(HostImportKind.ThreadSleep, "Thread", "SleepCore", "Void", ["Integer"]),
        new(HostImportKind.ThreadGetCurrentManagedId, "Thread", "GetCurrentManagedIdCore", "Integer", []),
        new(HostImportKind.ThreadStartRunnable, "Thread", "StartCore", "Integer", ["IRunnable"]),
        new(HostImportKind.ThreadJoin, "Thread", "JoinCore", "Void", ["Integer"]),
        new(HostImportKind.ThreadIsAlive, "Thread", "IsAliveCore", "Boolean", ["Integer"]),
        new(HostImportKind.MutexCreate, "Mutex", "CreateCore", "Integer", []),
        new(HostImportKind.MutexWaitOne, "Mutex", "WaitOneCore", "Boolean", ["Integer"]),
        new(HostImportKind.MutexRelease, "Mutex", "ReleaseCore", "Void", ["Integer"]),
        new(HostImportKind.MutexClose, "Mutex", "CloseCore", "Void", ["Integer"])
    ];

    private static HostImportKind ResolveHostImportKind(
        string methodName,
        TypeSymbol returnType,
        IReadOnlyList<ParameterSymbol> parameters,
        string? declaringTypeName,
        bool isStatic,
        bool isExtern)
    {
        if (!isExtern || !isStatic || declaringTypeName is null)
        {
            return HostImportKind.None;
        }

        return HostImportSignatures.FirstOrDefault(signature =>
            signature.DeclaringTypeName == declaringTypeName &&
            signature.MethodName == methodName &&
            signature.ReturnTypeName == returnType.Name &&
            HostImportParameterTypesMatch(signature.ParameterTypeNames, parameters))?.Kind ?? HostImportKind.None;
    }

    private static bool HostImportParameterTypesMatch(IReadOnlyList<string> expectedTypeNames, IReadOnlyList<ParameterSymbol> parameters)
    {
        if (expectedTypeNames.Count != parameters.Count)
        {
            return false;
        }

        for (var index = 0; index < expectedTypeNames.Count; index++)
        {
            if (parameters[index].Type.Name != expectedTypeNames[index])
            {
                return false;
            }
        }

        return true;
    }
}
