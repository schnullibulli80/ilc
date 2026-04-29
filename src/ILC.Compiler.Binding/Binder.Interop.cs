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

    private static HostImportKind ResolveHostImportKind(
        string methodName,
        TypeSymbol returnType,
        IReadOnlyList<ParameterSymbol> parameters,
        string? declaringTypeName,
        bool isStatic,
        bool isExtern)
    {
        if (!isExtern)
        {
            return HostImportKind.None;
        }

        if (declaringTypeName == "Console" && isStatic)
        {
            if (methodName == "Write" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.ConsoleWrite;
            }

            if (methodName == "WriteLine" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.ConsoleWriteLine;
            }

            if (methodName == "ReadLine" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.ConsoleReadLine;
            }
        }

        if (declaringTypeName == "Environment" && isStatic)
        {
            if (methodName == "GetCommandLineArgsCore" &&
                returnType.Name == $"{TypeSymbol.String.Name}[]" &&
                parameters.Count == 0)
            {
                return HostImportKind.EnvironmentGetCommandLineArgs;
            }

            if (methodName == "GetCurrentDirectoryCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.EnvironmentGetCurrentDirectory;
            }

            if (methodName == "GetUserNameCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.EnvironmentGetUserName;
            }

            if (methodName == "GetMachineNameCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.EnvironmentGetMachineName;
            }

            if (methodName == "GetHomeDirectoryCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.EnvironmentGetHomeDirectory;
            }

            if (methodName == "GetTempDirectoryCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.EnvironmentGetTempDirectory;
            }

            if (methodName == "GetEnvironmentVariableCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.EnvironmentGetEnvironmentVariable;
            }

            if (methodName == "SetEnvironmentVariableCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.String &&
                parameters[1].Type == TypeSymbol.String)
            {
                return HostImportKind.EnvironmentSetEnvironmentVariable;
            }
        }

        if (declaringTypeName == "Clock" && isStatic)
        {
            if (methodName == "GetMonotonicMillisecondsTextCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.ClockGetMonotonicMillisecondsText;
            }

            if (methodName == "GetWallMillisecondsTextCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.ClockGetWallMillisecondsText;
            }

            if (methodName == "GetWallDateTimeTextCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.ClockGetWallDateTimeText;
            }
        }

        if (declaringTypeName == "Exception" && isStatic)
        {
            if (methodName == "GetCurrentStackTraceCore" &&
                returnType.Name == $"{TypeSymbol.String.Name}[]" &&
                parameters.Count == 0)
            {
                return HostImportKind.ExceptionGetCurrentStackTrace;
            }
        }

        if (declaringTypeName == "File" && isStatic)
        {
            if (methodName == "ExistsCore" &&
                returnType == TypeSymbol.Boolean &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.FileExists;
            }

            if (methodName == "ReadAllTextCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.FileReadAllText;
            }

            if (methodName == "WriteAllTextCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.String &&
                parameters[1].Type == TypeSymbol.String)
            {
                return HostImportKind.FileWriteAllText;
            }

            if (methodName == "AppendAllTextCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.String &&
                parameters[1].Type == TypeSymbol.String)
            {
                return HostImportKind.FileAppendAllText;
            }
        }

        if (declaringTypeName == "Path" && isStatic)
        {
            if (methodName == "CombineCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.String &&
                parameters[1].Type == TypeSymbol.String)
            {
                return HostImportKind.PathCombine;
            }

            if (methodName == "GetFileNameCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.PathGetFileName;
            }

            if (methodName == "GetDirectoryNameCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.PathGetDirectoryName;
            }

            if (methodName == "GetExtensionCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.PathGetExtension;
            }
        }

        if (declaringTypeName == "TcpClient" && isStatic)
        {
            if (methodName == "ConnectCore" &&
                returnType == TypeSymbol.Integer &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.String &&
                parameters[1].Type == TypeSymbol.Integer)
            {
                return HostImportKind.TcpConnect;
            }

            if (methodName == "ReadLineCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.TcpReadLine;
            }

            if (methodName == "WriteLineCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.Integer &&
                parameters[1].Type == TypeSymbol.String)
            {
                return HostImportKind.TcpWriteLine;
            }

            if (methodName == "CloseCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.TcpClose;
            }
        }

        if (declaringTypeName == "HttpClient" && isStatic)
        {
            if (methodName == "GetStringCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.HttpGetString;
            }
        }

        if (declaringTypeName == "WebSocketClient" && isStatic)
        {
            if (methodName == "ConnectCore" &&
                returnType == TypeSymbol.Integer &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.WebSocketConnect;
            }

            if (methodName == "ReceiveTextCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.WebSocketReceiveText;
            }

            if (methodName == "SendTextCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.Integer &&
                parameters[1].Type == TypeSymbol.String)
            {
                return HostImportKind.WebSocketSendText;
            }

            if (methodName == "CloseCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.WebSocketClose;
            }
        }

        if (declaringTypeName == "Thread" && isStatic)
        {
            if (methodName == "SleepCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.ThreadSleep;
            }

            if (methodName == "GetCurrentManagedIdCore" &&
                returnType == TypeSymbol.Integer &&
                parameters.Count == 0)
            {
                return HostImportKind.ThreadGetCurrentManagedId;
            }

            if (methodName == "StartCore" &&
                returnType == TypeSymbol.Integer &&
                parameters.Count == 1 &&
                parameters[0].Type.Name == "IRunnable")
            {
                return HostImportKind.ThreadStartRunnable;
            }

            if (methodName == "JoinCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.ThreadJoin;
            }

            if (methodName == "IsAliveCore" &&
                returnType == TypeSymbol.Boolean &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.ThreadIsAlive;
            }
        }

        if (declaringTypeName == "Mutex" && isStatic)
        {
            if (methodName == "CreateCore" &&
                returnType == TypeSymbol.Integer &&
                parameters.Count == 0)
            {
                return HostImportKind.MutexCreate;
            }

            if (methodName == "WaitOneCore" &&
                returnType == TypeSymbol.Boolean &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.MutexWaitOne;
            }

            if (methodName == "ReleaseCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.MutexRelease;
            }

            if (methodName == "CloseCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.MutexClose;
            }
        }

        return HostImportKind.None;
    }

}
