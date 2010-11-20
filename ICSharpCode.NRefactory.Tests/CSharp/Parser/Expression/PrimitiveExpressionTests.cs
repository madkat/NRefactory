﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System;
using System.Linq;
using NUnit.Framework;

namespace ICSharpCode.NRefactory.CSharp.Parser.Expression
{
	[TestFixture]
	public class PrimitiveExpressionTests
	{
		[Test]
		public void HexIntegerTest1()
		{
			InvocationExpression invExpr = ParseUtilCSharp.ParseExpression<InvocationExpression>("0xAFFE.ToString()");
			Assert.AreEqual(0, invExpr.Arguments.Count());
			Assert.IsTrue(invExpr.Target is MemberReferenceExpression);
			MemberReferenceExpression fre = invExpr.Target as MemberReferenceExpression;
			Assert.AreEqual("ToString", fre.Identifier);
			
			Assert.IsTrue(fre.Target is PrimitiveExpression);
			PrimitiveExpression pe = fre.Target as PrimitiveExpression;
			
			Assert.AreEqual(0xAFFE, (int)pe.Value);
			
		}
		
		void CheckLiteral(string code, object value)
		{
			PrimitiveExpression pe = ParseUtilCSharp.ParseExpression<PrimitiveExpression>(code);
			Assert.AreEqual(value.GetType(), pe.Value.GetType());
			Assert.AreEqual(value, pe.Value);
		}
		
		[Test]
		public void DoubleTest1()
		{
			CheckLiteral(".5e-06", .5e-06);
		}
		
		[Test]
		public void CharTest1()
		{
			CheckLiteral("'\\u0356'", '\u0356');
		}
		
		[Test]
		public void IntMinValueTest()
		{
			CheckLiteral("-2147483648", -2147483648);
		}
		
		[Test]
		public void IntMaxValueTest()
		{
			CheckLiteral("2147483647", 2147483647); // int
			CheckLiteral("2147483648", 2147483648); // uint
		}
		
		[Test]
		public void LongMinValueTest()
		{
			CheckLiteral("-9223372036854775808", -9223372036854775808);
		}
		
		[Test]
		public void LongMaxValueTest()
		{
			CheckLiteral("9223372036854775807", 9223372036854775807); // long
			CheckLiteral("9223372036854775808", 9223372036854775808); // ulong
		}
		
		[Test]
		public void StringTest1()
		{
			CheckLiteral("\"\\n\\t\\u0005 Hello World !!!\"", "\n\t\u0005 Hello World !!!");
		}
		
		[Test]
		public void TestSingleDigit()
		{
			CheckLiteral("5", 5);
		}
		
		[Test]
		public void TestZero()
		{
			CheckLiteral("0", 0);
		}
		
		[Test]
		public void TestInteger()
		{
			CheckLiteral("66", 66);
		}
		
		[Test]
		public void TestNonOctalInteger()
		{
			// C# does not have octal integers, so 077 should parse to 77
			Assert.IsTrue(077 == 77);
			
			CheckLiteral("077", 077);
			CheckLiteral("056", 056);
		}
		
		[Test]
		public void TestHexadecimalInteger()
		{
			CheckLiteral("0x99F", 0x99F);
			CheckLiteral("0xAB1f", 0xAB1f);
			CheckLiteral("0xffffffff", 0xffffffff);
			CheckLiteral("0xffffffffL", 0xffffffffL);
			CheckLiteral("0xffffffffuL", 0xffffffffuL);
		}
		
		[Test]
		public void InvalidHexadecimalInteger()
		{
			// don't check result, just make sure there is no exception
			ParseUtilCSharp.ParseExpression<PrimitiveExpression>("0x2GF", expectErrors: true);
			ParseUtilCSharp.ParseExpression<PrimitiveExpression>("0xG2F", expectErrors: true);
			ParseUtilCSharp.ParseExpression<PrimitiveExpression>("0x", expectErrors: true); // SD-457
			// hexadecimal integer >ulong.MaxValue
			ParseUtilCSharp.ParseExpression<PrimitiveExpression>("0xfedcba98765432100", expectErrors: true);
		}
		
		[Test]
		public void TestLongHexadecimalInteger()
		{
			CheckLiteral("0x4244636f446c6d58", 0x4244636f446c6d58);
			CheckLiteral("0xf244636f446c6d58", 0xf244636f446c6d58);
		}
		
		[Test]
		public void TestLongInteger()
		{
			CheckLiteral("9223372036854775807", 9223372036854775807); // long.MaxValue
			CheckLiteral("9223372036854775808", 9223372036854775808); // long.MaxValue+1
			CheckLiteral("18446744073709551615", 18446744073709551615); // ulong.MaxValue
			CheckLiteral("18446744073709551616f", 18446744073709551616f); // ulong.MaxValue+1 as float
			CheckLiteral("18446744073709551616d", 18446744073709551616d); // ulong.MaxValue+1 as double
			CheckLiteral("18446744073709551616m", 18446744073709551616m); // ulong.MaxValue+1 as decimal
		}
		
		[Test]
		public void TestDouble()
		{
			CheckLiteral("1.0", 1.0);
			CheckLiteral("1.1", 1.1);
			CheckLiteral("1.1e-2", 1.1e-2);
		}
		
		[Test]
		public void TestFloat()
		{
			CheckLiteral("1f", 1f);
			CheckLiteral("1.0f", 1.0f);
			CheckLiteral("1.1f", 1.1f);
			CheckLiteral("1.1e-2f", 1.1e-2f);
		}
		
		[Test]
		public void TestDecimal()
		{
			CheckLiteral("1m", 1m);
			CheckLiteral("1.0m", 1.0m);
			CheckLiteral("1.1m", 1.1m);
			CheckLiteral("1.1e-2m", 1.1e-2m);
			CheckLiteral("2.0e-5m", 2.0e-5m);
		}
		
		[Test]
		public void TestString()
		{
			CheckLiteral(@"@""-->""""<--""", @"-->""<--");
			CheckLiteral(@"""-->\""<--""", "-->\"<--");
			
			CheckLiteral(@"""\U00000041""", "\U00000041");
			CheckLiteral(@"""\U00010041""", "\U00010041");
		}
		
		[Test]
		public void TestCharLiteral()
		{
			CheckLiteral(@"'a'", 'a');
			CheckLiteral(@"'\u0041'", '\u0041');
			CheckLiteral(@"'\x41'", '\x41');
			CheckLiteral(@"'\x041'", '\x041');
			CheckLiteral(@"'\x0041'", '\x0041');
			CheckLiteral(@"'\U00000041'", '\U00000041');
		}
	}
}