using ILC.Compiler.Binding;
using ILC.Compiler.Bytecode;
using ILC.Compiler.Core;
using ILC.Compiler.Lowering;
using ILC.Compiler.Syntax;

var failures = new List<string>();

var tree = SyntaxTree.Parse("""
namespace Demo.App;
uses System, Demo.Core;
var value := 42;
Program.Add(3, 4);
public class Program
begin
  public static var Accumulator: Integer;
  public var Counter: Integer;
  public var Items: array of Integer;
  public var Children: array of Program;
  public var Matrix: array[3, 3] of Integer;
  public static property Total: Integer read Accumulator write Accumulator;
  public property Value: Integer read Counter write Counter;
  public property Grid: array[3, 3] of Integer read Matrix write Matrix;
  public default property Item[index: Integer]: Integer read Items write Items;
  public default property Child[index: Integer]: Program read Children write Children;
  public static property AutoTotal: Integer { get; set; };
  public property AutoValue: Integer { get; set; };
  public static property HiddenTotal: Integer { get; private set; };
  public static property SecretRead: Integer { private get; set; };
  public property Created: Integer { get; init; };
  public property Adjusted: Integer
  begin
    get
    begin
      return Counter + 10;
    end;

    set(value)
    begin
      Counter := value - 10;
    end;
  end;

  public constructor(seed: Integer);
  begin
    Counter := seed;
    Items := new Integer[4];
    Children := new Program[2];
    Created := seed + 100;
  end;

  public static function Add(left: Integer, right: Integer): Integer;
  begin
    if left > right then
    begin
      return left - right;
    end;

    return left + right;
  end;

  public static method Main;
  begin
    Program.Total := Program.Add(1, 2);
    Program.AutoTotal := Program.Total;
    Program.HiddenTotal := Program.AutoTotal;
    Program.SecretRead := Program.HiddenTotal + 1;
    var result := Program.Total;
    var text := 'abcd';
    var first := text[0];
    var textSize := text.Length;
    var fixedValues: array[10] of Integer;
    var matrix := new Integer[2, 2];
    var numbers: array of Integer := new Integer[4];
    numbers[0] := result;
    numbers[1] := numbers[0] + 2;
    matrix[0, 1] := numbers[1];
    var matrixCell := matrix[0, 1];
    var size := numbers.Length;
    var p := new Program(5);
    p.Increment();
    p.Value := p.Value + 3;
    p[0] := p.Value;
    p.Matrix := new Integer[3, 3];
    p.Matrix[1, 2] := matrixCell;
    var fieldCell := p.Matrix[1, 2];
    p.Grid := new Integer[3, 3];
    p.Grid[1, 2] := fieldCell;
    var propertyCell := p.Grid[1, 2];
    var programs: array of Program := new Program[2];
    programs[0] := p;
    programs[1] := new Program(7);
    programs[0].Value := programs[0].Value + 1;
    programs[0].Items[0] := programs[1].Current();
    var chainedItem := programs[0].Items[0];
    var chainedCurrent := programs[1].Current();
    p.Child[0] := programs[1];
    var nestedCurrent := p.Child[0].Current();
    p.AutoValue := p.Value + 2;
    p.Adjusted := p.AutoValue + 1;
    var indexed := p[0];
    var current := p.Adjusted + p.Created + numbers[1] + matrixCell + fieldCell + propertyCell + chainedCurrent + chainedItem + nestedCurrent + size + textSize + indexed;
    while result > 0 do
    begin
      result := result - 1;
    end;

    Program.Total := result + current + Program.AutoTotal + Program.SecretRead;
    return;
  end;

  public method Increment;
  begin
    Counter := Counter + 1;
    self.Counter := Counter + 1;
    return;
  end;

  public function Current: Integer;
  begin
    return self.Value;
  end;
end;
""");

if (tree.Root.Tokens.Count == 0)
{
    failures.Add("SyntaxTree.Parse should produce tokens.");
}

if (tree.Root.Namespace?.Name.ToDisplayString() != "Demo.App")
{
    failures.Add("Parser should capture the namespace declaration.");
}

if (tree.Root.Uses?.Imports.Count != 2)
{
    failures.Add("Parser should capture the uses clause.");
}

if (tree.Root.Members.Count != 3)
{
    failures.Add("Parser should capture top-level members.");
}

if (tree.Root.Members[2] is not ClassDeclarationSyntax classDeclaration)
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
}

var binding = new Binder().Bind(tree);
if (binding.HasErrors)
{
    failures.Add("Valid fixture code should not produce binding errors.");
}

