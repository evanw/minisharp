using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.PatternMatching;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using System.Collections.Generic;

namespace MiniSharp
{
	internal class LoweringContext : IAstVisitor
	{
		private static KnownTypeCode[] knownTypeCodes = new KnownTypeCode[] {
			KnownTypeCode.Boolean,
			KnownTypeCode.Double,
			KnownTypeCode.Single,
			KnownTypeCode.String,
			KnownTypeCode.Void,

			// Integer types
			KnownTypeCode.Char,
			KnownTypeCode.SByte,
			KnownTypeCode.Byte,
			KnownTypeCode.Int16,
			KnownTypeCode.UInt16,
			KnownTypeCode.Int32,
			KnownTypeCode.UInt32,
			KnownTypeCode.Int64,
			KnownTypeCode.UInt64,
		};

		private Input input;
		private InputContext context;
		private bool wasSuccessful = true;
		private CSharpAstResolver resolver;
		private int nextEnumValue = 0;
		private Dictionary<IType, KnownTypeCode> knownTypes = new Dictionary<IType, KnownTypeCode>();

		public static bool Lower(InputContext context)
		{
			var lowering = new LoweringContext(context);

			// Cache information about important types
			foreach (var code in knownTypeCodes) {
				lowering.knownTypes[context.compilation.FindType(code)] = code;
			}

			// Lower the content in each file (generated code will
			// need to be inserted into one of these to be lowered)
			foreach (var input in context.inputs) {
				lowering.resolver = input.resolver;
				lowering.input = input;
				input.tree.AcceptVisitor(lowering);
			}

			return lowering.wasSuccessful;
		}

		private LoweringContext(InputContext context)
		{
			this.context = context;
		}

		private static AstNode UnparenthesizedParent(AstNode node)
		{
			do {
				node = node.Parent;
			} while (node is ParenthesizedExpression);
			return node;
		}

		private static bool WillConvertOperandsToIntegers(AstNode node)
		{
			var binary = node as BinaryOperatorExpression;
			if (binary != null) {
				switch (binary.Operator) {
					case BinaryOperatorType.BitwiseAnd:
					case BinaryOperatorType.BitwiseOr:
					case BinaryOperatorType.ExclusiveOr:
					case BinaryOperatorType.ShiftLeft:
					case BinaryOperatorType.ShiftRight: {
						return true;
					}
				}
				return false;
			}

			var unary = node as UnaryOperatorExpression;
			if (unary != null && unary.Operator == UnaryOperatorType.BitNot) {
				return true;
			}

			return false;
		}

		private bool IsIntegerTypeCode(KnownTypeCode code)
		{
			switch (code) {
				case KnownTypeCode.Char:
				case KnownTypeCode.SByte:
				case KnownTypeCode.Byte:
				case KnownTypeCode.Int16:
				case KnownTypeCode.UInt16:
				case KnownTypeCode.Int32:
				case KnownTypeCode.UInt32:
				case KnownTypeCode.Int64:
				case KnownTypeCode.UInt64: {
					return true;
				}
			}

			return false;
		}

		private bool IsFloatingPointTypeCode(KnownTypeCode code)
		{
			return code == KnownTypeCode.Single || code == KnownTypeCode.Double;
		}

		private KnownTypeCode TypeCode(IType type)
		{
			KnownTypeCode code;
			if (!knownTypes.TryGetValue(type, out code)) {
				var definition = type.GetDefinition();
				if (definition != null) {
					type = definition.EnumUnderlyingType;
					knownTypes.TryGetValue(type, out code);
				}
			}
			return code;
		}

		// This doesn't handle generic type parameters yet
		private Expression CreateDefaultValue(IType type)
		{
			var code = TypeCode(type);

			switch (code) {
				case KnownTypeCode.Boolean: return new PrimitiveExpression(false);
				case KnownTypeCode.Single: return new PrimitiveExpression(0.0f);
				case KnownTypeCode.Double: return new PrimitiveExpression(0.0);
			}

			return IsIntegerTypeCode(code) ? (Expression)new PrimitiveExpression(0) : new NullReferenceExpression();
		}

