using ICSharpCode.NRefactory.CSharp;
using System.Collections.Generic;

namespace MiniSharp
{
	internal class ManglingContext : DepthFirstAstVisitor
	{
		public static void Mangle(InputContext context)
		{
			var mangling = new ManglingContext();

			// Mangle the content in each file (generated code will
			// need to be inserted into one of these to be mangled)
			foreach (var input in context.inputs) {
				input.tree.AcceptVisitor(mangling);
			}
		}

		private AstNode PreviousSiblingIgnoringWhitespace(AstNode node)
		{
			var previous = node.PrevSibling;
			while (previous != null && (previous is NewLineNode || previous is Comment)) {
				previous = previous.PrevSibling;
			}
			return previous;
		}

		private void ReplaceBlockWithSingleStatement(Statement node)
		{
			var block = node as BlockStatement;
			if (block != null) {
				var statements = block.Statements;
				var count = statements.Count;
				if (count == 0) {
					node.ReplaceWith(new EmptyStatement());
				} else if (count == 1) {
					node.ReplaceWith(statements.FirstOrNullObject());
				}
			}
		}

		private void MangleStatement(AstNodeCollection<Statement> statements, AstNode node)
		{
			// Remove stray semicolons
			if (node is EmptyStatement) {
				node.Remove();
				return;
			}

			// Inline nested block statements
			var block = node as BlockStatement;
			if (block != null) {
				foreach (var replacement in block.Statements) {
					replacement.Remove();
					statements.InsertBefore((Statement)node, replacement);
				}
				node.Remove();
				return;
			}

			// Merge adjacent variable declarations
			var declaration = node as VariableDeclarationStatement;
			if (declaration != null) {
				var previous = PreviousSiblingIgnoringWhitespace(node) as VariableDeclarationStatement;
				if (previous != null) {
					foreach (var variable in declaration.Variables) {
						variable.Remove();
						previous.Variables.Add(variable);
					}
					node.Remove();
				}
				return;
			}

			// Bring previous variable declarations into for loops
			var loop = node as ForStatement;
			if (loop != null) {
				var previous = PreviousSiblingIgnoringWhitespace(node) as VariableDeclarationStatement;
				if (previous != null) {
					var count = loop.Initializers.Count;
					if (count == 0) {
						previous.Remove();
						loop.Initializers.Add(previous);
					} else if (count == 1) {
						var initializers = loop.Initializers.FirstOrNullObject() as VariableDeclarationStatement;
						if (initializers != null) {
							foreach (var variable in initializers.Variables) {
								variable.Remove();
								previous.Variables.Add(variable);
							}
							previous.Remove();
							initializers.Remove();
							loop.Initializers.Add(previous);
						}
					}
				}
				return;
			}
		}

		private bool? BoolConstant(Expression node)
		{
			var primitive = node as PrimitiveExpression;
			return primitive != null && primitive.Value is bool ? (bool)primitive.Value : (bool?)null;
		}

		public override void VisitBlockStatement(BlockStatement node)
		{
			VisitChildren(node);

			var statements = node.Statements;
			for (AstNode child = node.FirstChild, next = null; child != null; child = next) {
				next = child.NextSibling;
				MangleStatement(statements, child);
			}
		}

		public override void VisitSwitchSection(SwitchSection node)
		{
			VisitChildren(node);

			var statements = node.Statements;
			for (AstNode child = node.FirstChild, next = null; child != null; child = next) {
				next = child.NextSibling;
				MangleStatement(statements, child);
			}
		}

		public override void VisitForStatement(ForStatement node)
		{
			VisitChildren(node);

			// Special-case constant conditions
			var constant = BoolConstant(node.Condition);
			if (constant == true) {
				node.Condition.Remove();
			}

			ReplaceBlockWithSingleStatement(node.EmbeddedStatement);
		}

		public override void VisitDoWhileStatement(DoWhileStatement node)
		{
			VisitChildren(node);
			ReplaceBlockWithSingleStatement(node.EmbeddedStatement);
		}

		public override void VisitWhileStatement(WhileStatement node)
		{
			VisitChildren(node);

			var condition = node.Condition;
			var body = node.EmbeddedStatement;
			condition.Remove();
			body.Remove();

			// Special-case constant conditions
			var constant = BoolConstant(condition);
			if (constant == true) {
				condition = null; // Convert "while (true)" => "for(;;)"
			} else if (constant == false) {
				node.Remove(); // Remove "while (false)"
				return;
			}

			// Convert "while (x)" => "for (;x;)" since it's shorter and compresses better
			var loop = new ForStatement();
			loop.Condition = condition;
			loop.EmbeddedStatement = body;
			node.ReplaceWith(loop);

			ReplaceBlockWithSingleStatement(body);
		}

		public override void VisitIfElseStatement(IfElseStatement node)
		{
			VisitChildren(node);

			var yes = node.TrueStatement;
			var no = node.FalseStatement;
			var constant = BoolConstant(node.Condition);

			// Special-case constant conditions
			if (constant == true) {
				node.ReplaceWith(yes); // Inline "if (true)"
			} else if (constant == false) {
				node.ReplaceWith(no); // Inline "if (false)"
			}

			// Inline single statements
			else {
				ReplaceBlockWithSingleStatement(no);
				if (no.IsNull) {
					ReplaceBlockWithSingleStatement(yes);
				}

				// Be careful to avoid the dangling-else issue
				else {
					var block = yes as BlockStatement;
					if (block != null) {
						var statements = block.Statements;
						if (statements.Count == 1) {
							var first = statements.FirstOrNullObject();
							var ifElse = first as IfElseStatement;
							if (ifElse == null || !ifElse.FalseStatement.IsNull) {
								yes.ReplaceWith(first);
							}
						}
					}
				}
			}
		}
	}
}
