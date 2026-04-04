namespace ILC.Compiler.Bytecode;

using ILC.Compiler.Binding;
using ILC.Compiler.Lowering;

public enum OpCode : byte
{
    Nop = 0x00,
    LdI32 = 0x02,
    LdStr = 0x03,
    Mov = 0x09,
    AddI32 = 0x10,
    ShlI32 = 0x17,
    ShrI32 = 0x54,
    AndI32 = 0x14,
    OrI32 = 0x15,
    NotI32 = 0x16,
    SubI32 = 0x11,
    MulI32 = 0x12,
    DivI32 = 0x13,
    ModI32 = 0x55,
    CmpEqI32 = 0x18,
    CmpNeI32 = 0x19,
    CmpLtI32 = 0x1A,
    CmpLeI32 = 0x1B,
    CmpGtI32 = 0x1C,
    CmpGeI32 = 0x1D,
    CmpEqStr = 0x1E,
    CmpNeStr = 0x1F,
    CmpEqRef = 0x20,
    CmpNeRef = 0x21,
    ConcatStr = 0x22,
    StartsWithStr = 0x23,
    EndsWithStr = 0x24,
    ContainsStr = 0x25,
    IndexOfStr = 0x26,
    LastIndexOfStr = 0x27,
    ReplaceStr = 0x28,
    InsertStr = 0x29,
    RemoveStr = 0x2A,
    ToUpperStr = 0x2B,
    ToLowerStr = 0x2C,
    TrimStr = 0x2D,
    TrimStartStr = 0x2E,
    TrimEndStr = 0x2F,
    StrToI32 = 0x50,
    TryStrToI32 = 0x53,
    I32ToStr = 0x52,
    Throw = 0x80,
    Rethrow = 0x81,
    NewObj = 0x60,
    NewArr = 0x61,
    LdField = 0x36,
    StField = 0x37,
    LdSField = 0x38,
    StSField = 0x39,
    LdElem = 0x74,
    StElem = 0x75,
    LdLen = 0x76,
    SliceStr = 0x77,
    Call = 0x30,
    CallVirt = 0x51,
    Br = 0x31,
    BrFalse = 0x32,
    Ret = 0x34
}

public enum InstructionImmediateKind : byte
{
    None = 0,
    InlineInt32 = 1,
    StringTableIndex = 2,
    ConstantTableIndex = 3
}

public sealed record Instruction(
    OpCode OpCode,
    ushort Destination = 0,
    ushort Left = 0,
    ushort Right = 0,
    int Immediate = 0,
    InstructionImmediateKind ImmediateKind = InstructionImmediateKind.None);

public sealed record BytecodeArrayShapeInfo(ushort ArrayRegister, IReadOnlyList<ushort> ExtentRegisters);
public sealed record BytecodeModuleArrayShapeInfo(uint FunctionId, ushort ArrayRegister, IReadOnlyList<ushort> ExtentRegisters);
public sealed record BytecodeExceptionHandlerInfo(ushort TryStart, ushort TryEnd, ushort HandlerStart, ushort HandlerEnd, ushort TargetRegister, uint CatchTypeId);
public sealed record BytecodeModuleExceptionHandlerInfo(uint FunctionId, ushort TryStart, ushort TryEnd, ushort HandlerStart, ushort HandlerEnd, ushort TargetRegister, uint CatchTypeId);

public sealed record BytecodeFunction(
    uint FunctionId,
    string Name,
    ushort RegisterCount,
    ushort ArgumentCount,
    HostImportKind HostImportKind,
    IReadOnlyList<Instruction> Instructions,
    IReadOnlyList<BytecodeArrayShapeInfo> ArrayShapes,
    IReadOnlyList<BytecodeExceptionHandlerInfo> ExceptionHandlers,
    IReadOnlyList<string> StringLiterals);

public sealed record BytecodeModule(
    IReadOnlyList<BytecodeFunction> Functions,
    IReadOnlyList<BytecodeModuleArrayShapeInfo> ArrayShapes,
    IReadOnlyList<BytecodeModuleExceptionHandlerInfo> ExceptionHandlers);

public enum IlbSectionKind : uint
{
    StringTable = 1,
    BlobTable = 2,
    TypeTable = 3,
    FieldTable = 4,
    MethodTable = 5,
    ConstantTable = 6,
    CodeSection = 7,
    ExceptionTable = 8,
    EntryPoint = 9
}

public sealed record IlbSectionDirectoryEntry(
    IlbSectionKind Kind,
    uint Offset,
    uint Size,
    uint ElementCount,
    uint Alignment,
    uint Flags);

public sealed record IlbImage(
    byte[] Bytes,
    IReadOnlyList<IlbSectionDirectoryEntry> Sections);

public sealed class IlbSerializer
{
    private const ushort MajorVersion = 1;
    private const ushort MinorVersion = 0;
    private const ushort HeaderSize = 64;
    private const uint SectionAlignment = 8;