		private Expression FullReference(ISymbol symbol)
		{
			var parent = context.ParentSymbol(symbol);
			if (parent != null) {
				return new MemberReferenceExpression(FullReference(parent), symbol.Name);
			}
			return new IdentifierExpression(symbol.Name);
		}

		private void InsertStuffIntoConstructorBody(IType type, ConstructorDeclaration declaration)
		{
			var body = declaration.Body;
			var isEmpty = body.Statements.Count == 0;
			var insertBefore = body.LBraceToken.NextSibling ?? body.FirstChild;
			var shouldInsertNewlines = isEmpty || insertBefore is NewLineNode;

			// Lower the ": base()" or ": this()" expression into the constructor body
			var initializer = declaration.Initializer;
			var isForward = initializer.ConstructorInitializerType == ConstructorInitializerType.This;
			if (!initializer.IsNull) {
				var arguments = new List<Expression>(initializer.Arguments);
				foreach (var argument in arguments) {
					argument.Remove();
				}

				// Add an invocation inside the constructor body
				var target = isForward ? (Expression)new ThisReferenceExpression() : new BaseReferenceExpression();
				if (shouldInsertNewlines) {
					body.InsertChildBefore(insertBefore, new NewLineNode(), Roles.NewLine);
				}
				body.InsertChildBefore(insertBefore, new InvocationExpression(target, arguments), BlockStatement.StatementRole);
			}

			// For constructor forwarding, the base class initializer and all field initializers are in the other constructor
			if (!isForward) {
				foreach (var field in type.GetFields(null, GetMemberOptions.IgnoreInheritedMembers)) {
					VariableInitializer variable;
					Expression value;

					// Ignore non-instance fields
					if (field.IsStatic || !context.fields.TryGetValue(field, out variable)) {
						continue;
					}

					// Use the initializer if present
					if (variable.Initializer.IsNull) {
						value = CreateDefaultValue(field.Type);
					} else {
						value = variable.Initializer.Clone();
					}

					// Add an assignment inside the constructor body
					if (shouldInsertNewlines) {
						body.InsertChildBefore(insertBefore, new NewLineNode(), Roles.NewLine);
					}
					body.InsertChildBefore(insertBefore, new ExpressionStatement(new AssignmentExpression(
						new MemberReferenceExpression(new ThisReferenceExpression(),
							field.Name), value)), BlockStatement.StatementRole);
				}
			}

			// Make sure empty bodies have a newline after the last statement
			if (isEmpty) {
				body.InsertChildBefore(insertBefore, new NewLineNode(), Roles.NewLine);
			}
		}

		private void VisitChildren(AstNode node)
		{
			context.originalInputs[node] = input;

			for (AstNode child = node.FirstChild, next = null; child != null; child = next) {
				next = child.NextSibling;
				child.AcceptVisitor(this);
			}
		}

		private void NotSupported(AstNode node)
		{
			context.ReportError(node.Region, node.GetType().Name + " is unsupported");
			wasSuccessful = false;
		}

		public void VisitAnonymousMethodExpression(AnonymousMethodExpression node)
		{
			VisitChildren(node);
		}

		public void VisitUndocumentedExpression(UndocumentedExpression node)
		{
			NotSupported(node);
		}

		public void VisitArrayCreateExpression(ArrayCreateExpression node)
		{
			NotSupported(node);
		}

		public void VisitArrayInitializerExpression(ArrayInitializerExpression node)
		{
			NotSupported(node);
		}

		public void VisitAsExpression(AsExpression node)
		{
			NotSupported(node);
		}

		public void VisitAssignmentExpression(AssignmentExpression node)
		{
			VisitChildren(node);
		}

		public void VisitBaseReferenceExpression(BaseReferenceExpression node)
		{
			VisitChildren(node);
		}

