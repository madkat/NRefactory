﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.TypeSystem.Implementation;
using ICSharpCode.NRefactory.Utils;

namespace ICSharpCode.NRefactory.CSharp
{
	/// <summary>
	/// Produces type and member definitions from the DOM.
	/// </summary>
	public class TypeSystemConvertVisitor : AbstractDomVisitor<object, IEntity>
	{
		readonly ParsedFile parsedFile;
		UsingScope usingScope;
		DefaultTypeDefinition currentTypeDefinition;
		DefaultMethod currentMethod;
		
		public TypeSystemConvertVisitor(IProjectContent pc, string fileName)
		{
			this.parsedFile = new ParsedFile(fileName, new UsingScope(pc));
			this.usingScope = parsedFile.RootUsingScope;
		}
		
		public ParsedFile ParsedFile {
			get { return parsedFile; }
		}
		
		DomRegion MakeRegion(DomLocation start, DomLocation end)
		{
			return new DomRegion(parsedFile.FileName, start.Line, start.Column, end.Line, end.Column);
		}
		
		DomRegion MakeRegion(INode node)
		{
			if (node == null)
				return DomRegion.Empty;
			else
				return MakeRegion(node.StartLocation, node.EndLocation);
		}
		
		#region Using Declarations
		// TODO: extern aliases
		
		public override IEntity VisitUsingDeclaration(UsingDeclaration usingDeclaration, object data)
		{
			ITypeOrNamespaceReference r = null;
			foreach (Identifier identifier in usingDeclaration.NameIdentifier.NameParts) {
				if (r == null) {
					// TODO: alias?
					r = new SimpleTypeOrNamespaceReference(identifier.Name, null, currentTypeDefinition, usingScope, true);
				} else {
					r = new MemberTypeOrNamespaceReference(r, identifier.Name, null, currentTypeDefinition, usingScope);
				}
			}
			if (r != null)
				usingScope.Usings.Add(r);
			return null;
		}
		
		public override IEntity VisitUsingAliasDeclaration(UsingAliasDeclaration usingDeclaration, object data)
		{
			throw new NotImplementedException();
		}
		#endregion
		
		#region Namespace Declaration
		public override IEntity VisitNamespaceDeclaration(NamespaceDeclaration namespaceDeclaration, object data)
		{
			DomRegion region = MakeRegion(namespaceDeclaration);
			UsingScope previousUsingScope = usingScope;
			foreach (Identifier ident in namespaceDeclaration.NameIdentifier.NameParts) {
				usingScope = new UsingScope(usingScope, NamespaceDeclaration.BuildQualifiedName(usingScope.NamespaceName, ident.Name));
				usingScope.Region = region;
			}
			base.VisitNamespaceDeclaration(namespaceDeclaration, data);
			parsedFile.UsingScopes.Add(usingScope); // add after visiting children so that nested scopes come first
			usingScope = previousUsingScope;
			return null;
		}
		#endregion
		
		// TODO: assembly attributes
		
		#region Type Definitions
		DefaultTypeDefinition CreateTypeDefinition(string name)
		{
			DefaultTypeDefinition newType;
			if (currentTypeDefinition != null) {
				newType = new DefaultTypeDefinition(currentTypeDefinition, name);
				currentTypeDefinition.InnerClasses.Add(newType);
			} else {
				newType = new DefaultTypeDefinition(usingScope.ProjectContent, usingScope.NamespaceName, name);
				parsedFile.TopLevelTypeDefinitions.Add(newType);
			}
			return newType;
		}
		
		public override IEntity VisitTypeDeclaration(TypeDeclaration typeDeclaration, object data)
		{
			var td = currentTypeDefinition = CreateTypeDefinition(typeDeclaration.Name);
			td.ClassType = typeDeclaration.ClassType;
			td.Region = MakeRegion(typeDeclaration);
			td.BodyRegion = MakeRegion(typeDeclaration.LBrace.StartLocation, typeDeclaration.RBrace.EndLocation);
			td.AddDefaultConstructorIfRequired = true;
			
			ConvertAttributes(td.Attributes, typeDeclaration.Attributes);
			ApplyModifiers(td, typeDeclaration.Modifiers);
			if (td.ClassType == ClassType.Interface)
				td.IsAbstract = true; // interfaces are implicitly abstract
			else if (td.ClassType == ClassType.Enum || td.ClassType == ClassType.Struct)
				td.IsSealed = true; // enums/structs are implicitly sealed
			
			//TODO ConvertTypeParameters(td.TypeParameters, typeDeclaration.TypeParameters, typeDeclaration.Constraints);
			
			// TODO: base type references?
			
			foreach (AbstractMemberBase member in typeDeclaration.Members) {
				member.AcceptVisitor(this, data);
			}
			
			currentTypeDefinition = (DefaultTypeDefinition)currentTypeDefinition.DeclaringTypeDefinition;
			return td;
		}
		
