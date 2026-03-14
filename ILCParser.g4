parser grammar ILCParser;

options { tokenVocab=ILCLexer; }

// ILC 1.0 compiler edition parser
//
// Important implementation notes:
// - `TypeName(...)` is intentionally parsed as a normal invocation expression.
//   The binder can later reinterpret it as constructor invocation when the
//   callee resolves to a type symbol.
// - `match` exists both as statement and expression. The two forms are kept
//   separate to reduce recovery pain in real-world parser error handling.
// - Modifier validity, override legality, generic constraint semantics,
//   nullability flow and overload resolution belong in semantic analysis.

compilationUnit
    : namespaceDecl? usesClause? attributeSection* topLevelDeclaration* EOF
    ;

namespaceDecl
    : NAMESPACE qualifiedIdentifier SEMI
    ;

usesClause
    : USES qualifiedIdentifier (COMMA qualifiedIdentifier)* SEMI
    ;

attributeSection
    : LBRACK attributeTargetSpec? attributeList RBRACK
    ;

attributeTargetSpec
    : identifier COLON
    ;

attributeList
    : attribute (COMMA attribute)*
    ;

attribute
    : qualifiedIdentifier (LPAREN argumentList? RPAREN)?
    ;

topLevelDeclaration
    : typeAliasDeclaration
    | classDeclaration
    | structDeclaration
    | recordDeclaration
    | interfaceDeclaration
    | enumDeclaration
    | delegateDeclaration
    | extensionBlockDeclaration
    | fieldDeclaration
    | constantDeclaration
    | methodLikeDeclaration
    | topLevelStatement
    ;

modifier
    : visibilityModifier
    | STATIC
    | ABSTRACT
    | VIRTUAL
    | OVERRIDE
    | SEALED
    | PARTIAL
    | EXTERN
    | UNSAFE
    | ASYNC
    | READONLY
    | CONST
    | VOLATILE
    | EXTENSION
    | ITERATOR
    ;

visibilityModifier
    : PUBLIC
    | PRIVATE
    | PROTECTED
    | INTERNAL
    | PROTECTED INTERNAL
    | PRIVATE PROTECTED
    ;

typeAliasDeclaration
    : attributeSection* modifier* TYPE identifier genericParameterList? ASSIGN_EQ typeReference SEMI
    ;

classDeclaration
    : attributeSection* modifier* CLASS identifier genericParameterList? inheritanceClause? classBody
    ;

structDeclaration
    : attributeSection* modifier* STRUCT identifier genericParameterList? inheritanceClause? structBody
    ;

recordDeclaration
    : attributeSection* modifier* RECORD (CLASS | STRUCT)? identifier genericParameterList? primaryConstructorSignature? inheritanceClause? (SEMI | recordBody)
    ;

primaryConstructorSignature
    : LPAREN parameterGroupList? RPAREN
    ;

interfaceDeclaration
    : attributeSection* modifier* INTERFACE identifier genericParameterList? inheritanceClause? interfaceBody
    ;

enumDeclaration
    : attributeSection* modifier* ENUM identifier (COLON typeReference)? enumBody
    ;

delegateDeclaration
    : attributeSection* modifier* DELEGATE delegateKind identifier genericParameterList? parameterList (COLON typeReference)? SEMI
    ;

extensionBlockDeclaration
    : attributeSection* modifier* EXTENSION identifier FOR typeReference BEGIN extensionMemberDeclaration* END SEMI
    ;

extensionMemberDeclaration
    : methodLikeDeclaration
    | propertyDeclaration
    | eventDeclaration
    | operatorDeclaration
    | SEMI
    ;

delegateKind
    : METHOD
    | FUNCTION
    | PROCEDURE
    ;

inheritanceClause
    : COLON typeReference (COMMA typeReference)* constraintClause*
    | constraintClause+
    ;

constraintClause
    : WHERE identifier IS constraintItem (COMMA constraintItem)*
    ;

