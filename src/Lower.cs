using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.PatternMatching;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using System.Collections.Generic;

namespace Shade
{
	internal class LoweringContext : IAstVisitor
	{
		private static KnownTypeCode[] knownTypeCodes = new KnownTypeCode[] {
			KnownTypeCode.Object,
			KnownTypeCode.Boolean,
			KnownTypeCode.Char,
			KnownTypeCode.SByte,
			KnownTypeCode.Byte,
			KnownTypeCode.Int16,
			KnownTypeCode.UInt16,
			KnownTypeCode.Int32,
			KnownTypeCode.UInt32,
			KnownTypeCode.Int64,
			KnownTypeCode.UInt64,
			KnownTypeCode.Single,
			KnownTypeCode.Double,
			KnownTypeCode.Decimal,
			KnownTypeCode.String,
			KnownTypeCode.Void,
		};

		private InputContext input;
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
				input.tree.AcceptVisitor(lowering);
			}

			return lowering.wasSuccessful;
		}

		private LoweringContext(InputContext input)
		{
			this.input = input;
		}

		// This doesn't handle generic type parameters yet
		public Expression CreateDefaultValue(IType type)
		{
			KnownTypeCode code;

			if (!knownTypes.TryGetValue(type, out code)) {
				var definition = type.GetDefinition();
				if (definition != null) {
					type = definition.EnumUnderlyingType;
					knownTypes.TryGetValue(type, out code);
				}
			}

			switch (code) {
				case KnownTypeCode.Boolean: {
					return new PrimitiveExpression(false);
				}

				case KnownTypeCode.Single: {
					return new PrimitiveExpression(0.0f);
				}

				case KnownTypeCode.Double: {
					return new PrimitiveExpression(0.0);
				}

				case KnownTypeCode.Char:
				case KnownTypeCode.SByte:
				case KnownTypeCode.Byte:
				case KnownTypeCode.Int16:
				case KnownTypeCode.UInt16:
				case KnownTypeCode.Int32:
				case KnownTypeCode.UInt32:
				case KnownTypeCode.Int64:
				case KnownTypeCode.UInt64: {
					return new PrimitiveExpression(0);
				}
			}

			return new NullReferenceExpression();
		}

		public void VisitChildren(AstNode node)
		{
			for (AstNode child = node.FirstChild, next = null; child != null; child = next) {
				next = child.NextSibling;
				child.AcceptVisitor(this);
			}
		}

		public void NotSupported(AstNode node)
		{
			input.ReportError(node.Region, node.GetType().Name + " is unsupported");
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
		}

		public void VisitCastExpression(CastExpression node)
		{
			VisitChildren(node);
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
			// Convert "x" to "this.x"
			var result = resolver.Resolve(node) as MemberResolveResult;
			if (result != null && result.Member.SymbolKind == SymbolKind.Field && !((IField)result.Member).IsStatic) {
				var member = new MemberReferenceExpression();
				member.Target = new ThisReferenceExpression();
				member.MemberName = node.Identifier;
				node.ReplaceWith(member);
			}
		}

		public void VisitIndexerExpression(IndexerExpression node)
		{
			VisitChildren(node);
		}

		public void VisitInvocationExpression(InvocationExpression node)
		{
			VisitChildren(node);
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
			NotSupported(node);
		}

		public void VisitAttributeSection(AttributeSection node)
		{
			NotSupported(node);
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

					var declaration = new ConstructorDeclaration();
					var block = new BlockStatement();
					block.AddChild(new NewLineNode(), Roles.NewLine);

					foreach (var field in result.Type.GetFields()) {
						VariableInitializer variable;
						Expression initializer;

						// Ignore non-instance fields
						if (field.IsStatic || !input.fields.TryGetValue(field, out variable)) {
							continue;
						}

						// Use the initializer if present
						if (variable.Initializer.IsNull) {
							initializer = CreateDefaultValue(field.Type);
						} else {
							initializer = variable.Initializer;
							initializer.Remove();
						}

						// Add an assignment inside the constructor body
						block.Add(new ExpressionStatement(new AssignmentExpression(
							new MemberReferenceExpression(new ThisReferenceExpression(),
								field.Name), initializer)));
						block.AddChild(new NewLineNode(), Roles.NewLine);
					}

					// Add the declaration to the tree so it will be emitted
					declaration.Name = result.Type.Name;
					declaration.Body = block;
					node.AddChild(declaration, Roles.TypeMemberRole);
					input.constructors[method] = declaration;
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
			VisitChildren(node);
		}

		public void VisitConstructorDeclaration(ConstructorDeclaration node)
		{
			VisitChildren(node);
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
			VisitChildren(node);
		}

		public void VisitVariableInitializer(VariableInitializer node)
		{
			VisitChildren(node);
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