		public override IEntity VisitDelegateDeclaration(DelegateDeclaration delegateDeclaration, object data)
		{
			var td = CreateTypeDefinition(delegateDeclaration.Name);
			td.ClassType = ClassType.Delegate;
			td.Region = MakeRegion(delegateDeclaration);
			td.BaseTypes.Add(multicastDelegateReference);
			
			ConvertAttributes(td.Attributes, delegateDeclaration.Attributes);
			ApplyModifiers(td, delegateDeclaration.Modifiers);
			td.IsSealed = true; // delegates are implicitly sealed
			
			// TODO: convert return type, convert parameters
			AddDefaultMethodsToDelegate(td, SharedTypes.UnknownType, EmptyList<IParameter>.Instance);
			
			return td;
		}
		
		static readonly ITypeReference multicastDelegateReference = typeof(MulticastDelegate).ToTypeReference();
		static readonly IParameter delegateObjectParameter = MakeParameter(TypeCode.Object.ToTypeReference(), "object");
		static readonly IParameter delegateIntPtrMethodParameter = MakeParameter(typeof(IntPtr).ToTypeReference(), "method");
		static readonly IParameter delegateAsyncCallbackParameter = MakeParameter(typeof(AsyncCallback).ToTypeReference(), "callback");
		static readonly IParameter delegateResultParameter = MakeParameter(typeof(IAsyncResult).ToTypeReference(), "result");
		
		static IParameter MakeParameter(ITypeReference type, string name)
		{
			DefaultParameter p = new DefaultParameter(type, name);
			p.Freeze();
			return p;
		}
		
		/// <summary>
		/// Adds the 'Invoke', 'BeginInvoke', 'EndInvoke' methods, and a constructor, to the <see cref="delegateType"/>.
		/// </summary>
		public static void AddDefaultMethodsToDelegate(DefaultTypeDefinition delegateType, ITypeReference returnType, IEnumerable<IParameter> parameters)
		{
			if (delegateType == null)
				throw new ArgumentNullException("delegateType");
			if (returnType == null)
				throw new ArgumentNullException("returnType");
			if (parameters == null)
				throw new ArgumentNullException("parameters");
			
			DomRegion region = new DomRegion(delegateType.Region.FileName, delegateType.Region.BeginLine, delegateType.Region.BeginColumn);
			
			DefaultMethod invoke = new DefaultMethod(delegateType, "Invoke");
			invoke.Accessibility = Accessibility.Public;
			invoke.IsSynthetic = true;
			invoke.Parameters.AddRange(parameters);
			invoke.ReturnType = returnType;
			invoke.Region = region;
			delegateType.Methods.Add(invoke);
			
			DefaultMethod beginInvoke = new DefaultMethod(delegateType, "BeginInvoke");
			beginInvoke.Accessibility = Accessibility.Public;
			beginInvoke.IsSynthetic = true;
			beginInvoke.Parameters.AddRange(invoke.Parameters);
			beginInvoke.Parameters.Add(delegateAsyncCallbackParameter);
			beginInvoke.Parameters.Add(delegateObjectParameter);
			beginInvoke.ReturnType = delegateResultParameter.Type;
			beginInvoke.Region = region;
			delegateType.Methods.Add(beginInvoke);
			
			DefaultMethod endInvoke = new DefaultMethod(delegateType, "EndInvoke");
			endInvoke.Accessibility = Accessibility.Public;
			endInvoke.IsSynthetic = true;
			endInvoke.Parameters.Add(delegateResultParameter);
			endInvoke.ReturnType = invoke.ReturnType;
			endInvoke.Region = region;
			delegateType.Methods.Add(endInvoke);
			
			DefaultMethod ctor = new DefaultMethod(delegateType, ".ctor");
			ctor.EntityType = EntityType.Constructor;
			ctor.Accessibility = Accessibility.Public;
			ctor.IsSynthetic = true;
			ctor.Parameters.Add(delegateObjectParameter);
			ctor.Parameters.Add(delegateIntPtrMethodParameter);
			ctor.ReturnType = delegateType;
			ctor.Region = region;
			delegateType.Methods.Add(ctor);
		}
		#endregion
		