		public void VisitBinaryOperatorExpression(BinaryOperatorExpression node)
		{
			VisitChildren(node);

			// Force certain operations on integers to stay integers afterwards.
			// This allows JavaScript JITs to omit overflow deoptimizations.
			if (!WillConvertOperandsToIntegers(node) && !WillConvertOperandsToIntegers(UnparenthesizedParent(node))) {
				var result = resolver.Resolve(node) as OperatorResolveResult;
				if (result != null && IsIntegerTypeCode(TypeCode(result.Type))) {
					var temp = new NullReferenceExpression();
					node.ReplaceWith(temp);
					temp.ReplaceWith(new BinaryOperatorExpression(node, BinaryOperatorType.BitwiseOr, new PrimitiveExpression(0)));
				}
			}
		}

		public void VisitCastExpression(CastExpression node)
		{
			VisitChildren(node);

			// Implement primitive casts
			var result = resolver.Resolve(node) as ConversionResolveResult;
			if (result != null) {
				var expression = node.Expression;
				var fromType = resolver.Resolve(expression).Type;
				var isDynamic = fromType == SpecialType.Dynamic;
				var toCode = TypeCode(result.Type);
				expression.Remove();

				// Integer cast
				if (IsIntegerTypeCode(toCode) && (!IsIntegerTypeCode(TypeCode(fromType)) || isDynamic)) {
					node.ReplaceWith(new BinaryOperatorExpression(expression,
						BinaryOperatorType.BitwiseOr, new PrimitiveExpression(0)));
				}

				// Boolean cast
				else if (isDynamic && toCode == KnownTypeCode.Boolean) {
					node.ReplaceWith(new UnaryOperatorExpression(
						UnaryOperatorType.Not, new UnaryOperatorExpression(
							UnaryOperatorType.Not, expression)));
				}

				// Floating-point cast
				else if (isDynamic && IsFloatingPointTypeCode(toCode)) {
					node.ReplaceWith(new UnaryOperatorExpression(
						UnaryOperatorType.Plus, expression));
				}

				// No cast needed
				else {
					node.ReplaceWith(expression);
				}
			}
		}

		public void VisitCheckedExpression(CheckedExpression node)
		{
			NotSupported(node);
		}

		public void VisitConditionalExpression(ConditionalExpression node)
		{
			VisitChildren(node);
		}

		public void VisitDefaultValueExpression(DefaultValueExpression node)
		{
			VisitChildren(node);

			// Generate the default value now
			var result = resolver.Resolve(node.Type) as TypeResolveResult;
			if (result != null) {
				node.ReplaceWith(CreateDefaultValue(result.Type));
			}
		}

		public void VisitDirectionExpression(DirectionExpression node)
		{
			NotSupported(node);
		}

		public void VisitIdentifierExpression(IdentifierExpression node)
		{
			VisitChildren(node);

			// Make member references explicit
			var result = resolver.Resolve(node) as MemberResolveResult;
			if (result != null && result.Member.SymbolKind == SymbolKind.Field) {
				if (((IField)result.Member).IsStatic) {
					node.ReplaceWith(FullReference(result.Member));
				} else {
					node.ReplaceWith(new MemberReferenceExpression(new ThisReferenceExpression(), node.Identifier));
				}
			}
		}

		public void VisitIndexerExpression(IndexerExpression node)
		{
			VisitChildren(node);
		}

		public void VisitInvocationExpression(InvocationExpression node)
		{
			VisitChildren(node);

			var result = resolver.Resolve(node) as CSharpInvocationResolveResult;
			if (result != null) {
				var target = node.Target;

				// Expand extension methods
				var reference = target as MemberReferenceExpression;
				if (result.IsExtensionMethodInvocation && reference != null) {
					var self = reference.Target;
					self.Remove();
					target.ReplaceWith(FullReference(result.ReducedMethod.ReducedFrom));
					node.Arguments.InsertAfter(null, self);
				}

				// Make member references explicit
				else if (result.Member.SymbolKind == SymbolKind.Method) {
					if (((IMethod)result.Member).IsStatic) {
						target.ReplaceWith(FullReference(result.Member));
					} else {
						target.ReplaceWith(new MemberReferenceExpression(new ThisReferenceExpression(), result.Member.Name));
					}
				}
			}
		}

		public void VisitIsExpression(IsExpression node)
		{
			VisitChildren(node);
		}