    public IlbImage Serialize(
        BytecodeModule module,
        IEnumerable<MethodSymbol> methods,
        IEnumerable<FieldSymbol> fields,
        IEnumerable<TypeSymbol> types,
        MethodSymbol? entryPoint)
    {
        var methodList = methods.ToArray();
        var fieldList = fields.ToArray();
        var typeList = CollectTypes(methodList, fieldList, types).ToArray();
        var stringTable = new IlbStringTableBuilder();
        var blobTable = new IlbBlobTableBuilder();

        foreach (var type in typeList)
        {
            if (type is not null)
            {
                stringTable.GetOrAdd(GetNamespace(type));
                stringTable.GetOrAdd(GetSimpleTypeName(type.Name));
            }
        }

        foreach (var field in fieldList)
        {
            stringTable.GetOrAdd(field.Name);
        }

        foreach (var method in methodList)
        {
            stringTable.GetOrAdd(method.Name);
            blobTable.Add(BuildSignatureBlob(method, typeList));
        }

        var typeIds = typeList
            .Select((type, index) => (type, id: (uint)(index + 1)))
            .ToDictionary(pair => pair.type.Name, pair => pair.id, StringComparer.Ordinal);
        var fieldIds = fieldList
            .Select((field, index) => (field, id: (uint)(index + 1)))
            .ToDictionary(pair => GetFieldKey(pair.field), pair => pair.id, StringComparer.Ordinal);
        var methodIds = methodList
            .Select((method, index) => (method, id: (uint)(index + 1)))
            .ToDictionary(pair => GetMethodKey(pair.method), pair => pair.id, StringComparer.Ordinal);
        var functionStringIds = new Dictionary<uint, IReadOnlyList<uint>>();
        foreach (var function in module.Functions)
        {
            functionStringIds[function.FunctionId] = function.StringLiterals
                .Select(stringTable.GetOrAdd)
                .ToArray();
        }

        var codeInfo = BuildCodeSection(module.Functions, functionStringIds);
        var exceptionInfo = BuildExceptionTableSection(module.Functions);
        var stringsPayload = BuildStringTableSection(stringTable);
        var blobsPayload = BuildBlobTableSection(blobTable);
        var typesPayload = BuildTypeTableSection(typeList, stringTable, typeIds, fieldList, methodList);
        var fieldsPayload = BuildFieldTableSection(fieldList, typeIds, stringTable);
        var methodsPayload = BuildMethodTableSection(methodList, typeIds, stringTable, blobTable, codeInfo, exceptionInfo, methodIds);
        var codePayload = codeInfo.SectionBytes;
        var exceptionPayload = exceptionInfo.SectionBytes;
        var entryPayload = BuildEntryPointSection(entryPoint, methodIds);

        var sections = new List<(IlbSectionKind Kind, byte[] Payload, uint Count)>
        {
            (IlbSectionKind.StringTable, stringsPayload, (uint)stringTable.Count),
            (IlbSectionKind.BlobTable, blobsPayload, (uint)blobTable.Count),
            (IlbSectionKind.TypeTable, typesPayload, (uint)typeList.Length),
            (IlbSectionKind.FieldTable, fieldsPayload, (uint)fieldList.Length),
            (IlbSectionKind.MethodTable, methodsPayload, (uint)methodList.Length),
            (IlbSectionKind.CodeSection, codePayload, (uint)module.Functions.Count)
        };

        if (exceptionPayload.Length > 0)
        {
            sections.Add((IlbSectionKind.ExceptionTable, exceptionPayload, (uint)exceptionInfo.RowCount));
        }

        if (entryPayload.Length > 0)
        {
            sections.Add((IlbSectionKind.EntryPoint, entryPayload, 1));
        }

        var sectionDirectoryOffset = HeaderSize;
        var sectionDirectorySize = sections.Count * 24;
        var payloadOffset = Align((uint)(HeaderSize + sectionDirectorySize), SectionAlignment);
        var directory = new List<IlbSectionDirectoryEntry>();
        var positionedPayloads = new List<(IlbSectionDirectoryEntry Section, byte[] Payload)>();

        foreach (var section in sections)
        {
            payloadOffset = Align(payloadOffset, SectionAlignment);
            var entry = new IlbSectionDirectoryEntry(
                section.Kind,
                payloadOffset,
                (uint)section.Payload.Length,
                section.Count,
                SectionAlignment,
                0);
            directory.Add(entry);
            positionedPayloads.Add((entry, section.Payload));
            payloadOffset += (uint)section.Payload.Length;
        }

        var fileSize = payloadOffset;
        using var stream = new MemoryStream((int)fileSize);
        using var writer = new BinaryWriter(stream);
        WriteHeader(writer, (uint)sections.Count, (uint)sectionDirectoryOffset, fileSize, entryPoint is null);
        stream.Position = sectionDirectoryOffset;
        foreach (var entry in directory)
        {
            writer.Write((uint)entry.Kind);
            writer.Write(entry.Offset);
            writer.Write(entry.Size);
            writer.Write(entry.ElementCount);
            writer.Write(entry.Alignment);
            writer.Write(entry.Flags);
        }

        foreach (var positioned in positionedPayloads)
        {
            stream.Position = positioned.Section.Offset;
            writer.Write(positioned.Payload);
        }

        var bytes = stream.ToArray();
        WriteChecksums(bytes);
        return new IlbImage(bytes, directory);
    }

    private static TypeSymbol[] CollectTypes(IEnumerable<MethodSymbol> methods, IEnumerable<FieldSymbol> fields, IEnumerable<TypeSymbol> declaredTypes)
    {
        var types = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        foreach (var type in declaredTypes)
        {
            types[type.Name] = type;
        }

        foreach (var field in fields)
        {
            types[field.Type.Name] = field.Type;
        }

        foreach (var method in methods)
        {
            if (method.ReturnType != TypeSymbol.Void)
            {
                types[method.ReturnType.Name] = method.ReturnType;
            }

            foreach (var parameter in method.Parameters)
            {
                types[parameter.Type.Name] = parameter.Type;
            }
        }

        return types.Values.OrderBy(type => type.Name, StringComparer.Ordinal).ToArray();
    }