		#region Fields
		public override IEntity VisitFieldDeclaration(FieldDeclaration fieldDeclaration, object data)
		{
			bool isSingleField = fieldDeclaration.Variables.Count() == 1;
			Modifiers modifiers = fieldDeclaration.Modifiers;
			DefaultField field = null;
			foreach (VariableInitializer vi in fieldDeclaration.Variables) {
				field = new DefaultField(currentTypeDefinition, vi.Name);
				
				field.Region = isSingleField ? MakeRegion(fieldDeclaration) : MakeRegion(vi);
				field.BodyRegion = MakeRegion(vi);
				ConvertAttributes(field.Attributes, fieldDeclaration.Attributes);
				
				ApplyModifiers(field, modifiers);
				field.IsVolatile = (modifiers & Modifiers.Volatile) != 0;
				field.IsReadOnly = (modifiers & Modifiers.Readonly) != 0;
				
				field.ReturnType = ConvertType(fieldDeclaration.ReturnType);
				if ((modifiers & Modifiers.Fixed) != 0) {
					field.ReturnType = PointerTypeReference.Create(field.ReturnType);
				}
				
				if ((modifiers & Modifiers.Const) != 0) {
					field.ConstantValue = ConvertConstantValue(field.ReturnType, vi.Initializer);
				}
				
				currentTypeDefinition.Fields.Add(field);
			}
			return isSingleField ? field : null;
		}
		#endregion
		
		#region Methods
		public override IEntity VisitMethodDeclaration(MethodDeclaration methodDeclaration, object data)
		{
			DefaultMethod m = new DefaultMethod(currentTypeDefinition, methodDeclaration.Name);
			currentMethod = m; // required for resolving type parameters
			m.Region = MakeRegion(methodDeclaration);
			m.BodyRegion = MakeRegion(methodDeclaration.Body);
			
			
			//TODO ConvertTypeParameters(m.TypeParameters, methodDeclaration.TypeParameters, methodDeclaration.Constraints);
			m.ReturnType = ConvertType(methodDeclaration.ReturnType);
			ConvertAttributes(m.Attributes, methodDeclaration.Attributes.Where(s => s.AttributeTarget != AttributeTarget.Return));
			ConvertAttributes(m.ReturnTypeAttributes, methodDeclaration.Attributes.Where(s => s.AttributeTarget == AttributeTarget.Return));
			
			ApplyModifiers(m, methodDeclaration.Modifiers);
			m.IsExtensionMethod = methodDeclaration.IsExtensionMethod;
			
			ConvertParameters(m.Parameters, methodDeclaration.Parameters);
			if (methodDeclaration.PrivateImplementationType != null) {
				m.Accessibility = Accessibility.None;
				m.InterfaceImplementations.Add(ConvertInterfaceImplementation(methodDeclaration.PrivateImplementationType, m.Name));
			}
			
			currentTypeDefinition.Methods.Add(m);
			currentMethod = null;
			return m;
		}
		
		DefaultExplicitInterfaceImplementation ConvertInterfaceImplementation(INode interfaceType, string memberName)
		{
			return new DefaultExplicitInterfaceImplementation(ConvertType(interfaceType), memberName);
		}
		#endregion
		
		#region Operators
		public override IEntity VisitOperatorDeclaration(OperatorDeclaration operatorDeclaration, object data)
		{
			DefaultMethod m = new DefaultMethod(currentTypeDefinition, operatorDeclaration.Name);
			m.EntityType = EntityType.Operator;
			m.Region = MakeRegion(operatorDeclaration);
			m.BodyRegion = MakeRegion(operatorDeclaration.Body);
			
			m.ReturnType = ConvertType(operatorDeclaration.ReturnType);
			ConvertAttributes(m.Attributes, operatorDeclaration.Attributes.Where(s => s.AttributeTarget != AttributeTarget.Return));
			ConvertAttributes(m.ReturnTypeAttributes, operatorDeclaration.Attributes.Where(s => s.AttributeTarget == AttributeTarget.Return));
			
			ApplyModifiers(m, operatorDeclaration.Modifiers);
			
			ConvertParameters(m.Parameters, operatorDeclaration.Parameters);
			
			currentTypeDefinition.Methods.Add(m);
			return m;
		}
		#endregion
		
		#region Constructors
		public override IEntity VisitConstructorDeclaration(ConstructorDeclaration constructorDeclaration, object data)
		{
			Modifiers modifiers = constructorDeclaration.Modifiers;
			bool isStatic = (modifiers & Modifiers.Static) != 0;
			DefaultMethod ctor = new DefaultMethod(currentTypeDefinition, isStatic ? ".cctor" : ".ctor");
			ctor.EntityType = EntityType.Constructor;
			ctor.Region = MakeRegion(constructorDeclaration);
			if (constructorDeclaration.Initializer != null) {
				ctor.BodyRegion = MakeRegion(constructorDeclaration.Initializer.StartLocation, constructorDeclaration.EndLocation);
			} else {
				ctor.BodyRegion = MakeRegion(constructorDeclaration.Body);
			}
			ctor.ReturnType = currentTypeDefinition;
			
			ConvertAttributes(ctor.Attributes, constructorDeclaration.Attributes);
			ConvertParameters(ctor.Parameters, constructorDeclaration.Parameters);
			
			if (isStatic)
				ctor.IsStatic = true;
			else
				ApplyModifiers(ctor, modifiers);
			
			currentTypeDefinition.Methods.Add(ctor);
			return ctor;
		}
		#endregion
		