		public void VisitLambdaExpression(LambdaExpression node)
		{
			// Convert "() => 0" to "() => { return 0; }"
			if (node.Body.NodeType == NodeType.Expression) {
				var expression = (Expression)node.Body;
				var block = new BlockStatement();
				expression.ReplaceWith(block);
				block.Add(new ReturnStatement(expression));
			}

			VisitChildren(node);
		}

		public void VisitMemberReferenceExpression(MemberReferenceExpression node)
		{
			VisitChildren(node);
		}

		public void VisitNamedArgumentExpression(NamedArgumentExpression node)
		{
			node.Expression.AcceptVisitor(this);
			node.ReplaceWith(node.Expression);
		}

		public void VisitNamedExpression(NamedExpression node)
		{
			NotSupported(node);
		}

		public void VisitNullReferenceExpression(NullReferenceExpression node)
		{
			VisitChildren(node);
		}

		public void VisitObjectCreateExpression(ObjectCreateExpression node)
		{
			VisitChildren(node);
		}

		public void VisitAnonymousTypeCreateExpression(AnonymousTypeCreateExpression node)
		{
			NotSupported(node);
		}

		public void VisitParenthesizedExpression(ParenthesizedExpression node)
		{
			VisitChildren(node);
		}

		public void VisitPointerReferenceExpression(PointerReferenceExpression node)
		{
			NotSupported(node);
		}

		public void VisitPrimitiveExpression(PrimitiveExpression node)
		{
			VisitChildren(node);
		}

		public void VisitSizeOfExpression(SizeOfExpression node)
		{
			NotSupported(node);
		}

		public void VisitStackAllocExpression(StackAllocExpression node)
		{
			NotSupported(node);
		}

		public void VisitThisReferenceExpression(ThisReferenceExpression node)
		{
			VisitChildren(node);
		}

		public void VisitTypeOfExpression(TypeOfExpression node)
		{
			NotSupported(node);
		}

		public void VisitTypeReferenceExpression(TypeReferenceExpression node)
		{
			NotSupported(node);
		}

		public void VisitUnaryOperatorExpression(UnaryOperatorExpression node)
		{
			VisitChildren(node);
		}

		public void VisitUncheckedExpression(UncheckedExpression node)
		{
			NotSupported(node);
		}

		public void VisitQueryExpression(QueryExpression node)
		{
			NotSupported(node);
		}

		public void VisitQueryContinuationClause(QueryContinuationClause node)
		{
			NotSupported(node);
		}

		public void VisitQueryFromClause(QueryFromClause node)
		{
			NotSupported(node);
		}

		public void VisitQueryLetClause(QueryLetClause node)
		{
			NotSupported(node);
		}

		public void VisitQueryWhereClause(QueryWhereClause node)
		{
			NotSupported(node);
		}

		public void VisitQueryJoinClause(QueryJoinClause node)
		{
			NotSupported(node);
		}

		public void VisitQueryOrderClause(QueryOrderClause node)
		{
			NotSupported(node);
		}

		public void VisitQueryOrdering(QueryOrdering node)
		{
			NotSupported(node);
		}

		public void VisitQuerySelectClause(QuerySelectClause node)
		{
			NotSupported(node);
		}

		public void VisitQueryGroupClause(QueryGroupClause node)
		{
			NotSupported(node);
		}

		public void VisitAttribute(ICSharpCode.NRefactory.CSharp.Attribute node)
		{
		}

		public void VisitAttributeSection(AttributeSection node)
		{
		}

		public void VisitDelegateDeclaration(DelegateDeclaration node)
		{
			VisitChildren(node);
		}

		public void VisitNamespaceDeclaration(NamespaceDeclaration node)
		{
			VisitChildren(node);
		}