    private static byte[] BuildStringTableSection(IlbStringTableBuilder strings)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)strings.Count);
        foreach (var value in strings.Values)
        {
            var utf8 = System.Text.Encoding.UTF8.GetBytes(value);
            writer.Write((uint)utf8.Length);
            writer.Write(utf8);
        }

        return stream.ToArray();
    }

    private static byte[] BuildBlobTableSection(IlbBlobTableBuilder blobs)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)blobs.Count);
        foreach (var blob in blobs.Values)
        {
            writer.Write((uint)blob.Length);
            writer.Write(blob);
        }

        return stream.ToArray();
    }

    private static byte[] BuildTypeTableSection(
        IReadOnlyList<TypeSymbol> types,
        IlbStringTableBuilder strings,
        IReadOnlyDictionary<string, uint> typeIds,
        IReadOnlyList<FieldSymbol> fields,
        IReadOnlyList<MethodSymbol> methods)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var type in types)
        {
            writer.Write(strings.GetOrAdd(GetNamespace(type)));
            writer.Write(strings.GetOrAdd(GetSimpleTypeName(type.Name)));
            writer.Write(GetTypeKind(type));
            writer.Write(GetTypeFlags(type));
            writer.Write(0u);
            writer.Write(0u);
            var ownedFields = fields.Where(field => field.DeclaringTypeName == type.Name).ToArray();
            var ownedMethods = methods.Where(method => method.DeclaringTypeName == type.Name).ToArray();
            writer.Write(ownedFields.Length > 0 ? GetFieldId(ownedFields[0], fields) : 0u);
            writer.Write((uint)ownedFields.Length);
            writer.Write(ownedMethods.Length > 0 ? GetMethodId(ownedMethods[0], methods) : 0u);
            writer.Write((uint)ownedMethods.Length);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
        }

        return stream.ToArray();
    }

    private static byte[] BuildFieldTableSection(
        IReadOnlyList<FieldSymbol> fields,
        IReadOnlyDictionary<string, uint> typeIds,
        IlbStringTableBuilder strings)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var field in fields)
        {
            writer.Write(typeIds[field.DeclaringTypeName!]);
            writer.Write(strings.GetOrAdd(field.Name));
            writer.Write(typeIds[field.Type.Name]);
            writer.Write(GetFieldFlags(field));
            writer.Write((ushort)0);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
        }

        return stream.ToArray();
    }

    private static byte[] BuildMethodTableSection(
        IReadOnlyList<MethodSymbol> methods,
        IReadOnlyDictionary<string, uint> typeIds,
        IlbStringTableBuilder strings,
        IlbBlobTableBuilder blobs,
        IlbCodeSectionInfo codeInfo,
        IlbExceptionSectionInfo exceptionInfo,
        IReadOnlyDictionary<string, uint> methodIds)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var method in methods)
        {
            writer.Write(method.DeclaringTypeName is null ? 0u : typeIds[method.DeclaringTypeName]);
            writer.Write(strings.GetOrAdd(method.Name));
            writer.Write(blobs.GetId(BuildSignatureBlob(method, typeIds.Keys.Select(name => new TypeSymbol(name, true)).ToArray())));
            writer.Write(GetMethodFlags(method));
            var function = codeInfo.FunctionsById[methodIds[GetMethodKey(method)]];
            writer.Write(function.RegisterCount);
            writer.Write((ushort)method.Parameters.Count);
            var localCount = (ushort)Math.Max(function.RegisterCount - function.ArgumentCount - (method.ReturnType != TypeSymbol.Void ? 1 : 0), 0);
            writer.Write(localCount);
            writer.Write(method.ReturnType == TypeSymbol.Void ? 0u : typeIds[method.ReturnType.Name]);
            writer.Write(function.CodeOffset);
            writer.Write(function.CodeSize);
            if (exceptionInfo.FunctionsById.TryGetValue(function.FunctionId, out var functionExceptions))
            {
                writer.Write(functionExceptions.ExceptionStart);
                writer.Write(functionExceptions.ExceptionCount);
            }
            else
            {
                writer.Write(0u);
                writer.Write(0u);
            }
            writer.Write((uint)method.HostImportKind);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
        }

        return stream.ToArray();
    }

    private static byte[] BuildEntryPointSection(MethodSymbol? entryPoint, IReadOnlyDictionary<string, uint> methodIds)
    {
        if (entryPoint is null)
        {
            return [];
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(methodIds[GetMethodKey(entryPoint)]);
        writer.Write(0u);
        return stream.ToArray();
    }

    private static IlbCodeSectionInfo BuildCodeSection(
        IReadOnlyList<BytecodeFunction> functions,
        IReadOnlyDictionary<uint, IReadOnlyList<uint>> functionStringIds)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var map = new Dictionary<uint, IlbCodeFunctionInfo>();
        foreach (var function in functions)
        {
            var offset = (uint)stream.Position;
            foreach (var instruction in function.Instructions)
            {
                var immediate = instruction.Immediate;
                if (instruction.ImmediateKind == InstructionImmediateKind.StringTableIndex &&
                    instruction.OpCode == OpCode.LdStr &&
                    instruction.Immediate > 0 &&
                    functionStringIds.TryGetValue(function.FunctionId, out var stringIds) &&
                    instruction.Immediate <= stringIds.Count)
                {
                    immediate = (int)stringIds[instruction.Immediate - 1];
                }

                writer.Write((byte)instruction.OpCode);
                writer.Write(instruction.Destination);
                writer.Write(instruction.Left);
                writer.Write(instruction.Right);
                writer.Write(immediate);
            }

            var size = (uint)stream.Position - offset;
            map[function.FunctionId] = new IlbCodeFunctionInfo(function.FunctionId, offset, size, function.RegisterCount, function.ArgumentCount);
        }

        return new IlbCodeSectionInfo(stream.ToArray(), map);
    }

    private static IlbExceptionSectionInfo BuildExceptionTableSection(IReadOnlyList<BytecodeFunction> functions)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var map = new Dictionary<uint, IlbFunctionExceptionInfo>();
        uint nextExceptionId = 1;

        foreach (var function in functions)
        {
            if (function.ExceptionHandlers.Count == 0)
            {
                continue;
            }

            var start = nextExceptionId;
            foreach (var handler in function.ExceptionHandlers)
            {
                writer.Write(nextExceptionId++);
                writer.Write((uint)handler.TryStart);
                writer.Write((uint)handler.TryEnd);
                writer.Write((uint)handler.HandlerStart);
                writer.Write((uint)handler.HandlerEnd);
                writer.Write((ushort)1);
                writer.Write((ushort)0);
                writer.Write(handler.CatchTypeId);
                writer.Write(handler.TargetRegister);
                writer.Write((ushort)0);
            }

            map[function.FunctionId] = new IlbFunctionExceptionInfo(function.FunctionId, start, (uint)function.ExceptionHandlers.Count);
        }

        return new IlbExceptionSectionInfo(stream.ToArray(), map, nextExceptionId - 1);
    }

    private static byte[] BuildSignatureBlob(MethodSymbol method, IReadOnlyList<TypeSymbol> types)
    {
        var typeIds = types
            .Select((type, index) => (type, id: (uint)(index + 1)))
            .ToDictionary(pair => pair.type.Name, pair => pair.id, StringComparer.Ordinal);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)(method.IsStatic ? 1 : 2));
        writer.Write(method.ReturnType == TypeSymbol.Void ? 0u : typeIds[method.ReturnType.Name]);
        writer.Write((ushort)method.Parameters.Count);
        foreach (var parameter in method.Parameters)
        {
            writer.Write(typeIds[parameter.Type.Name]);
        }

        writer.Write((ushort)0);
        return stream.ToArray();
    }

    private static ushort GetTypeKind(TypeSymbol type) =>
        type.Name switch
        {
            "Boolean" or "Char" or "Integer" or "String" or "Nil" => 7,
            _ when type.Name.Contains('[') => 6,
            _ => 1
        };

    private static ushort GetTypeFlags(TypeSymbol type)
    {
        ushort flags = 0;
        if (type.IsReferenceType)
        {
            flags |= 1 << 6;
        }
        else
        {
            flags |= 1 << 5;
        }

        return flags;
    }

    private static ushort GetFieldFlags(FieldSymbol field)
    {
        ushort flags = 0;
        flags |= 1 << 0;
        if (field.IsStatic)
        {
            flags |= 1 << 4;
        }

        return flags;
    }

    private static uint GetMethodFlags(MethodSymbol method)
    {
        uint flags = 1u << 0;
        if (method.IsStatic)
        {
            flags |= 1u << 4;
        }

        if (method.IsExtern)
        {
            flags |= 1u << 16;
        }

        if (method.Name == "Main")
        {
            flags |= 1u << 12;
        }

        flags |= 1u << 15;
        return flags;
    }

    private static uint GetFieldId(FieldSymbol field, IReadOnlyList<FieldSymbol> fields) =>
        (uint)(Array.IndexOf(fields.ToArray(), field) + 1);

    private static uint GetMethodId(MethodSymbol method, IReadOnlyList<MethodSymbol> methods) =>
        (uint)(Array.IndexOf(methods.ToArray(), method) + 1);

    private static string GetMethodKey(MethodSymbol method) =>
        $"{method.DeclaringTypeName ?? "<global>"}::{method.Name}/{method.Parameters.Count}";

    private static string GetFieldKey(FieldSymbol field) =>
        $"{field.DeclaringTypeName ?? "<global>"}::{field.Name}";

    private static string GetNamespace(TypeSymbol type) => string.Empty;
    private static string GetSimpleTypeName(string typeName)
    {
        var separator = typeName.LastIndexOf('.');
        return separator >= 0 ? typeName[(separator + 1)..] : typeName;
    }

    private static void WriteHeader(BinaryWriter writer, uint sectionCount, uint directoryOffset, uint fileSize, bool isLibrary)
    {
        writer.Write(System.Text.Encoding.ASCII.GetBytes("ILB1"));
        writer.Write(MajorVersion);
        writer.Write(MinorVersion);
        writer.Write(HeaderSize);
        ushort flags = 0;
        if (isLibrary)
        {
            flags |= 1 << 2;
        }

        flags |= 1 << 3;
        writer.Write(flags);
        writer.Write(sectionCount);
        writer.Write(directoryOffset);
        writer.Write(fileSize);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(Guid.Empty.ToByteArray());
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0UL);
    }

    private static void WriteChecksums(byte[] bytes)
    {
        WriteUInt32(bytes, 24, ComputeChecksum(bytes, 24));
        WriteUInt32(bytes, 28, ComputeChecksum(bytes, 28));
    }

    private static uint ComputeChecksum(byte[] bytes, int skipOffset)
    {
        uint checksum = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (index >= skipOffset && index < skipOffset + 4)
            {
                continue;
            }

            checksum = unchecked((checksum * 16777619) ^ bytes[index]);
        }

        return checksum;
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static uint Align(uint value, uint alignment) =>
        (value + alignment - 1) / alignment * alignment;

    private sealed class IlbStringTableBuilder
    {
        private readonly Dictionary<string, uint> _ids = new(StringComparer.Ordinal);
        private readonly List<string> _values = [];

        public int Count => _values.Count;
        public IReadOnlyList<string> Values => _values;

        public uint GetOrAdd(string value)
        {
            if (_ids.TryGetValue(value, out var existing))
            {
                return existing;
            }

            var id = (uint)(_values.Count + 1);
            _values.Add(value);
            _ids[value] = id;
            return id;
        }
    }

    private sealed class IlbBlobTableBuilder
    {
        private readonly Dictionary<string, uint> _ids = new(StringComparer.Ordinal);
        private readonly List<byte[]> _values = [];

        public int Count => _values.Count;
        public IReadOnlyList<byte[]> Values => _values;

        public uint Add(byte[] blob)
        {
            var key = Convert.ToBase64String(blob);
            if (_ids.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var id = (uint)(_values.Count + 1);
            _values.Add(blob);
            _ids[key] = id;
            return id;
        }

        public uint GetId(byte[] blob) => Add(blob);
    }

    private sealed record IlbCodeFunctionInfo(uint FunctionId, uint CodeOffset, uint CodeSize, ushort RegisterCount, ushort ArgumentCount);
    private sealed record IlbCodeSectionInfo(byte[] SectionBytes, IReadOnlyDictionary<uint, IlbCodeFunctionInfo> FunctionsById);
    private sealed record IlbFunctionExceptionInfo(uint FunctionId, uint ExceptionStart, uint ExceptionCount);
    private sealed record IlbExceptionSectionInfo(byte[] SectionBytes, IReadOnlyDictionary<uint, IlbFunctionExceptionInfo> FunctionsById, uint RowCount);
}