constraintItem
    : CLASS
    | STRUCT
    | RECORD
    | ENUM
    | DELEGATE
    | UNMANAGED
    | NOTNULL
    | NEW LPAREN RPAREN
    | typeReference
    ;

typeReference
    : namedType QMARK?
    | arrayType QMARK?
    | tupleType QMARK?
    | functionType QMARK?
    ;

namedType
    : qualifiedIdentifier genericArgumentList?
    ;

genericParameterList
    : LT identifier (COMMA identifier)* GT
    ;

genericArgumentList
    : LT typeReference (COMMA typeReference)* GT
    ;

arrayType
    : ARRAY OF typeReference
    | ARRAY LBRACK expression (COMMA expression)* RBRACK OF typeReference
    ;

tupleType
    : LPAREN tupleTypeElement (SEMI tupleTypeElement)* RPAREN
    ;

tupleTypeElement
    : (identifier COLON)? typeReference
    ;

functionType
    : DELEGATE delegateKind parameterList (COLON typeReference)?
    ;

classBody
    : BEGIN classSection* END SEMI
    ;

structBody
    : BEGIN classSection* END SEMI
    ;

recordBody
    : BEGIN classSection* END SEMI
    ;

interfaceBody
    : BEGIN interfaceSection* END SEMI
    ;

enumBody
    : BEGIN enumMember (COMMA enumMember)* COMMA? END SEMI
    ;

enumMember
    : attributeSection* identifier (ASSIGN_EQ expression)?
    ;

classSection
    : accessSection
    | classMemberDeclaration
    ;

interfaceSection
    : accessSection
    | interfaceMemberDeclaration
    ;

accessSection
    : visibilityModifier classMemberDeclaration*
    ;

classMemberDeclaration
    : fieldDeclaration
    | constantDeclaration
    | constructorDeclaration
    | classConstructorDeclaration
    | destructorDeclaration
    | methodLikeDeclaration
    | propertyDeclaration
    | eventDeclaration
    | operatorDeclaration
    | nestedTypeDeclaration
    | extensionBlockDeclaration
    | SEMI
    ;

interfaceMemberDeclaration
    : methodSignature
    | propertySignature
    | eventSignature
    | operatorSignature
    | nestedTypeDeclaration
    | SEMI
    ;

nestedTypeDeclaration
    : typeAliasDeclaration
    | classDeclaration
    | structDeclaration
    | recordDeclaration
    | interfaceDeclaration
    | enumDeclaration
    | delegateDeclaration
    ;

fieldDeclaration
    : attributeSection* modifier* VAR variableDeclaratorList SEMI
    ;

constantDeclaration
    : attributeSection* modifier* CONST constantDeclaratorList SEMI
    ;

variableDeclaratorList
    : variableDeclarator (COMMA variableDeclarator)*
    ;

variableDeclarator
    : identifier (COLON typeReference)? (ASSIGN expression)?
    ;

constantDeclaratorList
    : constantDeclarator (COMMA constantDeclarator)*
    ;

constantDeclarator
    : identifier (COLON typeReference)? ASSIGN_EQ expression
    ;

methodLikeDeclaration
    : methodDeclaration
    | functionDeclaration
    | procedureDeclaration
    ;

methodSignature
    : methodDeclarationHeader SEMI
    ;

methodDeclaration
    : attributeSection* modifier* METHOD identifier genericParameterList? parameterList (COLON typeReference)? constraintClause* methodBodyOrTerminator
    ;

functionDeclaration
    : attributeSection* modifier* FUNCTION identifier genericParameterList? parameterList COLON typeReference constraintClause* methodBodyOrTerminator
    ;

procedureDeclaration
    : attributeSection* modifier* PROCEDURE identifier genericParameterList? parameterList? constraintClause* methodBodyOrTerminator
    ;

methodDeclarationHeader
    : attributeSection* modifier* (METHOD | FUNCTION | PROCEDURE) identifier genericParameterList? parameterList? (COLON typeReference)? constraintClause*
    ;