		public void VisitTypeDeclaration(TypeDeclaration node)
		{
			nextEnumValue = 0;
			VisitChildren(node);

			// Automatically generate constructor bodies
			var result = resolver.Resolve(node) as TypeResolveResult;
			if (result != null) {
				foreach (var method in result.Type.GetConstructors()) {
					if (!method.IsSynthetic) {
						continue;
					}

					// Add the declaration to the tree so it will be emitted
					var declaration = new ConstructorDeclaration();
					var body = new BlockStatement();
					declaration.Name = result.Type.Name;
					declaration.Body = body;
					InsertStuffIntoConstructorBody(result.Type, declaration);
					node.AddChild(declaration, Roles.TypeMemberRole);
					context.constructors[method] = declaration;
				}
			}
		}

		public void VisitUsingAliasDeclaration(UsingAliasDeclaration node)
		{
			NotSupported(node);
		}

		public void VisitUsingDeclaration(UsingDeclaration node)
		{
			VisitChildren(node);
		}

		public void VisitExternAliasDeclaration(ExternAliasDeclaration node)
		{
			NotSupported(node);
		}

		public void VisitBlockStatement(BlockStatement node)
		{
			VisitChildren(node);
		}

		public void VisitBreakStatement(BreakStatement node)
		{
			VisitChildren(node);
		}

		public void VisitCheckedStatement(CheckedStatement node)
		{
			NotSupported(node);
		}

		public void VisitContinueStatement(ContinueStatement node)
		{
			VisitChildren(node);
		}

		public void VisitDoWhileStatement(DoWhileStatement node)
		{
			VisitChildren(node);
		}

		public void VisitEmptyStatement(EmptyStatement node)
		{
			VisitChildren(node);
		}

		public void VisitExpressionStatement(ExpressionStatement node)
		{
			VisitChildren(node);
		}

		public void VisitFixedStatement(FixedStatement node)
		{
			NotSupported(node);
		}

		public void VisitForeachStatement(ForeachStatement node)
		{
			VisitChildren(node);
		}

		public void VisitForStatement(ForStatement node)
		{
			VisitChildren(node);
		}

		public void VisitGotoCaseStatement(GotoCaseStatement node)
		{
			NotSupported(node);
		}

		public void VisitGotoDefaultStatement(GotoDefaultStatement node)
		{
			NotSupported(node);
		}

		public void VisitGotoStatement(GotoStatement node)
		{
			NotSupported(node);
		}

		public void VisitIfElseStatement(IfElseStatement node)
		{
			VisitChildren(node);
		}

		public void VisitLabelStatement(LabelStatement node)
		{
			NotSupported(node);
		}

		public void VisitLockStatement(LockStatement node)
		{
			NotSupported(node);
		}

		public void VisitReturnStatement(ReturnStatement node)
		{
			VisitChildren(node);
		}

		public void VisitSwitchStatement(SwitchStatement node)
		{
			VisitChildren(node);
		}

		public void VisitSwitchSection(SwitchSection node)
		{
			VisitChildren(node);
		}

		public void VisitCaseLabel(CaseLabel node)
		{
			VisitChildren(node);
		}

		public void VisitThrowStatement(ThrowStatement node)
		{
			VisitChildren(node);
		}

		public void VisitTryCatchStatement(TryCatchStatement node)
		{
			VisitChildren(node);
		}

		public void VisitCatchClause(CatchClause node)
		{
			VisitChildren(node);
		}

		public void VisitUncheckedStatement(UncheckedStatement node)
		{
			NotSupported(node);
		}

		public void VisitUnsafeStatement(UnsafeStatement node)
		{
			NotSupported(node);
		}

		public void VisitUsingStatement(UsingStatement node)
		{
			NotSupported(node);
		}

		public void VisitVariableDeclarationStatement(VariableDeclarationStatement node)
		{
			VisitChildren(node);
		}

		public void VisitWhileStatement(WhileStatement node)
		{
			VisitChildren(node);
		}

		public void VisitYieldBreakStatement(YieldBreakStatement node)
		{
			NotSupported(node);
		}

		public void VisitYieldReturnStatement(YieldReturnStatement node)
		{
			NotSupported(node);
		}

		public void VisitAccessor(Accessor node)
		{
			NotSupported(node);
		}

		public void VisitConstructorDeclaration(ConstructorDeclaration node)
		{
			VisitChildren(node);

			var result = resolver.Resolve(node) as MemberResolveResult;
			if (result != null) {
				InsertStuffIntoConstructorBody(result.Member.DeclaringType, node);
			}
		}

