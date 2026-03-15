using ILC.Compiler.Binding;
using ILC.Compiler.Bytecode;
using ILC.Compiler.Core;
using ILC.Compiler.Lowering;
using ILC.Compiler.Syntax;

var failures = new List<string>();

var tree = SyntaxTree.Parse("""
namespace Demo.App;
uses Sys = System, Demo.Core;
const TopBonus: Integer = 2;
public enum Mode
begin
  Idle;
  Busy;
  Done = 5;
end;
public record Point
begin
  public var X: Integer;
  public var Y: Integer;
end;
var value := 42;
Program.Add(3, 4);
public class Program
begin
  public static const DefaultSeed: Integer = 1;
  public static const CaseHit: Integer = 90;
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

  public static function ParseSeed(text: String; out value: Integer): Boolean;
  begin
    value := Integer.Parse(text);
    return true;
  end;

  public static procedure Bump(ref value: Integer);
  begin
    value := value + 1;
  end;

  public static procedure ReadOnly(in value: Integer);
  begin
    Program.Total := Program.Total + value;
  end;

  public static procedure Collect(params values: array of Integer);
  begin
    Program.Total := Program.Total + values.Length;
  end;

  public static function Add(left: Integer; right: Integer): Integer;
  begin
    if left > right then
    begin
      exit left - right;
    end;

    exit left + right;
  end;

  public static method Main;
  begin
    Program.Total := Program.Add(Program.DefaultSeed, TopBonus);
    Program.AutoTotal := Program.Total;
    Program.HiddenTotal := Program.AutoTotal;
    Program.SecretRead := Program.HiddenTotal + 1;
    var result := Program.Total;
    var text := 'abcd';
    var first := text[0];
    var textSize := text.Length;
    var maybeProgram: Program := nil;
    var noProgram := maybeProgram = nil;
    var sameText := text = 'abcd';
    var differentText := text <> 'abce';
    var maybeText: String := nil;
    var noText := maybeText = nil;
    var hasMissingText := not noText;
    var invertedZero := not 0;
    var coalescedText := maybeText ?? 'xy';
    var preservedText := coalescedText ?? 'zz';
    var hasTextType := text is String;
    var castText := text as String;
    var castTextOk := castText <> nil;
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
    var hasProgramType := programs[1] is Program;
    var castProgram := programs[1] as Program;
    var castProgramOk := castProgram <> nil;
    var castMismatch := text as Program;
    var castMismatchNil := castMismatch = nil;
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
    if sameText then
    begin
      current := current + 1;
    end;

    if differentText then
    begin
      current := current + 1;
    end;

    if noProgram then
    begin
      current := current + 1;
    end;

    if hasProgramType then
    begin
      current := current + 1;
    end;

    if noText then
    begin
      current := current + 1;
    end;

    if hasTextType then
    begin
      current := current + 1;
    end;

    if castTextOk then
    begin
      current := current + 1;
    end;

    if castProgramOk then
    begin
      current := current + 1;
    end;

    if castMismatchNil then
    begin
      current := current + 1;
    end;

    try
      raise 7;
    except
      current := current + 1;
    end;

    try
      raise new Program(6);
    except
      on ex: Program do
        current := current + ex.Current();
      current := current + 2;
    end;

    try
      current := current + 1;
    finally
      current := current + 1;
    end;

    try
      raise new Program(3);
    except
      on ex: Program do
        current := current + ex.Current();
    finally
      current := current + 1;
    end;

    var index := 0;
    for var ascending := 0 to 2 do
    begin
      current := current + ascending;
    end;

    for var descending := 2 downto 0 do
    begin
      current := current + descending;
    end;

    for var steppedUp := 0 to 4 step 2 do
    begin
      current := current + steppedUp;
    end;

    for var steppedDown := 4 downto 0 step 2 do
    begin
      current := current + steppedDown;
    end;

    for each var item in numbers do
    begin
      current := current + item;
    end;

    var numberSlice := numbers[0..1];
    current := current + numberSlice.Length + numberSlice[0];

    var textSlice := text[1..2];
    current := current + textSlice.Length;
    if textSlice = 'bc' then
    begin
      current := current + 1;
    end;

    var joinedText := textSlice + 'd';
    current := current + joinedText.Length;
    if joinedText = 'bcd' then
    begin
      current := current + 1;
    end;

    if joinedText.StartsWith('bc') then
    begin
      current := current + 1;
    end;

    if joinedText.EndsWith('cd') then
    begin
      current := current + 1;
    end;

    if joinedText.Contains('c') then
    begin
      current := current + 1;
    end;

    current := current + joinedText.IndexOf('c');
    current := current + joinedText.LastIndexOf('d');
    var middleText := joinedText.Substring(1, 2);
    current := current + middleText.Length;
    if middleText = 'cd' then
    begin
      current := current + 1;
    end;
    var replacedText := joinedText.Replace('z', 'x');
    current := current + replacedText.Length;
    if replacedText.EndsWith('x') then
    begin
      current := current + 1;
    end;
    var insertedText := joinedText.Insert(1, 'x');
    current := current + insertedText.Length;
    if insertedText = 'bxcd' then
    begin
      current := current + 1;
    end;
    var removedText := insertedText.Remove(1, 1);
    current := current + removedText.Length;
    if removedText = 'bcd' then
    begin
      current := current + 1;
    end;
    var upperText := removedText.ToUpper();
    current := current + upperText.Length;
    if upperText = 'BCD' then
    begin
      current := current + 1;
    end;
    var lowerText := upperText.ToLower();
    current := current + lowerText.Length;
    if lowerText = 'bcd' then
    begin
      current := current + 1;
    end;
    var paddedText := '  bcd  ';
    var trimmedText := paddedText.Trim();
    current := current + trimmedText.Length;
    if trimmedText = 'bcd' then
    begin
      current := current + 1;
    end;
    var trimStartText := paddedText.TrimStart();
    current := current + trimStartText.Length;
    if trimStartText = 'bcd  ' then
    begin
      current := current + 1;
    end;
    var trimEndText := paddedText.TrimEnd();
    current := current + trimEndText.Length;
    if trimEndText = '  bcd' then
    begin
      current := current + 1;
    end;
    var parsedTextValue := Integer.Parse('15');
    current := current + parsedTextValue;
    var tryParsedValue: Integer := 99;
    if Integer.TryParse('18', tryParsedValue) then
    begin
      current := current + tryParsedValue;
    end;
    if Integer.TryParse('oops', tryParsedValue) then
    begin
      current := current + 100;
    end
    else
    begin
      current := current + 1;
    end;
    if tryParsedValue = 0 then
    begin
      current := current + 1;
    end;
    Program.Collect(1, 2, 3);
    var formattedValue := parsedTextValue.ToString();
    current := current + formattedValue.Length;
    if formattedValue = '15' then
    begin
      current := current + 1;
    end;
    if hasMissingText then
    begin
      current += 100;
    end
    else
    begin
      current += 1;
    end;
    if invertedZero <> 0 then
    begin
      current += 1;
    end;
    var shiftedLeft := 3 shl 2;
    var shiftedRight := 16 shr 2;
    current += shiftedLeft;
    current += shiftedRight;
    current shl= 1;
    current shr= 1;
    var moduloValue := 17 mod 5;
    current += moduloValue;
    current mod= 7;
    var quotientValue := 17 div 5;
    current += quotientValue;
    var quotientAccumulator := 20;
    quotientAccumulator div= 3;
    current += quotientAccumulator;
    current += coalescedText.Length;
    if coalescedText = 'xy' then
    begin
      current += 1;
    end;
    current += preservedText.Length;
    if preservedText = 'xy' then
    begin
      current += 1;
    end;
    current += 1;
    current *= 2;
    current -= 3;
    current /= 3;
    textSlice += 'd';
    current += textSlice.Length;
    maybeText ??= 'xy';
    current += maybeText.Length;
    if maybeText = 'xy' then
    begin
      current += 1;
    end;
    maybeText ??= 'zz';
    if maybeText = 'xy' then
    begin
      current += 1;
    end;

    foreach var ch in text do
    begin
      current := current + 1;
    end;

    for index := 0 to 4 do
    begin
      if index = 3 then
      begin
        break;
      end;

      current := current + index;
    end;

    for index := 0 to 3 do
    begin
      if index = 1 then
      begin
        continue;
      end;

      current := current + 1;
    end;

    repeat
      result := result + 1;
      if result = 2 then
      begin
        continue;
      end;

      if result = 4 then
      begin
        break;
      end;

      current := current + 1;
    until result > 10;

    case current of
      88:
        current := current + 2;
      89, Program.CaseHit:
        current := current + 3;
    else
      current := current + 9;
    end;

    case 2 of
      1..3:
        current := current + 1;
    end;

    case text of
      'abcd':
        current := current + 1;
    else
      current := current + 7;
    end;

    var mode: Mode := Mode.Busy;
    var activeModes: set of Mode := [Mode.Busy, Mode.Done];
    var extraModes: set of Mode := [Mode.Idle..Mode.Busy, Mode.Done];
    var combinedModes := activeModes + extraModes;
    var commonModes := combinedModes * [Mode.Done];
    var reducedModes := combinedModes - [Mode.Busy];
    case mode of
      Mode.Idle:
        current := current + 1000;
      Mode.Busy:
        current := current + 3;
      Mode.Done:
        current := current + 1000;
    end;

    case mode of
      Mode.Busy when Mode.Done in activeModes:
        current := current + 2;
    else
      current := current + 1000;
    end;

    match mode with
      Mode.Idle => current := current + 1000;
      Mode.Busy or Mode.Done => current := current + 2;
    else
      current := current + 1000;
    end match;

    var modeText := match mode with
      Mode.Busy => 'busy'
      Mode.Done => 'done'
      _ => 'idle'
    end;
    current := current + modeText.Length;

    match castText with
      String s when s.Length = 4 => current := current + 2;
    else
      current := current + 1000;
    end match;

    var matchedText := match castText with
      String s when s.Length = 4 => s
      _ => 'bad'
    end;
    current := current + matchedText.Length;

    var objectText := match castText with
      Object any => 'obj'
      _ => 'bad'
    end;
    current := current + objectText.Length;

    match 5 with
      < 0 => current := current + 1000;
      >= 5 => current := current + 2;
    else
      current := current + 1000;
    end match;

    var sizedText := match 5 with
      >= 5 => 'high'
      _ => 'low'
    end;
    current := current + sizedText.Length;

    match 7 with
      >= 5 and <= 10 => current := current + 2;
    else
      current := current + 1000;
    end match;

    var boundedText := match 7 with
      >= 5 and < 10 => 'mid'
      _ => 'low'
    end;
    current := current + boundedText.Length;

    match 12 with
      < 0 or >= 10 => current := current + 2;
    else
      current := current + 1000;
    end match;

    var edgeText := match 12 with
      < 0 or >= 10 => 'edge'
      _ => 'inner'
    end;
    current := current + edgeText.Length;
    var negativeSeed := -1;
    current := current + (negativeSeed + 2);
    inc(current);
    dec(current);
    inc(p.Value);
    dec(Program.Total);

    match 12 with
      not < 0 or 7 => current := current + 2;
    else
      current := current + 1000;
    end match;

    var mixedText := match 12 with
      not < 0 or 7 => 'mix'
      _ => 'bad'
    end;
    current := current + mixedText.Length;

    match 5 with
      not < 0 => current := current + 2;
    else
      current := current + 1000;
    end match;

    var notText := match 1 with
      not < 0 => 'x'
      _ => 'ok'
    end;
    current := current + notText.Length;

    if Mode.Busy in activeModes then
    begin
      current := current + 3;
    end;

    if Mode.Busy in combinedModes then
    begin
      current := current + 1;
    end;

    if Mode.Done in commonModes then
    begin
      current := current + 1;
    end;

    if Mode.Idle in reducedModes then
    begin
      current := current + 1;
    end;
    activeModes or= [Mode.Idle];
    if Mode.Idle in activeModes then
    begin
      current := current + 1;
    end;
    activeModes and= [Mode.Done];
    if Mode.Done in activeModes then
    begin
      current := current + 1;
    end;
    if mode in activeModes then
    begin
      current := current + 2;
    end;
    if mode not in activeModes then
    begin
      current := current + 100;
    end
    else
    begin
      current := current + 1;
    end;
    activeModes xor= [Mode.Done];
    if Mode.Done in activeModes then
    begin
      current := current + 100;
    end
    else
    begin
      current := current + 1;
    end;
    include(activeModes, Mode.Busy);
    if Mode.Busy in activeModes then
    begin
      current := current + 1;
    end;
    exclude(activeModes, Mode.Busy);
    if Mode.Busy in activeModes then
    begin
      current := current + 100;
    end
    else
    begin
      current := current + 1;
    end;
    for each var listedMode in extraModes do
    begin
      current := current + 1;
    end;

    var point := new Point();
    point.X := 1;
    point.Y := 2;
    var point2 := new Point();
    current := current + point.X + point.Y;
    with point2 do
    begin
      X := 1;
      Y := 2;
      current := current + X + Y;
    end;
    if point = point2 then
    begin
      current := current + 1;
    end;

    var compoundProgram := new Program(5);
    compoundProgram.Value += 2;
    compoundProgram.Counter += 3;
    Program.Total += 4;
    current := current + compoundProgram.Value + compoundProgram.Counter;

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
else if (tree.Root.Uses.Imports[0].AliasIdentifier?.Text != "Sys" ||
         tree.Root.Uses.Imports[0].NamespaceName.ToDisplayString() != "System")
{
    failures.Add("Parser should capture uses aliases.");
}

if (tree.Root.Members.Count != 6)
{
    failures.Add("Parser should capture top-level members.");
}

if (tree.Root.Members[5] is not ClassDeclarationSyntax classDeclaration)
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

if (binding.Diagnostics.Any(diagnostic => diagnostic.Id is "ILC2151" or "ILC2152" or "ILC2153" or "ILC2154" or "ILC2155" or "ILC2156" or "ILC2157"))
{
    failures.Add("Valid fixture code should not produce bootstrap set diagnostics.");
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

if (!binding.Compilation.GetAllConstants().Any(constant => constant.DeclaringTypeName == "Mode" && constant.Name == "Busy" && Equals(constant.Value, 1)))
{
    failures.Add("Binder should surface enum members as typed constants with sequential values.");
}

if (programType is null)
{
    failures.Add("Binder should surface declared classes as named types.");
}
else if (programType.Methods.Count != 11)
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
        var lowerer = new Lowerer(binding.Compilation.GetAllMethods(), binding.Compilation.GetAllFields(), binding.Compilation.Types, binding.Compilation.GetAllProperties(), binding.Compilation.GetAllConstants());
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
        var lowerer = new Lowerer(binding.Compilation.GetAllMethods(), binding.Compilation.GetAllFields(), binding.Compilation.Types, binding.Compilation.GetAllProperties(), binding.Compilation.GetAllConstants());
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

var mainIr = new Lowerer(binding.Compilation.GetAllMethods(), binding.Compilation.GetAllFields(), binding.Compilation.Types, binding.Compilation.GetAllProperties(), binding.Compilation.GetAllConstants()).Lower(mainMethod);
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

                    if (!mainBytecode.Instructions
                            .Skip(paramsArrayAllocation.index + 1)
                            .Take(collectCallIndex - paramsArrayAllocation.index - 1)
                            .Any(instruction =>
                                instruction.OpCode == OpCode.Mov &&
                                instruction.Destination == collectCall.Left &&
                                instruction.Left == paramsArrayRegister))
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
        var incrementIr = new Lowerer(programType.Methods, programType.Fields, binding.Compilation.Types, programType.Properties, binding.Compilation.GetAllConstants()).Lower(incrementMethod);
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
        var currentIr = new Lowerer(programType.Methods, programType.Fields, binding.Compilation.Types, programType.Properties, binding.Compilation.GetAllConstants()).Lower(currentMethod);
        if (!currentIr.Blocks.SelectMany(block => block.Instructions).Any(instruction => instruction.OpCode == IrOpCode.LoadField))
        {
            failures.Add("Instance field reads through self should lower to field load IR.");
        }
    }
}

var method = binding.Compilation.Methods[0];
var ir = new Lowerer(binding.Compilation.GetAllMethods(), binding.Compilation.GetAllFields(), binding.Compilation.Types, binding.Compilation.GetAllProperties(), binding.Compilation.GetAllConstants()).Lower(method);
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
var module = new BytecodeEmitter().EmitModule(binding.Compilation.Methods.Concat(programType?.Methods ?? []), allFields, binding.Compilation.Types, new Lowerer(binding.Compilation.Methods.Concat(programType?.Methods ?? []), allFields, binding.Compilation.Types, binding.Compilation.GetAllProperties(), binding.Compilation.GetAllConstants()));
if (module.Functions.Count != 12)
{
    failures.Add("Module emission should include the synthetic entry point, declared methods and synthesized accessor methods.");
}
else if (module.Functions.Count(function => function.Name == "Main") != 1)
{
    failures.Add("Module emission should contain only one declared Main function name.");
}
else if (!module.ExceptionHandlers.Any())
{
    failures.Add("Module emission should surface exception handler metadata when try/except is present.");
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

var invalidFinallyTree = SyntaxTree.Parse("""
public class Program
begin
  public method Main: Integer;
  begin
    try
      return 1;
    finally
      return 2;
    end;
  end;
end;
""");

var invalidFinallyBinding = new Binder().Bind(invalidFinallyTree);
if (!invalidFinallyBinding.Diagnostics.Any(diagnostic => diagnostic.Id == "ILC2134"))
{
    failures.Add("Binder should report unsupported return inside try/finally.");
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
        var externLowerer = new Lowerer(externBinding.Compilation.GetAllMethods(), externBinding.Compilation.GetAllFields(), externBinding.Compilation.Types, externBinding.Compilation.GetAllProperties(), externBinding.Compilation.GetAllConstants());
        var externModule = new BytecodeEmitter().EmitModule(externBinding.Compilation.GetAllMethods(), externBinding.Compilation.GetAllFields(), externBinding.Compilation.Types, externLowerer);
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