constructorDeclaration
    : attributeSection* modifier* CONSTRUCTOR parameterList? constructorInitializer? methodBodyOrTerminator
    ;

classConstructorDeclaration
    : attributeSection* modifier* CLASS CONSTRUCTOR parameterList? methodBodyOrTerminator
    ;

destructorDeclaration
    : attributeSection* modifier* DESTRUCTOR methodBodyOrTerminator
    ;

constructorInitializer
    : COLON (BASE | THIS) LPAREN argumentList? RPAREN
    ;

operatorDeclaration
    : attributeSection* modifier* OPERATOR (operatorName | conversionOperatorName) parameterList COLON typeReference methodBodyOrTerminator
    ;

operatorSignature
    : attributeSection* modifier* OPERATOR operatorName parameterList COLON typeReference SEMI
    ;

operatorName
    : PLUS
    | MINUS
    | STAR
    | SLASH
    | DIV
    | MOD
    | ASSIGN_EQ
    | NOT_EQ
    | LT
    | LE
    | GT
    | GE
    | AND
    | OR
    | XOR
    | NOT
    | SHL
    | SHR
    | TRUE
    | FALSE
    ;

conversionOperatorName
    : IMPLICIT
    | EXPLICIT
    ;

methodBodyOrTerminator
    : SEMI
    | ARROW expression SEMI
    | block
    ;

parameterList
    : LPAREN parameterGroupList? RPAREN
    ;

parameterGroupList
    : parameter (SEMI parameter)*
    ;

parameter
    : attributeSection* parameterModifier? identifier COLON typeReference (ASSIGN_EQ expression)?
    | PARAMS identifier COLON ARRAY OF typeReference
    ;

parameterModifier
    : REF
    | OUT
    | IN
    ;

propertyDeclaration
    : attributeSection* modifier* DEFAULT? PROPERTY identifier? propertyParameterList? COLON typeReference propertyImplementation
    ;

propertySignature
    : attributeSection* modifier* DEFAULT? PROPERTY identifier? propertyParameterList? COLON typeReference propertySignatureBody
    ;

propertyParameterList
    : LBRACK parameterGroupList? RBRACK
    ;

propertyImplementation
    : propertyReadWriteForm
    | autoPropertyBody
    | BEGIN propertyAccessor* END SEMI
    ;

propertySignatureBody
    : propertyReadWriteForm
    | autoPropertyBody
    | BEGIN propertyAccessorHeader* END SEMI
    ;

propertyReadWriteForm
    : (READ expressionOrIdentifier)? (WRITE expressionOrIdentifier)? SEMI
    ;

autoPropertyBody
    : LBRACE propertyAutoAccessor+ RBRACE
    ;

propertyAutoAccessor
    : visibilityModifier? GET SEMI
    | visibilityModifier? SET SEMI
    | visibilityModifier? INIT SEMI
    ;

expressionOrIdentifier
    : expression
    | identifier
    ;

propertyAccessor
    : GET block
    | SET (LPAREN identifier RPAREN)? block
    | INIT (LPAREN identifier RPAREN)? block
    ;

propertyAccessorHeader
    : GET SEMI
    | SET SEMI
    | INIT SEMI
    | visibilityModifier GET SEMI
    | visibilityModifier SET SEMI
    | visibilityModifier INIT SEMI
    ;

eventDeclaration
    : attributeSection* modifier* EVENT identifier COLON typeReference eventImplementation
    ;

eventSignature
    : attributeSection* modifier* EVENT identifier COLON typeReference SEMI
    ;

eventImplementation
    : SEMI
    | BEGIN eventAccessor* END SEMI
    ;

eventAccessor
    : ADD (LPAREN identifier RPAREN)? block
    | REMOVE (LPAREN identifier RPAREN)? block
    ;

statement
    : block
    | variableDeclarationStatement
    | yieldStatement
    | expressionStatement
    | ifStatement
    | caseStatement
    | matchStatement
    | whileStatement
    | repeatStatement
    | forStatement
    | foreachStatement
    | breakStatement
    | continueStatement
    | returnStatement
    | tryStatement
    | usingStatement
    | lockStatement
    | checkedStatement
    | uncheckedStatement
    | raiseStatement
    | SEMI
    ;

