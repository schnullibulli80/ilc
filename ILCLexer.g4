lexer grammar ILCLexer;

// ILC 1.0 compiler edition lexer
//
// Deliberate parser-facing design choices in this split:
// - `{ ... }` comments are NOT enabled here because `{ get; set; }` is used
//   for auto-properties. Supported comments are `// ...` and `(* ... *)`.
// - Keywords are lowercase in the grammar and therefore case-sensitive at the
//   lexer level. A later compiler pass may normalize casing if ILC should be
//   case-insensitive in the final implementation.
// - Pascal-style constructor calls `TypeName(...)` are parsed as ordinary
//   invocations and can be rebound as constructor calls during semantic analysis.

NAMESPACE : 'namespace';
USES : 'uses';

PUBLIC : 'public';
PRIVATE : 'private';
PROTECTED : 'protected';
INTERNAL : 'internal';

STATIC : 'static';
ABSTRACT : 'abstract';
VIRTUAL : 'virtual';
OVERRIDE : 'override';
SEALED : 'sealed';
PARTIAL : 'partial';
EXTERN : 'extern';
UNSAFE : 'unsafe';
ASYNC : 'async';
READONLY : 'readonly';
CONST : 'const';
VOLATILE : 'volatile';
EXTENSION : 'extension';
ITERATOR : 'iterator';

TYPE : 'type';
CLASS : 'class';
STRUCT : 'struct';
RECORD : 'record';
INTERFACE : 'interface';
ENUM : 'enum';
DELEGATE : 'delegate';

METHOD : 'method';
FUNCTION : 'function';
PROCEDURE : 'procedure';
CONSTRUCTOR : 'constructor';
DESTRUCTOR : 'destructor';
PROPERTY : 'property';
DEFAULT : 'default';
EVENT : 'event';
OPERATOR : 'operator';
IMPLICIT : 'implicit';
EXPLICIT : 'explicit';

VAR : 'var';
BEGIN : 'begin';
END : 'end';
IF : 'if';
THEN : 'then';
ELSE : 'else';
CASE : 'case';
OF : 'of';
MATCH : 'match';
WITH : 'with';
WHEN : 'when';
WHILE : 'while';
DO : 'do';
REPEAT : 'repeat';
UNTIL : 'until';
FOR : 'for';
FOREACH : 'foreach';
EACH : 'each';
TO : 'to';
DOWNTO : 'downto';
IN : 'in';
IS : 'is';
WHERE : 'where';
NOTNULL : 'notnull';
UNMANAGED : 'unmanaged';
NEW : 'new';
TRY : 'try';
EXCEPT : 'except';
ON : 'on';
FINALLY : 'finally';
USING : 'using';
LOCK : 'lock';
CHECKED : 'checked';
UNCHECKED : 'unchecked';
RAISE : 'raise';
THROW : 'throw';
RETURN : 'return';
BREAK : 'break';
CONTINUE : 'continue';
GET : 'get';
SET : 'set';
INIT : 'init';
ADD : 'add';
REMOVE : 'remove';
READ : 'read';
WRITE : 'write';
REF : 'ref';
OUT : 'out';
PARAMS : 'params';
ARRAY : 'array';
TRUE : 'true';
FALSE : 'false';
NIL : 'nil';
SELF : 'self';
THIS : 'this';
BASE : 'base';
INHERITED : 'inherited';
AWAIT : 'await';
TYPEOF : 'typeof';
NAMEOF : 'nameof';
YIELD : 'yield';
AND : 'and';
OR : 'or';
XOR : 'xor';
NOT : 'not';
DIV : 'div';
MOD : 'mod';
SHL : 'shl';
SHR : 'shr';