		#region Destructors
		static readonly GetClassTypeReference voidReference = new GetClassTypeReference("System.Void", 0);
		
		public override IEntity VisitDestructorDeclaration(DestructorDeclaration destructorDeclaration, object data)
		{
			DefaultMethod dtor = new DefaultMethod(currentTypeDefinition, "Finalize");
			dtor.EntityType = EntityType.Destructor;
			dtor.Region = MakeRegion(destructorDeclaration);
			dtor.BodyRegion = MakeRegion(destructorDeclaration.Body);
			dtor.Accessibility = Accessibility.Protected;
			dtor.IsOverride = true;
			dtor.ReturnType = voidReference;
			
			ConvertAttributes(dtor.Attributes, destructorDeclaration.Attributes);
			
			currentTypeDefinition.Methods.Add(dtor);
			return dtor;
		}
		#endregion
		
		#region Modifiers
		static void ApplyModifiers(DefaultTypeDefinition td, Modifiers modifiers)
		{
			td.Accessibility = GetAccessibility(modifiers) ?? (td.DeclaringTypeDefinition != null ? Accessibility.Private : Accessibility.Internal);
			td.IsAbstract = (modifiers & (Modifiers.Abstract | Modifiers.Static)) != 0;
			td.IsSealed = (modifiers & (Modifiers.Sealed | Modifiers.Static)) != 0;
			td.IsShadowing = (modifiers & Modifiers.New) != 0;
		}
		
		static void ApplyModifiers(TypeSystem.Implementation.AbstractMember m, Modifiers modifiers)
		{
			m.Accessibility = GetAccessibility(modifiers) ?? Accessibility.Private;
			m.IsAbstract = (modifiers & Modifiers.Abstract) != 0;
			m.IsOverride = (modifiers & Modifiers.Override) != 0;
			m.IsSealed = (modifiers & Modifiers.Sealed) != 0;
			m.IsShadowing = (modifiers & Modifiers.New) != 0;
			m.IsStatic = (modifiers & Modifiers.Static) != 0;
			m.IsVirtual = (modifiers & Modifiers.Virtual) != 0;
		}
		
		static Accessibility? GetAccessibility(Modifiers modifiers)
		{
			switch (modifiers & Modifiers.VisibilityMask) {
				case Modifiers.Private:
					return Accessibility.Private;
				case Modifiers.Internal:
					return Accessibility.Internal;
				case Modifiers.Protected | Modifiers.Internal:
					return Accessibility.ProtectedOrInternal;
				case Modifiers.Protected:
					return Accessibility.Protected;
				case Modifiers.Public:
					return Accessibility.Public;
				default:
					return null;
			}
		}
		#endregion
		
		#region Attributes
		void ConvertAttributes(IList<IAttribute> outputList, IEnumerable<AttributeSection> attributes)
		{
			foreach (AttributeSection section in attributes) {
				throw new NotImplementedException();
			}
		}
		#endregion
		
		#region Types
		ITypeReference ConvertType(INode node)
		{
			return SharedTypes.UnknownType;
		}
		#endregion
		
		#region Constant Values
		IConstantValue ConvertConstantValue(ITypeReference targetType, INode expression)
		{
			return new SimpleConstantValue(targetType, null);
		}
		#endregion
		
		#region Parameters
		void ConvertParameters(IList<IParameter> outputList, IEnumerable<ParameterDeclaration> parameters)
		{
			foreach (ParameterDeclaration pd in parameters) {
				DefaultParameter p = new DefaultParameter(ConvertType(pd.Type), pd.Name);
				p.Region = MakeRegion(pd);
				ConvertAttributes(p.Attributes, pd.Attributes);
				switch (pd.ParameterModifier) {
					case ParameterModifier.Ref:
						p.IsRef = true;
						p.Type = ByReferenceTypeReference.Create(p.Type);
						break;
					case ParameterModifier.Out:
						p.IsOut = true;
						p.Type = ByReferenceTypeReference.Create(p.Type);
						break;
					case ParameterModifier.Params:
						p.IsParams = true;
						break;
				}
				if (pd.DefaultExpression != null)
					p.DefaultValue = ConvertConstantValue(p.Type, pd.DefaultExpression);
				outputList.Add(p);
			}
		}
		#endregion
	}
}