		public void VisitConstructorInitializer(ConstructorInitializer node)
		{
			VisitChildren(node);
		}

		public void VisitDestructorDeclaration(DestructorDeclaration node)
		{
			NotSupported(node);
		}

		public void VisitEnumMemberDeclaration(EnumMemberDeclaration node)
		{
			// Ensure all enum members have a value
			if (node.Initializer.IsNull) {
				node.Initializer = new PrimitiveExpression(nextEnumValue++);
			} else {
				var result = resolver.Resolve(node.Initializer) as ConstantResolveResult;
				if (result != null && result.ConstantValue is int) {
					node.Initializer = new PrimitiveExpression((int)result.ConstantValue);
					nextEnumValue = (int)result.ConstantValue + 1;
				} else {
					NotSupported(node.Initializer);
				}
			}

			VisitChildren(node);
		}

		public void VisitEventDeclaration(EventDeclaration node)
		{
			NotSupported(node);
		}

		public void VisitCustomEventDeclaration(CustomEventDeclaration node)
		{
			NotSupported(node);
		}

		public void VisitFieldDeclaration(FieldDeclaration node)
		{
			VisitChildren(node);
		}

		public void VisitIndexerDeclaration(IndexerDeclaration node)
		{
			VisitChildren(node);
		}

		public void VisitMethodDeclaration(MethodDeclaration node)
		{
			VisitChildren(node);
		}

		public void VisitOperatorDeclaration(OperatorDeclaration node)
		{
			VisitChildren(node);
		}

		public void VisitParameterDeclaration(ParameterDeclaration node)
		{
			VisitChildren(node);
		}

		public void VisitPropertyDeclaration(PropertyDeclaration node)
		{
			NotSupported(node);
		}

		public void VisitVariableInitializer(VariableInitializer node)
		{
			VisitChildren(node);

			// Generate the default value now
			if (node.Initializer.IsNull) {
				var result = resolver.Resolve(node) as MemberResolveResult;
				if (result != null) {
					node.Initializer = CreateDefaultValue(result.Member.ReturnType);
				}
			}
		}

		public void VisitFixedFieldDeclaration(FixedFieldDeclaration node)
		{
			NotSupported(node);
		}

		public void VisitFixedVariableInitializer(FixedVariableInitializer node)
		{
			NotSupported(node);
		}

		public void VisitSyntaxTree(SyntaxTree node)
		{
			VisitChildren(node);
		}

		public void VisitSimpleType(SimpleType node)
		{
			VisitChildren(node);
		}

		public void VisitMemberType(MemberType node)
		{
			VisitChildren(node);
		}

		public void VisitComposedType(ComposedType node)
		{
			VisitChildren(node);
		}

		public void VisitArraySpecifier(ArraySpecifier node)
		{
			VisitChildren(node);
		}

		public void VisitPrimitiveType(PrimitiveType node)
		{
			VisitChildren(node);
		}

		public void VisitComment(Comment node)
		{
		}

		public void VisitNewLine(NewLineNode node)
		{
		}

		public void VisitWhitespace(WhitespaceNode node)
		{
		}

		public void VisitText(TextNode node)
		{
		}

		public void VisitPreProcessorDirective(PreProcessorDirective node)
		{
			NotSupported(node);
		}

		public void VisitDocumentationReference(DocumentationReference node)
		{
			NotSupported(node);
		}

		public void VisitTypeParameterDeclaration(TypeParameterDeclaration node)
		{
			VisitChildren(node);
		}

		public void VisitConstraint(Constraint node)
		{
			NotSupported(node);
		}

		public void VisitCSharpTokenNode(CSharpTokenNode node)
		{
		}

		public void VisitIdentifier(Identifier node)
		{
		}

		public void VisitNullNode(AstNode node)
		{
		}

		public void VisitErrorNode(AstNode node)
		{
		}

		public void VisitPatternPlaceholder(AstNode node, Pattern pattern)
		{
		}
	}
}
