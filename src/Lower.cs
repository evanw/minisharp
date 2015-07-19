using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.PatternMatching;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;

namespace Shade
{
	internal class LoweringContext : IAstVisitor
	{
		private InputContext input;
		private bool wasSuccessful = true;
		private CSharpAstResolver resolver;
		private int nextEnumValue = 0;

		public static bool Lower(InputContext context)
		{
			var lowering = new LoweringContext(context);

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
			NotSupported(node);
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
				var statement = new ReturnStatement();
				statement.Expression = expression;
				block.Add(statement);
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