topLevelStatement
    : statement
    ;

block
    : BEGIN statement* END SEMI
    ;

variableDeclarationStatement
    : VAR variableDeclaratorList SEMI
    ;

yieldStatement
    : YIELD BREAK SEMI
    | YIELD expression SEMI
    ;

expressionStatement
    : expression SEMI
    ;

ifStatement
    : IF expression THEN statement (ELSE statement)?
    ;

caseStatement
    : CASE expression OF caseLabelSection* (ELSE statementSequence)? END SEMI
    ;

caseLabelSection
    : caseLabelList COLON statementSequence
    ;

caseLabelList
    : caseLabel (COMMA caseLabel)*
    ;

caseLabel
    : expression
    | expression RANGE expression
    ;

matchStatement
    : MATCH expression WITH matchArm+ (ELSE statementSequence)? END MATCH SEMI
    ;

matchArm
    : pattern (WHEN expression)? ARROW statement
    ;

matchExpression
    : MATCH expression WITH matchExpressionArm+ (UNDERSCORE ARROW expression)? END
    ;

matchExpressionArm
    : pattern (WHEN expression)? ARROW expression
    ;

whileStatement
    : WHILE expression DO statement
    ;

repeatStatement
    : REPEAT statementSequence UNTIL expression SEMI
    ;

forStatement
    : FOR (variableDeclarationForInit | expressionForInit) (TO | DOWNTO) expression DO statement
    ;

variableDeclarationForInit
    : VAR identifier ASSIGN expression
    ;

expressionForInit
    : expression
    ;

foreachStatement
    : (FOREACH | FOR EACH) VAR? identifier IN expression DO statement
    ;

breakStatement
    : BREAK SEMI
    ;

continueStatement
    : CONTINUE SEMI
    ;

returnStatement
    : RETURN expression? SEMI
    ;

tryStatement
    : TRY statementSequence (exceptClause finallyClause? | finallyClause) END SEMI
    ;

exceptClause
    : EXCEPT exceptionHandler*
    ;

exceptionHandler
    : ON identifier COLON typeReference DO statement
    | statement
    ;

finallyClause
    : FINALLY statementSequence
    ;

usingStatement
    : USING VAR identifier ASSIGN expression (DO statement)?
    ;

lockStatement
    : LOCK expression DO statement
    ;

checkedStatement
    : CHECKED statement
    ;

uncheckedStatement
    : UNCHECKED statement
    ;

raiseStatement
    : (RAISE | THROW) expression? SEMI
    ;

statementSequence
    : statement*
    ;

pattern
    : logicalOrPattern
    ;

logicalOrPattern
    : logicalAndPattern (OR logicalAndPattern)*
    ;

logicalAndPattern
    : simplePattern (AND simplePattern)*
    ;

simplePattern
    : discardPattern
    | constantPattern
    | declarationPattern
    | relationalPattern
    | propertyPattern
    | listPattern
    | rangePattern
    | parenthesizedPattern
    ;

discardPattern
    : UNDERSCORE
    ;

constantPattern
    : literal
    | qualifiedIdentifier
    ;

declarationPattern
    : typeReference identifier
    ;

relationalPattern
    : (LT | LE | GT | GE) expression
    ;

propertyPattern
    : typeReference? LPAREN propertySubpatternList? RPAREN
    ;

propertySubpatternList
    : propertySubpattern (COMMA propertySubpattern)*
    ;

propertySubpattern
    : identifier COLON pattern
    | identifier COLON VAR identifier
    ;

listPattern
    : LBRACK listPatternElementList? RBRACK
    ;

listPatternElementList
    : listPatternElement (COMMA listPatternElement)*
    ;

listPatternElement
    : pattern
    | RANGE VAR identifier
    | RANGE
    ;

