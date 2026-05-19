namespace ILC.Compiler.Lowering;

using ILC.Compiler.Binding;
using ILC.Compiler.Core;
using ILC.Compiler.Syntax;
using System.Collections;
using System.Reflection;

public sealed partial class Lowerer
{
    private void LowerExpressionStatement(
        ExpressionSyntax expression,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var tempType = SemanticFacts.InferExpressionType(expression, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var tempRegister = new IrValue($"r{registers.Count}", tempType, (ushort)registers.Count);
        registers.Add(tempRegister);
        LowerExpressionInto(expression, tempRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
    }

    private static bool IsDiscardAssignmentTarget(ExpressionSyntax expression) =>
        expression is NameExpressionSyntax { Name.Parts.Count: 1 } name &&
        name.Name.Parts[0].Text == "_";

    private void LowerIncDecStatement(
        ExpressionSyntax target,
        SyntaxKind operatorKind,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var resultRegister = AllocateTemp(TypeSymbol.Integer, registers);
        var operatorToken = new SyntaxToken(operatorKind, operatorKind == SyntaxKind.PlusAssignToken ? "+=" : "-=", null, new ILC.Compiler.Core.TextSpan(0, 0));
        var oneToken = new SyntaxToken(SyntaxKind.NumberToken, "1", 1, new ILC.Compiler.Core.TextSpan(0, 0));
        LowerCompoundAssignmentInto(
            new CompoundAssignmentExpressionSyntax(target, operatorToken, new LiteralExpressionSyntax(oneToken)),
            resultRegister,
            registerByName,
            arrayShapesByName,
            registers,
            instructions,
            currentMethod);
    }

    private void LowerIncludeExcludeStatement(
        ExpressionSyntax target,
        ExpressionSyntax value,
        bool isInclude,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var localTypes = GetLocalTypes(registerByName);
        var targetType = SemanticFacts.InferExpressionType(target, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var elementType = SemanticFacts.GetSetElementType(targetType) ?? TypeSymbol.Integer;
        var currentValueRegister = AllocateTemp(targetType, registers);
        LowerExpressionInto(target, currentValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var elementRegister = AllocateTemp(elementType, registers);
        LowerExpressionInto(value, elementRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var oneRegister = AllocateTemp(targetType, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, oneRegister, 1));

        var maskRegister = AllocateTemp(targetType, registers);
        instructions.Add(new IrInstruction(IrOpCode.ShiftLeft, maskRegister, (oneRegister, elementRegister)));

        var resultRegister = AllocateTemp(targetType, registers);
        if (isInclude)
        {
            instructions.Add(new IrInstruction(IrOpCode.BitwiseOr, resultRegister, (currentValueRegister, maskRegister)));
        }
        else
        {
            var notMaskRegister = AllocateTemp(targetType, registers);
            instructions.Add(new IrInstruction(IrOpCode.BitwiseNot, notMaskRegister, maskRegister));
            instructions.Add(new IrInstruction(IrOpCode.BitwiseAnd, resultRegister, (currentValueRegister, notMaskRegister)));
        }

        StoreValueIntoTarget(target, resultRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
    }

    private void LowerReturnExpression(
        ExpressionSyntax expression,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        MethodSymbol? currentMethod = null)
    {
        LowerExpressionInto(expression, returnRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
    }
}
