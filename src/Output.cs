using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.TypeSystem;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace MiniSharp
{
	public enum SourceMap
	{
		None,
		Inline,
		External,
	}

	public class OutputContext
	{
		private InputContext input;
		private Dictionary<ITypeDefinition, IMethod> primaryConstructors = new Dictionary<ITypeDefinition, IMethod>();
		private List<ITypeDefinition> types = new List<ITypeDefinition>();
		private List<INamespace> namespaces = new List<INamespace>();
		private StringBuilder builder = new StringBuilder();
		private bool shouldEmitNewline = false;
		private bool shouldEmitSemicolon = false;
		private bool hasGeneratedCode = false;
		private int line = 0;
		private int column = 0;
		private string indent = "";
		private string space = " ";
		private string newline = "\n";
		private int indentLevel = 0;
		private List<string> indentPool = new List<string> { "" };
		private ITypeDefinition currentType;
		private StatementVisitor statementVisitor;
		private ExpressionVisitor expressionVisitor;
		private SourceMapGenerator sourceMap = new SourceMapGenerator();

		public OutputContext(InputContext input)
		{
			this.input = input;
			IndentAmount = "\t";
		}

		public bool ShouldMinify { get; set; }
		public bool ShouldMangle { get; set; }
		public string IndentAmount { get; set; }
		public SourceMap SourceMap { get; set; }

		public string Code
		{
			get {
				GenerateCodeIfNeeded();
				return builder.ToString();
			}
		}

		public string SourceMapCode
		{
			get {
				if (SourceMap != SourceMap.External) {
					return null;
				}
				GenerateCodeIfNeeded();
				return sourceMap.ToString();
			}
		}

		private void GenerateCodeIfNeeded()
		{
			if (hasGeneratedCode) {
				return;
			}

			if (ShouldMangle) {
				var mangling = Stopwatch.StartNew();
				ManglingContext.Mangle(input);
				input.timingInMilliseconds["Mangling"] = mangling.ElapsedMilliseconds;
			}

			ScanTypes(input.root);
			SortTypes();

			if (ShouldMinify) {
				space = newline = "";
			}

			statementVisitor = new StatementVisitor(this);
			expressionVisitor = new ExpressionVisitor(this);

			var emitting = Stopwatch.StartNew();
			Emit("(function()" + space + "{" + newline);
			IncreaseIndent();
			EmitNamespaces();
			EmitTypes();
			EmitVariables();
			DecreaseIndent();
			Emit("})();\n");
			if (SourceMap == SourceMap.Inline) {
				Emit("//# sourceMappingURL=" + sourceMap.ToURL() + "\n");
			}
			input.timingInMilliseconds["Emitting"] = emitting.ElapsedMilliseconds;

			hasGeneratedCode = true;
		}

		private void ScanTypes(INamespace parent)
		{
			namespaces.Add(parent);

			foreach (var child in parent.ChildNamespaces) {
				ScanTypes(child);
			}

			foreach (var child in parent.Types) {
				ScanTypes(child);
			}
		}

		private void ScanTypes(ITypeDefinition parent)
		{
			types.Add(parent);

			foreach (var method in parent.Methods) {
				if (method.SymbolKind == SymbolKind.Constructor) {
					primaryConstructors[parent] = method;
					break;
				}
			}

			foreach (var child in parent.NestedTypes) {
				ScanTypes(child);
			}
		}

		private static bool HasBaseClass(IType derivedClass, IType definition)
		{
			var baseClass = derivedClass.BaseClass();
			return baseClass != null && (baseClass.GetDefinition() == definition || baseClass.Kind == TypeKind.Class && HasBaseClass(baseClass, definition));
		}

		private static bool IsContainedBy(IType nestedClass, IType definition)
		{
			var type = nestedClass.DeclaringType;
			return type != null && (type.GetDefinition() == definition || IsContainedBy(type, definition));
		}

		private static bool TypeComesBefore(IType before, IType after)
		{
			// Uses GetDefinition() so this also works on generic types
			if (after.Kind == TypeKind.Class) {
				var definition = before.GetDefinition();
				if (definition != null) {
					return before.Kind == TypeKind.Class && HasBaseClass(after, definition) || IsContainedBy(after, definition);
				}
			}
			return false;
		}

		private void SortTypes()
		{
			// Sort by inheritance and containment
			for (var i = 0; i < types.Count; i++) {
				var j = i;

				// Select an object that comes before all other types
				while (j < types.Count) {
					var symbol = types[j];
					var k = i;

					// Check to see if this comes before all other types
					while (k < types.Count) {
						if (j != k && TypeComesBefore(types[k], symbol)) {
							break;
						}
						k++;
					}
					if (k == types.Count) {
						break;
					}
					j++;
				}

				// Swap the object into the correct order
				if (j < types.Count) {
					var temp = types[i];
					types[i] = types[j];
					types[j] = temp;
				}
			}
		}

		private void AddMapping(AstNode node)
		{
			if (SourceMap != SourceMap.None) {
				var region = node.Region;
				if (region.BeginLine > 0 && region.BeginColumn > 0) {
					var original = input.OriginalInput(node);
					if (original != null) {
						sourceMap.AddMapping(original, region.BeginLine - 1, region.BeginColumn - 1, line, column);
					}
				}
			}
		}

		private void EmitNamespaces()
		{
			foreach (var symbol in namespaces) {
				if (symbol.ParentNamespace != null) {
					Emit(indent + (input.IsTopLevel(symbol) ? "var " + symbol.Name : symbol.FullName) + space + '=' + space + "{};" + newline);
					shouldEmitNewline = true;
				}
			}

			foreach (var type in types) {
				if (type.Kind == TypeKind.Enum || type.Kind == TypeKind.Class && type.IsStatic) {
					Emit(indent + (input.IsTopLevel(type) ? "var " + type.Name : type.FullName) + space + '=' + space + "{};" + newline);
					shouldEmitNewline = true;
				}
			}
		}

		private void EmitTypes()
		{
			foreach (var type in types) {
				currentType = type;
				switch (type.Kind) {
					case TypeKind.Enum: {
						var isFirst = true;
						foreach (var field in type.Fields) {
							EnumMemberDeclaration initializer;
							if (isFirst) {
								isFirst = false;
								EmitNewlineBeforeDefinition();
							}
							Emit(indent + field.FullName + space + '=' + space);
							if (input.enums.TryGetValue(field, out initializer) && initializer.Initializer != null) {
								initializer.Initializer.AcceptVisitor(expressionVisitor, Precedence.Highest);
							}
							Emit(";" + newline);
							shouldEmitNewline = true;
						}
						break;
					}

					case TypeKind.Class: {
						IMethod primaryConstructor;
						if (primaryConstructors.TryGetValue(type, out primaryConstructor)) {
							EmitMethod(type, primaryConstructor, true);
							foreach (var method in type.Methods) {
								if (method != primaryConstructor) {
									EmitMethod(type, method, false);
								}
							}
						} else {
							foreach (var method in type.Methods) {
								EmitMethod(type, method, false);
							}
						}
						break;
					}
				}
			}
		}

		private void EmitVariables()
		{
			foreach (var type in types) {
				if (type.Kind == TypeKind.Class) {
					foreach (var field in type.Fields) {
						VariableInitializer initializer;
						if (field.IsStatic && input.fields.TryGetValue(field, out initializer) && !initializer.Initializer.IsNull) {
							EmitNewlineBeforeDefinition();
							EmitIndent();
							Emit(field.FullName + space + '=' + space);
							initializer.Initializer.AcceptVisitor(expressionVisitor, Precedence.Assignment);
							Emit(";" + newline);
						}
					}
				}
			}
		}

		private void EmitParameters(AstNodeCollection<ParameterDeclaration> parameters)
		{
			var isFirst = true;
			foreach (var parameter in parameters) {
				if (isFirst) {
					isFirst = false;
				} else {
					Emit("," + space);
				}
				Emit(parameter.Name);
			}
		}

		private void EmitMethod(ITypeDefinition type, IMethod method, bool isPrimaryConstructor)
		{
			var isFunctionExpression = true;
			BlockStatement body = null;
			ConstructorInitializer initializer = null;
			AstNodeCollection<ParameterDeclaration> parameters = null;
			MethodDeclaration methodParent;
			OperatorDeclaration operatorParent;
			ConstructorDeclaration constructorParent;

			if (input.methods.TryGetValue(method, out methodParent)) {
				body = methodParent.Body;
				parameters = methodParent.Parameters;
			} else if (input.constructors.TryGetValue(method, out constructorParent)) {
				body = constructorParent.Body;
				parameters = constructorParent.Parameters;
				initializer = constructorParent.Initializer;
			} else if (input.operators.TryGetValue(method, out operatorParent)) {
				body = operatorParent.Body;
				parameters = operatorParent.Parameters;
			}

			// Ignore abstract methods
			if (body == null) {
				return;
			}

			// Start the declaration
			EmitNewlineBeforeDefinition();
			if (isPrimaryConstructor) {
				if (input.IsTopLevel(type)) {
					Emit(indent + "function " + type.Name + "(");
					isFunctionExpression = false;
				} else {
					Emit(indent + type.FullName + space + '=' + space + "function(");
				}
			} else {
				Emit(indent + type.FullName + (method.IsStatic || method.IsConstructor ? "." : ".prototype.") + method.Name + space + '=' + space + "function(");
			}
			if (parameters != null) {
				EmitParameters(parameters);
			}
			Emit(")");

			// Emit the body
			EmitNewlineBefore(body);
			EmitIndent();
			AddMapping(body);
			Emit("{");
			var old = builder.Length;
			IncreaseIndent();
			if (initializer != null && !initializer.IsNull) {
				Emit(newline);
				initializer.AcceptVisitor(statementVisitor);
			}
			foreach (var node in body.Children) {
				node.AcceptVisitor(statementVisitor);
			}
			DecreaseIndent();
			if (builder.Length != old) {
				EmitIndent();
			}
			Emit("}");
			shouldEmitSemicolon = false;
			Emit(isFunctionExpression ? ";" + newline : newline);
			shouldEmitNewline = true;

			// Emit some code to extend the prototype
			if (method.SymbolKind == SymbolKind.Constructor) {
				var baseClass = type.BaseClass();
				if (baseClass != null) {
					EmitNewlineBeforeDefinition();
					EmitIndent();
					if (isPrimaryConstructor) {
						Emit(type.FullName + ".prototype" + space + '=' + space + "Object.create(" + baseClass.FullName + ".prototype);" + newline);
					} else {
						Emit(method.FullName + ".prototype" + space + '=' + space + type.FullName + ".prototype;" + newline);
					}
					shouldEmitNewline = true;
				}
			}
		}

		private void Emit(string text)
		{
			foreach (var c in text) {
				if (c == '\n') {
					line++;
					column = 0;
				} else {
					column++;
				}
			}
			builder.Append(text);
		}

		private void EmitSemicolonIfNeeded()
		{
			if (shouldEmitSemicolon) {
				Emit(";");
				shouldEmitSemicolon = false;
			}
		}

		private void EmitSemicolonAfterStatement()
		{
			if (ShouldMinify) {
				shouldEmitSemicolon = true;
			} else {
				Emit(";");
			}
		}

		private void EmitNewlineBeforeDefinition()
		{
			if (shouldEmitNewline) {
				Emit(newline);
				shouldEmitNewline = false;
			}
		}

		private void EmitIndent()
		{
			Emit(column == 0 ? indent : space);
		}

		private void IncreaseIndent()
		{
			if (!ShouldMinify) {
				indentLevel++;
				if (indentLevel >= indentPool.Count) {
					indentPool.Add(indent + IndentAmount);
				}
				indent = indentPool[indentLevel];
			}
		}

		private void DecreaseIndent()
		{
			if (!ShouldMinify) {
				indentLevel--;
				indent = indentPool[indentLevel];
			}
		}

		private void EmitComment(Comment comment)
		{
			if (!ShouldMinify) {
				EmitIndent();
				if (comment.CommentType == CommentType.SingleLine || comment.CommentType == CommentType.Documentation) {
					Emit("//" + comment.Content);
				} else {
					Emit("/*" + comment.Content + "*/");
				}
			}
		}

		private bool EmitNewlineBefore(AstNode node)
		{
			if (!ShouldMinify && node.PrevSibling is NewLineNode) {
				Emit(newline);
				return true;
			}
			return false;
		}

		private bool NeedsSpaceAfterIdentifier(AstNode node)
		{
			if (!ShouldMinify || node is BlockStatement) {
				return false;
			}

			var unary = node as UnaryOperatorExpression;
			if (unary != null) {
				return IsPostfix(unary.Operator);
			}

			return true;
		}

		private enum Previous
		{
			Identifier,
			Other,
		}

		private void EmitWhitespaceBeforeChild(AstNode parent, AstNode child, Previous previous)
		{
			if (!EmitNewlineBefore(child) && previous == Previous.Identifier && NeedsSpaceAfterIdentifier(child)) {
				Emit(" ");
			} else {
				EmitIndent();
			}
		}

		private static bool IsPostfix(UnaryOperatorType type)
		{
			return type == UnaryOperatorType.PostDecrement || type == UnaryOperatorType.PostIncrement;
		}

		private class StatementVisitor : DepthFirstAstVisitor
		{
			public OutputContext context;

			public StatementVisitor(OutputContext context)
			{
				this.context = context;
			}

			public void VisitBlockOrStatement(AstNode parent, AstNode child, Previous previous)
			{
				if (previous == Previous.Identifier && context.NeedsSpaceAfterIdentifier(child)) {
					context.Emit(" ");
				}
				var needsIndent = context.EmitNewlineBefore(child) && !(child is BlockStatement);
				if (needsIndent) {
					context.IncreaseIndent();
				}
				context.shouldEmitSemicolon = false;
				child.AcceptVisitor(this);
				context.EmitSemicolonIfNeeded();
				if (needsIndent) {
					context.DecreaseIndent();
				}
			}

			public override void VisitComment(Comment node)
			{
				context.EmitComment(node);
			}

			public override void VisitNewLine(NewLineNode node)
			{
				context.Emit(context.newline);
			}

			public override void VisitBlockStatement(BlockStatement node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit("{");
				var old = context.builder.Length;
				context.IncreaseIndent();
				VisitChildren(node);
				context.DecreaseIndent();
				if (context.builder.Length != old) {
					context.EmitIndent();
				}
				context.Emit("}");
				context.shouldEmitSemicolon = false;
			}

			public override void VisitEmptyStatement(EmptyStatement node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.EmitSemicolonAfterStatement();
			}

			public override void VisitVariableDeclarationStatement(VariableDeclarationStatement node)
			{
				var isFirst = true;
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit("var");
				context.IncreaseIndent();

				foreach (var variable in node.Variables) {
					if (isFirst) {
						isFirst = false;
						context.EmitWhitespaceBeforeChild(node, variable, Previous.Identifier);
					} else {
						context.Emit(",");
						context.EmitWhitespaceBeforeChild(node, variable, Previous.Other);
					}
					context.AddMapping(variable);
					context.Emit(variable.Name);
					if (!variable.Initializer.IsNull) {
						context.EmitWhitespaceBeforeChild(variable, variable.AssignToken, Previous.Other);
						context.Emit("=");
						context.EmitWhitespaceBeforeChild(variable, variable.Initializer, Previous.Other);
						variable.Initializer.AcceptVisitor(context.expressionVisitor, Precedence.Comma);
					}
				}

				context.DecreaseIndent();
				context.EmitSemicolonAfterStatement();
			}

			public override void VisitReturnStatement(ReturnStatement node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);

				if (!node.Expression.IsNull) {
					context.Emit("return");
					context.EmitWhitespaceBeforeChild(node, node.Expression, Previous.Identifier);
					node.Expression.AcceptVisitor(context.expressionVisitor, Precedence.Highest);
				} else {
					context.Emit("return");
				}
				context.EmitSemicolonAfterStatement();
			}

			public override void VisitThrowStatement(ThrowStatement node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit("throw");
				context.EmitWhitespaceBeforeChild(node, node.Expression, Previous.Identifier);
				node.Expression.AcceptVisitor(context.expressionVisitor, Precedence.Highest);
				context.EmitSemicolonAfterStatement();
			}

			public override void VisitExpressionStatement(ExpressionStatement node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.IncreaseIndent();
				context.AddMapping(node);
				node.Expression.AcceptVisitor(context.expressionVisitor, Precedence.Highest);
				context.DecreaseIndent();
				context.EmitSemicolonAfterStatement();
			}

			public override void VisitIfElseStatement(IfElseStatement node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit("if" + context.space + "(");
				node.Condition.AcceptVisitor(context.expressionVisitor, Precedence.Highest);
				context.Emit(")");
				VisitBlockOrStatement(node, node.TrueStatement, Previous.Other);

				if (!node.FalseStatement.IsNull) {
					context.EmitWhitespaceBeforeChild(node, node.ElseToken, Previous.Other);
					context.Emit("else");
					VisitBlockOrStatement(node, node.FalseStatement, Previous.Identifier);
				}
			}

			public override void VisitWhileStatement(WhileStatement node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit("while" + context.space + "(");
				node.Condition.AcceptVisitor(context.expressionVisitor, Precedence.Highest);
				context.Emit(")");
				VisitBlockOrStatement(node, node.EmbeddedStatement, Previous.Other);
			}

			public override void VisitDoWhileStatement(DoWhileStatement node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit("do");
				VisitBlockOrStatement(node, node.EmbeddedStatement, Previous.Identifier);
				context.EmitWhitespaceBeforeChild(node, node.WhileToken, Previous.Other);
				context.Emit("while" + context.space + "(");
				node.Condition.AcceptVisitor(context.expressionVisitor, Precedence.Highest);
				context.Emit(");");
			}

			public override void VisitForeachStatement(ForeachStatement node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit("for" + context.space + "(var " + node.VariableName + " in ");
				node.InExpression.AcceptVisitor(context.expressionVisitor, Precedence.Highest);
				context.Emit(")");
				VisitBlockOrStatement(node, node.EmbeddedStatement, Previous.Other);
			}

			public override void VisitBreakStatement(BreakStatement node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit("break");
				context.EmitSemicolonAfterStatement();
			}

			public override void VisitContinueStatement(ContinueStatement node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit("continue");
				context.EmitSemicolonAfterStatement();
			}

			public override void VisitForStatement(ForStatement node)
			{
				var isFirst = true;
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit("for" + context.space + "(");
				context.IncreaseIndent();

				foreach (var child in node.Initializers) {
					var declaration = child as VariableDeclarationStatement;
					if (declaration != null) {
						context.Emit("var");
						foreach (var variable in declaration.Variables) {
							if (isFirst) {
								isFirst = false;
								context.EmitWhitespaceBeforeChild(declaration, variable, Previous.Identifier);
							} else {
								context.Emit(",");
								context.EmitWhitespaceBeforeChild(declaration, variable, Previous.Other);
							}
							context.Emit(variable.Name);
							if (!variable.Initializer.IsNull) {
								context.EmitWhitespaceBeforeChild(node, variable.AssignToken, Previous.Other);
								context.Emit("=");
								context.EmitWhitespaceBeforeChild(node, variable.Initializer, Previous.Other);
								variable.Initializer.AcceptVisitor(context.expressionVisitor, Precedence.Comma);
							}
						}
					}

					else {
						var expression = child as ExpressionStatement;
						if (expression != null) {
							if (isFirst) {
								isFirst = false;
							} else {
								context.Emit(",");
							}
							context.EmitWhitespaceBeforeChild(node, child, Previous.Other);
							expression.Expression.AcceptVisitor(context.expressionVisitor, Precedence.Comma);
						}
					}
				}

				context.Emit(";");
				if (!node.Condition.IsNull) {
					context.EmitWhitespaceBeforeChild(node, node.Condition, Previous.Other);
					node.Condition.AcceptVisitor(context.expressionVisitor, Precedence.Highest);
				}
				context.Emit(";");
				isFirst = true;

				foreach (var child in node.Iterators) {
					var expression = child as ExpressionStatement;
					if (expression != null) {
						if (isFirst) {
							isFirst = false;
						} else {
							context.Emit(",");
						}
						context.EmitWhitespaceBeforeChild(node, child, Previous.Other);
						expression.Expression.AcceptVisitor(context.expressionVisitor, Precedence.Comma);
					}
				}

				context.DecreaseIndent();
				context.Emit(")");
				VisitBlockOrStatement(node, node.EmbeddedStatement, Previous.Other);
			}

			public override void VisitSwitchStatement(SwitchStatement node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit("switch" + context.space + "(");
				node.Expression.AcceptVisitor(context.expressionVisitor, Precedence.Highest);
				context.Emit(")");
				context.EmitWhitespaceBeforeChild(node, node.LBraceToken, Previous.Other);
				context.Emit("{");
				context.IncreaseIndent();

				// Iterate over all children instead of just the switch sections to also get comments
				for (var child = node.LBraceToken.IsNull ? (AstNode)node.SwitchSections.FirstOrNullObject() : node.LBraceToken; child != null; child = child.NextSibling) {
					child.AcceptVisitor(this);
				}

				context.DecreaseIndent();
				context.EmitIndent();
				context.Emit("}");
				context.shouldEmitSemicolon = false;
			}

			public override void VisitCaseLabel(CaseLabel node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);
				if (node.Expression.IsNull) {
					context.Emit("default:");
				} else {
					context.Emit("case");
					context.EmitWhitespaceBeforeChild(node, node.Expression, Previous.Identifier);
					node.Expression.AcceptVisitor(context.expressionVisitor, Precedence.Highest);
					context.Emit(":");
				}
			}

			public override void VisitSwitchSection(SwitchSection node)
			{
				var last = node.CaseLabels.LastOrNullObject();
				var shouldIncreaseIndent = false;
				var shouldDecreaseIndent = false;

				for (var child = node.FirstChild; child != null; child = child.NextSibling) {
					if (!shouldDecreaseIndent && shouldIncreaseIndent && !(child is NewLineNode)) {
						if (child is BlockStatement) {
							shouldIncreaseIndent = false; // Don't do an indent increase for blocks, which already cause an indent
						} else {
							context.IncreaseIndent();
							shouldDecreaseIndent = true;
						}
					}

					child.AcceptVisitor(this);

					// Prepare for an indent increase after the last label
					if (!shouldIncreaseIndent && child == last) {
						shouldIncreaseIndent = true;
					}
				}

				if (shouldDecreaseIndent) {
					context.DecreaseIndent();
				}
			}

			public override void VisitTryCatchStatement(TryCatchStatement node)
			{
				context.EmitSemicolonIfNeeded();
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit("try");
				context.EmitNewlineBefore(node.TryBlock);

				var stop = !node.FinallyToken.IsNull ? (AstNode)node.FinallyToken : !node.FinallyBlock.IsNull ? node.FinallyBlock : null;
				for (AstNode child = node.TryBlock; child != stop; child = child.NextSibling) {
					child.AcceptVisitor(this);
				}

				if (!node.FinallyBlock.IsNull) {
					context.EmitIndent();
					context.Emit("finally");
					context.EmitNewlineBefore(node.FinallyBlock);
					node.FinallyBlock.AcceptVisitor(this);
				}
			}

			public override void VisitCatchClause(CatchClause node)
			{
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit("catch" + context.space + "(" + node.VariableName + ")");
				context.EmitNewlineBefore(node.Body);
				node.Body.AcceptVisitor(this);
			}

			public override void VisitConstructorInitializer(ConstructorInitializer node)
			{
				var baseClass = context.currentType.BaseClass();
				if (baseClass == null) {
					return;
				}
				context.EmitIndent();
				context.AddMapping(node);
				context.Emit(baseClass.FullName + ".call");
				context.IncreaseIndent();
				context.Emit("(this");
				foreach (var argument in node.Arguments) {
					context.Emit(",");
					context.EmitWhitespaceBeforeChild(node, argument, Previous.Other);
					argument.AcceptVisitor(context.expressionVisitor, Precedence.Comma);
				}
				context.Emit(")");
				context.DecreaseIndent();
				context.EmitSemicolonAfterStatement();
			}
		}

		// https://msdn.microsoft.com/en-us/library/aa691323.aspx
		private enum Precedence
		{
			Primary,
			Unary,
			Multiplicative,
			Additive,
			Shift,
			Relational,
			Equality,
			LogicalAnd,
			LogicalXor,
			LogicalOr,
			ConditionalAnd,
			ConditionalOr,
			Conditional,
			Assignment,
			Comma,
			Highest,
		}

		private static Precedence BinaryOperatorPrecedence(BinaryOperatorType type)
		{
			switch (type) {
				case BinaryOperatorType.Add: return Precedence.Additive;
				case BinaryOperatorType.BitwiseAnd: return Precedence.LogicalAnd;
				case BinaryOperatorType.BitwiseOr: return Precedence.LogicalOr;
				case BinaryOperatorType.ConditionalAnd: return Precedence.ConditionalAnd;
				case BinaryOperatorType.ConditionalOr: return Precedence.ConditionalOr;
				case BinaryOperatorType.Divide: return Precedence.Multiplicative;
				case BinaryOperatorType.Equality: return Precedence.Equality;
				case BinaryOperatorType.ExclusiveOr: return Precedence.LogicalXor;
				case BinaryOperatorType.GreaterThan: return Precedence.Relational;
				case BinaryOperatorType.GreaterThanOrEqual: return Precedence.Relational;
				case BinaryOperatorType.InEquality: return Precedence.Equality;
				case BinaryOperatorType.LessThan: return Precedence.Relational;
				case BinaryOperatorType.LessThanOrEqual: return Precedence.Relational;
				case BinaryOperatorType.Modulus: return Precedence.Multiplicative;
				case BinaryOperatorType.Multiply: return Precedence.Multiplicative;
				case BinaryOperatorType.ShiftLeft: return Precedence.Shift;
				case BinaryOperatorType.ShiftRight: return Precedence.Shift;
				case BinaryOperatorType.Subtract: return Precedence.Additive;
			}
			return Precedence.Highest;
		}

		private class ExpressionVisitor : DepthFirstAstVisitor<Precedence, object>
		{
			public OutputContext context;

			public ExpressionVisitor(OutputContext context)
			{
				this.context = context;
			}

			public void VisitCommaSeparatedExpressions(AstNode parent, AstNodeCollection<Expression> children)
			{
				var isFirst = true;
				foreach (var child in children) {
					if (isFirst) {
						isFirst = false;
						if (context.EmitNewlineBefore(child)) {
							context.EmitIndent();
						}
					} else {
						context.Emit(",");
						context.EmitWhitespaceBeforeChild(parent, child, Previous.Other);
					}
					child.AcceptVisitor(this, Precedence.Comma);
				}
			}

			public override object VisitComment(Comment node, Precedence precedence)
			{
				context.EmitComment(node);
				return null;
			}

			public override object VisitNewLine(NewLineNode node, Precedence precedence)
			{
				context.Emit("\n");
				return null;
			}

			public override object VisitSimpleType(SimpleType node, Precedence precedence)
			{
				context.AddMapping(node);
				context.Emit(node.Identifier);
				return null;
			}

			public override object VisitIdentifierExpression(IdentifierExpression node, Precedence precedence)
			{
				context.AddMapping(node);
				context.Emit(node.Identifier);
				return null;
			}

			public override object VisitMemberReferenceExpression(MemberReferenceExpression node, Precedence precedence)
			{
				context.AddMapping(node);
				if (node.Target is BaseReferenceExpression) {
					context.Emit("base");
				} else {
					node.Target.AcceptVisitor(this, Precedence.Primary);
				}
				context.Emit("." + node.MemberName);
				return null;
			}

			public override object VisitObjectCreateExpression(ObjectCreateExpression node, Precedence precedence)
			{
				context.AddMapping(node);
				context.Emit("new ");
				node.Type.AcceptVisitor(this, Precedence.Primary);
				context.Emit("(");
				VisitCommaSeparatedExpressions(node, node.Arguments);
				context.Emit(")");
				return null;
			}

			public override object VisitInvocationExpression(InvocationExpression node, Precedence precedence)
			{
				context.AddMapping(node);
				if (node.Target is BaseReferenceExpression) {
					context.Emit("base");
				} else if (node.Target is ThisReferenceExpression) {
					context.Emit("this");
				} else {
					node.Target.AcceptVisitor(this, Precedence.Primary);
				}
				context.Emit("(");
				VisitCommaSeparatedExpressions(node, node.Arguments);
				context.Emit(")");
				return null;
			}

			public override object VisitPrimitiveExpression(PrimitiveExpression node, Precedence precedence)
			{
				var value = node.Value;
				context.AddMapping(node);
				if (value is bool) {
					context.Emit(context.ShouldMangle
						? (bool)value ? "!0" : "!1"
						: (bool)value ? "true" : "false");
				} else if (value is string) {
					context.Emit(((string)value).Quote(QuoteStyle.SingleOrDouble));
				} else {
					context.Emit(value.ToString());
				}
				return null;
			}

			public override object VisitNullReferenceExpression(NullReferenceExpression node, Precedence precedence)
			{
				context.AddMapping(node);
				context.Emit("null");
				return null;
			}

			public override object VisitThisReferenceExpression(ThisReferenceExpression node, Precedence precedence)
			{
				context.AddMapping(node);
				context.Emit("this");
				return null;
			}

			public override object VisitConditionalExpression(ConditionalExpression node, Precedence precedence)
			{
				context.AddMapping(node);
				if (precedence < Precedence.Conditional) {
					context.Emit("(");
				}
				node.Condition.AcceptVisitor(this, Precedence.Conditional);
				context.Emit(context.space + "?" + context.space);
				node.TrueExpression.AcceptVisitor(this, Precedence.Conditional);
				context.Emit(context.space + ":" + context.space);
				node.FalseExpression.AcceptVisitor(this, Precedence.Conditional);
				if (precedence < Precedence.Conditional) {
					context.Emit(")");
				}
				return null;
			}

			public override object VisitUnaryOperatorExpression(UnaryOperatorExpression node, Precedence precedence)
			{
				var isPostfix = IsPostfix(node.Operator);
				context.AddMapping(node);
				if (precedence < Precedence.Unary) {
					context.Emit("(");
				}
				if (!isPostfix) {
					context.Emit(UnaryOperatorExpression.GetOperatorRole(node.Operator).Token);
				}
				node.Expression.AcceptVisitor(this, Precedence.Unary);
				if (isPostfix) {
					context.Emit(UnaryOperatorExpression.GetOperatorRole(node.Operator).Token);
				}
				if (precedence < Precedence.Unary) {
					context.Emit(")");
				}
				return null;
			}

			public override object VisitAssignmentExpression(AssignmentExpression node, Precedence precedence)
			{
				context.AddMapping(node);
				if (precedence < Precedence.Assignment) {
					context.Emit("(");
				}
				node.Left.AcceptVisitor(this, (Precedence)(Precedence.Assignment - 1));
				context.EmitWhitespaceBeforeChild(node, node.OperatorToken, Previous.Other);
				context.Emit(AssignmentExpression.GetOperatorRole(node.Operator).Token);
				context.EmitWhitespaceBeforeChild(node, node.Right, Previous.Other);
				node.Right.AcceptVisitor(this, Precedence.Assignment);
				if (precedence < Precedence.Assignment) {
					context.Emit(")");
				}
				return null;
			}

			public override object VisitBinaryOperatorExpression(BinaryOperatorExpression node, Precedence precedence)
			{
				var self = BinaryOperatorPrecedence(node.Operator);
				context.AddMapping(node);
				if (precedence < self) {
					context.Emit("(");
				}
				node.Left.AcceptVisitor(this, self);
				context.EmitWhitespaceBeforeChild(node, node.OperatorToken, Previous.Other);
				context.Emit(BinaryOperatorExpression.GetOperatorRole(node.Operator).Token);
				context.EmitWhitespaceBeforeChild(node, node.Right, Previous.Other);
				node.Right.AcceptVisitor(this, (Precedence)(self - 1));
				if (precedence < self) {
					context.Emit(")");
				}
				return null;
			}

			public override object VisitIsExpression(IsExpression node, Precedence precedence)
			{
				context.AddMapping(node);
				if (precedence < Precedence.Relational) {
					context.Emit("(");
				}
				node.Expression.AcceptVisitor(this, Precedence.Relational);
				context.Emit(" instanceof ");
				node.Type.AcceptVisitor(this, (Precedence)(Precedence.Relational - 1));
				if (precedence < Precedence.Relational) {
					context.Emit(")");
				}
				return null;
			}

			public override object VisitIndexerExpression(IndexerExpression node, Precedence precedence)
			{
				context.AddMapping(node);
				node.Target.AcceptVisitor(this, Precedence.Primary);
				context.Emit("[");
				VisitCommaSeparatedExpressions(node, node.Arguments);
				context.Emit("]");
				return null;
			}

			public override object VisitAnonymousMethodExpression(AnonymousMethodExpression node, Precedence precedence)
			{
				context.AddMapping(node);
				context.Emit("function(");
				context.EmitParameters(node.Parameters);
				context.Emit(")");
				context.DecreaseIndent();
				node.Body.AcceptVisitor(context.statementVisitor);
				context.IncreaseIndent();
				return null;
			}

			public override object VisitLambdaExpression(LambdaExpression node, Precedence precedence)
			{
				context.AddMapping(node);
				context.Emit("function(");
				context.EmitParameters(node.Parameters);
				context.Emit(")");
				context.DecreaseIndent();
				node.Body.AcceptVisitor(context.statementVisitor);
				context.IncreaseIndent();
				return null;
			}

			public override object VisitParenthesizedExpression(ParenthesizedExpression node, Precedence precedence)
			{
				// When not minifying, pass the lowest possible precedence to make parentheses appear in more cases
				node.Expression.AcceptVisitor(this, context.ShouldMinify ? precedence : Precedence.Primary);
				return null;
			}
		}
	}
}