public sealed class BytecodeEmitter
{
    public BytecodeFunction Emit(uint functionId, IrFunction function, MethodSymbol method)
    {
        var instructions = new List<Instruction>();
        var labelOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var pendingBranches = new List<(int Index, string Label)>();
        var stringIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var stringLiterals = new List<string>();
        var arrayShapes = new List<BytecodeArrayShapeInfo>();
        var exceptionHandlers = new List<BytecodeExceptionHandlerInfo>();
        var nextScratchRegister = function.Registers.Count;

        foreach (var block in function.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                switch (instruction.OpCode)
                {
                    case IrOpCode.LoadConstant:
                        if (instruction.Operand is string text)
                        {
                            if (!stringIds.TryGetValue(text, out var stringId))
                            {
                                stringId = stringIds.Count + 1;
                                stringIds[text] = stringId;
                                stringLiterals.Add(text);
                            }

                            instructions.Add(new Instruction(
                                OpCode.LdStr,
                                instruction.Destination?.Index ?? 0,
                                0,
                                0,
                                stringId,
                                InstructionImmediateKind.StringTableIndex));
                        }
                        else
                        {
                            instructions.Add(new Instruction(
                                OpCode.LdI32,
                                instruction.Destination?.Index ?? 0,
                                0,
                                0,
                                Convert.ToInt32(instruction.Operand),
                                InstructionImmediateKind.InlineInt32));
                        }
                        break;
                    case IrOpCode.Copy:
                        instructions.Add(new Instruction(
                            OpCode.Mov,
                            instruction.Destination?.Index ?? 0,
                            ((IrValue?)instruction.Operand)?.Index ?? 0,
                            0));
                        break;
                    case IrOpCode.Add:
                        instructions.Add(EmitBinaryInstruction(OpCode.AddI32, instruction));
                        break;
                    case IrOpCode.ShiftLeft:
                        instructions.Add(EmitBinaryInstruction(OpCode.ShlI32, instruction));
                        break;
                    case IrOpCode.ShiftRight:
                        instructions.Add(EmitBinaryInstruction(OpCode.ShrI32, instruction));
                        break;
                    case IrOpCode.BitwiseAnd:
                        instructions.Add(EmitBinaryInstruction(OpCode.AndI32, instruction));
                        break;
                    case IrOpCode.BitwiseOr:
                        instructions.Add(EmitBinaryInstruction(OpCode.OrI32, instruction));
                        break;
                    case IrOpCode.BitwiseNot:
                        instructions.Add(new Instruction(
                            OpCode.NotI32,
                            instruction.Destination?.Index ?? 0,
                            ((IrValue?)instruction.Operand)?.Index ?? 0,
                            0));
                        break;
                    case IrOpCode.Subtract:
                        instructions.Add(EmitBinaryInstruction(OpCode.SubI32, instruction));
                        break;
                    case IrOpCode.Multiply:
                        instructions.Add(EmitBinaryInstruction(OpCode.MulI32, instruction));
                        break;
                    case IrOpCode.Divide:
                        instructions.Add(EmitBinaryInstruction(OpCode.DivI32, instruction));
                        break;
                    case IrOpCode.Modulo:
                        instructions.Add(EmitBinaryInstruction(OpCode.ModI32, instruction));
                        break;
                    case IrOpCode.CompareEqual:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpEqI32, instruction));
                        break;
                    case IrOpCode.CompareNotEqual:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpNeI32, instruction));
                        break;
                    case IrOpCode.CompareEqualString:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpEqStr, instruction));
                        break;
                    case IrOpCode.CompareNotEqualString:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpNeStr, instruction));
                        break;
                    case IrOpCode.CompareEqualReference:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpEqRef, instruction));
                        break;
                    case IrOpCode.CompareNotEqualReference:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpNeRef, instruction));
                        break;
                    case IrOpCode.ConcatString:
                        instructions.Add(EmitBinaryInstruction(OpCode.ConcatStr, instruction));
                        break;
                    case IrOpCode.ReplaceString:
                    {
                        var target = (IrStringReplaceTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.ReplaceStr,
                            instruction.Destination!.Index,
                            target.Source.Index,
                            target.OldValue.Index,
                            target.NewValue.Index));
                        break;
                    }
                    case IrOpCode.InsertString:
                    {
                        var target = (IrStringInsertTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.InsertStr,
                            instruction.Destination!.Index,
                            target.Source.Index,
                            target.Index.Index,
                            target.Value.Index));
                        break;
                    }
                    case IrOpCode.RemoveString:
                    {
                        var target = (IrStringRemoveTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.RemoveStr,
                            instruction.Destination!.Index,
                            target.Source.Index,
                            target.Index.Index,
                            target.Length.Index));
                        break;
                    }
                    case IrOpCode.ToUpperString:
                        instructions.Add(new Instruction(
                            OpCode.ToUpperStr,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.ToLowerString:
                        instructions.Add(new Instruction(
                            OpCode.ToLowerStr,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.TrimString:
                        instructions.Add(new Instruction(
                            OpCode.TrimStr,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.TrimStartString:
                        instructions.Add(new Instruction(
                            OpCode.TrimStartStr,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.TrimEndString:
                        instructions.Add(new Instruction(
                            OpCode.TrimEndStr,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.ParseStringToInteger:
                        instructions.Add(new Instruction(
                            OpCode.StrToI32,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.TryParseStringToInteger:
                    {
                        var target = (IrStringTryParseTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.TryStrToI32,
                            instruction.Destination!.Index,
                            target.Source.Index,
                            target.ParsedValue.Index));
                        break;
                    }
                    case IrOpCode.ConvertIntegerToString:
                        instructions.Add(new Instruction(
                            OpCode.I32ToStr,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.StartsWithString:
                        instructions.Add(EmitBinaryInstruction(OpCode.StartsWithStr, instruction));
                        break;
                    case IrOpCode.EndsWithString:
                        instructions.Add(EmitBinaryInstruction(OpCode.EndsWithStr, instruction));
                        break;
                    case IrOpCode.ContainsString:
                        instructions.Add(EmitBinaryInstruction(OpCode.ContainsStr, instruction));
                        break;
                    case IrOpCode.IndexOfString:
                        instructions.Add(EmitBinaryInstruction(OpCode.IndexOfStr, instruction));
                        break;
                    case IrOpCode.LastIndexOfString:
                        instructions.Add(EmitBinaryInstruction(OpCode.LastIndexOfStr, instruction));
                        break;
                    case IrOpCode.CompareLess:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpLtI32, instruction));
                        break;
                    case IrOpCode.CompareLessOrEqual:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpLeI32, instruction));
                        break;
                    case IrOpCode.CompareGreater:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpGtI32, instruction));
                        break;
                    case IrOpCode.CompareGreaterOrEqual:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpGeI32, instruction));
                        break;
                    case IrOpCode.NewObject:
                        instructions.Add(new Instruction(
                            OpCode.NewObj,
                            instruction.Destination?.Index ?? 0,
                            0,
                            0,
                            ResolveTypeId((string)instruction.Operand!)));
                        break;
                    case IrOpCode.NewArray:
                        var newArrayOperand = (IrNewArrayTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.NewArr,
                            instruction.Destination?.Index ?? 0,
                            newArrayOperand.LengthRegister.Index,
                            0,
                            ResolveTypeId(newArrayOperand.ElementTypeName)));
                        arrayShapes.Add(new BytecodeArrayShapeInfo(
                            instruction.Destination?.Index ?? 0,
                            newArrayOperand.Shape.Extents.Select(extent => extent.Index).ToArray()));
                        break;
                    case IrOpCode.Call:
                        var callTarget = (IrCallTarget)instruction.Operand!;
                        var firstArgumentRegister = callTarget.Receiver is null
                            ? EmitPackedArguments(instructions, callTarget.Arguments, ref nextScratchRegister)
                            : EmitPackedCallFrame(instructions, callTarget.Receiver, callTarget.Arguments, ref nextScratchRegister);
                        instructions.Add(new Instruction(
                            OpCode.Call,
                            instruction.Destination?.Index ?? 0,
                            firstArgumentRegister,
                            (ushort)(callTarget.Arguments.Count + (callTarget.Receiver is null ? 0 : 1)),
                            ResolveFunctionId(callTarget.Method)));
                        break;
                    case IrOpCode.CallVirtual:
                        var virtualCallTarget = (IrCallTarget)instruction.Operand!;
                        var callBaseRegister = EmitPackedCallFrame(
                            instructions,
                            virtualCallTarget.Receiver,
                            virtualCallTarget.Arguments,
                            ref nextScratchRegister);
                        instructions.Add(new Instruction(
                            OpCode.CallVirt,
                            instruction.Destination?.Index ?? 0,
                            callBaseRegister,
                            (ushort)virtualCallTarget.Arguments.Count,
                            ResolveFunctionId(virtualCallTarget.Method)));
                        break;
                    case IrOpCode.LoadStaticField:
                        var loadFieldTarget = (IrFieldTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.LdSField,
                            instruction.Destination?.Index ?? 0,
                            0,
                            0,
                            ResolveFieldId(loadFieldTarget.Field)));
                        break;
                    case IrOpCode.LoadField:
                        var loadInstanceFieldTarget = (IrFieldTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.LdField,
                            instruction.Destination?.Index ?? 0,
                            loadInstanceFieldTarget.Receiver?.Index ?? 0,
                            0,
                            ResolveFieldId(loadInstanceFieldTarget.Field)));
                        break;
                    case IrOpCode.StoreField:
                        var storeInstanceFieldTarget = (IrFieldTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.StField,
                            storeInstanceFieldTarget.Receiver?.Index ?? 0,
                            instruction.Destination?.Index ?? 0,
                            0,
                            ResolveFieldId(storeInstanceFieldTarget.Field)));
                        break;
                    case IrOpCode.StoreStaticField:
                        var storeFieldTarget = (IrFieldTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.StSField,
                            0,
                            instruction.Destination?.Index ?? 0,
                            0,
                            ResolveFieldId(storeFieldTarget.Field)));
                        break;
                    case IrOpCode.LoadElement:
                        var loadElementTarget = (IrArrayTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.LdElem,
                            instruction.Destination?.Index ?? 0,
                            loadElementTarget.Array.Index,
                            loadElementTarget.Index?.Index ?? 0));
                        break;
                    case IrOpCode.StoreElement:
                        var storeElementTarget = (IrArrayTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.StElem,
                            storeElementTarget.Array.Index,
                            storeElementTarget.Index?.Index ?? 0,
                            instruction.Destination?.Index ?? 0));
                        break;
                    case IrOpCode.LoadLength:
                        var lengthTarget = (IrArrayTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.LdLen,
                            instruction.Destination?.Index ?? 0,
                            lengthTarget.Array.Index,
                            0));
                        break;
                    case IrOpCode.SliceString:
                        var stringSliceTarget = (IrStringSliceTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.SliceStr,
                            instruction.Destination?.Index ?? 0,
                            stringSliceTarget.Source.Index,
                            stringSliceTarget.Start.Index,
                            stringSliceTarget.End.Index));
                        break;
                    case IrOpCode.Throw:
                        instructions.Add(new Instruction(
                            OpCode.Throw,
                            instruction.Destination?.Index ?? 0,
                            0,
                            0,
                            0));
                        break;
                    case IrOpCode.Rethrow:
                        instructions.Add(new Instruction(
                            OpCode.Rethrow,
                            0,
                            0,
                            0,
                            0));
                        break;
                    case IrOpCode.Branch:
                        pendingBranches.Add((instructions.Count, (string)instruction.Operand!));
                        instructions.Add(new Instruction(OpCode.Br));
                        break;
                    case IrOpCode.BranchIfFalse:
                        pendingBranches.Add((instructions.Count, (string)instruction.Operand!));
                        instructions.Add(new Instruction(
                            OpCode.BrFalse,
                            instruction.Destination?.Index ?? 0));
                        break;
                    case IrOpCode.Label:
                        labelOffsets[(string)instruction.Operand!] = instructions.Count;
                        break;
                    case IrOpCode.Return:
                        instructions.Add(new Instruction(OpCode.Ret));
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported IR opcode '{instruction.OpCode}'.");
                }
            }
        }

        foreach (var pendingBranch in pendingBranches)
        {
            if (!labelOffsets.TryGetValue(pendingBranch.Label, out var targetOffset))
            {
                throw new InvalidOperationException($"Unknown branch label '{pendingBranch.Label}'.");
            }

            instructions[pendingBranch.Index] = instructions[pendingBranch.Index] with { Immediate = targetOffset };
        }

        foreach (var handler in function.ExceptionHandlers)
        {
            if (!labelOffsets.TryGetValue(handler.TryStartLabel, out var tryStart) ||
                !labelOffsets.TryGetValue(handler.TryEndLabel, out var tryEnd) ||
                !labelOffsets.TryGetValue(handler.HandlerStartLabel, out var handlerStart) ||
                !labelOffsets.TryGetValue(handler.HandlerEndLabel, out var handlerEnd))
            {
                throw new InvalidOperationException("Unknown exception handler label.");
            }

            var catchTypeId = handler.CatchTypeName is null
                ? 0u
                : unchecked((uint)ResolveTypeId(handler.CatchTypeName));

            exceptionHandlers.Add(new BytecodeExceptionHandlerInfo(
                (ushort)tryStart,
                (ushort)tryEnd,
                (ushort)handlerStart,
                (ushort)handlerEnd,
                handler.TargetRegister,
                catchTypeId));
        }

        return new BytecodeFunction(
            functionId,
            function.Name,
            (ushort)nextScratchRegister,
            (ushort)(method.Parameters.Count + (method.DeclaringTypeName is not null && !method.IsStatic ? 1 : 0)),
            method.HostImportKind,
            instructions,
            arrayShapes,
            exceptionHandlers,
            stringLiterals);
    }

    private static ushort EmitPackedArguments(List<Instruction> instructions, IReadOnlyList<IrValue> arguments, ref int nextScratchRegister)
    {
        if (arguments.Count == 0)
        {
            return 0;
        }

        var firstRegister = (ushort)nextScratchRegister;
        for (var index = 0; index < arguments.Count; index++)
        {
            instructions.Add(new Instruction(
                OpCode.Mov,
                (ushort)(nextScratchRegister + index),
                arguments[index].Index,
                0,
                0));
        }

        nextScratchRegister += arguments.Count;
        return firstRegister;
    }

    private static ushort EmitPackedCallFrame(
        List<Instruction> instructions,
        IrValue? receiver,
        IReadOnlyList<IrValue> arguments,
        ref int nextScratchRegister)
    {
        if (receiver is null)
        {
            return EmitPackedArguments(instructions, arguments, ref nextScratchRegister);
        }

        var baseRegister = (ushort)nextScratchRegister;
        instructions.Add(new Instruction(
            OpCode.Mov,
            baseRegister,
            receiver.Index,
            0,
            0));
        nextScratchRegister++;

        for (var index = 0; index < arguments.Count; index++)
        {
            instructions.Add(new Instruction(
                OpCode.Mov,
                (ushort)(nextScratchRegister + index),
                arguments[index].Index,
                0,
                0));
        }

        nextScratchRegister += arguments.Count;
        return baseRegister;
    }

    private static Instruction EmitBinaryInstruction(OpCode opCode, IrInstruction instruction)
    {
        var operands = ((IrValue Left, IrValue Right))instruction.Operand!;
        return new Instruction(opCode, instruction.Destination?.Index ?? 0, operands.Left.Index, operands.Right.Index, 0);
    }

    public BytecodeModule EmitModule(IEnumerable<MethodSymbol> methods, IEnumerable<FieldSymbol> fields, IEnumerable<TypeSymbol> types, Lowerer lowerer)
    {
        var methodList = methods.ToArray();
        var fieldList = fields.ToArray();
        var typeList = CollectReferencedTypes(methodList, fieldList, types).ToArray();
        var functionIds = methodList
            .Select((method, index) => (method, functionId: (uint)(index + 1)))
            .ToDictionary(pair => GetMethodKey(pair.method), pair => pair.functionId, StringComparer.Ordinal);
        var fieldIds = fieldList
            .Select((field, index) => (field, fieldId: (uint)(index + 1)))
            .ToDictionary(pair => GetFieldKey(pair.field), pair => pair.fieldId, StringComparer.Ordinal);
        var typeIds = typeList
            .Select((type, index) => (type, typeId: (uint)(index + 1)))
            .ToDictionary(pair => pair.type.Name, pair => pair.typeId, StringComparer.Ordinal);

        var functions = new List<BytecodeFunction>();
        uint nextFunctionId = 1;

        foreach (var method in methodList)
        {
            var ir = lowerer.Lower(method);
            functions.Add(Emit(nextFunctionId++, ir, method, functionIds, fieldIds, typeIds));
        }

        var moduleShapes = functions
            .SelectMany(function => function.ArrayShapes.Select(shape => new BytecodeModuleArrayShapeInfo(function.FunctionId, shape.ArrayRegister, shape.ExtentRegisters)))
            .ToArray();
        var moduleExceptionHandlers = functions
            .SelectMany(function => function.ExceptionHandlers.Select(handler => new BytecodeModuleExceptionHandlerInfo(
                function.FunctionId,
                handler.TryStart,
                handler.TryEnd,
                handler.HandlerStart,
                handler.HandlerEnd,
                handler.TargetRegister,
                handler.CatchTypeId)))
            .ToArray();

        return new BytecodeModule(functions, moduleShapes, moduleExceptionHandlers);
    }

    private static TypeSymbol[] CollectReferencedTypes(IEnumerable<MethodSymbol> methods, IEnumerable<FieldSymbol> fields, IEnumerable<TypeSymbol> declaredTypes)
    {
        var collected = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        foreach (var type in declaredTypes)
        {
            collected[type.Name] = type;
        }

        foreach (var field in fields)
        {
            collected[field.Type.Name] = field.Type;
        }

        foreach (var method in methods)
        {
            if (method.ReturnType != TypeSymbol.Void)
            {
                collected[method.ReturnType.Name] = method.ReturnType;
            }

            foreach (var parameter in method.Parameters)
            {
                collected[parameter.Type.Name] = parameter.Type;
            }
        }

        return collected.Values.OrderBy(type => type.Name, StringComparer.Ordinal).ToArray();
    }

    private BytecodeFunction Emit(uint functionId, IrFunction function, MethodSymbol method, IReadOnlyDictionary<string, uint> functionIds, IReadOnlyDictionary<string, uint> fieldIds, IReadOnlyDictionary<string, uint> typeIds)
    {
        _functionIds = functionIds;
        _fieldIds = fieldIds;
        _typeIds = typeIds;
        return Emit(functionId, function, method);
    }

    private IReadOnlyDictionary<string, uint> _functionIds = new Dictionary<string, uint>();
    private IReadOnlyDictionary<string, uint> _fieldIds = new Dictionary<string, uint>();
    private IReadOnlyDictionary<string, uint> _typeIds = new Dictionary<string, uint>();

    private int ResolveFunctionId(MethodSymbol? method)
    {
        if (method is null)
        {
            return 0;
        }

        return _functionIds.TryGetValue(GetMethodKey(method), out var functionId) ? (int)functionId : 0;
    }

    private int ResolveFieldId(FieldSymbol? field)
    {
        if (field is null)
        {
            return 0;
        }

        return _fieldIds.TryGetValue(GetFieldKey(field), out var fieldId) ? (int)fieldId : 0;
    }

    private int ResolveTypeId(string typeName)
        => _typeIds.TryGetValue(typeName, out var typeId) ? (int)typeId : 0;

    private static string GetMethodKey(MethodSymbol method) =>
        $"{method.DeclaringTypeName ?? "<global>"}::{method.Name}/{method.Parameters.Count}";

    private static string GetFieldKey(FieldSymbol field) =>
        $"{field.DeclaringTypeName ?? "<global>"}::{field.Name}";
}