if (binding.Diagnostics.Any(diagnostic => diagnostic.Id is "ILC2100" or "ILC2101" or "ILC2102" or "ILC2103" or "ILC2104" or "ILC2105" or "ILC2106"))
{
    failures.Add("Valid fixture code should not produce bootstrap name or assignment diagnostics.");
}

if (binding.Compilation.Methods.Count != 1)
{
    failures.Add("Binder should synthesize one top-level method when top-level code exists.");
}
else if (binding.Compilation.Methods[0].Name != "__TopLevelMain")
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
if (programType is null)
{
    failures.Add("Binder should surface declared classes as named types.");
}
else if (programType.Methods.Count != 7)
{
    failures.Add("Binder should surface declared methods and synthesized property accessors for classes.");
}
else
{
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
        var lowerer = new Lowerer(programType.Methods, programType.Fields, binding.Compilation.Types, programType.Properties);
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
        var lowerer = new Lowerer(programType.Methods, programType.Fields, binding.Compilation.Types, programType.Properties);
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
        else if (mainMethod.Declaration?.Body?.Statements.OfType<ExpressionStatementSyntax>().FirstOrDefault()?.Expression is not AssignmentExpressionSyntax accumulatorAssignment ||
            accumulatorAssignment.Target is not NameExpressionSyntax accumulatorTargetName ||
            accumulatorTargetName.Name.ToDisplayString() != "Program.Total")
        {
            failures.Add("The test fixture expects the first statement to assign Program.Total.");
        }
        else if (mainMethod.Declaration?.Body?.Statements.OfType<LocalVariableDeclarationStatementSyntax>().FirstOrDefault()?.Declarators[0].Initializer is not NameExpressionSyntax fieldRead ||
            fieldRead.Name.ToDisplayString() != "Program.Total")
        {
            failures.Add("The test fixture expects the local initializer to read Program.Total.");
        }
        else if (mainMethod.Declaration?.Body?.Statements.OfType<LocalVariableDeclarationStatementSyntax>().FirstOrDefault()?.Declarators[0].TypeName is not null)
        {
            failures.Add("The test fixture expects local 'var result := Program.Total;' to remain implicitly typed.");
        }

        var mainIr = new Lowerer(programType.Methods, programType.Fields, binding.Compilation.Types, programType.Properties).Lower(mainMethod);
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
            failures.Add("Bytecode emission should lower 'result := result - 1' to subtraction.");
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
                if (stagedVirtualMoves.Length != expectedFrameSize)
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

        if (!mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.LdField) ||
            !mainBytecode.Instructions.Any(instruction => instruction.OpCode == OpCode.StField))
        {
            failures.Add("Bytecode emission should lower p.Value and p.AutoValue access to instance field opcodes.");
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
    if (incrementMethod is null)
    {
        failures.Add("Declared instance methods should be bindable by name.");
    }
    else
    {
        var incrementIr = new Lowerer(programType.Methods, programType.Fields, binding.Compilation.Types, programType.Properties).Lower(incrementMethod);
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
        var currentIr = new Lowerer(programType.Methods, programType.Fields, binding.Compilation.Types, programType.Properties).Lower(currentMethod);
        if (!currentIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.LoadField))
        {
            failures.Add("Instance field reads through self should lower to field load IR.");
        }
    }
}

var method = binding.Compilation.Methods[0];
var ir = new Lowerer(binding.Compilation.GetAllMethods(), binding.Compilation.GetAllFields(), binding.Compilation.Types, binding.Compilation.GetAllProperties()).Lower(method);
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

var allFields = binding.Compilation.GetAllFields();
var module = new BytecodeEmitter().EmitModule(binding.Compilation.Methods.Concat(programType?.Methods ?? []), allFields, binding.Compilation.Types, new Lowerer(binding.Compilation.Methods.Concat(programType?.Methods ?? []), allFields, binding.Compilation.Types, binding.Compilation.GetAllProperties()));
if (module.Functions.Count != 8)
{
    failures.Add("Module emission should include the synthetic entry point, declared methods and synthesized accessor methods.");
}
else if (module.Functions.Count(function => function.Name == "Main") != 1)
{
    failures.Add("Module emission should contain only one declared Main function name.");
}

var ilbImage = new IlbSerializer().Serialize(module, binding.Compilation.Methods.Concat(programType?.Methods ?? []).ToArray(), allFields, binding.Compilation.Types, binding.Compilation.EntryPoint);
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

var callPackingTree = SyntaxTree.Parse("""
public class Program
begin
  public static function Add(left: Integer, right: Integer): Integer;
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
    var packedLowerer = new Lowerer(packedMethods, packedProgram.Fields, callPackingBinding.Compilation.Types, packedProgram.Properties);
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