rangePattern
    : expression RANGE expression
    ;

parenthesizedPattern
    : LPAREN pattern RPAREN
    ;

expression
    : assignmentExpression
    ;

assignmentExpression
    : nullCoalescingExpression (assignmentOperator assignmentExpression)?
    ;

assignmentOperator
    : ASSIGN
    | ADD_ASSIGN
    | SUB_ASSIGN
    | MUL_ASSIGN
    | DIV_ASSIGN
    | AND_ASSIGN
    | OR_ASSIGN
    | XOR_ASSIGN
    | SHL_ASSIGN
    | SHR_ASSIGN
    | COALESCE_ASSIGN
    ;

nullCoalescingExpression
    : logicalOrExpression (COALESCE logicalOrExpression)*
    ;

logicalOrExpression
    : logicalXorExpression (OR logicalXorExpression)*
    ;

logicalXorExpression
    : logicalAndExpression (XOR logicalAndExpression)*
    ;

logicalAndExpression
    : equalityExpression (AND equalityExpression)*
    ;

equalityExpression
    : relationalExpression ((ASSIGN_EQ | NOT_EQ) relationalExpression)*
    ;

relationalExpression
    : shiftExpression ((LT | LE | GT | GE | IS | IN) shiftExpression)*
    ;

shiftExpression
    : additiveExpression ((SHL | SHR) additiveExpression)*
    ;

additiveExpression
    : multiplicativeExpression ((PLUS | MINUS) multiplicativeExpression)*
    ;

multiplicativeExpression
    : unaryExpression ((STAR | SLASH | DIV | MOD) unaryExpression)*
    ;

unaryExpression
    : primaryExpression
    | (PLUS | MINUS | NOT | AWAIT) unaryExpression
    | TYPEOF LPAREN typeReference RPAREN
    | NAMEOF LPAREN expression RPAREN
    | (INCREMENT | DECREMENT) unaryExpression
    ;

primaryExpression
    : atomExpression postfixPart*
    ;

postfixPart
    : DOT identifierOrKeyword
    | QDOT identifierOrKeyword
    | LBRACK argumentList? RBRACK
    | QLBRACK argumentList? RBRACK
    | LPAREN argumentList? RPAREN
    | INCREMENT
    | DECREMENT
    | BANG
    ;

atomExpression
    : literal
    | SELF
    | THIS
    | BASE
    | INHERITED
    | identifierOrKeyword
    | qualifiedIdentifier
    | tupleExpression
    | collectionExpression
    | newObjectCreationExpression
    | lambdaExpression
    | LPAREN expression RPAREN
    | matchExpression
    | DEFAULT LPAREN typeReference RPAREN
    ;

newObjectCreationExpression
    : NEW namedType LPAREN argumentList? RPAREN
    ;

collectionExpression
    : LBRACK argumentList? RBRACK
    ;

tupleExpression
    : LPAREN tupleArgument (COMMA tupleArgument)+ RPAREN
    ;

tupleArgument
    : (identifier COLON)? expression
    ;

lambdaExpression
    : lambdaParameters ARROW (expression | block)
    ;

lambdaParameters
    : identifier
    | LPAREN lambdaParameterList? RPAREN
    ;

lambdaParameterList
    : lambdaParameter (COMMA lambdaParameter)*
    ;

lambdaParameter
    : identifier
    | identifier COLON typeReference
    ;

argumentList
    : argument (COMMA argument)*
    ;

argument
    : (identifier COLON)? expression
    ;

literal
    : IntegerLiteral
    | FloatLiteral
    | StringLiteral
    | InterpolatedStringLiteral
    | RawStringLiteral
    | CharLiteral
    | TRUE
    | FALSE
    | NIL
    ;

qualifiedIdentifier
    : identifierOrKeyword (DOT identifierOrKeyword)*
    ;

identifierOrKeyword
    : identifier
    | escapedIdentifier
    ;

identifier
    : Identifier
    ;

escapedIdentifier
    : EscapedIdentifier
    ;

