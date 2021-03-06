﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ICSharpCode.NRefactory.CSharp.PatternMatching
{
	/// <summary>
	/// Base class for all patterns.
	/// </summary>
	public abstract class Pattern : AstNode
	{
		public override NodeType NodeType {
			get { return NodeType.Pattern; }
		}
		
		internal struct PossibleMatch
		{
			public readonly AstNode NextOther; // next node after the last matched node
			public readonly int Checkpoint; // checkpoint
			
			public PossibleMatch(AstNode nextOther, int checkpoint)
			{
				this.NextOther = nextOther;
				this.Checkpoint = checkpoint;
			}
		}
		
		public static implicit operator AstType(Pattern p)
		{
			return p != null ? new TypePlaceholder(p) : null;
		}
		
		public static implicit operator Expression(Pattern p)
		{
			return p != null ? new ExpressionPlaceholder(p) : null;
		}
		
		public static implicit operator Statement(Pattern p)
		{
			return p != null ? new StatementPlaceholder(p) : null;
		}
		
		public static implicit operator BlockStatement(Pattern p)
		{
			return p != null ? new BlockStatementPlaceholder(p) : null;
		}
		
		public static implicit operator VariableInitializer(Pattern p)
		{
			return p != null ? new VariablePlaceholder(p) : null;
		}
		
		// Make debugging easier by giving Patterns a ToString() implementation
		public override string ToString()
		{
			StringWriter w = new StringWriter();
			AcceptVisitor(new OutputVisitor(w, new CSharpFormattingPolicy()), null);
			return w.ToString();
		}
	}
}