ARROW : '=>';
RANGE : '..';
QDOT : '?.';
QLBRACK : '?[';
COALESCE : '??';
COALESCE_ASSIGN : '??=';
ADD_ASSIGN : '+=';
SUB_ASSIGN : '-=';
MUL_ASSIGN : '*=';
DIV_ASSIGN : '/=';
AND_ASSIGN : 'and=';
OR_ASSIGN : 'or=';
XOR_ASSIGN : 'xor=';
SHL_ASSIGN : 'shl=';
SHR_ASSIGN : 'shr=';
ASSIGN : ':=';
ASSIGN_EQ : '=';
NOT_EQ : '<>';
LE : '<=';
GE : '>=';
LT : '<';
GT : '>';
PLUS : '+';
MINUS : '-';
STAR : '*';
SLASH : '/';
INCREMENT : '++';
DECREMENT : '--';
BANG : '!';

LPAREN : '(';
RPAREN : ')';
LBRACK : '[';
RBRACK : ']';
LBRACE : '{';
RBRACE : '}';
COMMA : ',';
SEMI : ';';
COLON : ':';
DOT : '.';
QMARK : '?';

IntegerLiteral
    : DecimalIntegerLiteral
    | HexIntegerLiteral
    | BinaryIntegerLiteral
    | OctalIntegerLiteral
    ;

FloatLiteral
    : Digit (DigitOrUnderscore)* DOT Digit (DigitOrUnderscore)* ExponentPart?
    | Digit (DigitOrUnderscore)* ExponentPart
    ;

CharLiteral
    : SQUOTE (EscapeSequence | ~['\\\r\n]) SQUOTE
    ;

StringLiteral
    : SQUOTE (DoubleSQuote | EscapeSequence | ~['\\\r\n])* SQUOTE
    | DQUOTE (EscapeSequence | ~["\\\r\n])* DQUOTE
    ;

InterpolatedStringLiteral
    : '$' SQUOTE (DoubleSQuote | EscapeSequence | ~['\\\r\n])* SQUOTE
    | '$' DQUOTE (EscapeSequence | ~["\\\r\n])* DQUOTE
    ;

RawStringLiteral
    : TripleSQuote .*? TripleSQuote
    ;

UNDERSCORE
    : '_'
    ;

EscapedIdentifier
    : '&' IdentifierStart IdentifierPart*
    ;

Identifier
    : IdentifierStart IdentifierPart*
    ;

fragment DecimalIntegerLiteral
    : Digit (DigitOrUnderscore)*
    ;

fragment HexIntegerLiteral
    : '$' HexDigit (HexDigitOrUnderscore)*
    ;

fragment BinaryIntegerLiteral
    : '%' BinaryDigit (BinaryDigitOrUnderscore)*
    ;

fragment OctalIntegerLiteral
    : '&' OctalDigit (OctalDigitOrUnderscore)*
    ;

fragment TripleSQuote
    : SQUOTE SQUOTE SQUOTE
    ;

fragment DoubleSQuote
    : SQUOTE SQUOTE
    ;

fragment SQUOTE
    : '\''
    ;

fragment DQUOTE
    : '"'
    ;

fragment IdentifierStart
    : [_A-Za-z]
    ;

fragment IdentifierPart
    : IdentifierStart
    | [0-9]
    ;

fragment Digit
    : [0-9]
    ;

fragment DigitOrUnderscore
    : [0-9_]
    ;

fragment HexDigit
    : [0-9a-fA-F]
    ;

fragment HexDigitOrUnderscore
    : [0-9a-fA-F_]
    ;

fragment BinaryDigit
    : [01]
    ;

fragment BinaryDigitOrUnderscore
    : [01_]
    ;

fragment OctalDigit
    : [0-7]
    ;

fragment OctalDigitOrUnderscore
    : [0-7_]
    ;

fragment ExponentPart
    : [eE] [+\-]? Digit (DigitOrUnderscore)*
    ;

fragment EscapeSequence
    : '\\' [btnr'"\\]
    ;

LINE_COMMENT
    : '//' ~[\r\n]* -> skip
    ;

BLOCK_COMMENT
    : '(*' .*? '*)' -> skip
    ;

WS
    : [ \t\r\n\u000C]+ -> skip
    ;
